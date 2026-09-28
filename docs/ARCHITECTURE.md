# BrowserForWP — Architecture

## The three platform laws

Every design decision in this repository follows from three facts about
Windows Phone 8.1. Each was verified, not assumed. If you are about to write
code that contradicts one of them, stop — the platform will not honour it.

### Law 1 — The rendering engine is Trident (IE11) and cannot be replaced

`Windows.UI.Xaml.Controls.WebView` on WP8.1 is bound to the OS's Trident
engine. There is no API to substitute it, and no Chromium or Gecko binary
exists for WinRT-ARM 8.1:

| Engine | Status on Windows Phone 8.1 |
| --- | --- |
| Chromium / Blink | No platform port exists. AppContainers cannot host a sandboxed multi-process renderer. |
| Gecko (Firefox) | Mozilla cancelled Firefox for Windows Phone in 2015. No binary ever shipped. |
| EdgeHTML / Chromium-Edge | Windows 10 Mobile only; cannot run on WP8.1. |
| Trident (IE11) | **The only engine available.** |

**Consequence:** web *rendering* is capped at IE11. This is why
`IBrowserEngine` exists — it makes the cap a configuration detail instead of an
assumption baked into every call site.

### Law 2 — The OS offers TLS 1.2 at most

Schannel on WP8.1 negotiates TLS 1.0/1.1/1.2. There is no API to raise the
ceiling, and no system setting that enables 1.3.

**Consequence:** the *system* `WebView` can never use TLS 1.3. The application's
*own* transport can, because it does not go through Schannel at all — it
implements TLS 1.3 (RFC 8446) in managed code over a raw
`Windows.Networking.Sockets.StreamSocket`. That is `BrowserForWP.Net`.

### Law 3 — AppContainers block loopback

Windows AppContainers block traffic to `127.0.0.1` by default. This is
documented behaviour intended to stop an app from talking to a server on the
device.

**Consequence:** the classic architecture — run a local TLS-terminating proxy on
loopback and point the `WebView` at it — **does not work here**. That design was
evaluated and rejected. Anyone proposing "let's just proxy it locally" is
proposing something the OS will refuse.

## What that means for "modern"

The honest summary: **modern transport, modern compatibility layer, unchanged
renderer.**

| Layer | Status |
| --- | --- |
| Transport (the wire) | **Modern.** TLS 1.3, X25519, ChaCha20-Poly1305, DNS-over-HTTPS, all on-device. |
| Compatibility (what pages can run) | **Improved.** An injected ES5 shim raises the floor for modern pages. |
| Rendering (how it looks) | **Capped at IE11.** Not addressable on this OS. |

The compatibility probe (`BrowserForWP.Core/Diagnostics`) exists so that the
remaining gap is *reported*, not mysterious.

## Layers

```
BrowserForWP              (app)      XAML shell, assets, UI strings
    |
    +-- BrowserForWP.Core (core)     IBrowserEngine, tabs, history, address bar
    +-- BrowserForWP.Net  (net)      TLS 1.3, DoH, HTTP client
    +-- BrowserForWP.Crypto(crypto)  HKDF, X25519, ChaCha20-Poly1305, AES-GCM
    +-- BrowserForWP.Localization    language resolution + string lookup
    +-- BrowserForWP.Polyfill        injected ES5 compatibility layer
```

**Dependency direction is strictly downward and one-way:**

- `Crypto` depends on nothing. It is pure algorithms.
- `Net` depends on `Crypto`. It never touches XAML.
- `Core` depends on **neither** `Net` nor `Crypto`. It knows about documents,
  history and engines, not about key schedules.
- `Localization` depends on nothing but the WinRT globalization APIs.
- The app depends on all of them.

A change that adds a dependency pointing upward is a design bug. The value of
the split is that `Crypto` can be fully verified off-device (see
`tools/gen-vectors.mjs`) and `Core` can be unit-tested without a network.

## The engine seam

```vb
Public Interface IBrowserEngine
    ReadOnly Property Capabilities As EngineCapabilities
    ReadOnly Property Source As Object
    Sub Navigate(url As String)
    Sub GoBack() : Sub GoForward() : Sub Reload() : Sub [Stop]()
    Function InvokeScriptAsync(script As String) As Task(Of String)
End Interface
```

`EngineCapabilities` is not decoration. `TridentEngine` reports:

