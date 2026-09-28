# BrowserForWP

**A modern-transport browser for Windows Phone 8.1 — 100% on-device, zero backend.**

[English](README.md) · [Italiano](README.it.md)

---

## Read this first: what is actually possible on Windows Phone 8.1

This project makes an unusually honest promise, so here is the ground truth you
deserve before reading any feature list.

Windows Phone 8.1 is a **closed platform released in 2014 and end-of-lifed in
July 2017**. Its browser stack is **Trident (Internet Explorer 11)**, and the
operating system gives third-party apps **no way to substitute a rendering
engine**. This is a structural limitation of the OS, not a limitation of this
project's ambition.

| Goal | Reality on Windows Phone 8.1 | What BrowserForWP does |
| --- | --- | --- |
| Ship the **Chromium** engine | No build of Chromium/Blink exists for WinRT-ARM 8.1. App containers cannot host a sandboxed multi-process renderer. | Provides a pluggable `IBrowserEngine`. Ships `TridentEngine` on WP8.1; `WebView2Engine` (Chromium) and `GeckoViewEngine` (Firefox) drop in on any platform that has them. |
| Ship the **Firefox / Gecko** engine | Mozilla cancelled Firefox for Windows Phone in 2015. No binary ever shipped. | Same pluggable abstraction as above. |
| **TLS 1.3** | Schannel on WP8.1 tops out at **TLS 1.2**, and the OS offers no API to raise it. | **Implemented from the RFCs, in managed code, on-device**: a complete TLS 1.3 client (`BrowserForWP.Net`) running over a raw `StreamSocket`, so the app's own network layer speaks TLS 1.3 today. |
| **Modern HTTPS** | The system `WebView` negotiates whatever Schannel supports. | `Tls13Client` + DNS-over-HTTPS resolver + certificate pinning for the app's transport layer. |
| **Modern web pages** | IE11 cannot parse or run modern JavaScript. | An on-device polyfill/transpilation pipeline injected into every page (`BrowserForWP.Core`), plus a compatibility diagnostic that tells you *why* a given site still fails. |
| **No backend** | — | Every component — crypto, TLS, DNS, polyfills, history, localization — runs entirely on the handset. No server, no proxy service, no telemetry. |

