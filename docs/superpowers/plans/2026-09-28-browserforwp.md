# BrowserForWP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows Phone 8.1 browser that speaks a genuinely modern transport (TLS 1.3 + DNS-over-HTTPS, implemented on-device) and renders modern pages as well as Trident allows, with no backend of any kind.

**Architecture:** Five layers, strictly separated. `BrowserForWP.Crypto` (HKDF, X25519, ChaCha20-Poly1305, AES-GCM) has no dependency on anything. `BrowserForWP.Net` (TLS 1.3 record layer + handshake, DoH resolver, HTTP/1.1 client) depends only on Crypto. `BrowserForWP.Core` (engine abstraction, tabs, history, address bar) depends on neither. `BrowserForWP.Localization` is standalone. The app `BrowserForWP` wires them to XAML. `IBrowserEngine` is the seam that lets a real modern engine drop in on any platform that has one.

**Tech Stack:** VB.NET / WinRT 8.1, XAML, `Windows.Networking.Sockets.StreamSocket`, `Windows.Security.Cryptography.Core`, MSTest for unit tests, Node.js (tooling only, never shipped), Python 3 (asset generation only).

## Revision — 2026-09-28, after API research

Three assumptions in the first draft of this plan were wrong. They were found by
checking the WP8.1 WinRT API surface rather than trusting desktop .NET habits,
and the affected tasks below have been revised in the code.

1. **`System.Security.Cryptography` is largely absent.** `SHA256`,
   `HMACSHA256` and `RNGCryptoServiceProvider` do not exist in the ".NET for
   Windows Store apps" profile, so the original HKDF and X25519 could not have
   compiled. Everything now routes through `WinRtCrypto`
   (`Windows.Security.Cryptography.Core`). `RegexOptions.Compiled` is also
   unsupported.
2. **`BigInteger` is unconfirmed.** Its presence in this profile could not be
   established, so it was removed rather than bet on. X25519 field arithmetic is
   now radix-2^16 in `Int64` — chosen because power-of-two limbs make weights add
   exactly — and is **proven** by `tools/proto/w25519.mjs`, a line-for-line
   runnable prototype, before being transliterated to VB.
3. **A managed AEAD is unnecessary.** Offering only `TLS_AES_128_GCM_SHA256`
   (mandatory-to-implement per RFC 8446 §9.1, so zero interoperability cost) lets
   the platform's accelerated AES-GCM serve every record. Task 4
   (ChaCha20-Poly1305) is therefore **cancelled**, and the ChaCha vectors were
   removed from `tools/gen-vectors.mjs` rather than left verifying code that does
   not ship. The verified assertion count is now **52**, not 55.

**New required reading before touching the crypto:**
`docs/ARCHITECTURE.md`, section "The crypto API surface on WP8.1 WinRT".

## Revision 2 — implementation status

Tasks 1-3 and 6-11 are **implemented**. Task 4 (ChaCha20-Poly1305) is
**cancelled** and Task 5 is folded into Task 3. The tasks that were not written
from this document are listed below with where their verification actually came
from, because the verification is the part that matters:

| Task | Status | Verified by |
| --- | --- | --- |
| 1-3 Crypto (HKDF, X25519, AES-GCM) | Implemented | `tools/gen-vectors.mjs` (52 assertions), `tools/proto/w25519.mjs` (18 checks) |
| 4 ChaCha20-Poly1305 | **Cancelled** — see Revision | n/a |
| 6 TLS 1.3 key schedule | Implemented | `tools/proto/tls13.mjs`, live handshake |
| 7 Record layer | Implemented | `tools/proto/tls13.mjs`, live handshake |
| 8 Handshake client | Implemented | `tools/proto/tls13.mjs`, live handshake |
| 9 DoH resolver | Implemented | RFC 1035 wire format; needs a handset to exercise end to end |
| 10 Localization | Implemented | 27/27 resw keys present in both languages |
| 11 UI + wiring | Implemented | Builds in Visual Studio only (no WP8.1 SDK off-Windows) |

**The important change since this plan was written:** Tasks 6-9 are no longer
specified by prose and then transliterated blind. `tools/proto/tls13.mjs` is a
complete, runnable TLS 1.3 client that completes real handshakes with real
servers, and `BrowserForWP.Net/Tls13/` is its transliteration. That prototype
found three protocol bugs and one standards violation that no amount of local
testing would have caught — they are tabulated in `docs/ARCHITECTURE.md` under
"The TLS 1.3 prototype".

**Therefore:** for anything under `BrowserForWP.Net/Tls13/`, change
`tools/proto/tls13.mjs` first and watch it pass against a live host. Do not edit
the VB and hope.

Still not written, and not needed for the app to build: the two `tests/`
projects are referenced by this plan but only
`tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb` exists so far.

## Global Constraints

- **Target:** `TargetPlatformVersion 8.1`, `WindowsPhoneApp`, VB.NET. Builds in Visual Studio 2013 Update 4+ on Windows only.
- **No backend.** Every byte of logic — crypto, TLS, DNS, polyfills, history, strings — runs on the handset. No server, no proxy service, no telemetry, no network call other than the ones the user navigated to.
- **No loopback proxy.** Windows AppContainers block `127.0.0.1` by default, so a local proxy cannot feed the system `WebView`. Do not introduce one.
- **Rendering engine is Trident (IE11) and is not replaceable.** Do not write code that assumes otherwise. Capability is expressed through `IBrowserEngine`.
- **TLS ceiling through the OS is TLS 1.2.** TLS 1.3 is available only through `Tls13Client` in the app's own transport, never through the `WebView`.
- **`en-US` is the default and fallback language.** `it-IT` is the secondary. Every user-visible string exists in both.
- **Layer discipline.** Crypto knows nothing about TLS; TLS knows nothing about the UI; Core knows nothing about crypto.
- **Verification floor:** `node tools/gen-vectors.mjs` must print `52 assertions, 0 failure(s)` after any change to `BrowserForWP.Crypto/`.

---

## File Structure

| Path | Responsibility |
| --- | --- |
| `tools/gen-vectors.mjs` | Recomputes crypto from the RFCs, asserts against published constants, emits VB test data. **Exists.** |
| `tools/make_logo.py` | Renders every WP8.1 image asset from a continuous mark function. **Exists.** |
| `BrowserForWP.Crypto/Hkdf.vb` | RFC 5869 Extract / Expand / ExpandLabel. |
| `BrowserForWP.Crypto/X25519.vb` | RFC 7748 scalar multiplication, clamping, agreement. |
| `BrowserForWP.Crypto/ChaCha20Poly1305.vb` | RFC 8439 AEAD seal/open. |
| `BrowserForWP.Crypto/AesGcm.vb` | Thin adapter over WinRT native AES-GCM. |
| `BrowserForWP.Crypto/BrowserForWP.Crypto.vbproj` | WP8.1 class library project. |
| `BrowserForWP.Net/Tls13/TlsConstants.vb` | Cipher suites, handshake types, extension ids. |
| `BrowserForWP.Net/Tls13/TlsRecordLayer.vb` | Record framing, AEAD protection, sequence numbers. |
| `BrowserForWP.Net/Tls13/KeySchedule.vb` | The RFC 8446 §7.1 derivation chain. |
| `BrowserForWP.Net/Tls13/Tls13Client.vb` | Handshake state machine over `StreamSocket`. |
| `BrowserForWP.Net/Dns/DohResolver.vb` | RFC 8484 wire-format DNS over HTTPS. |
| `BrowserForWP.Net/Http/HttpClient13.vb` | HTTP/1.1 request/response over `Tls13Client`. |
| `BrowserForWP.Localization/LanguageCatalog.vb` | Known languages, display names, fallback chain. |
| `BrowserForWP.Localization/Localizer.vb` | Resolves display language; looks up strings with fallback. |
| `BrowserForWP.Core/Engine/IBrowserEngine.vb` | The engine seam. |
| `BrowserForWP.Core/Engine/TridentEngine.vb` | WP8.1 implementation over `Windows.UI.Xaml.Controls.WebView`. |
| `BrowserForWP.Core/Browser/BrowserSession.vb` | Tabs, active tab, desktop-mode state. |
| `BrowserForWP.Core/Browser/TabModel.vb` | One tab: history, index, title, url. |
| `BrowserForWP.Core/Browser/AddressNormalizer.vb` | URL vs. search classification, scheme repair. |
| `BrowserForWP.Polyfill/compat.js` | On-device compatibility layer injected into every document. |
| `BrowserForWP/MainPage.xaml(.vb)` | Browser shell UI. |
| `BrowserForWP/Strings/en-US/Resources.resw` | English UI strings. |
| `BrowserForWP/Strings/it-IT/Resources.resw` | Italian UI strings. |
| `tests/BrowserForWP.Crypto.Tests/` | MSTest project consuming `Vectors.generated.vb`. |

---

### Task 1: Image assets

**Files:**
- Create: `BrowserForWP/Assets/*.png` (13 files, generated)
- Exists: `tools/make_logo.py`

**Interfaces:**
- Consumes: nothing.
- Produces: the PNG file names referenced by `Package.appxmanifest` and `BrowserForWP.vbproj`: `Logo.png`, `Logo.scale-240.png`, `SmallLogo.png`, `SmallLogo.scale-240.png`, `StoreLogo.png`, `StoreLogo.scale-240.png`, `Square71x71Logo.png`, `Square71x71Logo.scale-240.png`, `WideLogo.png`, `WideLogo.scale-240.png`, `SplashScreen.png`, `SplashScreen.scale-240.png`.

**Note on permissions:** `BrowserForWP/` may be owned by another user. If
`python3 tools/make_logo.py` fails with `PermissionError`, fix ownership first
on the machine that owns the tree:

```bash
sudo chown -R "$(whoami)" BrowserForWP BrowserForWP.sln
```

- [ ] **Step 1: Generate the assets**

```bash
python3 tools/make_logo.py
```

Expected: 12 lines of `name.png  WxH`, then `done.`

- [ ] **Step 2: Verify the dimensions are what WP8.1 requires**

```bash
python3 - <<'PY'
import struct, glob, os
expect = {
  'StoreLogo.png': (50,50), 'StoreLogo.scale-240.png': (120,120),
  'SmallLogo.png': (44,44), 'SmallLogo.scale-240.png': (106,106),
  'Logo.png': (150,150), 'Logo.scale-240.png': (360,360),
  'Square71x71Logo.png': (71,71), 'Square71x71Logo.scale-240.png': (170,170),
  'WideLogo.png': (310,150), 'WideLogo.scale-240.png': (744,360),
  'SplashScreen.png': (480,800), 'SplashScreen.scale-240.png': (1152,1920),
}
bad = 0
for name, want in expect.items():
    p = os.path.join('BrowserForWP', 'Assets', name)
    if not os.path.exists(p):
        print('MISSING', name); bad += 1; continue
    d = open(p, 'rb').read(24)
    got = struct.unpack('>II', d[16:24])
    if got != want:
        print('WRONG  ', name, got, '!=', want); bad += 1
print('OK' if bad == 0 else f'{bad} problem(s)')
PY
```

Expected: `OK`

- [ ] **Step 3: Commit**

```bash
git add BrowserForWP/Assets tools/make_logo.py
git commit -m "chore(assets): generate WP8.1 tiles and splash from SVG-free renderer"
```

---

### Task 2: BrowserForWP.Crypto — HKDF

**Files:**
- Create: `BrowserForWP.Crypto/Hkdf.vb`
- Create: `BrowserForWP.Crypto/BrowserForWP.Crypto.vbproj`
- Test: `tests/BrowserForWP.Crypto.Tests/HkdfTests.vb`

**Interfaces:**
- Consumes: `System.Security.Cryptography.HMACSHA256` (available on WinRT 8.1).
- Produces:
  - `Public Shared Function Extract(salt As Byte(), ikm As Byte()) As Byte()`
  - `Public Shared Function Expand(prk As Byte(), info As Byte(), length As Integer) As Byte()`
  - `Public Shared Function ExpandLabel(secret As Byte(), label As String, context As Byte(), length As Integer) As Byte()`
  - All in `Public NotInheritable Class Hkdf` in namespace `BrowserForWP.Crypto`.

- [ ] **Step 1: Write the failing test**

```vb
Imports System.Text
Imports BrowserForWP.Crypto
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class HkdfTests

    <TestMethod>
    Public Sub Extract_MatchesRfc5869Case1Prk()
        Dim prk = Hkdf.Extract(Vectors.HkdfCase1Salt, Vectors.HkdfCase1Ikm)
        CollectionAssert.AreEqual(Vectors.HkdfCase1Prk, prk)
    End Sub

    <TestMethod>
    Public Sub Expand_MatchesRfc5869Case1Okm()
        Dim okm = Hkdf.Expand(Vectors.HkdfCase1Prk, Vectors.HkdfCase1Info, Vectors.HkdfCase1L)
        CollectionAssert.AreEqual(Vectors.HkdfCase1Okm, okm)
    End Sub

    <TestMethod>
    Public Sub Extract_WithEmptySaltMatchesRfc5869Case3()
        ' RFC 5869: an empty salt is replaced by HashLen zero octets, NOT by no salt.
        Dim prk = Hkdf.Extract(New Byte() {}, Vectors.HkdfCase3Ikm)
        CollectionAssert.AreEqual(Vectors.HkdfCase3Prk, prk)
    End Sub

    <TestMethod>
    Public Sub ExpandLabel_BuildsTheRfc8448InfoLayout()
        ' "derived" with the empty-transcript hash must produce the 49-octet
        ' info block that RFC 8448 §3 prints verbatim.
        Dim emptyHash = Sha256Of(New Byte() {})
        Dim expected = HexToBytes("00200d746c733133206465726976656420" &
                                  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
        Dim actual = Hkdf.BuildLabelInfo("derived", emptyHash, 32)
        CollectionAssert.AreEqual(expected, actual)
    End Sub

    Private Shared Function Sha256Of(data As Byte()) As Byte()
        Dim sha = New System.Security.Cryptography.SHA256Managed()
        Return sha.ComputeHash(data)
    End Function
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `HkdfTests` → Run.
Expected: FAIL to compile — `Hkdf` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports System.Security.Cryptography
Imports System.Text

Namespace Crypto

    ''' <summary>
    ''' HMAC-based Extract-and-Expand Key Derivation Function (RFC 5869), plus the
    ''' TLS 1.3 HkdfLabel framing from RFC 8446 §7.1.
    ''' </summary>
    Public NotInheritable Class Hkdf

        Public Const HashLength As Integer = 32   ' SHA-256

        Private Sub New()
        End Sub

        ''' <summary>RFC 5869 §2.2. An empty salt is replaced by HashLen zeros.</summary>
        Public Shared Function Extract(salt As Byte(), ikm As Byte()) As Byte()
            If salt Is Nothing OrElse salt.Length = 0 Then salt = New Byte(HashLength - 1) {}
            Return Mac(salt, ikm)
        End Function

        ''' <summary>RFC 5869 §2.3.</summary>
        Public Shared Function Expand(prk As Byte(), info As Byte(), length As Integer) As Byte()
            If length < 0 Then Throw New ArgumentOutOfRangeException("length")
            Dim n = CInt(Math.Ceiling(length / CDbl(HashLength)))
            If n > 255 Then Throw New ArgumentOutOfRangeException("length", "HKDF output too long")

            Dim output As New List(Of Byte)()
            Dim previous As Byte() = New Byte() {}
            For i As Integer = 1 To n
                Dim blockInput As New List(Of Byte)()
                blockInput.AddRange(previous)
                If info IsNot Nothing Then blockInput.AddRange(info)
                blockInput.Add(CByte(i))
                previous = Mac(prk, blockInput.ToArray())
                output.AddRange(previous)
            Next

            Dim result(length - 1) As Byte
            Array.Copy(output.ToArray(), result, length)
            Return result
        End Function

        ''' <summary>
        ''' The HkdfLabel structure (RFC 8446 §7.1):
        '''   struct { uint16 length; opaque label&lt;7..255&gt;; opaque context&lt;0..255&gt;; } HkdfLabel;
        ''' Exposed so the byte layout can be asserted directly against RFC 8448.
        ''' </summary>
        Public Shared Function BuildLabelInfo(label As String, context As Byte(), length As Integer) As Byte()
            Dim full = Encoding.UTF8.GetBytes("tls13 " & label)
            Dim ctx = If(context, New Byte() {})
            Dim info As New List(Of Byte)()
            info.Add(CByte((length >> 8) And &HFF))
            info.Add(CByte(length And &HFF))
            info.Add(CByte(full.Length))
            info.AddRange(full)
            info.Add(CByte(ctx.Length))
            info.AddRange(ctx)
            Return info.ToArray()
        End Function

        ''' <summary>HKDF-Expand-Label (RFC 8446 §7.1).</summary>
        Public Shared Function ExpandLabel(secret As Byte(), label As String,
                                           context As Byte(), length As Integer) As Byte()
            Return Expand(secret, BuildLabelInfo(label, context, length), length)
        End Function

        ''' <summary>
        ''' Derive-Secret: Derive-Secret(Secret, Label, Messages) =
        '''     HKDF-Expand-Label(Secret, Label, Transcript-Hash(Messages), Hash.length)
        ''' </summary>
        Public Shared Function DeriveSecret(secret As Byte(), label As String, transcript As Byte()) As Byte()
            Return ExpandLabel(secret, label, Sha256(transcript), HashLength)
        End Function

        Public Shared Function Sha256(data As Byte()) As Byte()
            Dim sha = New SHA256Managed()
            Return sha.ComputeHash(If(data, New Byte() {}))
        End Function

        Private Shared Function Mac(key As Byte(), data As Byte()) As Byte()
            Using h = New HMACSHA256(key)
                Return h.ComputeHash(data)
            End Using
        End Function
    End Class

End Namespace
```

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `HkdfTests` → Run.
Expected: 4 tests PASS.

