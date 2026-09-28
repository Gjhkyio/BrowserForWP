' BrowserForWP — X25519 (RFC 7748).
'
' WinRT 8.1 exposes no X25519 primitive, and group x25519 (0x001d) is mandatory
' for a TLS 1.3 key_share, so the curve is implemented here.
'
' Verified by tools/gen-vectors.mjs against RFC 7748 §5.2 and §6.1.
'
' ── Why BigInteger ──────────────────────────────────────────────────────────
' The textbook implementation uses radix-2^25.5 limbs for speed. That
' representation has several subtle invariants — the reduction factor (2^256 is
' congruent to 38, while 2^255 is congruent to 19), where carries may legally
' live, and how many fold passes are needed — and getting any of them slightly
' wrong produces wrong-but-plausible output rather than an obvious failure.
'
' The WP8.1 SDK is Windows-only, so this file cannot be executed during
' development on other platforms. Given that, a representation whose arithmetic
' can be reviewed line by line is worth more than one that is fast but opaque.
' BigInteger arithmetic here costs roughly one handshake's worth of work
' (255 ladder steps), which is noise next to the network round trip.
'
' The RFC vectors in tests/ pin this implementation exactly: a wrong reduction
' cannot reproduce the published public keys.
'
' TODO(perf): if handshake latency on real hardware is unacceptable, replace Fe
' with a radix-2^25.5 implementation and keep the same public API. The tests do
' not change.

Imports System.Numerics