```vb
.SupportsTls13 = False            ' Law 2
.SupportsModernJavaScript = False ' IE11 has no ES6+
.SupportsFetch = False
```

so the UI can warn accurately rather than silently degrade. A future
`WebView2Engine` (Chromium, Windows 10+) or `GeckoViewEngine` (Android) reports
the truth for its platform and **nothing above this interface changes**.

## TLS 1.3 design

Implemented from the RFCs, in five pieces:

1. **`Hkdf`** — RFC 5869 Extract/Expand, plus the RFC 8446 §7.1 `HkdfLabel`
   framing (`BuildLabelInfo`) and `DeriveSecret`.
2. **`X25519`** — RFC 7748 Montgomery ladder over `2^255 - 19`, hand-rolled
   because WinRT 8.1 exposes no X25519 primitive and group `x25519` is mandatory
   for TLS 1.3.

   Field arithmetic uses **radix 2^16 (16 limbs) in `Int64`**. A power-of-two
   radix is required, not merely convenient: it is the only representation where
   limb weights add exactly, `w(i+j) = w(i) + w(j)`. The widely used radix-2^25.5
   does not have that property (`w(1)+w(1) = 52` while `w(2) = 51`), and
   reconstructing its alternating half-limb bookkeeping from memory produced
   wrong-but-plausible output when first attempted here. With 2^16 limbs there
   are exactly two fold factors, both derivable from `p = 2^255 - 19`: products
   above limb 15 fold down with **38**, and limb 15's top bit folds with **19**.

   `BigInteger` is deliberately avoided. Its presence in the ".NET for Windows
   Store apps" profile could not be confirmed, and betting the crypto layer on an
   unverifiable platform dependency is not acceptable when the alternative is
   arithmetic that can be proven.

   **This implementation is not constant-time.** The carry chain branches and the
   ladder's conditional swap is a real branch, so an attacker able to measure the
   handset's timing may learn something. Documented rather than left implicit.

3. **No managed AEAD at all.** BrowserForWP offers exactly one TLS 1.3 cipher
   suite: `TLS_AES_128_GCM_SHA256`. RFC 8446 §9.1 makes that suite
   mandatory-to-implement, so a single-suite offer costs **no interoperability**.
   That decision removed the need for a hand-written ChaCha20-Poly1305 entirely —
   it was a performance optimisation for hardware without AES acceleration, and
   this handset's AES path is hardware-backed. It was deleted rather than left as
   dead code carrying real risk.
4. **`AesGcm`** — a thin adapter over the platform provider, because managed
   AES-GCM is unacceptably slow on 2014 ARM silicon. It owns the `AeadResult`
   type and is the only AEAD the record layer ever calls.
5. **`KeySchedule`** — the RFC 8446 §7.1 chain:
   `early → derived → handshake → derived → master`, plus
   `c hs traffic / s hs traffic / c ap traffic / s ap traffic` and the finished
   keys.

### Why the transcript ordering matters

Two bugs are unusually easy to introduce here and both are silent:

- The **Finished** MAC is computed over the transcript hash of everything
  *before* the Finished, not after.
- The **application traffic secrets** are derived over the transcript hash that
  *includes* the Finished.

`tools/gen-vectors.mjs` asserts both orderings against RFC 8448 §3, using the
raw handshake messages rather than published hashes, so a regression in either
direction fails the build.

## The crypto API surface on WP8.1 WinRT — read this before editing Crypto

A WP8.1 WinRT app (`NETFX_CORE`) compiles against the ".NET for Windows Store
apps" profile, which **strips the classic managed crypto types**. None of these
resolve:

| Type you might reach for | Status | Use instead |
| --- | --- | --- |
| `System.Security.Cryptography.SHA256` / `SHA256Managed` | absent | `HashAlgorithmProvider` + `HashAlgorithmNames.Sha256` |
| `System.Security.Cryptography.HMACSHA256` | absent | `MacAlgorithmProvider` + `CryptographicEngine.Sign` |
| `System.Security.Cryptography.RNGCryptoServiceProvider` | absent | `CryptographicBuffer.GenerateRandom` |
| `System.Security.Cryptography.AesGcm` | absent | `CryptographicEngine.EncryptAndAuthenticate` |