- [ ] **Step 5: Re-verify the vectors still reproduce**

```bash
node tools/gen-vectors.mjs
```
Expected: `52 assertions, 0 failure(s)`

- [ ] **Step 6: Commit**

```bash
git add BrowserForWP.Crypto/Hkdf.vb BrowserForWP.Crypto/BrowserForWP.Crypto.vbproj tests/BrowserForWP.Crypto.Tests/HkdfTests.vb
git commit -m "feat(crypto): add HKDF with TLS 1.3 HkdfLabel framing"
```

---

### Task 3: BrowserForWP.Crypto — X25519

**Files:**
- Create: `BrowserForWP.Crypto/X25519.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/X25519Tests.vb`

**Interfaces:**
- Consumes: nothing.
- Produces, in `Public NotInheritable Class X25519`:
  - `Public Const KeySize As Integer = 32`
  - `Public Shared Function ScalarMult(scalar As Byte(), u As Byte()) As Byte()`
  - `Public Shared Function PublicFromPrivate(privateKey As Byte()) As Byte()`
  - `Public Shared Function Agreement(privateKey As Byte(), peerPublic As Byte()) As Byte()`
  - `Public Shared Function GeneratePrivateKey() As Byte()`

**Why hand-rolled:** WinRT 8.1 exposes no X25519 primitive, and TLS 1.3's
mandatory key_share (`x25519`, group 0x001d) requires it. The field arithmetic
is `2^255 - 19` with a 16-limb 2^16 representation (the standard
ref10-style decomposition), which is what keeps it tractable in managed code.

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Crypto
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class X25519Tests

    <TestMethod>
    Public Sub PublicFromPrivate_MatchesRfc7748_6_1()
        CollectionAssert.AreEqual(Vectors.X25519AlicePub, X25519.PublicFromPrivate(Vectors.X25519AlicePriv))
        CollectionAssert.AreEqual(Vectors.X25519BobPub, X25519.PublicFromPrivate(Vectors.X25519BobPriv))
    End Sub

    <TestMethod>
    Public Sub Agreement_IsSymmetricAndMatchesRfc7748_6_1()
        Dim alice = X25519.Agreement(Vectors.X25519AlicePriv, Vectors.X25519BobPub)
        Dim bob = X25519.Agreement(Vectors.X25519BobPriv, Vectors.X25519AlicePub)
        CollectionAssert.AreEqual(Vectors.X25519Shared, alice)
        CollectionAssert.AreEqual(alice, bob)
    End Sub

    <TestMethod>
    Public Sub ScalarMult_MatchesRfc7748_5_2()
        CollectionAssert.AreEqual(Vectors.X25519IterOut, X25519.ScalarMult(Vectors.X25519IterScalar, Vectors.X25519IterU))
    End Sub

    <TestMethod>
    Public Sub ScalarMult_ClearsTheHighBitOfTheUCoordinate()
        ' RFC 7748 §5: the top bit of the final byte of the u-coordinate MUST be masked.
        Dim u = CType(Vectors.X25519IterU.Clone(), Byte())
        u(31) = CByte(u(31) Or &H80)
        CollectionAssert.AreEqual(Vectors.X25519IterOut, X25519.ScalarMult(Vectors.X25519IterScalar, u))
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `X25519Tests` → Run.
Expected: FAIL to compile — `X25519` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Namespace Crypto

    ''' <summary>
    ''' X25519 (RFC 7748) Diffie-Hellman over Curve25519.
    ''' </summary>
    Public NotInheritable Class X25519

        Public Const KeySize As Integer = 32

        Private Sub New()
        End Sub

        Public Shared Function GeneratePrivateKey() As Byte()
            Dim k(KeySize - 1) As Byte
            Dim rng = New System.Security.Cryptography.RNGCryptoServiceProvider()
            rng.GetBytes(k)
            Clamp(k)
            Return k
        End Function

        Public Shared Function PublicFromPrivate(privateKey As Byte()) As Byte()
            Dim baseU(KeySize - 1) As Byte
            baseU(0) = 9
            Return ScalarMult(privateKey, baseU)
        End Function

        Public Shared Function Agreement(privateKey As Byte(), peerPublic As Byte()) As Byte()
            If peerPublic Is Nothing OrElse peerPublic.Length <> KeySize Then
                Throw New ArgumentException("peer public key must be 32 bytes", "peerPublic")
            End If
            Dim shared = ScalarMult(privateKey, peerPublic)
            ' RFC 7748 §6.1: reject the all-zero output (low-order point).
            Dim allZero = True
            For Each b In shared
                If b <> 0 Then allZero = False : Exit For
            Next
            If allZero Then Throw New InvalidOperationException("X25519 produced a degenerate shared secret")
            Return shared
        End Function

        ''' <summary>RFC 7748 §5 decoding/clamping of a scalar.</summary>
        Private Shared Sub Clamp(k As Byte())
            k(0) = CByte(k(0) And &HF8)
            k(31) = CByte(k(31) And &H7F)
            k(31) = CByte(k(31) Or &H40)
        End Sub

        ''' <summary>
        ''' The Montgomery ladder, RFC 7748 §5. Implemented over a 2^16 limb
        ''' representation so products of two 16-bit limbs fit comfortably in a
        ''' signed 64-bit integer.
        ''' </summary>
        Public Shared Function ScalarMult(scalar As Byte(), u As Byte()) As Byte()
            Dim k = CType(scalar.Clone(), Byte())
            Clamp(k)
            Dim x1 = DecodeU(u)

            Dim x2 = Fe.One()
            Dim z2 = Fe.Zero()
            Dim x3 = x1
            Dim z3 = Fe.One()
            Dim swap As Integer = 0

            For t As Integer = 254 To 0 Step -1
                Dim kt = (k(t >> 3) >> (t And 7)) And 1
                swap = swap Xor kt
                If swap <> 0 Then
                    SwapFe(x2, x3) : SwapFe(z2, z3)
                End If
                swap = kt

                Dim a = Fe.Add(x2, z2)
                Dim aa = Fe.Mul(a, a)
                Dim b = Fe.Sub(x2, z2)
                Dim bb = Fe.Mul(b, b)
                Dim e = Fe.Sub(aa, bb)
                Dim c = Fe.Add(x3, z3)
                Dim d = Fe.Sub(x3, z3)
                Dim da = Fe.Mul(d, a)
                Dim cb = Fe.Mul(c, b)
                x3 = Fe.Mul(Fe.Add(da, cb), Fe.Add(da, cb))
                z3 = Fe.Mul(x1, Fe.Mul(Fe.Sub(da, cb), Fe.Sub(da, cb)))
                x2 = Fe.Mul(aa, bb)
                z2 = Fe.Mul(e, Fe.Add(bb, Fe.Mul(121665, e)))
            Next

            If swap <> 0 Then SwapFe(x2, x3) : SwapFe(z2, z3)
            Return Fe.Encode(Fe.Mul(x2, Fe.Invert(z2)))
        End Function

        Private Shared Sub SwapFe(ByRef a As Integer(), ByRef b As Integer())
            Dim t = a : a = b : b = t
        End Sub

        Private Shared Function DecodeU(u As Byte()) As Integer()
            Dim f(u.Length - 1) As Byte
            Array.Copy(u, f, u.Length)
            f(31) = CByte(f(31) And &H7F)   ' RFC 7748 §5: mask the top bit
            Return Fe.Decode(f)
        End Function
    End Class

End Namespace
```

`Fe` (field element over 2^255-19) is a `Friend NotInheritable Class` added in
the same file: `Zero()`, `One()`, `Add`, `Sub`, `Mul`, `Invert` (via the
standard `x^(2^255-21)` addition chain), `Decode`, `Encode` (carry-propagating
reduction mod 2^255-19). Its correctness is entirely pinned by the RFC 7748
vectors in Step 1 — if `Fe.Mul` or `Invert` is wrong, `PublicFromPrivate`
cannot produce the published public key.

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `X25519Tests` → Run.
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Crypto/X25519.vb tests/BrowserForWP.Crypto.Tests/X25519Tests.vb
git commit -m "feat(crypto): add X25519 key agreement over Curve25519"
```

---

### Task 4: BrowserForWP.Crypto — ChaCha20-Poly1305

**Files:**
> **CANCELLED — see Revision and Revision 2 at the top of this document.**
> BrowserForWP ships exactly one cipher suite (`TLS_AES_128_GCM_SHA256`), so no
> managed AEAD is needed and `BrowserForWP.Crypto/ChaCha20Poly1305.vb` does not
> and should not exist. Do not implement this task. The steps below are retained
> only as a record of the original plan.

- Create: `BrowserForWP.Crypto/ChaCha20Poly1305.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/ChaCha20Poly1305Tests.vb`

**Interfaces:**
- Consumes: nothing.
- Produces, in `Public NotInheritable Class ChaCha20Poly1305`:
  - `Public Const KeySize As Integer = 32`, `NonceSize As Integer = 12`, `TagSize As Integer = 16`
  - `Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As SealedResult`
  - `Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(), ciphertext As Byte(), tag As Byte()) As Byte()`
  - `Public NotInheritable Class SealedResult` with `Public ReadOnly Ciphertext As Byte()` and `Public ReadOnly Tag As Byte()`.

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Crypto
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class ChaCha20Poly1305Tests

    <TestMethod>
    Public Sub Seal_MatchesRfc8439_2_8_2()
        Dim r = ChaCha20Poly1305.Seal(Vectors.ChaChaKey, Vectors.ChaChaNonce, Vectors.ChaChaAad, Vectors.ChaChaPlaintext)
        CollectionAssert.AreEqual(Vectors.ChaChaCiphertext, r.Ciphertext)
        CollectionAssert.AreEqual(Vectors.ChaChaTag, r.Tag)
    End Sub

    <TestMethod>
    Public Sub Open_RoundTrips()
        Dim r = ChaCha20Poly1305.Seal(Vectors.ChaChaKey, Vectors.ChaChaNonce, Vectors.ChaChaAad, Vectors.ChaChaPlaintext)
        Dim back = ChaCha20Poly1305.Open(Vectors.ChaChaKey, Vectors.ChaChaNonce, Vectors.ChaChaAad, r.Ciphertext, r.Tag)
        CollectionAssert.AreEqual(Vectors.ChaChaPlaintext, back)
    End Sub

    <TestMethod>
    <ExpectedException(GetType(CryptographicException))>
    Public Sub Open_RejectsATamperedTag()
        Dim r = ChaCha20Poly1305.Seal(Vectors.ChaChaKey, Vectors.ChaChaNonce, Vectors.ChaChaAad, Vectors.ChaChaPlaintext)
        Dim bad = CType(r.Tag.Clone(), Byte())
        bad(0) = CByte(bad(0) Xor 1)
        ChaCha20Poly1305.Open(Vectors.ChaChaKey, Vectors.ChaChaNonce, Vectors.ChaChaAad, r.Ciphertext, bad)
    End Sub

    <TestMethod>
    <ExpectedException(GetType(CryptographicException))>
    Public Sub Open_RejectsTamperedAad()
        Dim r = ChaCha20Poly1305.Seal(Vectors.ChaChaKey, Vectors.ChaChaNonce, Vectors.ChaChaAad, Vectors.ChaChaPlaintext)
        Dim bad = CType(Vectors.ChaChaAad.Clone(), Byte())
        bad(0) = CByte(bad(0) Xor 1)
        ChaCha20Poly1305.Open(Vectors.ChaChaKey, Vectors.ChaChaNonce, bad, r.Ciphertext, r.Tag)
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `ChaCha20Poly1305Tests` → Run.
Expected: FAIL to compile — `ChaCha20Poly1305` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports System.Security.Cryptography

Namespace Crypto

    ''' <summary>ChaCha20-Poly1305 AEAD (RFC 8439).</summary>
    Public NotInheritable Class ChaCha20Poly1305

        Public Const KeySize As Integer = 32
        Public Const NonceSize As Integer = 12
        Public Const TagSize As Integer = 16

        Private Sub New()
        End Sub

        Public NotInheritable Class SealedResult
            Public Sub New(ciphertext As Byte(), tag As Byte())
                Me.Ciphertext = ciphertext
                Me.Tag = tag
            End Sub
            Public ReadOnly Ciphertext As Byte()
            Public ReadOnly Tag As Byte()
        End Class

        Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As SealedResult
            ValidateKeyNonce(key, nonce)
            Dim otk = Poly1305Key(key, nonce)
            Dim counter As UInteger = 1
            Dim ct = XorStream(key, nonce, counter, plaintext)
            Dim tag = Mac(otk, aad, ct)
            Return New SealedResult(ct, tag)
        End Function

        Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(),
                                    ciphertext As Byte(), tag As Byte()) As Byte()
            ValidateKeyNonce(key, nonce)
            If tag Is Nothing OrElse tag.Length <> TagSize Then
                Throw New CryptographicException("invalid tag length")
            End If
            Dim otk = Poly1305Key(key, nonce)
            Dim expected = Mac(otk, aad, ciphertext)
            If Not FixedTimeEquals(expected, tag) Then
                Throw New CryptographicException("authentication failed")
            End If
            Return XorStream(key, nonce, 1, ciphertext)
        End Function

        Private Shared Sub ValidateKeyNonce(key As Byte(), nonce As Byte())
            If key Is Nothing OrElse key.Length <> KeySize Then
                Throw New ArgumentException("key must be 32 bytes", "key")
            End If
            If nonce Is Nothing OrElse nonce.Length <> NonceSize Then
                Throw New ArgumentException("nonce must be 12 bytes", "nonce")
            End If
        End Sub

        ''' <summary>RFC 8439 §2.6 — first 32 bytes of the counter-0 keystream.</summary>
        Private Shared Function Poly1305Key(key As Byte(), nonce As Byte()) As Byte()
            Dim block = ChaChaBlock(key, 0, nonce)
            Dim otk(31) As Byte
            Array.Copy(block, otk, 32)
            Return otk
        End Function

        ''' <summary>RFC 8439 §2.8 — MAC over aad || pad || ciphertext || pad || lengths.</summary>
        Private Shared Function Mac(oneTimeKey As Byte(), aad As Byte(), ciphertext As Byte()) As Byte()
            Dim macInput As New List(Of Byte)()
            macInput.AddRange(If(aad, New Byte() {}))
            AddPadding(macInput, If(aad, New Byte() {}).Length)
            macInput.AddRange(If(ciphertext, New Byte() {}))
            AddPadding(macInput, If(ciphertext, New Byte() {}).Length)
            AppendUInt64LE(macInput, If(aad, New Byte() {}).Length)
            AppendUInt64LE(macInput, If(ciphertext, New Byte() {}).Length)
            Return Poly1305.Compute(oneTimeKey, macInput.ToArray())
        End Function

        Private Shared Sub AddPadding(target As List(Of Byte), length As Integer)
            Dim pad = (16 - (length Mod 16)) Mod 16
            For i = 1 To pad
                target.Add(0)
            Next
        End Sub

        Private Shared Sub AppendUInt64LE(target As List(Of Byte), value As Long)
            For i = 0 To 7
                target.Add(CByte((value >> (8 * i)) And &HFF))
            Next
        End Sub

        Private Shared Function XorStream(key As Byte(), nonce As Byte(), counter As UInteger, data As Byte()) As Byte()
            Dim out(If(data, New Byte() {}).Length - 1) As Byte
            If data Is Nothing OrElse data.Length = 0 Then Return New Byte() {}
            For offset As Integer = 0 To data.Length - 1 Step 64
                Dim block = ChaChaBlock(key, counter, nonce)
                counter += 1
                Dim n = Math.Min(64, data.Length - offset)
                For i = 0 To n - 1
                    out(offset + i) = CByte(data(offset + i) Xor block(i))
                Next
            Next
            Return out
        End Function

        Private Shared Function FixedTimeEquals(a As Byte(), b As Byte()) As Boolean
            If a.Length <> b.Length Then Return False
            Dim diff = 0
            For i = 0 To a.Length - 1
                diff = diff Or (a(i) Xor b(i))
            Next
            Return diff = 0
        End Function
    End Class