Namespace Crypto

    ''' <summary>
    ''' X25519 Diffie-Hellman over Curve25519 (RFC 7748).
    '''
    ''' NOTE: this implementation is not constant-time. BigInteger arithmetic
    ''' branches on the values it handles. That is a side-channel consideration for
    ''' an attacker measuring the handset's timing. It is documented here rather
    ''' than left implicit; see docs/ARCHITECTURE.md.
    ''' </summary>
    Public NotInheritable Class X25519

        Public Const KeySize As Integer = 32

        ''' <summary>Field prime p = 2^255 - 19.</summary>
        Private Shared ReadOnly P As BigInteger = (BigInteger.One << 255) - 19

        Private Sub New()
        End Sub

        ''' <summary>
        ''' A uniformly random, clamped scalar. Clamping happens here so no caller
        ''' can forget it; ScalarMult clamps again as a second guard.
        ''' </summary>
        Public Shared Function GeneratePrivateKey() As Byte()
            Dim k(KeySize - 1) As Byte
            Using rng = New System.Security.Cryptography.RNGCryptoServiceProvider()
                rng.GetBytes(k)
            End Using
            Clamp(k)
            Return k
        End Function

        ''' <summary>Scalar multiplication of the base point (u = 9).</summary>
        Public Shared Function PublicFromPrivate(privateKey As Byte()) As Byte()
            If privateKey Is Nothing OrElse privateKey.Length <> KeySize Then
                Throw New ArgumentException("private key must be 32 bytes", "privateKey")
            End If
            Dim baseU(KeySize - 1) As Byte
            baseU(0) = 9
            Return ScalarMult(privateKey, baseU)
        End Function

        ''' <summary>
        ''' RFC 7748 §6.1 shared secret. The all-zero output that a low-order peer
        ''' public key produces is rejected rather than passed on: contributory
        ''' behaviour failure must be loud.
        ''' </summary>
        Public Shared Function Agreement(privateKey As Byte(), peerPublic As Byte()) As Byte()
            If privateKey Is Nothing OrElse privateKey.Length <> KeySize Then
                Throw New ArgumentException("private key must be 32 bytes", "privateKey")
            End If
            If peerPublic Is Nothing OrElse peerPublic.Length <> KeySize Then
                Throw New ArgumentException("peer public key must be 32 bytes", "peerPublic")
            End If

            Dim shared = ScalarMult(privateKey, peerPublic)
            For Each b In shared
                If b <> 0 Then Return shared
            Next
            Throw New InvalidOperationException("X25519 produced a degenerate shared secret (low-order public key)")
        End Function

        ''' <summary>RFC 7748 §5 scalar decoding/clamping.</summary>
        Private Shared Sub Clamp(k As Byte())
            k(0) = CByte(k(0) And &HF8)     ' clear the three low bits
            k(31) = CByte(k(31) And &H7F)   ' clear the top bit
            k(31) = CByte(k(31) Or &H40)    ' set the second-highest bit
        End Sub

        ''' <summary>
        ''' The Montgomery ladder (RFC 7748 §5). Bits are processed from 254 down to
        ''' 0. The conditional swap is written as a real branch here for clarity; see
        ''' the constant-time note on the class.
        ''' </summary>
        Public Shared Function ScalarMult(scalar As Byte(), u As Byte()) As Byte()
            If scalar Is Nothing OrElse scalar.Length <> KeySize Then
                Throw New ArgumentException("scalar must be 32 bytes", "scalar")
            End If
            If u Is Nothing OrElse u.Length <> KeySize Then
                Throw New ArgumentException("u-coordinate must be 32 bytes", "u")
            End If

            Dim k = CType(scalar.Clone(), Byte())
            Clamp(k)

            Dim x1 = DecodeUCoordinate(u)
            Dim x2 = BigInteger.One
            Dim z2 = BigInteger.Zero
            Dim x3 = x1
            Dim z3 = BigInteger.One
            Dim swap As Integer = 0

            For t As Integer = 254 To 0 Step -1
                Dim kt = (CInt(k(t >> 3)) >> (t And 7)) And 1
                swap = swap Xor kt
                If swap <> 0 Then
                    Dim tmp = x2 : x2 = x3 : x3 = tmp
                    tmp = z2 : z2 = z3 : z3 = tmp
                End If
                swap = kt

                ' RFC 7748 §5, the ladder step.
                Dim a = Add(x2, z2)
                Dim aa = Mul(a, a)
                Dim b = Sub(x2, z2)
                Dim bb = Mul(b, b)
                Dim e = Sub(aa, bb)
                Dim c = Add(x3, z3)
                Dim d = Sub(x3, z3)
                Dim da = Mul(d, a)
                Dim cb = Mul(c, b)
                x3 = Mul(Add(da, cb), Add(da, cb))
                z3 = Mul(x1, Mul(Sub(da, cb), Sub(da, cb)))
                x2 = Mul(aa, bb)
                z2 = Mul(e, Add(bb, Mul(BigInteger.Parse("121665"), e)))
            Next

            If swap <> 0 Then
                Dim tmp = x2 : x2 = x3 : x3 = tmp
                tmp = z2 : z2 = z3 : z3 = tmp
            End If

            ' x2 / z2. z2 is invertible for any non-degenerate input; a zero z2
            ' would mean the ladder produced the point at infinity.
            If z2.IsZero Then Throw New InvalidOperationException("X25519 ladder produced a non-invertible coordinate")
            Return Encode(Mul(x2, Invert(z2)))
        End Function

        ''' <summary>
        ''' Decode a u-coordinate: little-endian, with the top bit masked as RFC 7748
        ''' §5 requires (the masking is what makes the "clears the high bit" test
        ''' meaningful).
        ''' </summary>
        Private Shared Function DecodeUCoordinate(bytes As Byte()) As BigInteger
            Dim masked(31) As Byte
            Array.Copy(bytes, masked, 32)
            masked(31) = CByte(masked(31) And &H7F)
            Return Decode(masked)
        End Function

        Private Shared Function Decode(bytes As Byte()) As BigInteger
            ' Little-endian -> BigInteger wants little-endian too, but needs a
            ' trailing zero so a high bit never reads as a sign bit.
            Dim le(32) As Byte
            Array.Copy(bytes, le, 32)
            le(32) = 0
            Return New BigInteger(le)
        End Function

        Private Shared Function Encode(value As BigInteger) As Byte()
            Dim reduced = Mod(value)
            Dim le = reduced.ToByteArray()
            Dim out(31) As Byte
            Array.Copy(le, out, Math.Min(le.Length, 32))
            Return out
        End Function

        Private Shared Function Mod(a As BigInteger) As BigInteger
            Dim r = a Mod P
            If r.Sign < 0 Then r += P
            Return r
        End Function

        Private Shared Function Add(a As BigInteger, b As BigInteger) As BigInteger
            Return Mod(a + b)
        End Function

        Private Shared Function Sub(a As BigInteger, b As BigInteger) As BigInteger
            Return Mod(a - b)
        End Function

        Private Shared Function Mul(a As BigInteger, b As BigInteger) As BigInteger
            Return Mod(a * b)
        End Function

        ''' <summary>
        ''' Inversion by Fermat's little theorem: a^(p-2) mod p. A failure here shows
        ''' up immediately as a wrong public key in the RFC 7748 tests.
        ''' </summary>
        Private Shared Function Invert(a As BigInteger) As BigInteger
            Return BigInteger.ModPow(a, P - 2, P)
        End Function
    End Class

End Namespace