`SHA256` derives from `HashAlgorithm`, and `HMACSHA256` and
`RNGCryptoServiceProvider` derive from the same stripped namespace, so the whole
family fails together. This was confirmed against Microsoft's documentation and
against the reported symptom ("Cannot find type
System.Security.Cryptography.SHA256 on Windows Phone 8.1").

`System.Text.RegularExpressions.RegexOptions.Compiled` is likewise
**unsupported** — runtime regex code generation does not exist in this profile.
It is omitted deliberately in `AddressNormalizer`.

Every platform-specific call is funnelled through
`BrowserForWP.Crypto/WinRtCrypto.vb`, so the rest of the library stays portable
and reviewable.

## Verification strategy

The Windows Phone 8.1 SDK is Windows-only, so the crypto cannot be compiled on
macOS or Linux. Rather than accept "untested", the algorithms are verified in a
language-independent way:

`tools/gen-vectors.mjs` recomputes every value from first principles using
Node's crypto, **asserts it against the constant published in the RFC**, and only
then emits the VB test data. Current status: **52 assertions, 0 failures**,
covering RFC 5869 A.1–A.3, RFC 7748 §5.2/§6.1, RFC 8439 §2.8.2, RFC 8448 §3 and
NIST CAVS AES-GCM.

This means a wrong reduction or a wrong counter increment is caught before the
code ever reaches a handset, and the VB tests are grounded in values that were
independently checked rather than hand-copied.

### The X25519 prototype — why there are two verifications

`tools/gen-vectors.mjs` verifies X25519 *as an algorithm*, using Node's own
crypto as the oracle. It cannot verify the limb arithmetic, because VB does not
run off-Windows.

`tools/proto/w25519.mjs` closes that gap. It is a **line-for-line prototype of
`X25519.vb`** — same limb layout, same fold factors, same carry chain, same
encode/decode — and it is executed. It reproduces every RFC 7748 and RFC 8448
vector, cross-checks add/sub/mul/sq/invert against `BigInt` over 66 randomised
cases, and round-trips encode/decode. It also **range-asserts every intermediate
against `Int64`**, so a value that would overflow on the handset fails here
instead of wrapping silently.

```bash
node tools/proto/w25519.mjs    # must print: 18 checks, 0 failure(s)
```

### The TLS 1.3 prototype — the strongest check in this repo

`tools/gen-vectors.mjs` proves the *primitives* are right. It cannot prove the
*protocol* is right, and a self-consistency test cannot either: a TLS client that
is wrong in the same way when sealing and opening will round-trip its own
records perfectly and still be unable to talk to any real server.

So `tools/proto/tls13.mjs` is a **complete TLS 1.3 client and HTTP/1.1 client**,
written from the RFCs, that **completes real handshakes with real servers**:

```bash
node tools/proto/tls13.mjs example.com    # must print: 31 checks, 0 failure(s)
node tools/proto/tls13.mjs www.google.com
node tools/proto/tls13.mjs cloudflare.com
```

It verifies, against live servers, that: the ClientHello is well-formed enough to
be answered; the key schedule derives the keys the server actually used (proven
by successfully decrypting the server's records); the server's `Finished`
verifies; the server accepts **our** `Finished`; and application data flows both
ways and returns a well-formed HTTP status line.

It is the transliteration source for everything under `BrowserForWP.Net/Tls13/`.
Three genuine bugs were found this way and are documented where they live:

| Bug | Symptom | Where |
| --- | --- | --- |
| `TLSInnerPlaintext` written as `type \|\| content` | Dead handshake right after ServerHello, no alert. **Only a live server catches this** — AEAD decryption still succeeds, because the tag covers the ciphertext, not the plaintext's field order. | `TlsRecordLayer.vb` |
| `server_name` missing its `NameType` byte; `key_share` missing the inner 2-byte key length | Server answers with a bare `decode_error` | `ClientHelloBuilder.vb` |
| ALPN read from `ServerHello` | Silently reports "no ALPN" for every server | `ServerMessageParser.vb` |

A fourth, different in kind: the prototype originally advertised `h2` in ALPN
while speaking only HTTP/1.1. Servers obeyed, selected HTTP/2, and answered our
HTTP/1.1 request with `http2_handshake_failed`. RFC 7301 requires a client to be
able to speak every protocol it offers, so the fix was to stop claiming it.

It caught three real bugs during development: a multiples-of-p offset that
reduced to zero, a wrong `a^9` in the inversion chain, and `BB + a24*E` where
RFC 7748 specifies `AA + a24*E`. Each would have produced plausible-looking
output. **If the prototype fails, fix the prototype — never hand-edit
`X25519.vb` to compensate.**