End Namespace
```

`ChaChaBlock` (RFC 8439 §2.3 quarter-round + 20 rounds) and
`Poly1305.Compute` (§2.5, 130-bit accumulator over 2^26 limbs) are
`Friend NotInheritable Class`es in the same file. The RFC 8439 §2.8.2 vector in
Step 1 pins both — a wrong round count or a wrong accumulator reduction cannot
reproduce the published tag.

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `ChaCha20Poly1305Tests` → Run.
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Crypto/ChaCha20Poly1305.vb tests/BrowserForWP.Crypto.Tests/ChaCha20Poly1305Tests.vb
git commit -m "feat(crypto): add ChaCha20-Poly1305 AEAD"
```

---

### Task 5: BrowserForWP.Crypto — AES-GCM adapter

**Files:**
- Create: `BrowserForWP.Crypto/AesGcm.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/AesGcmTests.vb`

**Interfaces:**
- Consumes: `Windows.Security.Cryptography.Core` (native, hardware-accelerated).
- Produces, in `Public NotInheritable Class AesGcm`:
  - `Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As ChaCha20Poly1305.SealedResult`
  - `Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(), ciphertext As Byte(), tag As Byte()) As Byte()`

  Returning the shared `SealedResult` type is deliberate: it lets the record
  layer hold one result type regardless of the negotiated suite.

**Why native:** managed AES-GCM on a 2014 ARM handset is punishingly slow, and
WinRT already exposes an accelerated implementation. Reusing it is the correct
engineering call, not a shortcut.

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Crypto
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class AesGcmTests

    <TestMethod>
    Public Sub Seal_EmptyPlaintextMatchesNistVector()
        Dim r = AesGcm.Seal(Vectors.AesGcmKey128, Vectors.AesGcmIv, New Byte() {}, New Byte() {})
        CollectionAssert.AreEqual(Vectors.AesGcmEmptyTag128, r.Tag)
    End Sub

    <TestMethod>
    Public Sub Seal_ZeroBlockMatchesNistVector()
        Dim r = AesGcm.Seal(Vectors.AesGcmKey128, Vectors.AesGcmIv, New Byte() {}, New Byte(15) {})
        CollectionAssert.AreEqual(Vectors.AesGcmBlockCt128, r.Ciphertext)
        CollectionAssert.AreEqual(Vectors.AesGcmBlockTag128, r.Tag)
    End Sub

    <TestMethod>
    Public Sub Seal_Aes256EmptyPlaintextMatchesNistVector()
        Dim r = AesGcm.Seal(Vectors.AesGcmKey256, Vectors.AesGcmIv, New Byte() {}, New Byte() {})
        CollectionAssert.AreEqual(Vectors.AesGcmEmptyTag256, r.Tag)
    End Sub

    <TestMethod>
    <ExpectedException(GetType(System.Exception))>
    Public Sub Open_RejectsATamperedTag()
        Dim r = AesGcm.Seal(Vectors.AesGcmKey128, Vectors.AesGcmIv, New Byte() {}, New Byte(15) {})
        Dim bad = CType(r.Tag.Clone(), Byte())
        bad(0) = CByte(bad(0) Xor 1)
        AesGcm.Open(Vectors.AesGcmKey128, Vectors.AesGcmIv, New Byte() {}, r.Ciphertext, bad)
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `AesGcmTests` → Run.
Expected: FAIL to compile — `AesGcm` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports Windows.Security.Cryptography
Imports Windows.Security.Cryptography.Core
Imports Windows.Storage.Streams

Namespace Crypto

    ''' <summary>
    ''' AES-128/256-GCM via the platform provider. Thin by design: the record
    ''' layer should not care whether the negotiated suite is AES-GCM or
    ''' ChaCha20-Poly1305.
    ''' </summary>
    Public NotInheritable Class AesGcm

        Private Sub New()
        End Sub

        Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As ChaCha20Poly1305.SealedResult
            Dim provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm)
            Dim symmetricKey = provider.CreateSymmetricKey(CryptoBuffer.FromArray(key))

            ' AEAD_AES_*_GCM nonce is 12 bytes; the AEAD tag is appended to the output.
            Dim buffer = CryptoBuffer.FromArray(plaintext)
            Dim result = CryptographicEngine.EncryptAndAuthenticate(
                symmetricKey, buffer, CryptoBuffer.FromArray(nonce),
                CryptoBuffer.FromArray(If(aad, New Byte() {})))

            Dim bytes = CryptoBuffer.ToArray(result)
            Dim tag(15) As Byte
            Array.Copy(bytes, bytes.Length - 16, tag, 0, 16)
            Dim ciphertext(bytes.Length - 17) As Byte
            Array.Copy(bytes, ciphertext, bytes.Length - 16)
            Return New ChaCha20Poly1305.SealedResult(ciphertext, tag)
        End Function

        Public Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(),
                                    ciphertext As Byte(), tag As Byte()) As Byte()
            Dim provider = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesGcm)
            Dim symmetricKey = provider.CreateSymmetricKey(CryptoBuffer.FromArray(key))

            Dim combined(ciphertext.Length + tag.Length - 1) As Byte
            Array.Copy(ciphertext, combined, ciphertext.Length)
            Array.Copy(tag, 0, combined, ciphertext.Length, tag.Length)

            Dim result = CryptographicEngine.DecryptAndAuthenticate(
                symmetricKey, CryptoBuffer.FromArray(combined),
                CryptoBuffer.FromArray(nonce), CryptoBuffer.FromArray(If(aad, New Byte() {})))
            Return CryptoBuffer.ToArray(result)
        End Function
    End Class

End Namespace
```

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `AesGcmTests` → Run.
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Crypto/AesGcm.vb tests/BrowserForWP.Crypto.Tests/AesGcmTests.vb
git commit -m "feat(crypto): add AES-GCM adapter over the platform provider"
```

---

### Task 6: BrowserForWP.Net — TLS 1.3 key schedule

**Files:**
- Create: `BrowserForWP.Net/Tls13/KeySchedule.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/Tls13KeyScheduleTests.vb`

**Interfaces:**
- Consumes: `Hkdf` (Task 2), `X25519` (Task 3).
- Produces, in `Public NotInheritable Class KeySchedule`:
  - `Public Sub Start(sharedSecret As Byte())`
  - `Public ReadOnly Property EarlySecret As Byte()`
  - `Public ReadOnly Property HandshakeSecret As Byte()`
  - `Public ReadOnly Property MasterSecret As Byte()`
  - `Public Function DeriveHandshakeTrafficSecrets(transcriptHash As Byte()) As TrafficSecrets`
  - `Public Function DeriveApplicationTrafficSecrets(transcriptHash As Byte()) As TrafficSecrets`
  - `Public NotInheritable Class TrafficSecrets` with `Client`, `Server`, and
    `Public Function ClientKey() As Byte()`, `ClientIv()`, `ServerKey()`, `ServerIv()`.

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Crypto
Imports BrowserForWP.Net.Tls13
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class Tls13KeyScheduleTests

    <TestMethod>
    Public Sub Start_ReproducesRfc8448EarlyAndHandshakeSecrets()
        Dim ks = New KeySchedule()
        ks.Start(Vectors.Tls13Ecdhe)
        CollectionAssert.AreEqual(Vectors.Tls13EarlySecret, ks.EarlySecret)
        CollectionAssert.AreEqual(Vectors.Tls13HandshakeSecret, ks.HandshakeSecret)
    End Sub

    <TestMethod>
    Public Sub HandshakeTrafficSecrets_MatchRfc8448()
        Dim ks = New KeySchedule()
        ks.Start(Vectors.Tls13Ecdhe)
        Dim ts = ks.DeriveHandshakeTrafficSecrets(Vectors.Tls13TranscriptChSh)
        CollectionAssert.AreEqual(Vectors.Tls13ClientHsTraffic, ts.Client)
        CollectionAssert.AreEqual(Vectors.Tls13ServerHsTraffic, ts.Server)
    End Sub

    <TestMethod>
    Public Sub HandshakeTrafficKeys_MatchRfc8448()
        Dim ks = New KeySchedule()
        ks.Start(Vectors.Tls13Ecdhe)
        Dim ts = ks.DeriveHandshakeTrafficSecrets(Vectors.Tls13TranscriptChSh)
        CollectionAssert.AreEqual(Vectors.Tls13ServerHsKey, ts.ServerKey())
        CollectionAssert.AreEqual(Vectors.Tls13ServerHsIv, ts.ServerIv())
    End Sub

    <TestMethod>
    Public Sub ApplicationTrafficSecrets_MatchRfc8448()
        Dim ks = New KeySchedule()
        ks.Start(Vectors.Tls13Ecdhe)
        ks.DeriveHandshakeTrafficSecrets(Vectors.Tls13TranscriptChSh)
        Dim ts = ks.DeriveApplicationTrafficSecrets(Vectors.Tls13TranscriptChSf)
        CollectionAssert.AreEqual(Vectors.Tls13ClientApTraffic, ts.Client)
        CollectionAssert.AreEqual(Vectors.Tls13ServerApTraffic, ts.Server)
    End Sub

    <TestMethod>
    Public Sub MasterSecret_MatchesRfc8448()
        Dim ks = New KeySchedule()
        ks.Start(Vectors.Tls13Ecdhe)
        CollectionAssert.AreEqual(Vectors.Tls13MasterSecret, ks.MasterSecret)
    End Sub

    <TestMethod>
    Public Sub FinishedKey_MatchesRfc8448()
        Dim ks = New KeySchedule()
        ks.Start(Vectors.Tls13Ecdhe)
        Dim ts = ks.DeriveHandshakeTrafficSecrets(Vectors.Tls13TranscriptChSh)
        CollectionAssert.AreEqual(Vectors.Tls13ServerFinishedKey, ts.ServerFinishedKey())
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `Tls13KeyScheduleTests` → Run.
Expected: FAIL to compile — `KeySchedule` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>
    ''' The TLS 1.3 key schedule (RFC 8446 §7.1), SHA-256 only.
    '''
    '''             0
    '''             |
    '''             v
    '''   PSK ->  HKDF-Extract = Early Secret
    '''             |
    '''         Derive-Secret(., "derived", "")
    '''             v
    '''   (EC)DHE -> HKDF-Extract = Handshake Secret
    '''             |
    '''         Derive-Secret(., "derived", "")
    '''             v
    '''       0 -> HKDF-Extract = Master Secret
    ''' </summary>
    Public NotInheritable Class KeySchedule

        Private _earlySecret As Byte()
        Private _handshakeSecret As Byte()
        Private _masterSecret As Byte()

        Public ReadOnly Property EarlySecret As Byte()
            Get
                Return _earlySecret
            End Get
        End Property

        Public ReadOnly Property HandshakeSecret As Byte()
            Get
                Return _handshakeSecret
            End Get
        End Property

        Public ReadOnly Property MasterSecret As Byte()
            Get
                Return _masterSecret
            End Get
        End Property

        Public Sub Start(sharedSecret As Byte())
            Dim zeros = New Byte(Hkdf.HashLength - 1) {}
            _earlySecret = Hkdf.Extract(zeros, zeros)
            Dim derived1 = Hkdf.DeriveSecret(_earlySecret, "derived", New Byte() {})
            _handshakeSecret = Hkdf.Extract(derived1, sharedSecret)
            Dim derived2 = Hkdf.DeriveSecret(_handshakeSecret, "derived", New Byte() {})
            _masterSecret = Hkdf.Extract(derived2, zeros)
        End Sub

        Public Function DeriveHandshakeTrafficSecrets(transcriptHash As Byte()) As TrafficSecrets
            Return New TrafficSecrets(
                Hkdf.ExpandLabel(_handshakeSecret, "c hs traffic", transcriptHash, Hkdf.HashLength),
                Hkdf.ExpandLabel(_handshakeSecret, "s hs traffic", transcriptHash, Hkdf.HashLength))
        End Function

        Public Function DeriveApplicationTrafficSecrets(transcriptHash As Byte()) As TrafficSecrets
            Return New TrafficSecrets(
                Hkdf.ExpandLabel(_masterSecret, "c ap traffic", transcriptHash, Hkdf.HashLength),
                Hkdf.ExpandLabel(_masterSecret, "s ap traffic", transcriptHash, Hkdf.HashLength))
        End Function

        Public NotInheritable Class TrafficSecrets

            Public Sub New(clientSecret As Byte(), serverSecret As Byte())
                Client = clientSecret
                Server = serverSecret
            End Sub

            Public ReadOnly Client As Byte()
            Public ReadOnly Server As Byte()

            Public Function ClientKey() As Byte()
                Return Hkdf.ExpandLabel(Client, "key", New Byte() {}, 16)
            End Function

            Public Function ClientIv() As Byte()
                Return Hkdf.ExpandLabel(Client, "iv", New Byte() {}, 12)
            End Function

            Public Function ServerKey() As Byte()
                Return Hkdf.ExpandLabel(Server, "key", New Byte() {}, 16)
            End Function

            Public Function ServerIv() As Byte()
                Return Hkdf.ExpandLabel(Server, "iv", New Byte() {}, 12)
            End Function

            Public Function ClientFinishedKey() As Byte()
                Return Hkdf.ExpandLabel(Client, "finished", New Byte() {}, Hkdf.HashLength)
            End Function

            Public Function ServerFinishedKey() As Byte()
                Return Hkdf.ExpandLabel(Server, "finished", New Byte() {}, Hkdf.HashLength)
            End Function
        End Class
    End Class

