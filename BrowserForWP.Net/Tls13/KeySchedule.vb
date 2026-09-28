' BrowserForWP — TLS 1.3 key schedule (RFC 8446 §7.1).
'
' Verified by tools/proto/tls13.mjs, which reproduces the RFC 8448 §3 schedule
' and, more importantly, completes a live handshake against a real server. That
' second check is what proves these derivations produce the keys a server
' actually used — a wrong label or a missing stage fails the AEAD tag, not a
' self-consistency test.
'
'   RFC 8446 §7.1:
'
'             0
'             |
'             v
'       PSK ->  HKDF-Extract = Early Secret
'             |
'             +-----> Derive-Secret(., "ext binder" | "res binder", "")
'             |                     = binder_key
'             v
'       Derive-Secret(., "derived", "")
'             |
'             v
'   (EC)DHE -> HKDF-Extract = Handshake Secret
'             |
'             +-----> Derive-Secret(., "c hs traffic", ClientHello...ServerHello)
'             +-----> Derive-Secret(., "s hs traffic", ClientHello...ServerHello)
'             v
'       Derive-Secret(., "derived", "")
'             |
'             v
'        0 -> HKDF-Extract = Master Secret
'             |
'             +-----> Derive-Secret(., "c ap traffic", ClientHello...server Finished)
'             +-----> Derive-Secret(., "s ap traffic", ClientHello...server Finished)
'             +-----> Derive-Secret(., "exp master", ...)
'             +-----> Derive-Secret(., "res master", ...)

Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>
    ''' The per-direction key material for one encryption level: the AEAD key, the
    ''' nonce base, and the HMAC key used to compute Finished verify_data.
    ''' </summary>
    Public NotInheritable Class TrafficKeys

        Public Sub New(key As Byte(), iv As Byte(), finishedKey As Byte())
            Me.Key = key
            Me.Iv = iv
            Me.FinishedKey = finishedKey
        End Sub

        ''' <summary>AEAD key: HKDF-Expand-Label(secret, "key", "", key_length).</summary>
        Public ReadOnly Key As Byte()

        ''' <summary>Nonce base: HKDF-Expand-Label(secret, "iv", "", iv_length).</summary>
        Public ReadOnly Iv As Byte()

        ''' <summary>HKDF-Expand-Label(secret, "finished", "", Hash.length).</summary>
        Public ReadOnly FinishedKey As Byte()
    End Class

    ''' <summary>
    ''' The TLS 1.3 key schedule, SHA-256 only.
    '''
    ''' SHA-256 only because BrowserForWP offers exactly one cipher suite,
    ''' TLS_AES_128_GCM_SHA256 (see CipherSuite). YAGNI: the SHA-384 schedule
    ''' would be unreachable code.
    ''' </summary>
    Public NotInheritable Class KeySchedule

        Private Const HashLength As Integer = Hkdf.HashLength
        Private Const AeadKeyLength As Integer = AesGcm.Tls13KeySize
        Private Const IvLength As Integer = AesGcm.NonceSize

        ''' <summary>
        ''' Hash(""). Needed as the transcript for every "derived" label, which
        ''' derives from the empty transcript rather than from earlier messages.
        ''' </summary>
        Private Shared ReadOnly EmptyTranscriptHash As Byte() = Hkdf.Sha256(New Byte() {})

        ''' <summary>No PSK is used, so the early secret comes from a zero IKM.</summary>
        Private Shared ReadOnly ZeroPsk As Byte() = New Byte(HashLength - 1) {}

        Private _earlySecret As Byte()
        Private _handshakeSecret As Byte()
        Private _masterSecret As Byte()

        ''' <summary>
        ''' Step 1: Early Secret. RFC 8446 §7.1 defines it as
        ''' HKDF-Extract(0, PSK); with no PSK configured the IKM is HashLen zeros.
        ''' </summary>
        Public Sub Start()
            _earlySecret = Hkdf.Extract(ZeroPsk, ZeroPsk)
        End Sub

        Public ReadOnly Property EarlySecret As Byte()
            Get
                Return _earlySecret
            End Get
        End Property

        ''' <summary>
        ''' Step 2: incorporate the (EC)DHE shared secret.
        ''' </summary>
        Public Sub AddSharedSecret(sharedSecret As Byte())
            If sharedSecret Is Nothing OrElse sharedSecret.Length = 0 Then
                Throw New ArgumentException("shared secret required", "sharedSecret")
            End If
            If _earlySecret Is Nothing Then Start()

            Dim derived = Hkdf.DeriveSecret(_earlySecret, "derived", EmptyTranscriptHash)
            _handshakeSecret = Hkdf.Extract(derived, sharedSecret)
        End Sub

        Public ReadOnly Property HandshakeSecret As Byte()
            Get
                Return _handshakeSecret
            End Get
        End Property

        ''' <summary>
        ''' Step 3: Master Secret. RFC 8446 §7.1 extracts it from a zero IKM,
        ''' *not* from the handshake secret directly.
        ''' </summary>
        Public Function GetMasterSecret() As Byte()
            If _masterSecret Is Nothing Then
                If _handshakeSecret Is Nothing Then
                    Throw New InvalidOperationException("handshake secret not derived yet")
                End If
                Dim derived = Hkdf.DeriveSecret(_handshakeSecret, "derived", EmptyTranscriptHash)
                _masterSecret = Hkdf.Extract(derived, ZeroPsk)
            End If
            Return _masterSecret
        End Function

        ''' <summary>
        ''' Expands a traffic secret into key, iv and finished key. The labels are
        ''' "key", "iv" and "finished" — the "tls13 " prefix is added by Hkdf itself.
        ''' </summary>
        Private Shared Function ExpandTraffic(secret As Byte()) As TrafficKeys
            Return New TrafficKeys(
                Hkdf.ExpandLabel(secret, "key", New Byte() {}, AeadKeyLength),
                Hkdf.ExpandLabel(secret, "iv", New Byte() {}, IvLength),
                Hkdf.ExpandLabel(secret, "finished", New Byte() {}, HashLength))
        End Function

        ''' <summary>
        ''' Client handshake traffic keys. The transcript is
        ''' ClientHello...ServerHello — i.e. the transcript hash taken AFTER
        ''' ServerHello, before any encrypted message is added.
        ''' </summary>
        Public Function ClientHandshakeTraffic(transcriptHash As Byte()) As TrafficKeys
            Return ExpandTraffic(Hkdf.DeriveSecret(_handshakeSecret, "c hs traffic", transcriptHash))
        End Function

        ''' <summary>Server handshake traffic keys. Same transcript as the client's.</summary>
        Public Function ServerHandshakeTraffic(transcriptHash As Byte()) As TrafficKeys
            Return ExpandTraffic(Hkdf.DeriveSecret(_handshakeSecret, "s hs traffic", transcriptHash))
        End Function

        ''' <summary>
        ''' Client application traffic keys. The transcript runs to the *server's*
        ''' Finished (RFC 8446 §7.1) — not to the client's, which has not been sent
        ''' yet and must not be included.
        ''' </summary>
        Public Function ClientApplicationTraffic(transcriptHash As Byte()) As TrafficKeys
            Return ExpandTraffic(Hkdf.DeriveSecret(GetMasterSecret(), "c ap traffic", transcriptHash))
        End Function

        ''' <summary>Server application traffic keys, over the same transcript.</summary>
        Public Function ServerApplicationTraffic(transcriptHash As Byte()) As TrafficKeys
            Return ExpandTraffic(Hkdf.DeriveSecret(GetMasterSecret(), "s ap traffic", transcriptHash))
        End Function

        ''' <summary>Exporter master secret, needed by RFC 8446 §7.5 exporters.</summary>
        Public Function ExporterMasterSecret(transcriptHash As Byte()) As Byte()
            Return Hkdf.DeriveSecret(GetMasterSecret(), "exp master", transcriptHash)
        End Function

        ''' <summary>
        ''' Finished verify_data (RFC 8446 §4.4.4):
        '''     HMAC(finished_key, Transcript-Hash(handshake_context))
        ''' </summary>
        Public Shared Function ComputeFinished(finishedKey As Byte(),
                                               transcriptHash As Byte()) As Byte()
            Return WinRtCrypto.HmacSha256(finishedKey, transcriptHash)
        End Function
    End Class

End Namespace
