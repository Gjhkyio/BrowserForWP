' BrowserForWP — ClientHello construction (RFC 8446 §4.1.2).
'
' Transliteration of buildClientHello in tools/proto/tls13.mjs, whose output a
' real server accepts.
'
' ── Two length bugs this file exists to prevent ─────────────────────────────
' Both were hit for real while building the prototype, and both produced the
' same server reaction: a plaintext `decode_error` alert, with no indication of
' which extension was wrong.
'
'   1. server_name. Its body is
'          ServerNameList = uint16 list_length, ServerName*
'          ServerName     = NameType(1 byte) | HostName
'      The leading NameType byte is required EVEN THOUGH 0 (host_name) is the
'      only value ever defined. Omitting it shifts every following byte by one.
'
'   2. key_share. KeyShareEntry is
'          NamedGroup(2) | opaque key_exchange<1..2^16-1>
'      so the public key carries its own 2-byte length. Writing the group
'      followed by a bare 32-byte key is a decode_error.
'
' Both were found only by talking to a real server. Bear that in mind before
' assuming a byte-layout change is harmless: the local checks cannot catch it.

Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>Builds the ClientHello and wraps it in its record.</summary>
    Public NotInheritable Class ClientHelloBuilder

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Builds a complete handshake record containing one ClientHello.
        ''' </summary>
        ''' <param name="hostName">SNI value; also used for certificate checks.</param>
        ''' <param name="x25519PublicKey">Our ephemeral x25519 public key, 32 bytes.</param>
        ''' <param name="random">32 random bytes; caller-supplied so tests are deterministic.</param>
        ''' <param name="legacySessionId">
        ''' 32 random bytes. A non-empty session id puts us in "compatibility mode"
        ''' (RFC 8446 §4.1.2), which is what lets TLS 1.2-only middleboxes pass the
        ''' handshake through instead of stalling it. The caller must also send a
        ''' dummy ChangeCipherSpec for that to take effect.
        ''' </param>
        Public Shared Function BuildRecord(hostName As String, x25519PublicKey As Byte(),
                                           random As Byte(), legacySessionId As Byte()) As Byte()
            Dim handshake = BuildHandshake(hostName, x25519PublicKey, random, legacySessionId)

            ' The outer record's legacy_record_version is 0x0301 (=0x0303 also
            ' works). The version that actually matters, 0x0304, appears ONLY
            ' inside supported_versions.
            Dim record As Byte() = New TlsWriter().
                U8(CInt(ContentType.Handshake)).
                U16(&H301).
                U16(handshake.Length).
                Bytes(handshake).
                ToArray()
            Return record
        End Function

        ''' <summary>
        ''' The bare handshake message, header included. Separate from
        ''' <see cref="BuildRecord"/> because the transcript hash is computed over
        ''' the handshake message, never over the record framing.
        ''' </summary>
        Public Shared Function BuildHandshake(hostName As String, x25519PublicKey As Byte(),
                                              random As Byte(), legacySessionId As Byte()) As Byte()
            If String.IsNullOrEmpty(hostName) Then Throw New ArgumentException("host required", "hostName")
            If x25519PublicKey Is Nothing OrElse x25519PublicKey.Length <> TlsLimits.X25519KeyLength Then
                Throw New ArgumentException("x25519 public key must be 32 bytes", "x25519PublicKey")
            End If
            If random Is Nothing OrElse random.Length <> TlsLimits.RandomLength Then
                Throw New ArgumentException("random must be 32 bytes", "random")
            End If
            If legacySessionId Is Nothing OrElse
               legacySessionId.Length <> TlsLimits.CompatSessionIdLength Then
                Throw New ArgumentException("legacy session id must be 32 bytes", "legacySessionId")
            End If

            Dim extensions As New List(Of Byte)()

            ' server_name
            Dim serverName As Byte() = New TlsWriter().
                U8(0).                                  ' NameType: host_name
                U16(System.Text.Encoding.UTF8.GetByteCount(hostName)).
                Ascii(hostName).
                ToArray()
            extensions.Add(New TlsWriter().
                U16(CInt(ExtensionType.ServerName)).
                Vec16(serverName).
                ToArray())

            ' supported_versions
            extensions.Add(New TlsWriter().
                U16(CInt(ExtensionType.SupportedVersions)).
                Vec8({CByte((TlsLimits.Tls13Version >> 8) And &HFF),
                      CByte(TlsLimits.Tls13Version And &HFF)}).
                ToArray())

            ' supported_groups
            extensions.Add(New TlsWriter().
                U16(CInt(ExtensionType.SupportedGroups)).
                Vec16({CByte((CInt(NamedGroup.X25519) >> 8) And &HFF),
                       CByte(CInt(NamedGroup.X25519) And &HFF)}).
                ToArray())

            ' signature_algorithms. rsa_pkcs1_sha256 is included for certificate
            ' chain signatures only; RFC 8446 §4.4.3 forbids it for
            ' CertificateVerify in TLS 1.3.
            extensions.Add(New TlsWriter().
                U16(CInt(ExtensionType.SignatureAlgorithms)).
                Vec16({CByte(&H4), CByte(&H3),      ' ecdsa_secp256r1_sha256
                       CByte(&H8), CByte(&H4),      ' rsa_pss_rsae_sha256
                       CByte(&H4), CByte(&H1)}).
                ToArray())

            ' ALPN: HTTP/1.1 only. RFC 7301 requires a client to be able to speak
            ' every protocol it offers, and BrowserForWP's HTTP client speaks
            ' HTTP/1.1. Offering "h2" makes servers select HTTP/2 and then reject
            ' our requests, which is a bug we hit in the prototype.
            Dim alpnProtocol As Byte() = System.Text.Encoding.UTF8.GetBytes("http/1.1")
            Dim alpnEntry As Byte() = New TlsWriter().U8(alpnProtocol.Length).
                                                  Bytes(alpnProtocol).ToArray()
            extensions.Add(New TlsWriter().
                U16(CInt(ExtensionType.Alpn)).
                Vec16(New TlsWriter().Vec16(alpnEntry).ToArray()).
                ToArray())

            ' key_share — the key carries its own 2-byte length. See file header.
            Dim keyShareEntry As Byte() = New TlsWriter().
                U16(CInt(NamedGroup.X25519)).
                U16(x25519PublicKey.Length).
                Bytes(x25519PublicKey).
                ToArray()
            extensions.Add(New TlsWriter().
                U16(CInt(ExtensionType.KeyShare)).
                Vec16(New TlsWriter().Vec16(keyShareEntry).ToArray()).
                ToArray())

            Dim extensionBlock As Byte() = New TlsWriter().Vec16(Flatten(extensions)).ToArray()

            Dim body As Byte() = New TlsWriter().
                U16(TlsLimits.LegacyVersion).           ' legacy_version
                Bytes(random).
                Vec8(legacySessionId).
                Vec16({CByte(&H13), CByte(&H1)}).       ' cipher_suites: TLS_AES_128_GCM_SHA256
                Vec8({CByte(0)}).                       ' legacy_compression_methods: null
                Bytes(extensionBlock).
                ToArray()

            Return New TlsWriter().
                U8(CInt(HandshakeType.ClientHello)).
                U24(body.Length).
                Bytes(body).
                ToArray()
        End Function

        ''' <summary>
        ''' The compatibility-mode ChangeCipherSpec: a single 0x01 byte of
        ''' payload. It is not encrypted and carries no key change in TLS 1.3;
        ''' it exists purely so middleboxes see the shape they expect.
        ''' </summary>
        Public Shared Function BuildCompatibilityChangeCipherSpec() As Byte()
            Return {CByte(ContentType.ChangeCipherSpec),
                    CByte((TlsLimits.LegacyVersion >> 8) And &HFF),
                    CByte(TlsLimits.LegacyVersion And &HFF),
                    0, 1, 1}
        End Function

        Private Shared Function Flatten(parts As List(Of Byte())) As Byte()
            Dim total = 0
            For Each part In parts
                total += part.Length
            Next
            Dim output(total - 1) As Byte
            Dim offset = 0
            For Each part In parts
                Array.Copy(part, 0, output, offset, part.Length)
                offset += part.Length
            Next
            Return output
        End Function
    End Class

End Namespace
