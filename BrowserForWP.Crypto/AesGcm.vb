' BrowserForWP — AES-GCM via the platform crypto provider.
'
' Verified by tools/gen-vectors.mjs against NIST CAVS AES-GCM vectors.
'
' ── Why there is no managed AEAD here ───────────────────────────────────────
' BrowserForWP offers exactly one TLS 1.3 cipher suite: TLS_AES_128_GCM_SHA256.
' RFC 8446 §9.1 makes that suite mandatory-to-implement, so every TLS 1.3 server
' accepts it — a single-suite offer costs no interoperability at all.
'
' That decision removes the need for a hand-written AEAD entirely. The
' TLS_CHACHA20_POLY1305_SHA256 suite is a performance optimisation for hardware
' without AES acceleration, and a 2014 ARM handset's AES path is hardware-backed
' through this provider, so the managed implementation would have been dead code
' carrying real risk. It has been deleted rather than left to rot.
'
' If a second suite is ever needed, tools/proto/ is the place to prove the
' arithmetic first — the same discipline that X25519 went through.
'
' ── Why the platform provider ───────────────────────────────────────────────
' Managed AES-GCM on 2014 ARM silicon is punishingly slow, and WinRT already
' exposes an accelerated implementation. CryptographicEngine.EncryptAndAuthenticate
' and DecryptAndAuthenticate explicitly support SymmetricAlgorithmNames.AesGcm.

Imports System.Security.Cryptography
Imports Windows.Security.Cryptography.Core

Namespace Crypto

    ''' <summary>The output of an AEAD seal: ciphertext plus its authentication tag.</summary>
    Public NotInheritable Class AeadResult

        Public Sub New(ciphertext As Byte(), tag As Byte())
            Me.Ciphertext = ciphertext
            Me.Tag = tag
        End Sub

        Public ReadOnly Ciphertext As Byte()
        Public ReadOnly Tag As Byte()
    End Class

    ''' <summary>AES-128/256-GCM, delegating to the platform crypto provider.</summary>
    Public NotInheritable Class AesGcm

        Public Const TagSize As Integer = 16
        Public Const NonceSize As Integer = 12

        ''' <summary>The suite BrowserForWP negotiates: TLS_AES_128_GCM_SHA256.</summary>
        Public Const Tls13KeySize As Integer = 16

        Private Sub New()
        End Sub

        Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As AeadResult
            If key Is Nothing OrElse (key.Length <> 16 AndAlso key.Length <> 32) Then
                Throw New ArgumentException("AES key must be 16 or 32 bytes", "key")
            End If
            If nonce Is Nothing OrElse nonce.Length <> NonceSize Then
                Throw New ArgumentException("GCM nonce must be 12 bytes", "nonce")
            End If

            Dim provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm)
            Dim symmetricKey = provider.CreateSymmetricKey(WinRtCrypto.ToBuffer(key))

            ' EncryptAndAuthenticate appends the 16-byte AEAD tag to the ciphertext.
            Dim combined = WinRtCrypto.ToArray(CryptographicEngine.EncryptAndAuthenticate(
                symmetricKey,
                WinRtCrypto.ToBuffer(If(plaintext, New Byte() {})),
                WinRtCrypto.ToBuffer(nonce),
                WinRtCrypto.ToBuffer(If(aad, New Byte() {}))))

            If combined.Length < TagSize Then
                Throw New CryptographicException("platform provider returned a short AEAD result")
            End If

            Dim ciphertext(combined.Length - TagSize - 1) As Byte
            Array.Copy(combined, ciphertext, ciphertext.Length)
            Dim tag(TagSize - 1) As Byte
            Array.Copy(combined, combined.Length - TagSize, tag, 0, TagSize)
            Return New AeadResult(ciphertext, tag)
        End Function

        Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(),
                                    ciphertext As Byte(), tag As Byte()) As Byte()
            If tag Is Nothing OrElse tag.Length <> TagSize Then
                Throw New CryptographicException("invalid GCM tag length")
            End If

            Dim provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm)
            Dim symmetricKey = provider.CreateSymmetricKey(WinRtCrypto.ToBuffer(key))

            Dim payload = If(ciphertext, New Byte() {})
            Dim combined(payload.Length + TagSize - 1) As Byte
            Array.Copy(payload, combined, payload.Length)
            Array.Copy(tag, 0, combined, payload.Length, TagSize)

            ' DecryptAndAuthenticate raises on a tag mismatch, which is exactly the
            ' fail-closed behaviour the TLS record layer requires: a forged record
            ' must never reach the caller.
            Return WinRtCrypto.ToArray(CryptographicEngine.DecryptAndAuthenticate(
                symmetricKey,
                WinRtCrypto.ToBuffer(combined),
                WinRtCrypto.ToBuffer(nonce),
                WinRtCrypto.ToBuffer(If(aad, New Byte() {}))))
        End Function
    End Class

End Namespace
