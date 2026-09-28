' BrowserForWP — platform cryptographic primitives.
'
' WHY THIS FILE EXISTS
' --------------------
' A Windows Phone 8.1 WinRT app compiles against the ".NET for Windows Store
' apps" profile, which does NOT include the classic managed crypto types:
'
'   System.Security.Cryptography.SHA256 / SHA256Managed   -> absent
'   System.Security.Cryptography.HMACSHA256               -> absent
'   System.Security.Cryptography.RNGCryptoServiceProvider -> absent
'
' `SHA256` derives from `HashAlgorithm`, and `HMACSHA256` and
' `RNGCryptoServiceProvider` derive from the same stripped namespace, so none of
' them resolve. The supported route is Windows.Security.Cryptography.Core:
'
'   SHA-256      HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256)
'   HMAC-SHA256  MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256)
'                then CryptographicEngine.Sign(key, message)
'   randomness   CryptographicBuffer.GenerateRandom
'
' Everything platform-specific about this library is funnelled through here, so
' the rest of BrowserForWP.Crypto stays portable and reviewable.

Imports Windows.Security.Cryptography
Imports Windows.Security.Cryptography.Core
Imports Windows.Storage.Streams

Namespace Crypto

    ''' <summary>WinRT-backed hashing, HMAC and randomness.</summary>
    Public NotInheritable Class WinRtCrypto

        ''' <summary>SHA-256 output length. The key schedule is SHA-256 only.</summary>
        Public Const Sha256Length As Integer = 32

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Cryptographically secure random bytes. This is the only entropy source
        ''' for key generation; never substitute a seeded pseudo-random generator
        ''' here, because an ephemeral key derived from a guessable seed breaks
        ''' forward secrecy completely.
        ''' </summary>
        Public Shared Function RandomBytes(count As Integer) As Byte()
            If count < 0 Then Throw New ArgumentOutOfRangeException("count")
            If count = 0 Then Return New Byte() {}
            Return ToArray(CryptographicBuffer.GenerateRandom(CUInt(count)))
        End Function

        Public Shared Function Sha256(data As Byte()) As Byte()
            Dim provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256)
            Dim hash = provider.CreateHash()
            hash.Append(ToBuffer(If(data, New Byte() {})))
            Return ToArray(hash.GetValueAndReset())
        End Function

        ''' <summary>HMAC-SHA256, used for every HKDF step in the TLS 1.3 schedule.</summary>
        Public Shared Function HmacSha256(key As Byte(), data As Byte()) As Byte()
            If key Is Nothing OrElse key.Length = 0 Then
                Throw New ArgumentException("HMAC key must not be empty", "key")
            End If

            Dim provider = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256)
            Dim macKey = provider.CreateKey(ToBuffer(key))
            Dim mac = CryptographicEngine.Sign(macKey, ToBuffer(If(data, New Byte() {})))

            If mac.Length <> Sha256Length Then
                Throw New InvalidOperationException(
                    "platform HMAC-SHA256 returned " & mac.Length & " bytes, expected " & Sha256Length)
            End If
            Return ToArray(mac)
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

        ''' <summary>Byte array to IBuffer. Note the method name: CreateFromByteArray.</summary>
        Friend Shared Function ToBuffer(bytes As Byte()) As IBuffer
            Return CryptographicBuffer.CreateFromByteArray(If(bytes, New Byte() {}))
        End Function

        ''' <summary>IBuffer to byte array, via the copy-out overload.</summary>
        Friend Shared Function ToArray(buffer As IBuffer) As Byte()
            If buffer Is Nothing Then Return New Byte() {}
            Dim bytes As Byte() = Nothing
            CryptographicBuffer.CopyToByteArray(buffer, bytes)
            Return If(bytes, New Byte() {})
        End Function
    End Class

End Namespace