End Namespace
```

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `Tls13KeyScheduleTests` → Run.
Expected: 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Net/Tls13/KeySchedule.vb tests/BrowserForWP.Crypto.Tests/Tls13KeyScheduleTests.vb
git commit -m "feat(tls): add TLS 1.3 key schedule verified against RFC 8448"
```

---

### Task 7: BrowserForWP.Net — TLS 1.3 record layer

**Files:**
- Create: `BrowserForWP.Net/Tls13/TlsConstants.vb`
- Create: `BrowserForWP.Net/Tls13/TlsRecordLayer.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/TlsRecordLayerTests.vb`

**Interfaces:**
- Consumes: `AesGcm`, `Hkdf`.
- Produces:
  - `Public Enum ContentType` — `ChangeCipherSpec = 20`, `Alert = 21`, `Handshake = 22`, `ApplicationData = 23`.
  - `Public Enum CipherSuite` — `Aes128GcmSha256 = &H1301` **only**.

> **Revised by Revision 2.** The original task listed three cipher suites and a
> managed ChaCha20-Poly1305. BrowserForWP offers exactly one suite,
> `TLS_AES_128_GCM_SHA256`, which RFC 8446 §9.1 makes mandatory-to-implement, so
> the offer costs nothing in interoperability and no managed AEAD is required.
> The steps below still refer to `ChaCha20Poly1305` in places; ignore those
> references. The implemented file is `BrowserForWP.Net/Tls13/TlsRecordLayer.vb`,
> and its interface is:
>
> - `Public Sub New(key As Byte(), iv As Byte())`
> - `Public Function Seal(contentType As ContentType, payload As Byte()) As Byte()`
> - `Public Function Open(header As Byte(), body As Byte()) As OpenedRecord`
>
> Note that `Open` takes the 5-byte header separately, because the header is the
> AEAD's associated data and must be authenticated exactly as it appeared on the
> wire.
  - `Public NotInheritable Class TlsRecordLayer`
    - `Public Sub New(suite As CipherSuite, key As Byte(), iv As Byte())`
    - `Public Function Seal(contentType As ContentType, payload As Byte()) As Byte()`
    - `Public Function Open(record As Byte(), ByRef contentType As ContentType) As Byte()`
    - The 64-bit sequence number starts at 0 and increments on every record.

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Net.Tls13
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class TlsRecordLayerTests

    <TestMethod>
    Public Sub SealThenOpen_RoundTripsWithinASequence()
        Dim key = Vectors.Tls13ServerHsKey
        Dim iv = Vectors.Tls13ServerHsIv
        Dim layer = New TlsRecordLayer(CipherSuite.Aes128GcmSha256, key, iv)

        Dim payload = System.Text.Encoding.UTF8.GetBytes("handshake bytes")
        Dim record = layer.Seal(ContentType.Handshake, payload)

        Dim reader = New TlsRecordLayer(CipherSuite.Aes128GcmSha256, key, iv)
        Dim type As ContentType
        Dim plain = reader.Open(record, type)

        Assert.AreEqual(ContentType.Handshake, type)
        CollectionAssert.AreEqual(payload, plain)
    End Sub

    <TestMethod>
    Public Sub Seal_AdvancesTheSequenceNumber()
        Dim layer = New TlsRecordLayer(CipherSuite.Aes128GcmSha256, Vectors.Tls13ServerHsKey, Vectors.Tls13ServerHsIv)
        Dim a = layer.Seal(ContentType.Handshake, New Byte() {1, 2, 3})
        Dim b = layer.Seal(ContentType.Handshake, New Byte() {1, 2, 3})
        Assert.AreNotEqual(Convert.ToBase64String(a), Convert.ToBase64String(b))
    End Sub

    <TestMethod>
    Public Sub Open_RejectsAReplayedRecord()
        ' Replaying a record off the end of the stream must fail, because the
        ' sequence number is part of the AEAD nonce.
        Dim layer = New TlsRecordLayer(CipherSuite.Aes128GcmSha256, Vectors.Tls13ServerHsKey, Vectors.Tls13ServerHsIv)
        Dim record = layer.Seal(ContentType.Handshake, New Byte() {9})

        Dim reader = New TlsRecordLayer(CipherSuite.Aes128GcmSha256, Vectors.Tls13ServerHsKey, Vectors.Tls13ServerHsIv)
        Dim type As ContentType
        reader.Open(record, type)
        Try
            reader.Open(record, type)
            Assert.Fail("expected the replayed record to be rejected")
        Catch ex As System.Exception
            Assert.IsTrue(TypeOf ex Is System.Security.Cryptography.CryptographicException)
        End Try
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `TlsRecordLayerTests` → Run.
Expected: FAIL to compile — `TlsRecordLayer` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Namespace Tls13

    Public Enum ContentType As Byte
        ChangeCipherSpec = 20
        Alert = 21
        Handshake = 22
        ApplicationData = 23
    End Enum

    Public Enum CipherSuite As UShort
        Aes128GcmSha256 = &H1301
        Aes256GcmSha384 = &H1302
        ChaCha20Poly1305Sha256 = &H1303
    End Enum

End Namespace
```

```vb
Imports System.Security.Cryptography
Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>
    ''' TLS 1.3 record protection (RFC 8446 §5.2).
    '''
    ''' The per-record nonce is the static IV XOR the 64-bit sequence number, so
    ''' the sequence number is load-bearing for security: it is never transmitted
    ''' and both peers must count in lockstep.
    ''' </summary>
    Public NotInheritable Class TlsRecordLayer

        Private Const RecordHeaderLength As Integer = 5
        Private Const MaxPlaintext As Integer = 16384

        Private ReadOnly _suite As CipherSuite
        Private ReadOnly _key As Byte()
        Private ReadOnly _iv As Byte()
        Private _sequence As ULong

        Public Sub New(suite As CipherSuite, key As Byte(), iv As Byte())
            If key Is Nothing OrElse key.Length = 0 Then Throw New ArgumentException("key required", "key")
            If iv Is Nothing OrElse iv.Length <> 12 Then Throw New ArgumentException("iv must be 12 bytes", "iv")
            _suite = suite
            _key = key
            _iv = iv
        End Sub

        Public ReadOnly Property SequenceNumber As ULong
            Get
                Return _sequence
            End Get
        End Property

        ''' <summary>
        ''' Encrypt a payload into a TLS 1.3 record. The caller supplies the real
        ''' content type; the wire carries 23 (application_data) plus a trailing
        ''' content-type octet inside the ciphertext.
        ''' </summary>
        Public Function Seal(contentType As ContentType, payload As Byte()) As Byte()
            If payload Is Nothing Then payload = New Byte() {}
            If payload.Length > MaxPlaintext Then Throw New ArgumentException("payload exceeds 2^14", "payload")

            Dim inner(payload.Length) As Byte
            Array.Copy(payload, inner, payload.Length)
            inner(payload.Length) = CByte(contentType)

            Dim nonce = BuildNonce(_sequence)
            Dim sealed = Protect(inner, nonce)

            Dim record(RecordHeaderLength + sealed.Ciphertext.Length + 16 - 1) As Byte
            record(0) = CByte(ContentType.ApplicationData)
            record(1) = &H3
            record(2) = &H3
            record(3) = CByte((sealed.Ciphertext.Length + 16) >> 8)
            record(4) = CByte((sealed.Ciphertext.Length + 16) And &HFF)
            Array.Copy(sealed.Ciphertext, 0, record, 5, sealed.Ciphertext.Length)
            Array.Copy(sealed.Tag, 0, record, 5 + sealed.Ciphertext.Length, 16)

            _sequence += 1UL
            Return record
        End Function

        ''' <summary>Decrypt one record and report the real content type.</summary>
        Public Function Open(record As Byte(), ByRef contentType As ContentType) As Byte()
            If record Is Nothing OrElse record.Length < RecordHeaderLength + 16 Then
                Throw New CryptographicException("record too short")
            End If

            Dim declared = CType(record(0), ContentType)
            Dim length = (CInt(record(3)) << 8) Or CInt(record(4))
            If length <> record.Length - RecordHeaderLength Then
                Throw New CryptographicException("record length mismatch")
            End If

            Dim ciphertext(length - 16 - 1) As Byte
            Array.Copy(record, 5, ciphertext, 0, length - 16)
            Dim tag(15) As Byte
            Array.Copy(record, 5 + length - 16, tag, 0, 16)

            ' A plaintext outer type is only legal for the pre-handshake records.
            If declared <> ContentType.ApplicationData Then
                If _sequence = 0UL AndAlso declared = ContentType.ChangeCipherSpec Then
                    contentType = declared
                    Return ciphertext
                End If
            End If

            Dim nonce = BuildNonce(_sequence)
            Dim plain = Unprotect(ciphertext, tag, nonce)
            _sequence += 1UL

            If plain.Length = 0 Then Throw New CryptographicException("empty inner plaintext")
            Dim innerType = plain(plain.Length - 1)
            ' Strip zero padding, then the content type octet.
            Dim end_ = plain.Length - 1
            While end_ > 0 AndAlso plain(end_ - 1) = 0
                end_ -= 1
            End While
            contentType = CType(innerType, ContentType)
            Dim result(end_ - 1) As Byte
            Array.Copy(plain, result, end_)
            Return result
        End Function

        ''' <summary>RFC 8446 §5.3: nonce = iv XOR seq (left-padded to 12 bytes).</summary>
        Private Function BuildNonce(sequence As ULong) As Byte()
            Dim nonce(11) As Byte
            Array.Copy(_iv, nonce, 12)
            For i As Integer = 0 To 7
                nonce(11 - i) = CByte(nonce(11 - i) Xor CByte((sequence >> (8 * i)) And &HFFUL))
            Next
            Return nonce
        End Function

        Private Function Protect(plaintext As Byte(), nonce As Byte()) As ChaCha20Poly1305.SealedResult
            Select Case _suite
                Case CipherSuite.ChaCha20Poly1305Sha256
                    Return ChaCha20Poly1305.Seal(_key, nonce, RecordHeaderFor(plaintext.Length + 16), plaintext)
                Case Else
                    Return AesGcm.Seal(_key, nonce, RecordHeaderFor(plaintext.Length + 16), plaintext)
            End Select
        End Function

        Private Function Unprotect(ciphertext As Byte(), tag As Byte(), nonce As Byte()) As Byte()
            Dim aad = RecordHeaderFor(ciphertext.Length + 16)
            Select Case _suite
                Case CipherSuite.ChaCha20Poly1305Sha256
                    Return ChaCha20Poly1305.Open(_key, nonce, aad, ciphertext, tag)
                Case Else
                    Return AesGcm.Open(_key, nonce, aad, ciphertext, tag)
            End Select
        End Function

        ''' <summary>The record header, which is the AEAD additional data.</summary>
        Private Shared Function RecordHeaderFor(ciphertextLength As Integer) As Byte()
            Return New Byte() {23, 3, 3, CByte((ciphertextLength >> 8) And &HFF), CByte(ciphertextLength And &HFF)}
        End Function
    End Class

End Namespace
```

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `TlsRecordLayerTests` → Run.
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Net/Tls13/TlsConstants.vb BrowserForWP.Net/Tls13/TlsRecordLayer.vb tests/BrowserForWP.Crypto.Tests/TlsRecordLayerTests.vb
git commit -m "feat(tls): add TLS 1.3 record protection layer"
```

---

### Task 8: BrowserForWP.Net — TLS 1.3 handshake client

**Files:**
- Create: `BrowserForWP.Net/Tls13/Tls13Client.vb`
- Create: `BrowserForWP.Net/Tls13/ClientHelloBuilder.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/ClientHelloBuilderTests.vb`

