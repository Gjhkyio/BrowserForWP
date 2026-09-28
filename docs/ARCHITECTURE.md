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
2. **`X25519`** — RFC 7748 Montgomery ladder over `2^255 - 19`. Hand-rolled
   because WinRT 8.1 exposes no X25519 primitive, and group `x25519` is
   mandatory for TLS 1.3. Field arithmetic uses `BigInteger` rather than
   radix-2^25.5 limbs: the WP8.1 SDK is Windows-only, so the arithmetic cannot
   be executed during development on other platforms, and a representation whose
   reduction is reviewable line by line is worth more than one that is fast but
   opaque. One handshake is ~255 ladder steps, which is noise beside the network
   round trip. **This implementation is not constant-time** — a documented
   trade-off, with a limb-based replacement noted as the optimisation path.
3. **`ChaCha20Poly1305`** — RFC 8439. ChaCha20 is natural 32-bit arithmetic;
   the Poly1305 accumulator uses `BigInteger` for the same reviewability reason
   as X25519. It is only reached when `TLS_CHACHA20_POLY1305_SHA256` is
   negotiated — the preferred suite, AES-GCM, goes through the platform
   provider and never touches this code.
4. **`AesGcm`** — a thin adapter over the platform provider, because managed
   AES-GCM is unacceptably slow on 2014 ARM silicon. It deliberately returns the
   *same* `SealedResult` type as ChaCha20-Poly1305 so the record layer is
   suite-agnostic.
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

## Verification strategy

The Windows Phone 8.1 SDK is Windows-only, so the crypto cannot be compiled on
macOS or Linux. Rather than accept "untested", the algorithms are verified in a
language-independent way:

`tools/gen-vectors.mjs` recomputes every value from first principles using
Node's crypto, **asserts it against the constant published in the RFC**, and only
then emits the VB test data. Current status: **55 assertions, 0 failures**,
covering RFC 5869 A.1–A.3, RFC 7748 §5.2/§6.1, RFC 8439 §2.8.2, RFC 8448 §3 and
NIST CAVS AES-GCM.

This means a wrong `Fe.Mul` or a wrong counter increment is caught before the
code ever reaches a handset, and the VB tests are grounded in values that were
independently checked rather than hand-copied.
