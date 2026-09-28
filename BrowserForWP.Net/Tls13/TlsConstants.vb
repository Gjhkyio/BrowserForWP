' BrowserForWP — TLS 1.3 constants (RFC 8446 Appendix B).
'
' Every numeric value here is normative. Do not "tidy" them.

Namespace Tls13

    ''' <summary>RFC 8446 §5. ContentType. Values are the wire values.</summary>
    Public Enum ContentType As Byte
        ChangeCipherSpec = 20
        Alert = 21
        Handshake = 22
        ApplicationData = 23
    End Enum

    ''' <summary>RFC 8446 §4. HandshakeType.</summary>
    Public Enum HandshakeType As Byte
        ClientHello = 1
        ServerHello = 2
        NewSessionTicket = 4
        EncryptedExtensions = 8
        Certificate = 11
        CertificateVerify = 15
        Finished = 20
    End Enum

    ''' <summary>RFC 8446 §6. AlertDescription.</summary>
    Public Enum AlertDescription As Byte
        CloseNotify = 0
        UnexpectedMessage = 10
        BadRecordMac = 20
        RecordOverflow = 22
        HandshakeFailure = 40
        IllegalParameter = 47
        UnknownCa = 48
        DecodeError = 50
        ProtocolVersion = 70
        InternalError = 80
        MissingExtension = 109
        UnsupportedExtension = 110
        UnrecognizedName = 112
        BadCertificate = 113
        CertificateRequired = 116
        NoApplicationProtocol = 120
    End Enum

    ''' <summary>ExtensionType. Only the ones we send or must recognise.</summary>
    Public Enum ExtensionType As Integer
        ServerName = 0
        SupportedGroups = 10
        SignatureAlgorithms = 13
        Alpn = 16
        SupportedVersions = 43
        KeyShare = 51
    End Enum

    ''' <summary>NamedGroup. x25519 is the only group we offer.</summary>
    Public Enum NamedGroup As Integer
        X25519 = &H1D
    End Enum

    ''' <summary>
    ''' RFC 8446 §B.4. BrowserForWP offers exactly ONE suite.
    '''
    ''' TLS_AES_128_GCM_SHA256 is mandatory-to-implement (RFC 8446 §9.1), so
    ''' offering only it costs nothing in interoperability, and it lets the
    ''' platform's accelerated AES-GCM serve every record. See AesGcm.vb.
    ''' </summary>
    Public Enum CipherSuite As Integer
        Aes128GcmSha256 = &H1301
    End Enum

    ''' <summary>SignatureScheme (RFC 8446 §4.2.3).</summary>
    Public Enum SignatureScheme As Integer
        EcdsaSecp256r1Sha256 = &H403
        EcdsaSecp384r1Sha384 = &H503
        RsaPssRsaeSha256 = &H804
        RsaPssRsaeSha384 = &H805
        RsaPkcs1Sha256 = &H401
    End Enum

    ''' <summary>Protocol-level constants that are easy to mistype.</summary>
    Public NotInheritable Class TlsLimits

        ''' <summary>legacy_version on every record and ClientHello (RFC 8446 §5.1).</summary>
        Public Const LegacyVersion As Integer = &H303

        ''' <summary>supported_versions value meaning TLS 1.3.</summary>
        Public Const Tls13Version As Integer = &H304

        ''' <summary>Random field length (§4.1.2).</summary>
        Public Const RandomLength As Integer = 32

        ''' <summary>legacy_session_id length that triggers compatibility mode (§4.1.2).</summary>
        Public Const CompatSessionIdLength As Integer = 32

        ''' <summary>TLS 1.3 record payload ceiling (§5.1: 2^14).</summary>
        Public Const MaxPlaintextLength As Integer = 16384

        ''' <summary>Extra room for the AEAD tag plus inner content type (§5.2).</summary>
        Public Const MaxCiphertextOverhead As Integer = 256

        ''' <summary>The x25519 shared secret is 32 bytes.</summary>
        Public Const X25519KeyLength As Integer = 32

        ''' <summary>
        ''' RFC 8446 §4.4.3. The 64 spaces and the trailing zero are part of the
        ''' signed content, not decoration.
        ''' </summary>
        Public Const CertVerifyContext As String = "TLS 1.3, server CertificateVerify"
        Public Const CertVerifySpaces As Integer = 64

        ''' <summary>
        ''' RFC 8446 §4.2.11/§4.3.1. The HelloRetryRequest magic value is the SHA-256
        ''' of "HelloRetryRequest", sent in place of ServerHello.random.
        ''' </summary>
        Public Const HelloRetryRequestRandomHex As String =
            "CF21AD74E59A6111BE1D8C021E65B891C2A211167ABB8C5E079E09E2C8A8339C"

        Private Sub New()
        End Sub
    End Class

End Namespace