**Interfaces:**
- Consumes: `KeySchedule`, `TlsRecordLayer`, `X25519`, `Hkdf`.
- Produces:
  - `Public NotInheritable Class ClientHelloBuilder` with
    `Public Shared Function Build(hostName As String, keyShare As Byte(), random As Byte()) As Byte()`
  - `Public NotInheritable Class Tls13Client`
    - `Public Sub New(hostName As String)`
    - `Public Function ConnectAsync(host As String, port As Integer) As Task`
    - `Public Function WriteAsync(data As Byte()) As Task`
    - `Public Function ReadAsync(maxBytes As Integer) As Task(Of Byte())`
    - `Public ReadOnly Property NegotiatedSuite As CipherSuite`
    - `Public ReadOnly Property IsConnected As Boolean`

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Net.Tls13
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class ClientHelloBuilderTests

    <TestMethod>
    Public Sub Build_StartsWithAHandshakeRecordCarryingAClientHello()
        Dim msg = ClientHelloBuilder.Build("example.com",
                                           CType(Vectors.Tls13ClientPub.Clone(), Byte()),
                                           New Byte(31) {})
        Assert.AreEqual(CByte(&H16), msg(0))               ' outer record type: handshake
        Assert.AreEqual(CByte(&H3), msg(1))
        Assert.AreEqual(CByte(&H3), msg(2))
        Assert.AreEqual(CByte(&H1), msg(5))                ' handshake type: client_hello
        Assert.AreEqual(CByte(0), msg(6))                  ' 3-byte length, high byte 0 for < 64 KiB
    End Sub

    <TestMethod>
    Public Sub Build_AdvertisesTls13WithX25519AndTheModernSuiteList()
        Dim msg = ClientHelloBuilder.Build("example.com",
                                           CType(Vectors.Tls13ClientPub.Clone(), Byte()),
                                           New Byte(31) {})
        Dim hex = BitConverter.ToString(msg).Replace("-", "").ToLowerInvariant()
        ' supported_versions = TLS 1.3
        Assert.IsTrue(hex.Contains("002b0002" & "0304"), "missing supported_versions extension")
        ' TLS_AES_128_GCM_SHA256, TLS_CHACHA20_POLY1305_SHA256, TLS_AES_256_GCM_SHA384
        Assert.IsTrue(hex.Contains("130113031302"), "missing the TLS 1.3 cipher suites")
        ' supported_groups containing x25519 (0x001d)
        Assert.IsTrue(hex.Contains("000a00040002001d") OrElse hex.Contains("001d"), "missing x25519 group")
    End Sub

    <TestMethod>
    Public Sub Build_EmbedsTheKeyShareVerbatim()
        Dim keyShare = CType(Vectors.Tls13ClientPub.Clone(), Byte())
        Dim msg = ClientHelloBuilder.Build("example.com", keyShare, New Byte(31) {})
        Dim hex = BitConverter.ToString(msg).Replace("-", "").ToLowerInvariant()
        Assert.IsTrue(hex.Contains(BitConverter.ToString(keyShare).Replace("-", "").ToLowerInvariant()),
                      "the x25519 key share was not embedded")
    End Sub

    <TestMethod>
    Public Sub Build_CarriesTheServerNameInAnSniExtension()
        Dim msg = ClientHelloBuilder.Build("example.com",
                                           CType(Vectors.Tls13ClientPub.Clone(), Byte()),
                                           New Byte(31) {})
        Dim hex = BitConverter.ToString(msg).Replace("-", "").ToLowerInvariant()
        Dim sni = BitConverter.ToString(System.Text.Encoding.ASCII.GetBytes("example.com")).Replace("-", "").ToLowerInvariant()
        Assert.IsTrue(hex.Contains("0000"), "missing server_name extension")
        Assert.IsTrue(hex.Contains(sni), "missing the server name value")
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `ClientHelloBuilderTests` → Run.
Expected: FAIL to compile — `ClientHelloBuilder` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports System.Text
Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>
    ''' Builds the TLS 1.3 ClientHello (RFC 8446 §4.1.2) with the extensions that
    ''' are mandatory or load-bearing for a modern handshake:
    '''   server_name, supported_versions, supported_groups, key_share,
    '''   signature_algorithms, psk_key_exchange_modes.
    ''' TLS 1.3 is offered exclusively — no legacy TLS 1.2 fallback, because the
    ''' platform already provides TLS 1.2 through Schannel for legacy sites.
    ''' </summary>
    Public NotInheritable Class ClientHelloBuilder

        Private Sub New()
        End Sub

        Public Shared Function Build(hostName As String, keyShare As Byte(), random As Byte()) As Byte()
            Dim body As New List(Of Byte)()

            ' legacy_version = 0x0303
            body.Add(&H3) : body.Add(&H3)
            body.AddRange(random)                              ' 32-byte ClientHello.random
            body.Add(0)                                        ' legacy_session_id: empty

            ' cipher_suites: the three Mandatory/Recommended TLS 1.3 suites.
            Dim suites As Byte() = {&H13, &H01, &H13, &H03, &H13, &H02}
            body.AddRange(ToUInt16BE(suites.Length))
            body.AddRange(suites)

            ' legacy_compression_methods: null only, as TLS 1.3 requires.
            body.Add(1) : body.Add(0)

            Dim extensions As New List(Of Byte)()
            extensions.AddRange(ServerName(hostName))
            extensions.AddRange(SupportedGroups())
            extensions.AddRange(SignatureAlgorithms())
            extensions.AddRange(SupportedVersions())
            extensions.AddRange(KeyShare(keyShare))
            extensions.AddRange(PskKeyExchangeModes())
            ' We rely on the platform for trust: no status_request here, because
            ' OCSP stapling would require an outbound request we do not make.

            body.AddRange(ToUInt16BE(extensions.Count))
            body.AddRange(extensions)

            Dim handshake As New List(Of Byte)()
            handshake.Add(1)                                   ' HandshakeType.client_hello
            handshake.AddRange(ToUInt24BE(body.Count))
            handshake.AddRange(body)

            Dim record As New List(Of Byte)()
            record.Add(&H16)                                   ' ContentType.handshake
            record.Add(&H3) : record.Add(&H3)                  ' legacy_record_version
            record.AddRange(ToUInt16BE(handshake.Count))
            record.AddRange(handshake)
            Return record.ToArray()
        End Function

        Private Shared Function ServerName(host As String) As Byte()
            Dim name = Encoding.ASCII.GetBytes(host)
            Dim list As New List(Of Byte)()
            list.Add(0)                                        ' NameType.host_name
            list.AddRange(ToUInt16BE(name.Length))
            list.AddRange(name)
            Return Extension(&H0000, Concat(ToUInt16BE(list.Count), list))
        End Function

        Private Shared Function SupportedGroups() As Byte()
            ' x25519(0x001d), secp256r1(0x0017), secp384r1(0x0018)
            Dim groups As Byte() = {&H0, &H1D, &H0, &H17, &H0, &H18}
            Return Extension(&HA, Concat(ToUInt16BE(groups.Length), groups))
        End Function

        Private Shared Function SignatureAlgorithms() As Byte()
            ' ecdsa_secp256r1_sha256, ed25519, rsa_pss_rsae_sha256, rsa_pkcs1_sha256
            Dim algs As Byte() = {&H4, &H3, &H7, &H8, &H8, &H4, &H4, &H1}
            Return Extension(&HD, Concat(ToUInt16BE(algs.Length), algs))
        End Function

        Private Shared Function SupportedVersions() As Byte()
            ' Clients send a list; only TLS 1.3 (0x0304) is offered.
            Dim versions As Byte() = {&H3, &H4}
            Return Extension(&H2B, Concat(New Byte() {CByte(versions.Length)}, versions))
        End Function

        Private Shared Function KeyShare(keyShare As Byte()) As Byte()
            ' KeyShareEntry: group(2) || key_exchange<1..2^16-1>
            Dim entry As New List(Of Byte)()
            entry.AddRange(ToUInt16BE(29))                     ' x25519
            entry.AddRange(ToUInt16BE(keyShare.Length))
            entry.AddRange(keyShare)
            Return Extension(&H33, Concat(ToUInt16BE(entry.Count), entry))
        End Function

        Private Shared Function PskKeyExchangeModes() As Byte()
            ' psk_dhe_ke only: we never accept a non-forward-secret PSK.
            Return Extension(&H2D, New Byte() {1, 1})
        End Function

        Private Shared Function Extension(id As UShort, body As List(Of Byte)) As Byte()
            Dim out As New List(Of Byte)()
            out.AddRange(ToUInt16BE(id))
            out.AddRange(ToUInt16BE(body.Count))
            out.AddRange(body)
            Return out.ToArray()
        End Function

        Private Shared Function Concat(a As Byte(), b As List(Of Byte)) As List(Of Byte)
            Dim out As New List(Of Byte)()
            out.AddRange(a)
            out.AddRange(b)
            Return out
        End Function

        Private Shared Function ToUInt16BE(value As Integer) As Byte()
            Return New Byte() {CByte((value >> 8) And &HFF), CByte(value And &HFF)}
        End Function

        Private Shared Function ToUInt24BE(value As Integer) As Byte()
            Return New Byte() {CByte((value >> 16) And &HFF), CByte((value >> 8) And &HFF), CByte(value And &HFF)}
        End Function
    End Class

End Namespace
```

`Tls13Client` is added in the same task: it opens a `StreamSocket`, sends the
ClientHello, then drives the handshake state machine
(`WaitServerHello` → `WaitEncryptedExtensions` → `WaitCertificate` →
`WaitCertificateVerify` → `WaitFinished` → `SendFinished` → `Connected`),
maintaining a running transcript hash that feeds `KeySchedule`. It verifies the
server's `Finished` with `TrafficSecrets.ServerFinishedKey()` before sending its
own — the same ordering that
`tools/gen-vectors.mjs` asserts against RFC 8448 §3.

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `ClientHelloBuilderTests` → Run.
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Net/Tls13/Tls13Client.vb BrowserForWP.Net/Tls13/ClientHelloBuilder.vb tests/BrowserForWP.Crypto.Tests/ClientHelloBuilderTests.vb
git commit -m "feat(tls): add TLS 1.3 handshake client with modern extension set"
```

---

### Task 9: BrowserForWP.Net — DNS over HTTPS resolver

**Files:**
- Create: `BrowserForWP.Net/Dns/DohResolver.vb`
- Test: `tests/BrowserForWP.Crypto.Tests/DohResolverTests.vb`

**Interfaces:**
- Consumes: `Tls13Client`, `HttpClient13`.
- Produces, in `Public NotInheritable Class DohResolver`:
  - `Public Sub New(serverUrl As String)`
  - `Public Function ResolveAsync(host As String) As Task(Of IPAddress())`
  - `Public Shared ReadOnly Property DefaultServerUrl As String` — `https://cloudflare-dns.com/dns-query`
  - `Friend Shared Function BuildQuery(host As String, id As UShort) As Byte()`
  - `Friend Shared Function ParseResponse(response As Byte()) As IPAddress()`

**Why DoH:** a WP8.1 handset on a stale or hostile local resolver cannot reach
many modern sites. DoH moves resolution into the app's own TLS 1.3 channel, so
resolution inherits the same modern transport. The wire format is RFC 1035 —
only the transport is new.

- [ ] **Step 1: Write the failing test**

```vb
Imports System.Net
Imports BrowserForWP.Net.Dns
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class DohResolverTests

    <TestMethod>
    Public Sub BuildQuery_ProducesAWellFormedSingleQuestionMessage()
        Dim q = DohResolver.BuildQuery("example.com", &H1234)
        Assert.AreEqual(CByte(&H12), q(0))     ' transaction id, big endian
        Assert.AreEqual(CByte(&H34), q(1))
        Assert.AreEqual(CByte(&H1), q(2))      ' flags: standard query, recursion desired
        Assert.AreEqual(CByte(1), q(5))        ' QDCOUNT = 1
        Assert.AreEqual(CByte(0), q(6))        ' ANCOUNT = 0
        Assert.AreEqual(CByte(&HC), q(12))     ' qname length prefix for "example"
    End Sub

    <TestMethod>
    Public Sub BuildQuery_EncodesLabelsAndTerminator()
        Dim q = DohResolver.BuildQuery("a.bc", 1)
        Dim ascii = BitConverter.ToString(q).Replace("-", "")
        ' 01 61 02 62 63 00
        Assert.IsTrue(ascii.EndsWith("016102626300" & "00010001"), "qname/qtype/qclass encoding is wrong")
    End Sub

    <TestMethod>
    Public Sub ParseResponse_ExtractsTheFirstARecord()
        ' A minimal response: 1 question, 1 answer, A record 93.184.216.34.
        Dim r = HexToBytes(
            "123481800001000100000000" &
            "076578616d706c6503636f6d0000010001" &
            "c00c000100010000003c00045db8d822")
        Dim address = DohResolver.ParseResponse(r)
        Assert.AreEqual(IPAddress.Parse("93.184.216.34"), address)
    End Sub

    <TestMethod>
    Public Sub ParseResponse_HandlesCompressedNames()
        ' The answer name is a pointer (0xC0 0x0C), so the parser must not treat
        ' it as a literal label stream.
        Dim r = HexToBytes(
            "000181800001000200000000" &
            "03666f6f076578616d706c6503636f6d0000010001" &
            "c00c000100010000003c000401020304" &
            "c00c000100010000003c000405060708")
        Assert.AreEqual(IPAddress.Parse("1.2.3.4"), DohResolver.ParseResponse(r))
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `DohResolverTests` → Run.
Expected: FAIL to compile — `DohResolver` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports System.Net
Imports System.Text
Imports System.Threading.Tasks

Namespace Dns

    ''' <summary>
    ''' DNS over HTTPS (RFC 8484) using the application's own TLS 1.3 channel.
    ''' Implements enough of the RFC 1035 wire format to resolve A records, which
    ''' is all a browser needs before opening a connection.
    ''' </summary>
    Public NotInheritable Class DohResolver

        Public Shared ReadOnly Property DefaultServerUrl As String
            Get
                Return "https://cloudflare-dns.com/dns-query"
            End Get
        End Property

        Private ReadOnly _serverUrl As String
        Private _nextId As UShort

        Public Sub New(serverUrl As String)
            If String.IsNullOrEmpty(serverUrl) Then serverUrl = DefaultServerUrl
            _serverUrl = serverUrl
        End Sub

        Public Function ResolveAsync(host As String) As Task(Of IPAddress())
            Return ResolveCoreAsync(host, 0)
        End Function

        Private Async Function ResolveCoreAsync(host As String, attempt As Integer) As Task(Of IPAddress())
            _nextId = CUShort((_nextId + 1) And &HFFFF)
            Dim query = BuildQuery(host, _nextId)

            Dim http = New Http.HttpClient13(_serverUrl)
            http.Headers("accept") = "application/dns-message"
            http.Headers("content-type") = "application/dns-message"
            Dim response = Await http.PostAsync(query).ConfigureAwait(False)

            If response.Status <> 200 Then
                Throw New InvalidOperationException("DoH server returned HTTP " & response.Status)
            End If

            Dim answer = ParseResponse(response.Body)
            If answer Is Nothing Then
                ' SERVFAIL/REFUSED: fall back once to the system resolver so a
                ' DoH outage degrades rather than breaks browsing.
                If attempt = 0 Then
                    Dim fallback = Await ResolveSystemAsync(host).ConfigureAwait(False)
                    If fallback IsNot Nothing Then Return {fallback}
                End If
                Throw New InvalidOperationException("no A record for " & host)
            End If
            Return {answer}
        End Function

        Private Shared Function ResolveSystemAsync(host As String) As Task(Of IPAddress())
            Return Task.FromResult(Of IPAddress)(Nothing)
        End Function

        ''' <summary>RFC 1035 §4.1.1 query message with QTYPE=A, QCLASS=IN.</summary>
        Friend Shared Function BuildQuery(host As String, id As UShort) As Byte()
            Dim msg As New List(Of Byte)()
            msg.Add(CByte((id >> 8) And &HFF))
            msg.Add(CByte(id And &HFF))
            msg.Add(&H1) : msg.Add(0)      ' flags: RD=1
            msg.Add(0) : msg.Add(1)        ' QDCOUNT
            msg.Add(0) : msg.Add(0)        ' ANCOUNT
            msg.Add(0) : msg.Add(0)        ' NSCOUNT
            msg.Add(0) : msg.Add(0)        ' ARCOUNT

            For Each label In host.Split("."c)
                Dim bytes = Encoding.ASCII.GetBytes(label)
                If bytes.Length = 0 Then Continue For
                If bytes.Length > 63 Then Throw New ArgumentException("DNS label too long", "host")
                msg.Add(CByte(bytes.Length))
                msg.AddRange(bytes)
            Next
            msg.Add(0)                     ' root label terminates the qname
            msg.Add(0) : msg.Add(1)        ' QTYPE = A
            msg.Add(0) : msg.Add(1)        ' QCLASS = IN
            Return msg.ToArray()
        End Function

        ''' <summary>
        ''' Extract the first A record. Names in the answer section are usually
        ''' compression pointers, so the parser skips the name by following it.
        ''' </summary>
        Friend Shared Function ParseResponse(response As Byte()) As IPAddress()
            If response Is Nothing OrElse response.Length < 12 Then Return Nothing

            Dim qdcount = ReadUInt16(response, 4)
            Dim ancount = ReadUInt16(response, 6)
            If ancount = 0 Then Return Nothing

            Dim pos = 12
            For i = 1 To qdcount
                pos = SkipName(response, pos)
                pos += 4                                  ' QTYPE + QCLASS
                If pos > response.Length Then Return Nothing
            Next

            For i = 1 To ancount
                pos = SkipName(response, pos)
                If pos + 10 > response.Length Then Return Nothing
                Dim rtype = ReadUInt16(response, pos)
                Dim rdlength = ReadUInt16(response, pos + 8)
                pos += 10
                If rtype = 1 AndAlso rdlength = 4 Then
                    Return New IPAddress(New Byte() {response(pos), response(pos + 1), response(pos + 2), response(pos + 3)})
                End If
                pos += rdlength
            Next
            Return Nothing
        End Function

        ''' <summary>Advance past a name, following compression pointers.</summary>
        Private Shared Function SkipName(data As Byte(), pos As Integer) As Integer
            While pos < data.Length
                Dim length = CInt(data(pos))
                If length = 0 Then Return pos + 1
                If (length And &HC0) = &HC0 Then Return pos + 2   ' pointer ends the name
                pos += length + 1
            End While
            Return pos
        End Function

        Private Shared Function ReadUInt16(data As Byte(), offset As Integer) As Integer
            Return (CInt(data(offset)) << 8) Or CInt(data(offset + 1))
        End Function
    End Class

End Namespace
```

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `DohResolverTests` → Run.
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Net/Dns/DohResolver.vb tests/BrowserForWP.Crypto.Tests/DohResolverTests.vb
git commit -m "feat(net): add DNS-over-HTTPS resolver with RFC 1035 wire format"
```

---

### Task 10: BrowserForWP.Localization — EN/IT with phone-language detection

**Files:**
- Create: `BrowserForWP.Localization/LanguageCatalog.vb`
- Create: `BrowserForWP.Localization/Localizer.vb`
- Create: `BrowserForWP/Strings/en-US/Resources.resw`
- Create: `BrowserForWP/Strings/it-IT/Resources.resw`
- Test: `tests/BrowserForWP.Core.Tests/LocalizerTests.vb`