> **On the on-device loopback proxy idea:** Windows AppContainers block
> `127.0.0.1` traffic by default, so a local proxy cannot feed the system
> `WebView`. That architecture is deliberately **not** used here. See
> [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full constraint analysis.

**Bottom line:** you get a genuinely modern *transport* layer (TLS 1.3, DoH,
pinning) and a genuinely modern *content* layer (polyfills), on an unchanged
*rendering* layer — because the rendering layer cannot be changed on this OS.
The engine abstraction means the day you point this at a device with a real
modern engine, the transport and content layers come with you.

---

## Features

- **Full browser shell** — address bar, back / forward / reload / stop, tabs,
  progress indicator, per-site security readout.
- **Smart address bar** — normalises input, distinguishes URLs from search
  queries, restores missing schemes, and refuses unsafe schemes.
- **TLS 1.3 transport (RFC 8446)** — managed implementation over
  `Windows.Networking.Sockets.StreamSocket`.
- **DNS over HTTPS (RFC 8484)** — resolves hostnames off the wire, so a stale
  or hijacked local resolver cannot break or redirect you.
- **Certificate pinning** — user-managed per-site pins with explicit,
  reversible override.
- **Polyfill injection** — a curated on-device compatibility layer is injected
  into every document before scripts run.
- **Bilingual UI** — English and Italian, auto-selected from the phone's
  display language, with per-app override.
- **Diagnostics** — a built-in probe that reports exactly which modern feature
  caused a page to fail, so the limitation is visible rather than mysterious.

## Repository layout

```
BrowserForWP/
├── BrowserForWP.sln            Visual Studio 2013+ solution
├── BrowserForWP/              Windows Phone 8.1 app (VB.NET / WinRT / XAML)
│   ├── MainPage.xaml(.vb)     Browser shell UI
│   ├── Assets/                Logo, tiles, splash (generated)
│   └── Strings/               en-US / it-IT UI resources
├── BrowserForWP.Core/         Engine abstraction, tabs, history, address bar
├── BrowserForWP.Net/          TLS 1.3, DoH, HTTP/1.1 client
├── BrowserForWP.Crypto/       HKDF, X25519, AES-128-GCM
├── BrowserForWP.Localization/ Language resolution + string lookup
├── BrowserForWP.Polyfill/     On-device JS compatibility bundle
├── docs/
│   ├── ARCHITECTURE.md        Design + platform constraint analysis
│   ├── MAINTAINING.md         How to build, run, and extend the project
│   └── superpowers/plans/     Implementation plans (one per feature)
├── tests/                     Unit tests (run in Visual Studio)
├── tools/
│   ├── make_logo.py           Regenerates every image asset from SVG
│   └── gen-vectors.mjs        Emits RFC known-answer vectors for the tests
└── .agents/skills/browserforwp/SKILL.md
                               Project skill: plan → commit → push → extend
```

## Building

Requirements: **Visual Studio 2013 Update 4 or later** with the *Windows Phone
8.1 SDK*, on Windows. The solution targets `TargetPlatformVersion 8.1` and
`WindowsPhoneApp`.

```
1. Open BrowserForWP.sln
2. Select a phone target: Debug | ARM  (device) or Debug | x86 (emulator)
3. Deploy to a developer-unlocked handset or the WP8.1 emulator
```

> The rendering engine, crypto and TLS code cannot be exercised on macOS or
> Linux: the WP8.1 SDK is Windows-only. The pure-managed crypto in
> `BrowserForWP.Crypto` is mirrored by `tests/`, which runs in Visual Studio.

Regenerate image assets and test vectors at any time:

```bash
python3 tools/make_logo.py          # rewrites BrowserForWP/Assets/*.png
node tools/gen-vectors.mjs          # rewrites tools/out/*.json
```

## Localization

The app ships **en-US** (default) and **it-IT**. The display language is chosen
in this order:

1. A per-app override, if the user has set one.
2. `Windows.Globalization.ApplicationLanguages.Languages` — the phone's
   ordered display-language list.
3. Fallback: `en-US`.

Adding a language means adding one folder of resources and one entry to the
language table — no code changes. See
[`docs/MAINTAINING.md`](docs/MAINTAINING.md#adding-a-language).

## Contributing

Read [`docs/MAINTAINING.md`](docs/MAINTAINING.md) for the build/test workflow
and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) before changing the engine or
transport layers. The project skill at
[`.agents/skills/browserforwp/SKILL.md`](.agents/skills/browserforwp/SKILL.md)
describes the required plan → test → commit → push loop.

## License

[MIT](LICENSE) © 2026 vincenzosco

---

## Disclosure: this project is 100% AI-made

Every part of BrowserForWP — the architecture, the TLS 1.3 stack, the crypto,
the UI, the tooling, the documentation and this notice — was written by an AI
coding agent, with a human directing the work and reviewing the result at each
step.

That is a real statement about how much you should trust it, so here is the
honest position rather than a boast:

- **Nothing here has been compiled.** No Windows Phone 8.1 SDK and no Visual
  Studio 2013 were available on the machine that wrote this, so the VB.NET has
  never been through a real compiler. `tools/check-vb.mjs` performs the
  mechanical checks that *can* be reproduced off-Windows, and it found genuine
  defects — but it is not a compiler, and a green run does not mean it builds.
- **The crypto and the TLS 1.3 protocol are verified, but not on a handset.**
  `tools/gen-vectors.mjs` (52 assertions against RFC 5869/7748/8439/8448 and
  NIST AES-GCM), `tools/proto/w25519.mjs` (18 checks) and
  `tools/proto/tls13.mjs` (31 checks, completing real handshakes with Google,
  Cloudflare and example.com) all pass.
- **It has never run on a phone.** XAML layout, WebView behaviour and
  performance on 2014 hardware are unverified.
- **A real build attempt was made and reported honestly.** The available
  Windows VM is ARM64, where Microsoft does not support pre-17.4 Visual Studio
  and where the WP8.1 SDK has no build targets. That is documented in
  [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) instead of being quietly skipped.
- **Claims were tested, and the false ones were dropped.** Chromium and Firefox
  cannot run on this OS and TLS 1.3 cannot be obtained from it; both facts are
  stated plainly rather than papered over. The work that *was* possible — a
  from-scratch TLS 1.3 stack — was done and verified.

Treat this as a well-documented starting point that still needs a real build and
a real device pass, not as a finished, shipped product.
