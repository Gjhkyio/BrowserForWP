' BrowserForWP — parsing of the server's half of the handshake.
'
' Transliteration of the parse* helpers in tools/proto/tls13.mjs, validated
' against live servers.
'
' Note where each message actually lives, because getting this wrong fails
' silently rather than loudly:
'
'   ServerHello         plaintext, the last unencrypted message we ever receive
'   EncryptedExtensions encrypted. Carries ALPN (RFC 8446 §4.3.1) — NOT
'                       ServerHello. A prototype that read ALPN from ServerHello
'                       reported "no ALPN" for every server and looked like it
'                       worked.
'   Certificate         encrypted
'   CertificateVerify   encrypted
'   Finished            encrypted

Imports System.Collections.Generic
Imports System.Text

Namespace Tls13

    ''' <summary>Parsed ServerHello (RFC 8446 §4.1.3).</summary>
    Public NotInheritable Class ServerHelloInfo

        Public Property LegacyVersion As Integer
        Public Property Random As Byte()
        Public Property LegacySessionId As Byte()
        Public Property CipherSuite As Integer
        Public Property SelectedVersion As Integer
        Public Property KeyShareGroup As Integer
        Public Property KeyShare As Byte()

        ''' <summary>
        ''' True when the server sent a HelloRetryRequest. RFC 8446 §4.1.4: an HRR
        ''' is a ServerHello whose random is the SHA-256 of "HelloRetryRequest".
        ''' BrowserForWP does not implement the retry, so it must at least detect it
        ''' and fail clearly instead of deriving keys from a meaningless share.
        ''' </summary>
        Public ReadOnly Property IsHelloRetryRequest As Boolean
            Get
                Return Random IsNot Nothing AndAlso
                       Random.Length = TlsLimits.RandomLength AndAlso
                       ToHex(Random) = TlsLimits.HelloRetryRequestRandomHex
            End Get
        End Property

        Friend Shared Function ToHex(bytes As Byte()) As String
            If bytes Is Nothing Then Return String.Empty
            Dim builder As New StringBuilder(bytes.Length * 2)
            For Each b In bytes
                builder.Append(b.ToString("X2"))
            Next
            Return builder.ToString()
        End Function
    End Class

    ''' <summary>Parsed CertificateVerify (RFC 8446 §4.4.3).</summary>
    '''
    ''' Declared at namespace level, NOT nested inside ServerMessageParser.
    ''' ServerHelloInfo already lives here, and
    ''' CertificateValidator.VerifyCertificateVerify names this type in its own
    ''' parameter list. While it was nested, that reference resolved to nothing
    ''' and reported the misleading BC30002 "Type 'CertificateVerifyInfo' is not
    ''' defined" — in a different file, with no mention of the nesting.
    Public NotInheritable Class CertificateVerifyInfo

        Public Property Scheme As Integer
        Public Property Signature As Byte()
    End Class

    ''' <summary>Parses the server's handshake messages.</summary>
    Public NotInheritable Class ServerMessageParser

        Private Sub New()
        End Sub

        ''' <summary>Parses a ServerHello body (the bytes after the 4-byte header).</summary>
        Public Shared Function ParseServerHello(body As Byte()) As ServerHelloInfo
            Dim reader = New TlsReader(body)
            Dim info As New ServerHelloInfo()
            info.LegacyVersion = reader.ReadU16()
            info.Random = reader.ReadBytes(TlsLimits.RandomLength)
            info.LegacySessionId = reader.ReadVec8()
            info.CipherSuite = reader.ReadU16()
            Dim legacyCompression = reader.ReadU8()
            If legacyCompression <> 0 Then
                Throw New TlsProtocolException("server selected a compression method; TLS 1.3 forbids this")
            End If

            Dim extensions = reader.SubReaderVec16()
            While extensions.Remaining > 0
                ' NOT `extensionType`: VB is case-insensitive, so a local named
                ' extensionType SHADOWS the ExtensionType enum, and every
                ' CInt(ExtensionType.X) below is then reported as "'X' is not a
                ' member of 'Integer'" — which points at the enum, not at the
                ' local that caused it. Same trap as supported/Supported,
                ' tag/Tag and Default/DefaultTag.
                Dim extType = extensions.ReadU16()
                Dim extensionBody = extensions.ReadVec16()

                Select Case extType
                    Case CInt(ExtensionType.SupportedVersions)
                        info.SelectedVersion = New TlsReader(extensionBody).ReadU16()

                    Case CInt(ExtensionType.KeyShare)
                        Dim keyShare = New TlsReader(extensionBody)
                        info.KeyShareGroup = keyShare.ReadU16()
                        info.KeyShare = keyShare.ReadVec16()

                    Case CInt(ExtensionType.ServerName)

                    Case Else
                        ' Unknown or irrelevant extensions are skipped rather than
                        ' rejected: RFC 8446 §4.2 requires ignoring what we do not
                        ' understand, and a client that errors out here would break
                        ' on every server that sends something new.

                End Select
            End While

            Return info
        End Function

    ''' <summary>Parsed EncryptedExtensions (RFC 8446 §4.3.1).</summary>
    Public NotInheritable Class EncryptedExtensionsInfo

            ''' <summary>The negotiated ALPN protocol, or Nothing when none was agreed.</summary>
            Public Property Alpn As String
        End Class

        Public Shared Function ParseEncryptedExtensions(body As Byte()) As EncryptedExtensionsInfo
            Dim extensions = New TlsReader(body).SubReaderVec16()
            Dim info As New EncryptedExtensionsInfo()

            While extensions.Remaining > 0
                Dim extType = extensions.ReadU16()
                Dim extensionBody = extensions.ReadVec16()

                If extType = CInt(ExtensionType.Alpn) Then
                    ' ProtocolNameList = uint16 list_length, ProtocolName*
                    ' ProtocolName     = uint8 name_length, bytes
                    Dim list = New TlsReader(extensionBody).ReadVec16()
                    Dim first = New TlsReader(list).ReadVec8()
                    info.Alpn = Encoding.UTF8.GetString(first, 0, first.Length)
                End If
            End While

            Return info
        End Function

        ''' <summary>
        ''' Parses a Certificate message (§4.4.2). Returns the DER of every
        ''' certificate in the chain, leaf first.
        ''' </summary>
        Public Shared Function ParseCertificate(body As Byte()) As IList(Of Byte())
            Dim reader = New TlsReader(body)
            Dim context = reader.ReadVec8()
            If context.Length <> 0 Then
                Throw New TlsProtocolException(
                    "Certificate has a non-empty request context; only the server's " &
                    "first Certificate message is supported")
            End If

            ' List(Of Byte()): one entry per certificate, each entry the whole DER.
            ' List(Of Byte) would not accept a DER here (BC30512 on Add and again
            ' on Return).
            Dim certificates As New List(Of Byte())()
            Dim list = reader.SubReaderVec24()
            While list.Remaining > 0
                ' CertificateEntry = opaque cert_data<1..2^24-1>, Extension*
                Dim der = list.ReadVec24()
                Dim extensionCount = list.ReadVec16()     ' per-cert extensions, ignored
                If der.Length = 0 Then
                    Throw New TlsProtocolException("empty certificate entry")
                End If
                certificates.Add(der)
            End While

            If certificates.Count = 0 Then
                Throw New TlsProtocolException("server sent an empty certificate chain")
            End If
            Return certificates
        End Function

        Public Shared Function ParseCertificateVerify(body As Byte()) As CertificateVerifyInfo
            Dim reader = New TlsReader(body)
            Dim info As New CertificateVerifyInfo()
            info.Scheme = reader.ReadU16()
            info.Signature = reader.ReadVec16()
            Return info
        End Function

        ''' <summary>Parsed Finished (RFC 8446 §4.4.4).</summary>
        Public Shared Function ParseFinished(body As Byte()) As Byte()
            If body Is Nothing OrElse body.Length = 0 Then
                Throw New TlsProtocolException("empty Finished")
            End If
            Return body
        End Function
    End Class

End Namespace