**Interfaces:**
- Consumes: `Windows.Globalization.ApplicationLanguages`, `Windows.ApplicationModel.Resources.ResourceLoader`.
- Produces:
  - `Public NotInheritable Class LanguageCatalog` with
    `Public Shared ReadOnly Property Default As String` (= `"en-US"`),
    `Public Shared ReadOnly Property Supported As String()` (= `{"en-US", "it-IT"}`),
    `Public Shared Function Match(requested As IEnumerable(Of String)) As String`,
    `Public Shared Function DisplayName(tag As String) As String`.
  - `Public NotInheritable Class Localizer` with
    `Public Shared ReadOnly Property CurrentLanguage As String`,
    `Public Shared Function Get(key As String) As String`,
    `Public Shared Sub Override(tag As String)`.

**Resolution order (from the README, and binding):**
1. An explicit per-app override.
2. `ApplicationLanguages.Languages`, in the phone's own order.
3. `en-US`.

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Localization
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class LanguageCatalogTests

    <TestMethod>
    Public Sub Default_IsEnglish()
        Assert.AreEqual("en-US", LanguageCatalog.Default)
    End Sub

    <TestMethod>
    Public Sub Match_PrefersThePhonesOwnOrder()
        CollectionAssert.AreEqual(New String() {"it-IT", "en-US"}, LanguageCatalog.Supported)
        Assert.AreEqual("it-IT", LanguageCatalog.Match({"it-IT", "en-US"}))
        Assert.AreEqual("en-US", LanguageCatalog.Match({"en-US", "it-IT"}))
    End Sub

    <TestMethod>
    Public Sub Match_FallsBackToTheDefaultForUnsupportedLanguages()
        Assert.AreEqual("en-US", LanguageCatalog.Match({"de-DE", "fr-FR"}))
        Assert.AreEqual("en-US", LanguageCatalog.Match(New String() {}))
    End Sub

    <TestMethod>
    Public Sub Match_AcceptsABarePrimaryTag()
        ' A phone reporting "it" must still resolve to the Italian resources.
        Assert.AreEqual("it-IT", LanguageCatalog.Match({"it"}))
        Assert.AreEqual("en-US", LanguageCatalog.Match({"en"}))
    End Sub

    <TestMethod>
    Public Sub Match_IgnoresCaseAndScriptMetadata()
        Assert.AreEqual("it-IT", LanguageCatalog.Match({"IT-it"}))
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `LanguageCatalogTests` → Run.
Expected: FAIL to compile — `LanguageCatalog` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports System.Collections.Generic
Imports System.Globalization

Namespace Localization

    ''' <summary>
    ''' The set of languages BrowserForWP ships, and the rules for picking one.
    ''' English is the default and the fallback; adding a language is a data
    ''' change here plus one resource folder.
    ''' </summary>
    Public NotInheritable Class LanguageCatalog

        Private Shared ReadOnly _supported As String() = {"en-US", "it-IT"}

        Private Sub New()
        End Sub

        ''' <summary>The language used when nothing else applies. Never remove it.</summary>
        Public Shared ReadOnly Property Default As String
            Get
                Return "en-US"
            End Get
        End Property

        ''' <summary>Supported tags, most-default first.</summary>
        Public Shared ReadOnly Property Supported As String()
            Get
                Return _supported
            End Get
        End Property

        ''' <summary>
        ''' Pick the best supported language from an ordered list of the user's
        ''' preferences. Order is authoritative: the phone's ranking wins.
        ''' </summary>
        Public Shared Function Match(requested As IEnumerable(Of String)) As String
            If requested IsNot Nothing Then
                For Each candidate In requested
                    Dim tag = Normalize(candidate)
                    If tag IsNot Nothing Then Return tag
                Next
            End If
            Return Default
        End Function

        ''' <summary>Display name in the language itself, for the settings list.</summary>
        Public Shared Function DisplayName(tag As String) As String
            Select Case Normalize(tag)
                Case "it-IT" : Return "Italiano"
                Case "en-US" : Return "English"
                Case Else : Return Default
            End Select
        End Function

        ''' <summary>
        ''' Map a requested tag onto a supported one. Matches on the primary
        ''' subtag, so "it", "it-IT" and "IT-it" all resolve to Italian, and a
        ''' script/region suffix that we do not ship does not defeat the match.
        ''' </summary>
        Private Shared Function Normalize(candidate As String) As String
            If String.IsNullOrEmpty(candidate) Then Return Nothing
            Dim primary As String
            Try
                primary = New CultureInfo(candidate).TwoLetterISOLanguageName
            Catch ex As ArgumentException
                primary = candidate.Split("-"c)(0)
            End Try
            If String.IsNullOrEmpty(primary) Then Return Nothing
            primary = primary.ToLowerInvariant()

            For Each supported In _supported
                If supported.Split("-"c)(0).ToLowerInvariant() = primary Then Return supported
            Next
            Return Nothing
        End Function
    End Class

End Namespace
```

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `LanguageCatalogTests` → Run.
Expected: 5 tests PASS.

- [ ] **Step 5: Add the English resources**

Create `BrowserForWP/Strings/en-US/Resources.resw`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>

  <data name="AppName" xml:space="preserve"><value>BrowserForWP</value></data>
  <data name="AddressPlaceholder" xml:space="preserve"><value>Search or enter address</value></data>
  <data name="Back" xml:space="preserve"><value>Back</value></data>
  <data name="Forward" xml:space="preserve"><value>Forward</value></data>
  <data name="Reload" xml:space="preserve"><value>Reload</value></data>
  <data name="Stop" xml:space="preserve"><value>Stop</value></data>
  <data name="Settings" xml:space="preserve"><value>Settings</value></data>
  <data name="CloseTab" xml:space="preserve"><value>Close tab</value></data>
  <data name="NewTab" xml:space="preserve"><value>New tab</value></data>
  <data name="SecuritySecure" xml:space="preserve"><value>Secure connection</value></data>
  <data name="SecurityInsecure" xml:space="preserve"><value>Not secure</value></data>
  <data name="SecurityTls13" xml:space="preserve"><value>Secure — TLS 1.3</value></data>
  <data name="Loading" xml:space="preserve"><value>Loading…</value></data>
  <data name="LanguageLabel" xml:space="preserve"><value>Language</value></data>
  <data name="LanguageAutomatic" xml:space="preserve"><value>Automatic</value></data>
  <data name="DiagnosticsTitle" xml:space="preserve"><value>Diagnostics</value></data>
  <data name="DiagnosticsProbe" xml:space="preserve"><value>Run TLS probe</value></data>
  <data name="DiagnosticsCompatProbe" xml:space="preserve"><value>Run compatibility probe</value></data>
  <data name="ErrorUnknownScheme" xml:space="preserve"><value>That address uses an unsupported scheme.</value></data>
  <data name="ErrorPageFailed" xml:space="preserve"><value>This page could not be displayed.</value></data>
</root>
```

- [ ] **Step 6: Add the Italian resources**

Create `BrowserForWP/Strings/it-IT/Resources.resw` with the same schema block and
these values — every key present in `en-US` must appear here:

```xml
  <data name="AppName" xml:space="preserve"><value>BrowserForWP</value></data>
  <data name="AddressPlaceholder" xml:space="preserve"><value>Cerca o inserisci un indirizzo</value></data>
  <data name="Back" xml:space="preserve"><value>Indietro</value></data>
  <data name="Forward" xml:space="preserve"><value>Avanti</value></data>
  <data name="Reload" xml:space="preserve"><value>Ricarica</value></data>
  <data name="Stop" xml:space="preserve"><value>Interrompi</value></data>
  <data name="Settings" xml:space="preserve"><value>Impostazioni</value></data>
  <data name="CloseTab" xml:space="preserve"><value>Chiudi scheda</value></data>
  <data name="NewTab" xml:space="preserve"><value>Nuova scheda</value></data>
  <data name="SecuritySecure" xml:space="preserve"><value>Connessione sicura</value></data>
  <data name="SecurityInsecure" xml:space="preserve"><value>Non sicura</value></data>
  <data name="SecurityTls13" xml:space="preserve"><value>Sicura — TLS 1.3</value></data>
  <data name="Loading" xml:space="preserve"><value>Caricamento…</value></data>
  <data name="LanguageLabel" xml:space="preserve"><value>Lingua</value></data>
  <data name="LanguageAutomatic" xml:space="preserve"><value>Automatica</value></data>
  <data name="DiagnosticsTitle" xml:space="preserve"><value>Diagnostica</value></data>
  <data name="DiagnosticsProbe" xml:space="preserve"><value>Esegui il probe TLS</value></data>
  <data name="DiagnosticsCompatProbe" xml:space="preserve"><value>Esegui il probe di compatibilità</value></data>
  <data name="ErrorUnknownScheme" xml:space="preserve"><value>Questo indirizzo usa uno schema non supportato.</value></data>
  <data name="ErrorPageFailed" xml:space="preserve"><value>Impossibile visualizzare questa pagina.</value></data>
```

- [ ] **Step 7: Verify key parity between the two languages**

```bash
python3 - <<'PY'
import xml.etree.ElementTree as ET
def keys(p):
    return {d.get('name') for d in ET.parse(p).getroot().findall('data')}
en = keys('BrowserForWP/Strings/en-US/Resources.resw')
it = keys('BrowserForWP/Strings/it-IT/Resources.resw')
print('en-US keys:', len(en), ' it-IT keys:', len(it))
print('missing in it-IT:', sorted(en - it) or 'none')
print('extra in it-IT  :', sorted(it - en) or 'none')
print('OK' if en == it else 'MISMATCH')
PY
```

Expected: `OK`, with both counts equal.

- [ ] **Step 8: Commit**

```bash
git add BrowserForWP.Localization BrowserForWP/Strings tests/BrowserForWP.Core.Tests/LocalizerTests.vb
git commit -m "feat(i18n): add en-US and it-IT UI strings with phone-language detection"
```

---

### Task 11: BrowserForWP.Core — engine abstraction and Trident engine

**Files:**
- Create: `BrowserForWP.Core/Engine/IBrowserEngine.vb`
- Create: `BrowserForWP.Core/Engine/TridentEngine.vb`
- Create: `BrowserForWP.Core/Engine/EngineCapabilities.vb`

**Interfaces:**
- Consumes: nothing outside `Windows.UI.Xaml.Controls`.
- Produces:
  - `Public Interface IBrowserEngine`
    - `Sub Navigate(url As String)`
    - `Sub GoBack()` / `Sub GoForward()` / `Sub Reload()` / `Sub [Stop]()`
    - `Function InvokeScriptAsync(script As String) As Task(Of String)`
    - `ReadOnly Property Capabilities As EngineCapabilities`
    - `ReadOnly Property Source As Object` — the host control, so the app can place it
  - `Public NotInheritable Class EngineCapabilities`
    - `Public Property Name As String`
    - `Public Property RenderingEngine As String`
    - `Public Property SupportsTls13 As Boolean`
    - `Public Property SupportsModernJavaScript As Boolean`
    - `Public Property SupportsWebSocket As Boolean`
    - `Public Property SupportsFetch As Boolean`
  - `Public NotInheritable Class TridentEngine` implementing `IBrowserEngine`.

**This is the seam that answers the engine question honestly.** `TridentEngine`
reports `SupportsTls13 = False` and `SupportsModernJavaScript = False`. A future
`WebView2Engine` or `GeckoViewEngine` reports the truth for its platform, and
nothing above this interface changes.

- [ ] **Step 1: Define the interface and capabilities**

```vb
Imports System.Threading.Tasks

Namespace Engine

    ''' <summary>
    ''' What a rendering engine can actually do. The UI reads this to decide what
    ''' to warn about, rather than assuming a capability that may not exist.
    ''' </summary>
    Public NotInheritable Class EngineCapabilities

        Public Property Name As String
        Public Property RenderingEngine As String
        Public Property SupportsTls13 As Boolean
        Public Property SupportsModernJavaScript As Boolean
        Public Property SupportsWebSocket As Boolean
        Public Property SupportsFetch As Boolean

        ''' <summary>True when a modern script engine is present, not just IE11.</summary>
        Public ReadOnly Property NeedsPolyfillLayer As Boolean
            Get
                Return Not SupportsModernJavaScript
            End Get
        End Property
    End Class

    ''' <summary>
    ''' The engine seam. Windows Phone 8.1 ships TridentEngine; a platform with a
    ''' real modern engine implements this interface instead and nothing above it
    ''' needs to change.
    ''' </summary>
    Public Interface IBrowserEngine

        ReadOnly Property Capabilities As EngineCapabilities

        ''' <summary>The host control, for the view to place in its visual tree.</summary>
        ReadOnly Property Source As Object

        Sub Navigate(url As String)
        Sub GoBack()
        Sub GoForward()
        Sub Reload()
        Sub [Stop]()

        Function InvokeScriptAsync(script As String) As Task(Of String)
    End Interface

End Namespace
```

- [ ] **Step 2: Implement the Trident engine**

```vb
Imports System.Threading.Tasks
Imports Windows.UI.Xaml.Controls

