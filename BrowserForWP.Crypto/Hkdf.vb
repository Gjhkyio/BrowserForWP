' BrowserForWP — HKDF (RFC 5869) and the TLS 1.3 label framing (RFC 8446 §7.1).
'
' Verified by tools/gen-vectors.mjs against RFC 5869 Appendix A.1-A.3 and by the
' info-dump assertions taken from RFC 8448 §3.

Imports System.Security.Cryptography
Imports System.Text

Namespace Crypto

    ''' <summary>
    ''' HMAC-based Extract-and-Expand Key Derivation Function (RFC 5869), plus the
    ''' TLS 1.3 HkdfLabel framing from RFC 8446 §7.1.
    '''
    ''' SHA-256 only: BrowserForWP negotiates TLS_AES_128_GCM_SHA256 and
    ''' TLS_CHACHA20_POLY1305_SHA256, which both use SHA-256, so the SHA-384
    ''' variants of the key schedule are deliberately not implemented (YAGNI).
    ''' </summary>
    Public NotInheritable Class Hkdf

        ''' <summary>SHA-256 output size, and therefore the length of every PRK.</summary>
        Public Const HashLength As Integer = 32

        Private Sub New()
        End Sub

        ''' <summary>
        ''' RFC 5869 §2.2. Note the specification's quirk, which is easy to get
        ''' wrong and is asserted by the RFC 5869 A.3 test: an *empty* salt is
        ''' replaced by HashLen zero octets, not by no salt at all.
        ''' </summary>
        Public Shared Function Extract(salt As Byte(), ikm As Byte()) As Byte()
            If salt Is Nothing OrElse salt.Length = 0 Then salt = New Byte(HashLength - 1) {}
            Return Mac(salt, If(ikm, New Byte() {}))
        End Function

        ''' <summary>RFC 5869 §2.3. T(0) = empty, T(n) = HMAC(PRK, T(n-1) || info || n).</summary>
        Public Shared Function Expand(prk As Byte(), info As Byte(), length As Integer) As Byte()
            If length < 0 Then Throw New ArgumentOutOfRangeException("length")
            If prk Is Nothing OrElse prk.Length = 0 Then Throw New ArgumentException("prk required", "prk")

            Dim blocks = CInt(Math.Ceiling(length / CDbl(HashLength)))
            If blocks > 255 Then Throw New ArgumentOutOfRangeException("length", "HKDF output exceeds 255*HashLen")

            Dim output(length - 1) As Byte
            Dim previous As Byte() = New Byte() {}
            Dim written As Integer = 0

            For i As Integer = 1 To blocks
                Dim buffer(previous.Length + If(info, New Byte() {}).Length) As Byte
                Array.Copy(previous, buffer, previous.Length)
                If info IsNot Nothing Then Array.Copy(info, 0, buffer, previous.Length, info.Length)
                buffer(buffer.Length - 1) = CByte(i)

                previous = Mac(prk, buffer)
                Dim take = Math.Min(HashLength, length - written)
                Array.Copy(previous, 0, output, written, take)
                written += take
            Next

            Return output
        End Function

        ''' <summary>
        ''' The HkdfLabel structure (RFC 8446 §7.1):
        '''   struct {
        '''       uint16 length;
        '''       opaque label&lt;7..255&gt;;   // prefixed with the literal "tls13 "
        '''       opaque context&lt;0..255&gt;;
        '''   } HkdfLabel;
        '''
        ''' Exposed separately because the byte layout is asserted directly against
        ''' the `info` dumps printed in RFC 8448 §3 — that pins the wire format
        ''' independently of any derived value.
        ''' </summary>
        Public Shared Function BuildLabelInfo(label As String, context As Byte(), length As Integer) As Byte()
            Dim full = Encoding.UTF8.GetBytes("tls13 " & If(label, String.Empty))
            If full.Length > 255 Then Throw New ArgumentException("label too long", "label")
            Dim ctx = If(context, New Byte() {})
            If ctx.Length > 255 Then Throw New ArgumentException("context too long", "context")

            Dim info(full.Length + ctx.Length + 3) As Byte
            info(0) = CByte((length >> 8) And &HFF)
            info(1) = CByte(length And &HFF)
            info(2) = CByte(full.Length)
            Array.Copy(full, 0, info, 3, full.Length)
            info(3 + full.Length) = CByte(ctx.Length)
            Array.Copy(ctx, 0, info, 4 + full.Length, ctx.Length)
            Return info
        End Function

        ''' <summary>HKDF-Expand-Label (RFC 8446 §7.1).</summary>
        Public Shared Function ExpandLabel(secret As Byte(), label As String,
                                          context As Byte(), length As Integer) As Byte()
            Return Expand(secret, BuildLabelInfo(label, context, length), length)
        End Function

        ''' <summary>
        ''' Derive-Secret(Secret, Label, Messages) =
        '''     HKDF-Expand-Label(Secret, Label, Transcript-Hash(Messages), Hash.length)
        '''
        ''' Passing an empty transcript yields the hash of the empty string, which is
        ''' exactly what the "derived" labels in the key schedule need.
        ''' </summary>
        Public Shared Function DeriveSecret(secret As Byte(), label As String, transcript As Byte()) As Byte()
            Return ExpandLabel(secret, label, Sha256(transcript), HashLength)
        End Function

        Public Shared Function Sha256(data As Byte()) As Byte()
            Using sha = New SHA256Managed()
                Return sha.ComputeHash(If(data, New Byte() {}))
            End Using
        End Function

        ''' <summary>Constant-time comparison, so tag checks do not leak via timing.</summary>
        Public Shared Function FixedTimeEquals(a As Byte(), b As Byte()) As Boolean
            If a Is Nothing OrElse b Is Nothing Then Return False
            If a.Length <> b.Length Then Return False
            Dim diff As Integer = 0
            For i As Integer = 0 To a.Length - 1
                diff = diff Or (CInt(a(i)) Xor CInt(b(i)))
            Next
            Return diff = 0
        End Function

        Private Shared Function Mac(key As Byte(), data As Byte()) As Byte()
            Using h = New HMACSHA256(key)
                Return h.ComputeHash(data)
            End Using
        End Function
    End Class

End Namespace
