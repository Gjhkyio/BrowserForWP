' BrowserForWP — TLS probe result (pure, no Net dependency).
'
' Core must not reference Net (layer discipline), so the probe execution lives
' in the app layer while the report shape lives here for testability.

Namespace Diagnostics

    ''' <summary>Outcome of a TLS probe against one host.</summary>
    Public NotInheritable Class TlsProbeResult

        Public Sub New(hostName As String, negotiatedVersion As String, cipherSuite As String, alpn As String, certificateValid As Boolean, detail As String)
            Me.HostName = If(hostName, String.Empty)
            Me.NegotiatedVersion = If(negotiatedVersion, String.Empty)
            Me.CipherSuite = If(cipherSuite, String.Empty)
            Me.Alpn = If(alpn, String.Empty)
            Me.CertificateValid = certificateValid
            Me.Detail = If(detail, String.Empty)
        End Sub

        Public ReadOnly HostName As String
        Public ReadOnly NegotiatedVersion As String
        Public ReadOnly CipherSuite As String
        Public ReadOnly Alpn As String
        Public ReadOnly CertificateValid As Boolean
        Public ReadOnly Detail As String

        Public ReadOnly Property IsTls13 As Boolean
            Get
                Return NegotiatedVersion = "TLS1.3"
            End Get
        End Property

        Public Overrides Function ToString() As String
            Dim certText As String = If(CertificateValid, "cert OK", "cert FAIL")
            Return HostName & ": " & NegotiatedVersion & " " & CipherSuite & " ALPN=" & Alpn & " " & certText & " " & Detail
        End Function
    End Class

End Namespace