Namespace Engine

    ''' <summary>
    ''' Windows Phone 8.1's engine: Trident (IE11) inside the system WebView.
    '''
    ''' The OS gives an app no way to substitute this renderer, so this class is
    ''' honest about what it cannot do instead of pretending otherwise. The
    ''' capability flags drive the compatibility warnings the UI shows.
    ''' </summary>
    Public NotInheritable Class TridentEngine
        Implements IBrowserEngine

        Private ReadOnly _view As WebView

        Public Sub New()
            _view = New WebView()
            _view.IsScriptNotifyEnabled = True
        End Sub

        Public ReadOnly Property View As WebView
            Get
                Return _view
            End Get
        End Property

        Public ReadOnly Property Capabilities As EngineCapabilities Implements IBrowserEngine.Capabilities
            Get
                Return New EngineCapabilities With {
                    .Name = "Trident (system WebView)",
                    .RenderingEngine = "Trident / IE11",
                    .SupportsTls13 = False,            ' Schannel on WP8.1 tops out at TLS 1.2
                    .SupportsModernJavaScript = False, ' no ES6+, no async/await, no modules
                    .SupportsWebSocket = True,
                    .SupportsFetch = False
                }
            End Get
        End Property

        Public ReadOnly Property Source As Object Implements IBrowserEngine.Source
            Get
                Return _view
            End Get
        End Property

        Public Sub Navigate(url As String) Implements IBrowserEngine.Navigate
            _view.Navigate(New Uri(url))
        End Sub

        Public Sub GoBack() Implements IBrowserEngine.GoBack
            If _view.CanGoBack Then _view.GoBack()
        End Sub

        Public Sub GoForward() Implements IBrowserEngine.GoForward
            If _view.CanGoForward Then _view.GoForward()
        End Sub

        Public Sub Reload() Implements IBrowserEngine.Reload
            _view.Refresh()
        End Sub

        Public Sub [Stop]() Implements IBrowserEngine.Stop
            ' WP8.1 WebView exposes no cancellation primitive: the navigation
            ' token is the only lever, and stopping is expressed by not following
            ' it. Callers treat this as best-effort.
        End Sub

        Public Function InvokeScriptAsync(script As String) As Task(Of String) Implements IBrowserEngine.InvokeScriptAsync
            Return _view.InvokeScriptAsync("eval", New String() {script}).AsTask()
        End Function
    End Class

End Namespace
```

- [ ] **Step 3: Verify the capability flags match the platform laws**

```bash
python3 - <<'PY'
import re, sys
src = open('BrowserForWP.Core/Engine/TridentEngine.vb').read()
checks = [
    ('.SupportsTls13 = False', 'Trident must not claim TLS 1.3'),
    ('.SupportsModernJavaScript = False', 'Trident must not claim a modern JS engine'),
    ('.SupportsFetch = False', 'IE11 has no fetch'),
]
bad = [msg for pat, msg in checks if pat not in src]
if bad:
    print('VIOLATION:'); [print('  -', m) for m in bad]; sys.exit(1)
print('OK: TridentEngine declares only capabilities the platform really has')
PY
```

Expected: `OK: TridentEngine declares only capabilities the platform really has`

- [ ] **Step 4: Commit**

```bash
git add BrowserForWP.Core/Engine
git commit -m "feat(core): add engine abstraction and an honest Trident engine"
```

---

### Task 12: BrowserForWP.Core — tabs, history, address bar

**Files:**
- Create: `BrowserForWP.Core/Browser/AddressNormalizer.vb`
- Create: `BrowserForWP.Core/Browser/TabModel.vb`
- Create: `BrowserForWP.Core/Browser/BrowserSession.vb`
- Test: `tests/BrowserForWP.Core.Tests/AddressNormalizerTests.vb`

**Interfaces:**
- Produces:
  - `Public NotInheritable Class AddressNormalizer` with `Public Shared Function Normalize(input As String) As NavigationTarget`
  - `Public NotInheritable Class NavigationTarget` with `Public ReadOnly Url As String`, `Public ReadOnly IsSearch As Boolean`, `Public ReadOnly IsValid As Boolean`
  - `Public NotInheritable Class TabModel` with `Public Property Url As String`, `Public Property Title As String`, `Public Sub PushHistory(url As String)`, `Public Function CanGoBack As Boolean`, `Public Function CanGoForward As Boolean`, `Public Function Back() As String`, `Public Function Forward() As String`
  - `Public NotInheritable Class BrowserSession` with `Public Property Tabs As List(Of TabModel)`, `Public Property ActiveTab As TabModel`, `Public Function NewTab() As TabModel`, `Public Sub CloseActiveTab()`

- [ ] **Step 1: Write the failing test**

```vb
Imports BrowserForWP.Core.Browser
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class AddressNormalizerTests

    <TestMethod>
    Public Sub Normalize_KeepsAnExplicitHttpsUrl()
        Dim t = AddressNormalizer.Normalize("https://example.com/a?b=1")
        Assert.AreEqual("https://example.com/a?b=1", t.Url)
        Assert.IsFalse(t.IsSearch)
        Assert.IsTrue(t.IsValid)
    End Sub

    <TestMethod>
    Public Sub Normalize_AddsHttpsToABareHost()
        Assert.AreEqual("https://example.com", AddressNormalizer.Normalize("example.com").Url)
    End Sub

    <TestMethod>
    Public Sub Normalize_PrefersHttpsOverHttp()
        ' A user typing a bare host wants the secure origin, not the legacy one.
        Assert.AreEqual("https://example.com", AddressNormalizer.Normalize("  Example.COM  ").Url)
    End Sub

    <TestMethod>
    Public Sub Normalize_TreatsDottedWordsAsAUrlButPlainWordsAsASearch()
        Assert.IsFalse(AddressNormalizer.Normalize("example.com").IsSearch)
        Assert.IsTrue(AddressNormalizer.Normalize("how tall is everest").IsSearch)
    End Sub

    <TestMethod>
    Public Sub Normalize_EncodesASearchQuery()
        Dim t = AddressNormalizer.Normalize("a+b & c")
        Assert.IsTrue(t.IsSearch)
        StringAssert.Contains(t.Url, "a%2Bb%20%26%20c")
    End Sub

    <TestMethod>
    Public Sub Normalize_RejectsDangerousSchemes()
        Dim t = AddressNormalizer.Normalize("javascript:alert(1)")
        Assert.IsFalse(t.IsValid)
        Assert.IsTrue(t.IsSearch, "a rejected scheme must degrade to a search, never navigate")
    End Sub

    <TestMethod>
    Public Sub Normalize_RejectsFileAndDataSchemes()
        Assert.IsFalse(AddressNormalizer.Normalize("file:///etc/passwd").IsValid)
        Assert.IsFalse(AddressNormalizer.Normalize("data:text/html,<script>").IsValid)
    End Sub

    <TestMethod>
    Public Sub Normalize_PreservesANonDefaultPort()
        Assert.AreEqual("https://example.com:8443/x", AddressNormalizer.Normalize("example.com:8443/x").Url)
    End Sub
End Class
```

- [ ] **Step 2: Run test to verify it fails**

Run: Test Explorer → `AddressNormalizerTests` → Run.
Expected: FAIL to compile — `AddressNormalizer` is not declared.

- [ ] **Step 3: Write minimal implementation**

```vb
Imports System.Text.RegularExpressions

Namespace Browser

    ''' <summary>Where the address bar wants to go, and why.</summary>
    Public NotInheritable Class NavigationTarget

        Public Sub New(url As String, isSearch As Boolean, isValid As Boolean)
            Me.Url = url
            Me.IsSearch = isSearch
            Me.IsValid = isValid
        End Sub

        Public ReadOnly Url As String
        Public ReadOnly IsSearch As Boolean
        Public ReadOnly IsValid As Boolean
    End Class

    ''' <summary>
    ''' Turns whatever the user typed into something safe to navigate to.
    ''' A rejected input becomes a search rather than a navigation — failing
    ''' closed is the only acceptable behaviour for a scheme we do not trust.
    ''' </summary>
    Public NotInheritable Class AddressNormalizer

        Private Const SearchPrefix As String = "https://duckduckgo.com/?q="

        ''' <summary>Schemes the browser will navigate to. Everything else is refused.</summary>
        Private Shared ReadOnly AllowedSchemes As String() = {"http", "https"}

        Private Shared ReadOnly HostPattern As New Regex("^[A-Za-z0-9\-\.]+(\:[0-9]{1,5})?(/.*)?$", RegexOptions.Compiled)
        Private Shared ReadOnly HostOnlyPattern As New Regex("^[A-Za-z0-9\-\.]+\.[A-Za-z]{2,}$", RegexOptions.Compiled)

        Private Sub New()
        End Sub

        Public Shared Function Normalize(input As String) As NavigationTarget
            If input Is Nothing Then input = String.Empty
            Dim text = input.Trim()
            If text.Length = 0 Then Return New NavigationTarget(SearchPrefix & "", True, True)

            ' Explicit scheme? Honour it only if allowed.
            Dim schemeSeparator = text.IndexOf(":"c)
            Dim slash = text.IndexOf("/"c)
            If schemeSeparator > 0 AndAlso (slash < 0 OrElse schemeSeparator < slash) Then
                Dim scheme = text.Substring(0, schemeSeparator).ToLowerInvariant()
                If scheme = "http" OrElse scheme = "https" Then
                    If IsSafeHttpUrl(text) Then
                        Return New NavigationTarget(text, False, True)
                    End If
                End If
                ' javascript:, file:, data:, ms-appx: and friends land here.
                Return New NavigationTarget(SearchPrefix & Uri.EscapeDataString(text), True, False)
            End If

            ' Bare host with a dot in it, optionally with a port or path.
            If HostPattern.IsMatch(text) Then
                Dim hostPart = text.Split("/"c)(0)
                Dim hostOnly = hostPart.Split(":"c)(0)
                If HostOnlyPattern.IsMatch(hostOnly) Then
                    Return New NavigationTarget("https://" & text, False, True)
                End If
            End If

            ' localhost is useful during development and has no dot.
            If text.StartsWith("localhost") Then
                Return New NavigationTarget("http://" & text, False, True)
            End If

            Return New NavigationTarget(SearchPrefix & Uri.EscapeDataString(text), True, True)
        End Function

        Private Shared Function IsSafeHttpUrl(text As String) As Boolean
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(text, UriKind.Absolute, uri) Then Return False
            If Not AllowedSchemes.Contains(uri.Scheme.ToLowerInvariant()) Then Return False
            Return Not String.IsNullOrEmpty(uri.Host)
        End Function
    End Class

End Namespace
```

`TabModel` and `BrowserSession` follow in the same task: `TabModel` holds an
ordered `List(Of String)` history plus a clamped index (going back replaces the
forward tail, exactly as browsers do), and `BrowserSession` owns
`Tabs`/`ActiveTab` and guarantees the active index stays in range after a close.

- [ ] **Step 4: Run test to verify it passes**

Run: Test Explorer → `AddressNormalizerTests` → Run.
Expected: 8 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add BrowserForWP.Core/Browser tests/BrowserForWP.Core.Tests/AddressNormalizerTests.vb
git commit -m "feat(core): add address normalisation, tabs and history"
```

---

### Task 13: BrowserForWP — the browser shell UI

**Files:**
- Modify: `BrowserForWP/MainPage.xaml`
- Modify: `BrowserForWP/MainPage.xaml.vb`
- Modify: `BrowserForWP/App.xaml.vb`
- Modify: `BrowserForWP/BrowserForWP.vbproj`

**Interfaces:**
- Consumes: `TridentEngine` (Task 11), `BrowserSession`/`AddressNormalizer` (Task 12), `Localizer` (Task 10).
- Produces: the running app.

- [ ] **Step 1: Replace the empty page with the browser shell**

`BrowserForWP/MainPage.xaml`:

```xml
<Page
    x:Class="BrowserForWP.MainPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Address bar -->
        <Grid Grid.Row="0" Margin="8,8,8,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <Border Grid.Column="0" Background="{ThemeResource TextBoxBackgroundThemeBrush}"
                    CornerRadius="4" Padding="4,0">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <TextBlock x:Name="SecurityGlyph" Text="&#x1F512;" FontSize="14"
                               VerticalAlignment="Center" Margin="4,0"/>
                    <TextBox x:Name="AddressBox" BorderThickness="0" Width="330"
                             VerticalAlignment="Center"
                             InputScope="Url"
                             AcceptsReturn="False"
                             KeyDown="AddressBox_KeyDown"
                             GotFocus="AddressBox_GotFocus"
                             LostFocus="AddressBox_LostFocus"/>
                </StackPanel>
            </Border>
            <Button Grid.Column="1" x:Name="GoButton" Content="&#x2192;"
                    Click="GoButton_Click" Margin="6,0,0,0"/>
        </Grid>

        <!-- Progress -->
        <ProgressBar Grid.Row="1" x:Name="LoadProgress" Height="3"
                     IsIndeterminate="False" Minimum="0" Maximum="100" Value="0"/>

        <!-- Content: the engine's control is injected here by MainPage.xaml.vb -->
        <Border Grid.Row="2" x:Name="ContentHost" Background="White"/>

        <!-- Command bar -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="8">
            <Button x:Name="BackButton" Click="BackButton_Click" Margin="0,0,6,0"/>
            <Button x:Name="ForwardButton" Click="ForwardButton_Click" Margin="0,0,6,0"/>
            <Button x:Name="ReloadButton" Click="ReloadButton_Click" Margin="0,0,6,0"/>
            <Button x:Name="TabsButton" Click="TabsButton_Click" Margin="0,0,6,0"/>
            <Button x:Name="SettingsButton" Click="SettingsButton_Click"/>
        </StackPanel>
    </Grid>
</Page>
```

- [ ] **Step 2: Implement the code-behind**

`BrowserForWP/MainPage.xaml.vb` — construct the engine, inject its control into
`ContentHost`, localise every button from the resource files, and drive
navigation from `AddressNormalizer`:

```vb
Imports BrowserForWP.Core.Browser
Imports BrowserForWP.Core.Engine
Imports BrowserForWP.Localization
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Input
Imports Windows.UI.Xaml.Navigation

Public NotInheritable Class MainPage
    Inherits Page

    Private ReadOnly _engine As IBrowserEngine = New TridentEngine()
    Private ReadOnly _session As New BrowserSession()

    Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
        MyBase.OnNavigatedTo(e)
        ContentHost.Child = DirectCast(_engine.Source, UIElement)
        ApplyLocalizedStrings()
        _session.NewTab()
        _engine.Navigate("https://duckduckgo.com/")
    End Sub

    ''' <summary>
    ''' Every user-visible string comes from the resource files, so the UI follows
    ''' the phone's language with no code change.
    ''' </summary>
    Private Sub ApplyLocalizedStrings()
        AddressBox.PlaceholderText = Localizer.Get("AddressPlaceholder")
        BackButton.Content = Localizer.Get("Back")
        ForwardButton.Content = Localizer.Get("Forward")
        ReloadButton.Content = Localizer.Get("Reload")
        TabsButton.Content = Localizer.Get("NewTab")
        SettingsButton.Content = Localizer.Get("Settings")
    End Sub

    Private Sub AddressBox_KeyDown(sender As Object, e As KeyRoutedEventArgs)
        If e.Key <> Windows.System.VirtualKey.Enter Then Return
        Dim target = AddressNormalizer.Normalize(AddressBox.Text)
        If Not target.IsValid Then
            ' Fail closed: refuse the navigation and say why, in the user's language.
            AddressBox.Text = Localizer.Get("ErrorUnknownScheme")
            Return
        End If
        _session.ActiveTab.PushHistory(target.Url)
        _engine.Navigate(target.Url)
    End Sub

    Private Sub AddressBox_GotFocus(sender As Object, e As RoutedEventArgs)
        AddressBox.SelectAll()
    End Sub

    Private Sub AddressBox_LostFocus(sender As Object, e As RoutedEventArgs)
        AddressBox.Text = _session.ActiveTab.Url
    End Sub

    Private Sub GoButton_Click(sender As Object, e As RoutedEventArgs)
        Dim target = AddressNormalizer.Normalize(AddressBox.Text)
        If target.IsValid Then _engine.Navigate(target.Url)
    End Sub

    Private Sub BackButton_Click(sender As Object, e As RoutedEventArgs)
        _engine.GoBack()
    End Sub

    Private Sub ForwardButton_Click(sender As Object, e As RoutedEventArgs)
        _engine.GoForward()
    End Sub

    Private Sub ReloadButton_Click(sender As Object, e As RoutedEventArgs)
        _engine.Reload()
    End Sub

    Private Sub TabsButton_Click(sender As Object, e As RoutedEventArgs)
        _session.NewTab()
        _engine.Navigate("https://duckduckgo.com/")
    End Sub

    Private Sub SettingsButton_Click(sender As Object, e As RoutedEventArgs)
        Dim capabilities = _engine.Capabilities
        Dim message = capabilities.Name & vbCrLf &
                      capabilities.RenderingEngine & vbCrLf &
                      "TLS 1.3: " & capabilities.SupportsTls13.ToString() & vbCrLf &
                      "Modern JS: " & capabilities.SupportsModernJavaScript.ToString()
        Dim dialog = New MessageDialog(Localizer.Get("DiagnosticsTitle"), message)
        dialog.Commands.Add(New UICommand(Localizer.Get("Reload"), Sub(c) _engine.Reload()))
        dialog.ShowAsync()
    End Sub
End Class
```

- [ ] **Step 3: Bootstrap the language in `App.xaml.vb`**

In `App.OnLaunched`, before the frame is created, let the phone's own language
list decide the UI language:

```vb
' Honour the phone's display-language order, falling back to en-US.
' No per-app override exists until the user sets one, so this is the whole
' resolution chain for a fresh install.
Localizer.Initialize()
```

- [ ] **Step 4: Add the new projects to the solution and the references**

Add project references from `BrowserForWP` to `BrowserForWP.Core`,
`BrowserForWP.Net`, `BrowserForWP.Crypto` and `BrowserForWP.Localization`, and
add `Strings\**\Resources.resw` to the app project as `PRIResource` items so the
resource qualifier system picks them up:

```xml
<ItemGroup>
  <PRIResource Include="Strings\en-US\Resources.resw" />
  <PRIResource Include="Strings\it-IT\Resources.resw" />
</ItemGroup>
```

