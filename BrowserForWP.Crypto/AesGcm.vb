' BrowserForWP — AES-GCM via the platform provider.
'
' Verified by tools/gen-vectors.mjs against NIST CAVS AES-GCM vectors.
'
' Why native rather than managed: AES-GCM on a 2014 ARM handset in managed code
' is punishingly slow, and WinRT already exposes an accelerated implementation
' through Windows.Security.Cryptography.Core. Reusing it is the correct
' engineering call, not a shortcut.
'
' The return type is deliberately ChaCha20Poly1305.SealedResult: the TLS record
' layer must not care which suite was negotiated.

Imports Windows.Security.Cryptography
Imports Windows.Security.Cryptography.Core

Namespace Crypto

    ''' <summary>AES-128/256-GCM, delegating to the platform crypto provider.</summary>
    Public NotInheritable Class AesGcm

        Public Const TagSize As Integer = 16
        Public Const NonceSize As Integer = 12

        Private Sub New()
        End Sub

        Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As ChaCha20Poly1305.SealedResult
            Dim provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm)
            Dim symmetricKey = provider.CreateSymmetricKey(CryptoBuffer.FromArray(key))

            ' EncryptAndAuthenticate appends the 16-byte AEAD tag to the ciphertext.
            Dim combined = CryptoBuffer.ToArray(CryptographicEngine.EncryptAndAuthenticate(
                symmetricKey,
                CryptoBuffer.FromArray(If(plaintext, New Byte() {})),
                CryptoBuffer.FromArray(nonce),
                CryptoBuffer.FromArray(If(aad, New Byte() {}))))

            If combined.Length < TagSize Then
                Throw New System.Security.Cryptography.CryptographicException("provider returned a short AEAD result")
            End If

            Dim ciphertext(combined.Length - TagSize - 1) As Byte
            Array.Copy(combined, ciphertext, ciphertext.Length)
            Dim tag(TagSize - 1) As Byte
            Array.Copy(combined, combined.Length - TagSize, tag, 0, TagSize)
            Return New ChaCha20Poly1305.SealedResult(ciphertext, tag)
        End Function

        Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(),
                                    ciphertext As Byte(), tag As Byte()) As Byte()
            If tag Is Nothing OrElse tag.Length <> TagSize Then
                Throw New System.Security.Cryptography.CryptographicException("invalid GCM tag length")
            End If

            Dim provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm)
            Dim symmetricKey = provider.CreateSymmetricKey(CryptoBuffer.FromArray(key))

            Dim payload = If(ciphertext, New Byte() {})
            Dim combined(payload.Length + TagSize - 1) As Byte
            Array.Copy(payload, combined, payload.Length)
            Array.Copy(tag, 0, combined, payload.Length, TagSize)

            ' DecryptAndAuthenticate raises on a tag mismatch, which is exactly the
            ' fail-closed behaviour the record layer needs.
            Return CryptoBuffer.ToArray(CryptographicEngine.DecryptAndAuthenticate(
                symmetricKey,
                CryptoBuffer.FromArray(combined),
                CryptoBuffer.FromArray(nonce),
                CryptoBuffer.FromArray(If(aad, New Byte() {}))))
        End Function
    End Class

End Namespace
