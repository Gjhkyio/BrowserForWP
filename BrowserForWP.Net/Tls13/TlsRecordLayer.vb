' BrowserForWP — TLS 1.3 record layer: AEAD protection and the inner plaintext.
'
' Transliteration of the RecordLayer class in tools/proto/tls13.mjs, which was
' validated by completing live handshakes against Google, Cloudflare and
' example.com.
'
' ─────────────────────────────────────────────────────────────────────────────
'  READ THIS BEFORE CHANGING ANYTHING IN THIS FILE
' ─────────────────────────────────────────────────────────────────────────────
' RFC 8446 §5.2 defines the encrypted record's plaintext as:
'
'   struct {
'       opaque content[TLSPlaintext.length];
'       ContentType type;
'       uint8 zeros[length_of_padding];
'   } TLSInnerPlaintext;
'
' The content type comes AFTER the content, and padding follows it. The first
' draft of the prototype had it as a PREFIX, which is TLS 1.2 thinking, and it
' produced a plausible-looking failure: because AEAD decryption succeeds (only
' the tag depends on the layout, and the tag covers the ciphertext, not the
' plaintext order), the mistake is invisible to any self-consistency test. It
' only surfaced against a real server, as a handshake that died immediately
' after ServerHello with no alert.
'
' Two consequences worth keeping in mind:
'   * A wrong inner layout is NOT caught by "seal then open" round-trip tests.
'     It is caught by decrypting a real server's bytes, or by asserting the
'     exact byte layout of a sealed record against a known vector.
'   * Padding is stripped from the END. Stripping a prefix instead silently
'     corrupts the first byte of every message you parse.
'
' Nonce construction (§5.3): the 64-bit sequence number is XORed into the low
' bytes of the static IV. Sequence numbers start at zero for each direction at
' each key change and are never reused, which is what keeps GCM safe here.

Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>One AEAD-protected TLS 1.3 record layer, for one direction.</summary>
    Public NotInheritable Class TlsRecordLayer

        ''' <summary>The real content type of an opened record.</summary>
        Public NotInheritable Class OpenedRecord

            Public Sub New(contentType As ContentType, payload As Byte())
                Me.ContentType = contentType
                Me.Payload = payload
            End Sub

            Public ReadOnly ContentType As ContentType
            Public ReadOnly Payload As Byte()
        End Class

        Private ReadOnly _key As Byte()
        Private ReadOnly _iv As Byte()
        Private _sequenceNumber As ULong

        Public Sub New(key As Byte(), iv As Byte())
            If key Is Nothing OrElse key.Length <> AesGcm.Tls13KeySize Then
                Throw New ArgumentException("TLS 1.3 AES-128-GCM key must be 16 bytes", "key")
            End If
            If iv Is Nothing OrElse iv.Length <> AesGcm.NonceSize Then
                Throw New ArgumentException("TLS 1.3 nonce base must be 12 bytes", "iv")
            End If
            _key = key
            _iv = iv
        End Sub

        ''' <summary>
        ''' The per-record nonce: IV with the sequence number XORed into its last
        ''' eight bytes, big-endian. Not a counter, and not the IV itself — every
        ''' record must use a distinct nonce under the same key.
        ''' </summary>
        Private Function BuildNonce() As Byte()
            Dim nonce(AesGcm.NonceSize - 1) As Byte
            Array.Copy(_iv, nonce, AesGcm.NonceSize)

            Dim sequence = _sequenceNumber
            Dim i As Integer = AesGcm.NonceSize - 1
            While i >= 0 AndAlso sequence <> 0UL
                nonce(i) = CByte(nonce(i) Xor CByte(sequence And &HFFUL))
                sequence >>= 8
                i -= 1
            End While
            Return nonce
        End Function

        ''' <summary>
        ''' Seals one record. Returns the complete wire record: the 5-byte header
        ''' (type 23, legacy version, length) followed by ciphertext and tag.
        ''' </summary>
        Public Function Seal(contentType As ContentType, payload As Byte()) As Byte()
            Dim content = If(payload, New Byte() {})

            ' content || type  — and NOT type || content. See the file header.
            Dim inner(content.Length) As Byte
            Array.Copy(content, inner, content.Length)
            inner(content.Length) = CByte(contentType)

            Dim cipherLength = inner.Length + AesGcm.TagSize
            If cipherLength > TlsLimits.MaxPlaintextLength + TlsLimits.MaxCiphertextOverhead Then
                Throw New TlsProtocolException("record exceeds the maximum permitted size")
            End If

            ' The AAD is the record header with the FINAL (ciphertext) length, which
            ' is why the header has to be built before sealing.
            Dim header As Byte() = {
                CByte(ContentType.ApplicationData),
                CByte((TlsLimits.LegacyVersion >> 8) And &HFF),
                CByte(TlsLimits.LegacyVersion And &HFF),
                CByte((cipherLength >> 8) And &HFF),
                CByte(cipherLength And &HFF)}

            Dim sealed = AesGcm.Seal(_key, BuildNonce(), header, inner)
            _sequenceNumber += 1UL

            Dim record(header.Length + sealed.Ciphertext.Length + sealed.Tag.Length - 1) As Byte
            Array.Copy(header, 0, record, 0, header.Length)
            Array.Copy(sealed.Ciphertext, 0, record, header.Length, sealed.Ciphertext.Length)
            Array.Copy(sealed.Tag, 0, record, header.Length + sealed.Ciphertext.Length, sealed.Tag.Length)
            Return record
        End Function

        ''' <summary>
        ''' Opens one record given its header and body. Raises on any AEAD tag
        ''' mismatch — a forged record must never reach the caller.
        ''' </summary>
        Public Function Open(header As Byte(), body As Byte()) As OpenedRecord
            If header Is Nothing OrElse header.Length <> 5 Then
                Throw New TlsProtocolException("record header must be 5 bytes")
            End If
            If body Is Nothing OrElse body.Length < AesGcm.TagSize Then
                Throw New TlsProtocolException("record body is shorter than the AEAD tag")
            End If

            Dim cipherLength = body.Length - AesGcm.TagSize
            Dim ciphertext(cipherLength - 1) As Byte
            Array.Copy(body, ciphertext, cipherLength)
            Dim tag(AesGcm.TagSize - 1) As Byte
            Array.Copy(body, cipherLength, tag, 0, AesGcm.TagSize)

            Dim inner As Byte()
            Try
                inner = AesGcm.Open(_key, BuildNonce(), header, ciphertext, tag)
            Catch ex As Exception
                ' Fail closed. The sequence number is deliberately NOT advanced on
                ' failure: a forged record ends the connection rather than
                ' desynchronising the nonce stream.
                Throw New TlsProtocolException(
                    "record failed authentication (bad key, replay, or tampering)", ex)
            End Try

            _sequenceNumber += 1UL

            ' Padding is zeros at the END; the content type sits immediately before
            ' it and may itself legitimately be non-zero.
            Dim endIndex = inner.Length
            While endIndex > 0 AndAlso inner(endIndex - 1) = 0
                endIndex -= 1
            End While
            If endIndex = 0 Then
                Throw New TlsProtocolException("inner plaintext was entirely padding")
            End If

            Dim contentType = CType(inner(endIndex - 1), ContentType)
            If contentType <> ContentType.Handshake AndAlso
               contentType <> ContentType.Alert AndAlso
               contentType <> ContentType.ApplicationData Then
                Throw New TlsProtocolException(
                    "inner plaintext declares invalid content type " & CInt(contentType))
            End If

            Dim content(endIndex - 2) As Byte
            Array.Copy(inner, 0, content, 0, endIndex - 1)
            Return New OpenedRecord(contentType, content)
        End Function
    End Class

End Namespace