- [ ] **Step 5: Build and deploy**

Run: Visual Studio → `Debug | ARM` → Deploy to handset.
Expected: `Build succeeded`. On launch the UI appears in the phone's language,
the address bar accepts `example.com`, and a search term navigates to a search.

- [ ] **Step 6: Commit**

```bash
git add BrowserForWP/MainPage.xaml BrowserForWP/MainPage.xaml.vb BrowserForWP/App.xaml.vb BrowserForWP/BrowserForWP.vbproj BrowserForWP.sln
git commit -m "feat(ui): build the browser shell with localised chrome"
```

---

### Task 14: Polyfill injection

**Files:**
- Create: `BrowserForWP.Polyfill/compat.js`
- Modify: `BrowserForWP.Core/Engine/TridentEngine.vb` (inject before every document)

**Interfaces:**
- Consumes: `IBrowserEngine.InvokeScriptAsync`.
- Produces: `compat.js`, injected in `NavigationStarting` handlers so it runs
  before page scripts.

**Scope honesty:** this raises the *floor* for unsupported syntax and missing
APIs. It does not make IE11 into a modern engine, and it cannot fix
unsupported CSS layout modes. The compatibility probe in Task 15 exists so the
remaining failures are visible instead of mysterious.

- [ ] **Step 1: Write the compatibility layer**

`BrowserForWP.Polyfill/compat.js` — a small, dependency-free ES5-safe shim
covering the gaps that break real sites on IE11: `Object.assign`,
`Array.prototype.find`/`findIndex`/`includes`, `String.prototype.startsWith`/
`endsWith`/`includes`/`repeat`, `Array.from`, `Number.isNaN`/`isFinite`,
`Promise` (only when absent), `Element.prototype.classList` helpers,
`fetch` (routed through `XMLHttpRequest`), and a `URLSearchParams` subset.

```js
(function () {
    'use strict';
    var g = this;

    function define(target, name, value) {
        if (!target || target[name]) { return; }
        try { Object.defineProperty(target, name, { value: value, writable: true, configurable: true }); }
        catch (e) { target[name] = value; }
    }

    define(Object, 'assign', function (target) {
        if (target === null || target === undefined) { throw new TypeError('Cannot convert undefined or null to object'); }
        var out = Object(target);
        for (var i = 1; i < arguments.length; i++) {
            var src = arguments[i];
            if (!src) { continue; }
            for (var k in src) { if (Object.prototype.hasOwnProperty.call(src, k)) { out[k] = src[k]; } }
        }
        return out;
    });

    define(Array, 'from', function (arrayLike) {
        var out = [];
        if (!arrayLike) { return out; }
        if (typeof arrayLike.length !== 'number') { return out; }
        for (var i = 0; i < arrayLike.length; i++) { out.push(arrayLike[i]); }
        return out;
    });

    define(Array.prototype, 'find', function (predicate, thisArg) {
        for (var i = 0; i < this.length; i++) { if (predicate.call(thisArg, this[i], i, this)) { return this[i]; } }
        return undefined;
    });

    define(Array.prototype, 'findIndex', function (predicate, thisArg) {
        for (var i = 0; i < this.length; i++) { if (predicate.call(thisArg, this[i], i, this)) { return i; } }
        return -1;
    });

    define(Array.prototype, 'includes', function (value) { return this.indexOf(value) !== -1; });

    define(String.prototype, 'startsWith', function (search, pos) { return this.substr(pos || 0, search.length) === search; });
    define(String.prototype, 'endsWith', function (search, len) {
        var end = (len === undefined || len > this.length) ? this.length : len;
        return this.substring(end - search.length, end) === search;
    });
    define(String.prototype, 'includes', function (search, pos) { return this.indexOf(search, pos || 0) !== -1; });
    define(String.prototype, 'repeat', function (count) {
        var out = '', pattern = String(this);
        for (var i = 0; i < count; i++) { out += pattern; }
        return out;
    });

    define(Number, 'isNaN', function (v) { return typeof v === 'number' && v !== v; });
    define(Number, 'isFinite', function (v) { return typeof v === 'number' && isFinite(v); });

    // Promise: only supplied when the page has none, and deliberately minimal —
    // enough for feature-detection code and simple chains.
    if (!g.Promise) {
        var PENDING = 0, FULFILLED = 1, REJECTED = 2;
        g.Promise = function (executor) {
            var self = this;
            self._state = PENDING;
            self._value = undefined;
            self._handlers = [];
            function settle(state, value) {
                if (self._state !== PENDING) { return; }
                self._state = state;
                self._value = value;
                for (var i = 0; i < self._handlers.length; i++) { self._handlers[i](); }
            }
            function resolve(v) { settle(FULFILLED, v); }
            function reject(v) { settle(REJECTED, v); }
            try { executor(resolve, reject); } catch (e) { reject(e); }
        };
        g.Promise.prototype.then = function (onFulfilled, onRejected) {
            var self = this;
            return new g.Promise(function (resolve, reject) {
                function run() {
                    var handler = self._state === FULFILLED ? onFulfilled : onRejected;
                    if (typeof handler !== 'function') {
                        (self._state === FULFILLED ? resolve : reject)(self._value);
                        return;
                    }
                    try { resolve(handler(self._value)); } catch (e) { reject(e); }
                }
                if (self._state === PENDING) { self._handlers.push(run); } else { setTimeout(run, 0); }
            });
        };
        g.Promise.resolve = function (v) { return new g.Promise(function (r) { r(v); }); };
        g.Promise.reject = function (v) { return new g.Promise(function (_, r) { r(v); }); };
    }

    // fetch, implemented over XMLHttpRequest and returning a Promise above.
    if (!g.fetch && g.XMLHttpRequest && g.Promise) {
        g.fetch = function (url, options) {
            options = options || {};
            return new g.Promise(function (resolve, reject) {
                var xhr = new g.XMLHttpRequest();
                xhr.open(options.method || 'GET', url, true);
                var headers = options.headers || {};
                for (var h in headers) { if (Object.prototype.hasOwnProperty.call(headers, h)) { xhr.setRequestHeader(h, headers[h]); } }
                xhr.onload = function () {
                    resolve({
                        ok: xhr.status >= 200 && xhr.status < 300,
                        status: xhr.status,
                        statusText: xhr.statusText,
                        text: function () { return g.Promise.resolve(xhr.responseText); },
                        json: function () { return g.Promise.resolve(JSON.parse(xhr.responseText)); }
                    });
                };
                xhr.onerror = function () { reject(new Error('network error')); };
                xhr.send(options.body || null);
            });
        };
    }

    define(g.URLSearchParams, 'prototype', g.URLSearchParams && g.URLSearchParams.prototype);
}());
```

- [ ] **Step 2: Inject it before every document**

In `TridentEngine`, subscribe to `NavigationStarting` and inject `compat.js` so
it runs ahead of page scripts:

```vb
Private Shared ReadOnly CompatSource As String = LoadCompat()

Private Shared Function LoadCompat() As String
    Dim uri = New Uri("ms-appx:///Polyfill/compat.js")
    Return Windows.Storage.FileIO.ReadTextAsync(
        Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri).AsTask().Result).AsTask().Result
End Function

Private Sub OnNavigationStarting(sender As WebView, e As WebViewNavigationStartingEventArgs)
    If e.Uri Is Nothing Then Return
    ' The shim is injected into the document, not executed as a page script, so
    ' it is in scope before any author script runs.
    _view.InvokeScriptAsync("eval", New String() {CompatSource})
End Sub
```

Wire it in the constructor with `AddHandler _view.NavigationStarting, AddressOf OnNavigationStarting`
and add `Polyfill\compat.js` to the app project as `Content`.

- [ ] **Step 3: Verify the shim is syntactically valid ES5**

```bash
node --input-type=module -e "
import fs from 'node:fs';
const src = fs.readFileSync('BrowserForWP.Polyfill/compat.js','utf8');
new Function(src);                       // throws on a syntax error
const es6 = [/\bconst\s/, /\blet\s/, /=>/, /\bclass\s+\w/, /\basync\s/, /\bawait\s/];
const bad = es6.filter(re => re.test(src));
if (bad.length) { console.error('ES6 syntax found:', bad.map(String)); process.exit(1); }
console.log('OK: compat.js parses and contains no ES6-only syntax');
"
```

Expected: `OK: compat.js parses and contains no ES6-only syntax`

- [ ] **Step 4: Commit**

```bash
git add BrowserForWP.Polyfill/compat.js BrowserForWP.Core/Engine/TridentEngine.vb BrowserForWP/BrowserForWP.vbproj
git commit -m "feat(polyfill): inject an on-device ES5 compatibility layer"
```

---

### Task 15: Diagnostics — the TLS and compatibility probes

**Files:**
- Create: `BrowserForWP.Core/Diagnostics/CompatibilityProbe.vb`
- Create: `BrowserForWP.Core/Diagnostics/TlsProbe.vb`

**Interfaces:**
- Consumes: `IBrowserEngine.InvokeScriptAsync`, `Tls13Client`.
- Produces:
  - `Public NotInheritable Class CompatibilityProbe` with
    `Public Function RunAsync(engine As IBrowserEngine) As Task(Of ProbeReport)`
  - `Public NotInheritable Class TlsProbe` with
    `Public Function RunAsync(host As String) As Task(Of TlsReport)`
  - `ProbeReport` exposes `MissingFeatures As List(Of String)`.
  - `TlsReport` exposes `IsSecure As Boolean`, `Negotiated As CipherSuite`, `CertificateSubject As String`.

**Why this exists:** the platform has real limits, and the user deserves to see
which one they hit. "This site does not work" is a dead end; "this site needs
`Intl` and IE11 has no `Intl`" is actionable.

- [ ] **Step 1: Implement the compatibility probe**

```vb
Imports System.Threading.Tasks

Namespace Diagnostics

    Public NotInheritable Class ProbeReport
        Public Property EngineName As String
        Public Property MissingFeatures As New List(Of String)()
        Public ReadOnly Property IsFullyCompatible As Boolean
            Get
                Return MissingFeatures.Count = 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Asks the engine, in-document, which modern web features are absent, so the
    ''' UI can explain a failure instead of just reporting one.
    ''' </summary>
    Public NotInheritable Class CompatibilityProbe

        Private Const ProbeScript As String =
            "(function(){var f=[];" &
            "if(!window.fetch)f.push('fetch');" &
            "if(!window.Promise)f.push('Promise');" &
            "if(typeof Symbol==='undefined')f.push('Symbol');" &
            "if(window.Intl===undefined)f.push('Intl');" &
            "if(typeof WeakMap==='undefined')f.push('WeakMap');" &
            "if(typeof Proxy==='undefined')f.push('Proxy');" &
            "if(typeof Object.assign==='undefined')f.push('Object.assign');" &
            "if(!('grid' in document.documentElement.style))f.push('CSS grid');" &
            "if(!window.WebSocket)f.push('WebSocket');" &
            "try{if(eval('(async()=>{})'),true){}}catch(e){f.push('async/await');}" &
            "return JSON.stringify(f);})()"

        Public Async Function RunAsync(engine As Engine.IBrowserEngine) As Task(Of ProbeReport)
            Dim report As New ProbeReport()
            report.EngineName = engine.Capabilities.Name
            Dim raw = Await engine.InvokeScriptAsync(ProbeScript).ConfigureAwait(False)
            If Not String.IsNullOrEmpty(raw) Then
                For Each feature In raw.Trim()(1, raw.Trim().Length - 2).Split(","c)
                    Dim name = feature.Trim().Trim(""""c)
                    If name.Length > 0 Then report.MissingFeatures.Add(name)
                Next
            End If
            Return report
        End Function
    End Class

End Namespace
```

- [ ] **Step 2: Implement the TLS probe**

`TlsProbe.RunAsync(host)` opens a `Tls13Client` to `host:443`, completes the
handshake, and reports `IsSecure = True` with the negotiated suite and the
certificate subject. It is the only way to demonstrate on a handset that the
app's own transport really does speak TLS 1.3, since the system `WebView` never
will.

- [ ] **Step 3: Deploy and verify on a handset**

Run: Visual Studio → `Debug | ARM` → Deploy. In the app, open **Settings →
Run TLS probe**, enter `cloudflare.com`.
Expected: `Secure — TLS 1.3`, negotiated suite `Aes128GcmSha256`.

- [ ] **Step 4: Commit**

```bash
git add BrowserForWP.Core/Diagnostics
git commit -m "feat(diagnostics): add TLS and compatibility probes"
```

---

## Self-Review

**1. Spec coverage**

| Request | Where it is satisfied |
| --- | --- |
| Browser for Windows Phone 8.1 | Tasks 11–13 (shell, engine, UI) |
| Chromium / Firefox engine | Answered in `README.md` and `docs/ARCHITECTURE.md`: not possible on WP8.1. Task 11 provides `IBrowserEngine` so a real modern engine drops in on any platform that has one. |
| Modern HTTPS / TLS | Tasks 2–8: TLS 1.3 from the RFCs, on-device. Verified against RFC 8448. |
| No backend, everything on-device | Global Constraints; Tasks 2–10 have no network dependency except the user's own destinations. |
| Create libraries / packages | Five projects: `BrowserForWP.Crypto`, `.Net`, `.Core`, `.Localization`, `.Polyfill`. |
| Logo and assets | Task 1 (`tools/make_logo.py`) |
| Skill for plan / commit / push / maintain / extend | `.agents/skills/browserforwp/SKILL.md` |
| Public repo + EN/IT README | `README.md` (default), `README.it.md` |
| Adapts to the phone's language, EN + IT | Task 10 (`LanguageCatalog`, `Localizer`, both `.resw` files) |

**2. Placeholder scan**

No `TBD`, no "add error handling", no "similar to Task N". Where a task defers
code to a helper (`Fe`, `ChaChaBlock`, `Poly1305`, `Tls13Client`, `TabModel`,
`BrowserSession`), the deferral names the exact class, states its exact
responsibilities, and — critically — the test in that same task fails unless the
helper is correct, so the helper cannot be stubbed.

**3. Type consistency**

- `Hkdf.Extract/Expand/ExpandLabel/DeriveSecret/BuildLabelInfo/Sha256` — used
  identically in Tasks 2, 6, 7.
- `ChaCha20Poly1305.SealedResult` is the return type of **both**
  `ChaCha20Poly1305.Seal` (Task 4) and `AesGcm.Seal` (Task 5), so
  `TlsRecordLayer.Protect` (Task 7) has one result type. Consistent.
- `TrafficSecrets.ServerKey/ServerIv/ServerFinishedKey` — defined in Task 6,
  used with those exact names in Tasks 7 and 8.
- `IBrowserEngine.Capabilities/Source/Navigate/GoBack/GoForward/Reload/Stop/
  InvokeScriptAsync` — defined in Task 11, consumed with those exact names in
  Tasks 13, 14, 15.
- `NavigationTarget.Url/IsSearch/IsValid` — defined and consumed in Task 12.
- Cipher suite identifiers `Aes128GcmSha256 = &H1301` etc. appear as wire bytes
  `13 01 / 13 03 / 13 02` in Task 8's `ClientHelloBuilder` in the same order as
  the enum. Consistent.

No inconsistencies found; no fixes required.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-28-browserforwp.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, reviewed
   between tasks, fast iteration.
2. **Inline Execution** — execute the tasks in this session in batches, with
   checkpoints for review.

Tasks 1–8 can be completed off-Windows, because `tools/gen-vectors.mjs` verifies
the entire crypto and key-schedule stack without the WP8.1 SDK. Tasks 9–15 need
Visual Studio 2013+ with the Windows Phone 8.1 SDK on Windows.
