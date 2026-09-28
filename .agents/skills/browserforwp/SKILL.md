---
name: browserforwp
description: Use when working on the BrowserForWP project (Windows Phone 8.1 browser with a modern on-device TLS 1.3 transport). Covers the mandatory plan → test → commit → push loop, how to locate and build the project, and exactly how to add or modify a feature. Trigger this whenever a change touches BrowserForWP.sln, its projects, the crypto/TLS layers, the UI, or the localization resources.
---

# BrowserForWP — Project Skill

Governs every change to this repository. Read
[`docs/MAINTAINING.md`](../../../docs/MAINTAINING.md) and
[`docs/ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md) before touching the
transport or engine layers.

## The non-negotiable loop

Every request — feature, bug, refactor, docs — runs this loop. No exceptions,
including "trivial" one-line changes.

```
1. PLAN      write the plan to docs/superpowers/plans/YYYY-MM-DD-<slug>.md
2. TEST      write the failing test FIRST (see "Testing rules")
3. IMPLEMENT the minimum that makes it pass
4. VERIFY    run the verification commands for the layer you touched
5. COMMIT    one logical change per commit, conventional-commit subject
6. PUSH      git push — always, at the end of every completed task
```

Step 6 is not optional. If a push fails, say so loudly and stop; do not
silently leave work unpushed and continue.

## Step 1 — Always plan first

Save to `docs/superpowers/plans/YYYY-MM-DD-<feature-slug>.md`. A plan has:

- A one-sentence **Goal**.
- **Global Constraints** copied verbatim from the request (version floors,
  platform limits, "no backend", "on-device only").
- A **File Structure** section: every file created or modified, and why.
- One section per **task**, each task ending in an independently testable
  deliverable with its own commit.
- **Exact** file paths, **complete** code in every code step, **exact**
  commands with **expected output**.

Never write `TBD`, "add error handling", "similar to Task N", or a step that
describes work without showing the code. If a step changes code, show the code.

**Hard platform constraint — check every plan against it.** Windows Phone 8.1
cannot host Chromium or Firefox, cannot replace Trident, and cannot exceed
TLS 1.2 through the OS. Anything that assumes otherwise is a plan error. See
[`docs/ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md#the-three-platform-laws).

**Second hard constraint — the API surface.** A WP8.1 WinRT app compiles against
the ".NET for Windows Store apps" profile, *not* desktop .NET. `SHA256`,
`HMACSHA256` and `RNGCryptoServiceProvider` do **not exist** there; use
`WinRtCrypto` (`Windows.Security.Cryptography.Core`). `RegexOptions.Compiled` is
unsupported. Before introducing any `System.*` type, confirm it exists in that
profile — do not assume a desktop API is available.

**Third — never edit `X25519.vb` or `BrowserForWP.Net/Tls13/` by hand.** Both
are transliterations of executable prototypes, and those prototypes are the only
real checks available off-Windows. Change the prototype first, watch it pass, then
port the change:

- `X25519.vb` -> `tools/proto/w25519.mjs` -> `18 checks, 0 failure(s)`
- `BrowserForWP.Net/Tls13/*` -> `tools/proto/tls13.mjs` -> `31 checks, 0 failure(s)`

**Fourth — a self-consistent TLS client proves nothing.** Sealing and opening your
own records will round-trip any bug that is symmetric. `tools/proto/tls13.mjs`
completes handshakes with real servers precisely so that field-order and
length-prefix mistakes cannot hide; three such bugs were found this way. If you
change record framing or ClientHello layout, re-run it against a live host.

## Step 4 — Verification commands

Run the checks for the layer you actually changed. Do not claim a layer is
verified if you skipped its command.

| You changed | Run | Expected |
| --- | --- | --- |
| Anything in `BrowserForWP.Crypto/` | `node tools/gen-vectors.mjs` | `52 assertions, 0 failure(s)` |
| `X25519.vb` (or its prototype) | `node tools/proto/w25519.mjs` | `18 checks, 0 failure(s)` |
| Anything in `BrowserForWP.Net/Tls13/` | `node tools/proto/tls13.mjs example.com` | `31 checks, 0 failure(s)` |
| Test vectors themselves | `node tools/gen-vectors.mjs` | every line prefixed `✓`, exit code 0 |
| `BrowserForWP.Polyfill/compat.js` | `node tools/check-polyfill.mjs` | `is valid ES5` |
| `BrowserForWP/Assets/**` | `python3 tools/make_logo.py` | one line per generated PNG, exit code 0 |
| UI / XAML / VB app code | Build in Visual Studio: `Debug \| ARM` | `Build succeeded` |
| Crypto unit tests | Test Explorer → run `BrowserForWP.Crypto.Tests` | all tests green |
| TLS / DoH / sockets | Deploy to handset, run **Diagnostics → TLS probe** | reports negotiated `TLS1.3` |

`node tools/gen-vectors.mjs` is the fastest real signal available off-Windows:
it recomputes the algorithms from the RFCs and aborts on any mismatch. If you
edit crypto and this script still passes, your change is at least
algorithmically sound.

## Step 5 — Commit conventions

Conventional Commits, imperative mood, subject ≤ 72 chars. Reference the plan.

```
feat(tls): add X25519 key agreement
fix(core): keep history index in range after back-navigation
docs(plan): add plan for polyfill injection pipeline
test(crypto): cover HKDF with RFC 5869 A.2
chore(assets): regenerate tiles at scale-240
```

Body: what changed and **why**. One logical change per commit — do not bundle
a refactor with a feature.

## Step 6 — Push

```bash
git push
```

Always run it after a completed task. If there is no upstream:
`git push -u origin main`. Never force-push a shared branch.

## How to find the project

```
BrowserForWP.sln              ← open this in Visual Studio 2013+
BrowserForWP/                 ← the WP8.1 app: XAML UI, assets, UI strings
BrowserForWP.Core/            ← engine abstraction, tabs, history, address bar
BrowserForWP.Net/             ← TLS 1.3, DoH, HTTP client
BrowserForWP.Crypto/          ← HKDF, X25519, ChaCha20-Poly1305, AES-GCM
BrowserForWP.Localization/    ← language resolution + string lookup
docs/ARCHITECTURE.md          ← design + the platform laws
docs/MAINTAINING.md           ← build/run/extend recipes
tools/gen-vectors.mjs         ← crypto verification (runs anywhere)
tools/make_logo.py            ← regenerates every image asset
```

Rule of thumb: **crypto knows nothing about TLS; TLS knows nothing about the
UI; Core knows nothing about crypto.** A change that violates a layer boundary
is a design bug, not a shortcut.

## How to add a new feature

Worked example: *add a "desktop site" toggle.*

1. **Plan.** `docs/superpowers/plans/2026-09-28-desktop-site-toggle.md`.
2. **Decide the layer.** This is browser state + UI, so it lives in
   `BrowserForWP.Core` and `BrowserForWP/`, and touches neither crypto nor TLS.
3. **Write the failing test first.** In `tests/BrowserForWP.Core.Tests/`:

   ```vb
   <TestMethod>
   Public Sub DesktopMode_ChangesUserAgent()
       Dim session = New BrowserSession()
       session.DesktopMode = True
       Assert.AreEqual(UserAgents.DesktopWindows, session.EffectiveUserAgent)
   End Sub
   ```

4. **Run it and watch it fail.** Expected: `BrowserSession` has no
   `DesktopMode` — a compile error is a valid red result here.
5. **Implement the minimum.** Add the property and the lookup:

   ```vb
   Public Property DesktopMode As Boolean = False

   Public ReadOnly Property EffectiveUserAgent As String
       Get
           Return If(DesktopMode, UserAgents.DesktopWindows, UserAgents.MobileDefault)
       End Get
   End Property
   ```

6. **Run the test.** Expected: PASS.
7. **Localize any new user-visible string.** Add the key to **both**
   `BrowserForWP/Strings/en-US/Resources.resw` and
   `BrowserForWP/Strings/it-IT/Resources.resw`. A key present in only one
   language is a bug — see "Adding a language" below.
8. **Wire the UI.** Add the control to `MainPage.xaml`, its handler to
   `MainPage.xaml.vb`, and bind the label to the resource key.
9. **Verify.** Build `Debug | ARM` in Visual Studio; deploy; toggle the setting;
   confirm the server sees the desktop UA.
10. **Commit and push.**

   ```bash
   git add tests/BrowserForWP.Core.Tests/DesktopModeTests.vb \
           BrowserForWP.Core/BrowserSession.vb \
           BrowserForWP/MainPage.xaml BrowserForWP/MainPage.xaml.vb \
           BrowserForWP/Strings/en-US/Resources.resw \
           BrowserForWP/Strings/it-IT/Resources.resw \
           docs/superpowers/plans/2026-09-28-desktop-site-toggle.md
   git commit -m "feat(core): add desktop-site user agent toggle"
   git push
   ```

## How to modify an existing feature

Same loop, but the diagnosis comes first.

1. **Reproduce.** Write a test that fails for the *observed* behaviour, not the
   behaviour you assume. If you cannot reproduce it, you cannot fix it.
2. **Locate by layer, not by grepping the symptom.** A site rendering wrongly
   is a Core/polyfill problem. A site refusing to connect is a Net/TLS problem.
   A string showing in the wrong language is a Localization problem. Grepping
   the symptom usually finds the wrong file.
3. **Check the platform laws before "fixing".** If the report is "modern site
   does not work", confirm it is not simply IE11 failing on syntax the OS
   cannot parse. Run **Diagnostics → Compatibility probe** first. Do not reach
   for a code change that the platform will never honour.
4. **Change one thing.** Update the test to encode the new expected behaviour,
   watch it fail, make it pass.
5. **Regression-guard.** If the bug could return, leave the test that catches it.
6. **Verify, commit, push** — as above.

## Adding a language

1. Create `BrowserForWP/Strings/<bcp-47>/Resources.resw`, e.g. `fr-FR`.
2. Copy every key from `en-US`. Missing keys fall back to English at runtime,
   but the parity test will flag them.
3. Register the display name in `BrowserForWP.Localization/LanguageCatalog.vb`.
4. Add the language to the `<Resources>` list in `Package.appxmanifest`.
5. Test that `Localizer` picks it when
   `ApplicationLanguages.PrimaryLanguageOverride` is set to it.

`en-US` is the **default and fallback** language. Never delete it.

## Things that will get a change rejected

- Claiming a Chromium or Firefox engine works on WP8.1.
- Claiming the system `WebView` uses TLS 1.3.
- A local loopback proxy feeding the `WebView` (AppContainers block
  `127.0.0.1`).
- A new user-visible string added to only one language file.
- A change to `BrowserForWP.Crypto/` without a passing
  `node tools/gen-vectors.mjs`.
- Work left uncommitted or unpushed.
