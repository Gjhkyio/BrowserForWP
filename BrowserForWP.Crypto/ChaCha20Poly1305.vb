' BrowserForWP — ChaCha20-Poly1305 AEAD (RFC 8439).
'
' Verified by tools/gen-vectors.mjs against RFC 8439 §2.8.2 and by the
' authenticated-failure tests in tests/BrowserForWP.Crypto.Tests.
'
' ChaCha20 itself is pure 32-bit integer arithmetic and is implemented in the
' natural form: it is fast, simple and exactly reproducible from the RFC's
' pseudocode.
'
' Poly1305 uses BigInteger for the accumulator. See the note on X25519 for the
' reasoning: the WP8.1 SDK is Windows-only, so a representation whose arithmetic
' can be reviewed is worth more here than a limb-based one that is faster but
' cannot be executed during development. Poly1305 is only used when the
' TLS_CHACHA20_POLY1305_SHA256 suite is negotiated; AES-GCM (the preferred
' suite) goes through the platform's accelerated provider and never touches this
' code.
'
' TODO(perf): replace the Poly1305 accumulator with 2^26 limbs if profiling on
' hardware shows it matters. The RFC 8439 vectors do not change.

Imports System.Numerics
Imports System.Security.Cryptography

Namespace Crypto

    ''' <summary>ChaCha20-Poly1305 AEAD (RFC 8439).</summary>
    Public NotInheritable Class ChaCha20Poly1305

        Public Const KeySize As Integer = 32
        Public Const NonceSize As Integer = 12
        Public Const TagSize As Integer = 16

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The AEAD output. Deliberately the same type that AesGcm returns, so the
        ''' TLS record layer can hold one result type regardless of the negotiated
        ''' cipher suite.
        ''' </summary>
        Public NotInheritable Class SealedResult

            Public Sub New(ciphertext As Byte(), tag As Byte())
                Me.Ciphertext = ciphertext
                Me.Tag = tag
            End Sub

            Public ReadOnly Ciphertext As Byte()
            Public ReadOnly Tag As Byte()
        End Class

        Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As SealedResult
            ValidateKeyAndNonce(key, nonce)
            Dim payload = If(plaintext, New Byte() {})

            Dim oneTimeKey = Poly1305Key(key, nonce)
            Dim ciphertext = XorKeystream(key, nonce, 1UI, payload)
            Dim tag = Mac(oneTimeKey, If(aad, New Byte() {}), ciphertext)
            Return New SealedResult(ciphertext, tag)
        End Function

        Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(),
                                    ciphertext As Byte(), tag As Byte()) As Byte()
            ValidateKeyAndNonce(key, nonce)
            If tag Is Nothing OrElse tag.Length <> TagSize Then
                Throw New CryptographicException("invalid Poly1305 tag length")
            End If

            Dim payload = If(ciphertext, New Byte() {})
            Dim oneTimeKey = Poly1305Key(key, nonce)
            Dim expected = Mac(oneTimeKey, If(aad, New Byte() {}), payload)

            ' Authenticate before decrypting, and compare in constant time: a
            ' timing-sensitive comparison here would leak the tag byte by byte.
            If Not Hkdf.FixedTimeEquals(expected, tag) Then
                Throw New CryptographicException("ChaCha20-Poly1305 authentication failed")
            End If

            Return XorKeystream(key, nonce, 1UI, payload)
        End Function

        Private Shared Sub ValidateKeyAndNonce(key As Byte(), nonce As Byte())
            If key Is Nothing OrElse key.Length <> KeySize Then
                Throw New ArgumentException("key must be 32 bytes", "key")
            End If
            If nonce Is Nothing OrElse nonce.Length <> NonceSize Then
                Throw New ArgumentException("nonce must be 12 bytes", "nonce")
            End If
        End Sub

        ''' <summary>RFC 8439 §2.6: the Poly1305 one-time key is the first 32 bytes of the counter-0 keystream.</summary>
        Private Shared Function Poly1305Key(key As Byte(), nonce As Byte()) As Byte()
            Dim block = ChaChaBlock(key, 0UI, nonce)
            Dim oneTimeKey(31) As Byte
            Array.Copy(block, oneTimeKey, 32)
            Return oneTimeKey
        End Function

        ''' <summary>
        ''' RFC 8439 §2.8: MAC over aad || pad16 || ciphertext || pad16 ||
        ''' little-endian uint64 lengths.
        ''' </summary>
        Private Shared Function Mac(oneTimeKey As Byte(), aad As Byte(), ciphertext As Byte()) As Byte()
            Dim input As New List(Of Byte)(aad.Length + ciphertext.Length + 32)
            input.AddRange(aad)
            AddPadding(input, aad.Length)
            input.AddRange(ciphertext)
            AddPadding(input, ciphertext.Length)
            AppendUInt64LE(input, CLng(aad.Length))
            AppendUInt64LE(input, CLng(ciphertext.Length))
            Return Poly1305.Compute(oneTimeKey, input.ToArray())
        End Function

        Private Shared Sub AddPadding(target As List(Of Byte), length As Integer)
            Dim pad = (16 - (length Mod 16)) Mod 16
            For i As Integer = 1 To pad
                target.Add(0)
            Next
        End Sub

        Private Shared Sub AppendUInt64LE(target As List(Of Byte), value As Long)
            For i As Integer = 0 To 7
                target.Add(CByte((value >> (8 * i)) And &HFF))
            Next
        End Sub

        ''' <summary>
        ''' Encrypt or decrypt by XOR with the ChaCha20 keystream, starting at the
        ''' given block counter. The counter is incremented per 64-byte block and
        ''' must never repeat under the same key/nonce pair.
        ''' </summary>
        Private Shared Function XorKeystream(key As Byte(), nonce As Byte(), counter As UInteger, data As Byte()) As Byte()
            Dim output(data.Length - 1) As Byte
            If data.Length = 0 Then Return output

            Dim offset As Integer = 0
            Dim blockCounter = counter
            While offset < data.Length
                Dim block = ChaChaBlock(key, blockCounter, nonce)
                blockCounter += 1UI
                Dim take = Math.Min(64, data.Length - offset)
                For i As Integer = 0 To take - 1
                    output(offset + i) = CByte(data(offset + i) Xor block(i))
                Next
                offset += take
            End While
            Return output
        End Function

        ''' <summary>
        ''' One 64-byte ChaCha20 block (RFC 8439 §2.3). The state is four constants,
        ''' the key, the counter, and the nonce; 20 rounds are applied as 10
        ''' double-rounds, then the original state is added back.
        ''' </summary>
        Private Shared Function ChaChaBlock(key As Byte(), counter As UInteger, nonce As Byte()) As Byte()
            Dim state(15) As UInteger
            state(0) = &H61707865UI   ' "expa"
            state(1) = &H3320646EUI   ' "nd 3"
            state(2) = &H79622D32UI   ' "2-by"
            state(3) = &H6B206574UI   ' "te k"
            For i As Integer = 0 To 7
                state(4 + i) = ReadUInt32LE(key, i * 4)
            Next
            state(12) = counter
            For i As Integer = 0 To 2
                state(13 + i) = ReadUInt32LE(nonce, i * 4)
            Next

            Dim working(15) As UInteger
            Array.Copy(state, working, 16)

            For round As Integer = 1 To 10
                QuarterRound(working, 0, 4, 8, 12)
                QuarterRound(working, 1, 5, 9, 13)
                QuarterRound(working, 2, 6, 10, 14)
                QuarterRound(working, 3, 7, 11, 15)
                QuarterRound(working, 0, 5, 10, 15)
                QuarterRound(working, 1, 6, 11, 12)
                QuarterRound(working, 2, 7, 8, 13)
                QuarterRound(working, 3, 4, 9, 14)
            Next

            Dim output(63) As Byte
            For i As Integer = 0 To 15
                Dim word = working(i) + state(i)
                output(i * 4) = CByte(word And &HFFUI)
                output(i * 4 + 1) = CByte((word >> 8) And &HFFUI)
                output(i * 4 + 2) = CByte((word >> 16) And &HFFUI)
                output(i * 4 + 3) = CByte((word >> 24) And &HFFUI)
            Next
            Return output
        End Function

        ''' <summary>RFC 8439 §2.1 quarter round, operating in place.</summary>
        Private Shared Sub QuarterRound(s As UInteger(), a As Integer, b As Integer, c As Integer, d As Integer)
            s(a) += s(b) : s(d) = RotateLeft(s(d) Xor s(a), 16)
            s(c) += s(d) : s(b) = RotateLeft(s(b) Xor s(c), 12)
            s(a) += s(b) : s(d) = RotateLeft(s(d) Xor s(a), 8)
            s(c) += s(d) : s(b) = RotateLeft(s(b) Xor s(c), 7)
        End Sub

        Private Shared Function RotateLeft(value As UInteger, bits As Integer) As UInteger
            Return (value << bits) Or (value >> (32 - bits))
        End Function

        Private Shared Function ReadUInt32LE(data As Byte(), offset As Integer) As UInteger
            Return CUInt(data(offset)) Or
                   (CUInt(data(offset + 1)) << 8) Or
                   (CUInt(data(offset + 2)) << 16) Or
                   (CUInt(data(offset + 3)) << 24)
        End Function

        ''' <summary>
        ''' Poly1305 (RFC 8439 §2.5). The accumulator is
        ''' ((acc + block) * r) mod (2^130 - 5), where r is the clamped low half of
        ''' the one-time key and s is the high half added at the end.
        ''' </summary>
        Friend NotInheritable Class Poly1305

            Private Shared ReadOnly Modulus As BigInteger = (BigInteger.One << 130) - 5

            Private Sub New()
            End Sub

            Friend Shared Function Compute(oneTimeKey As Byte(), message As Byte()) As Byte()
                If oneTimeKey Is Nothing OrElse oneTimeKey.Length <> 32 Then
                    Throw New ArgumentException("Poly1305 key must be 32 bytes", "oneTimeKey")
                End If

                ' r is clamped (RFC 8439 §2.5).
                Dim rBytes(16) As Byte
                Array.Copy(oneTimeKey, 0, rBytes, 0, 16)
                rBytes(3) = CByte(rBytes(3) And &H0F)
                rBytes(7) = CByte(rBytes(7) And &H0F)
                rBytes(11) = CByte(rBytes(11) And &H0F)
                rBytes(15) = CByte(rBytes(15) And &H0F)
                rBytes(4) = CByte(rBytes(4) And &HFC)
                rBytes(8) = CByte(rBytes(8) And &HFC)
                rBytes(12) = CByte(rBytes(12) And &HFC)
                Dim r = New BigInteger(rBytes)

                Dim sBytes(16) As Byte
                Array.Copy(oneTimeKey, 16, sBytes, 0, 16)
                Dim s = New BigInteger(sBytes)

                Dim accumulator = BigInteger.Zero
                Dim offset As Integer = 0
                While offset < message.Length
                    Dim take = Math.Min(16, message.Length - offset)
                    ' Each block is read as a little-endian integer with an extra
                    ' high bit set — that is what turns Poly1305 into a
                    ' one-time-MAC rather than a plain polynomial evaluation.
                    Dim blockBytes(16) As Byte
                    Array.Copy(message, offset, blockBytes, 0, take)
                    blockBytes(take) = 1
                    Dim block = New BigInteger(blockBytes)

                    accumulator = ((accumulator + block) * r) Mod Modulus
                    offset += take
                End While

                accumulator = (accumulator + s) Mod (BigInteger.One << 128)

                Dim result = accumulator.ToByteArray()
                Dim tag(15) As Byte
                Array.Copy(result, tag, Math.Min(result.Length, 16))
                Return tag
            End Function
        End Class
    End Class

End Namespace
