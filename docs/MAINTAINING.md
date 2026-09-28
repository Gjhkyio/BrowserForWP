# Maintaining BrowserForWP

Read [`ARCHITECTURE.md`](ARCHITECTURE.md) first — the three platform laws explain
why several otherwise-reasonable changes are impossible.

## Requirements

| Purpose | Requirement |
| --- | --- |
| Building the app and the transport | **Visual Studio 2013 Update 4+** with the *Windows Phone 8.1 SDK*, on Windows |
| Verifying crypto off-device | **Node.js 18+** (any OS) |
| Regenerating image assets | **Python 3.8+** (any OS) — no third-party packages |
| Running the crypto tests | Visual Studio Test Explorer |

The application cannot be built on macOS or Linux. The *crypto* and the *test
vectors* can be fully verified there, which is why the verification tooling is
deliberately dependency-free and cross-platform.

## Build and run

```
1. Open BrowserForWP.sln
2. Debug | ARM   → a developer-unlocked handset
   Debug | x86   → the WP8.1 emulator
3. Deploy (F5)
```

`Build succeeded` is the expected result. A failure in
`BrowserForWP.Crypto` almost always means a managed-crypto API that WinRT 8.1
does not expose — check the API against the WinRT 8.1 surface, not against
desktop .NET.

## The verification commands

Run these before committing. They are cheap and each covers a different layer.

```bash
# Crypto + TLS 1.3 key schedule. Runs anywhere. Must print "52 assertions, 0 failure(s)".
node tools/gen-vectors.mjs

# Verify only, writing nothing. Useful in CI.
node tools/gen-vectors.mjs --check

# X25519 limb arithmetic. Must print "18 checks, 0 failure(s)".
# This is a line-for-line prototype of X25519.vb and is the ONLY way to verify
# that file off-Windows. If it fails, fix the prototype, not the VB.
node tools/proto/w25519.mjs

# The TLS 1.3 client. Must print "31 checks, 0 failure(s)".
# This one needs NETWORK access: it performs a real handshake with a real
# server. It is the transliteration source for BrowserForWP.Net/Tls13/, and the
# only check that can catch a protocol-level mistake, because a self-consistent
# TLS client round-trips its own bugs happily.
node tools/proto/tls13.mjs example.com
node tools/proto/tls13.mjs www.google.com
node tools/proto/tls13.mjs cloudflare.com

# Confirm the polyfill shim is valid ES5 (comment-aware, so it does not
# false-positive on backticks inside comments).
node tools/check-polyfill.mjs

# Static VB.NET structural check. Must print "0 finding(s)", exit code 0.
# This is NOT a compiler. It catches block-balance errors, missing Implements
# members, project/disk drift, namespace mismatch, resw key drift and unwired
# XAML handlers — and it found a real End Property/End Class error. A green run
# still does not mean the project compiles.
node tools/check-vb.mjs

# Regenerate every WP8.1 image asset from the renderer.
python3 tools/make_logo.py
```

In Visual Studio, run the `BrowserForWP.Crypto.Tests` project from Test Explorer.
Those tests consume `Vectors.generated.vb`, which is **generated** — never edit
it by hand. Regenerate with `node tools/gen-vectors.mjs`.

## Do not use these APIs in BrowserForWP.Crypto

A WP8.1 WinRT app compiles against the ".NET for Windows Store apps" profile.
None of the following exist, and none of them are compile errors you can fix
locally — the namespaces are simply absent:

- `System.Security.Cryptography.SHA256`, `SHA256Managed`, `HashAlgorithm`
- `System.Security.Cryptography.HMACSHA256`, `KeyedHashAlgorithm`
- `System.Security.Cryptography.RNGCryptoServiceProvider`, `RandomNumberGenerator`
- `System.Security.Cryptography.AesGcm`

Use `WinRtCrypto` instead, which wraps `Windows.Security.Cryptography.Core`.
See [`ARCHITECTURE.md`](ARCHITECTURE.md#the-crypto-api-surface-on-wp81-winrt--read-this-before-editing-crypto)
for the replacement table.

`RegexOptions.Compiled` is also unsupported in this profile — do not add it back.

## Adding a test vector

1. Add the constant to the appropriate RFC section in `tools/gen-vectors.mjs`.
2. **Assert it against the recomputed value**, not the other way round — the
   script must fail if the implementation and the published constant disagree.
3. Run `node tools/gen-vectors.mjs`. It must reach
   `0 failure(s)` before it will emit anything; it refuses to write vectors that
   did not verify.
4. Add the corresponding `Assert` to the VB test project, referencing the
   generated name.

The script is fail-closed by design: if an assertion fails, **no** vector file is
written, so a broken implementation cannot quietly become the expected value.

## Adding a language

Worked example: adding German.

1. Create `BrowserForWP/Strings/de-DE/Resources.resw`, copying every key from
   `en-US/Resources.resw`.
2. Add `"de-DE"` to `_supported` in
   `BrowserForWP.Localization/LanguageCatalog.vb`.
3. Add its display name to `LanguageCatalog.DisplayName` (`"Deutsch"`).
4. Add `de-DE` to `<Resources>` in `Package.appxmanifest`.
5. Verify key parity (this is the step people skip):

   ```bash
   python3 - <<'PY'
   import xml.etree.ElementTree as ET, glob
   sets = {p: {d.get('name') for d in ET.parse(p).getroot().findall('data')}
           for p in sorted(glob.glob('BrowserForWP/Strings/*/Resources.resw'))}
   base = list(sets.values())[0]
   for p, keys in sets.items():
       print(p, len(keys), 'missing:', sorted(base - keys) or 'none', 'extra:', sorted(keys - base) or 'none')
   PY
   ```

6. Commit. `en-US` is the fallback — never remove it.

## Adding a feature

Follow `.agents/skills/browserforwp/SKILL.md`. In short: plan to
`docs/superpowers/plans/`, write the failing test, implement the minimum, verify,
commit, push.

**Decide the layer before writing code.** The single most common mistake is
putting network or key-schedule logic in `Core`, or XAML in `Net`. If you cannot
name the layer, the change is probably two changes.

## Modifying the transport

`BrowserForWP.Net` is the most delicate layer. Rules:

- Never weaken a check to make a site work. A handshake that fails
  authentication must throw, not fall back.
- Never add a TLS 1.2 fallback to `Tls13Client`. TLS 1.2 already exists through
  Schannel for legacy destinations; conflating the two hides which path a
  connection actually took.
- Any change to `KeySchedule` or `TlsRecordLayer` requires
  `node tools/gen-vectors.mjs` to pass **and** a handset run of
  **Settings → Run TLS probe**.
- The record layer's sequence number is part of the AEAD nonce. Reusing one is a
  catastrophic failure, not a glitch. `TlsRecordLayerTests` guards it.

## Modifying the engine layer

`IBrowserEngine` is a public contract. Adding a member means implementing it in
every engine. Before adding one, ask whether `Capabilities` or `InvokeScriptAsync`
already covers the need.

When adding a capability flag, set it to what the engine **actually** does.
`TridentEngine` claiming `SupportsTls13 = True` would be a lie that the UI then
repeats to the user — see the guard in the plan's Task 11, Step 3.

## Build host requirements

A real build needs **all three** of the following. Without them, the only
verification available is `tools/check-vb.mjs`, which is a static checker and
not a compiler.

1. **An x64 Windows host.** Visual Studio 2013 is not supported on Arm64
   Windows. Microsoft states that pre-17.4 Visual Studio "can run on Arm-powered
   devices via x64 emulation, but some features aren't supported on Arm", and
   the WP8.1 SDK ships no Arm64 MSBuild targets. An Arm64 VM cannot do this
   build, at any setting.
2. **Visual Studio 2013 Update 4 or later.** Update 2 is the documented minimum
   for Windows Phone 8.1.
3. **The Windows Phone 8.1 SDK and the Windows 8.1 SDK.** The latter is required
   by `TargetPlatformVersion 8.1`.

### Status of the build

**A real build has been run, from Visual Studio inside the Windows VM, over the
Parallels shared folder** (`C:\Mac\Home\Documents\BrowserForWP\...`). It did not
succeed. This section is the record; keep it current rather than aspirational.

The Visual Studio version and SDK build numbers were not captured — fill them in
on the next attempt.

**Observed failures and what they meant.** Approximately 78 errors, but they
were the product of two defects:

| Symptom | Real cause | Fix applied |
| --- | --- | --- |
| `The property "Content" can only be set once. MainPage.xaml (1,1)` | `MainPage.xaml` had **two direct children of `<Page>`** (the layout grid and the settings overlay). `Page.Content` can hold one object. | Wrapped both in a single root `<Grid>`; the overlay is declared second so it still draws on top. |
| `'Sub Main' was not found`, `'InitializeComponent' is not declared`, and ~65 × `'<Name>' is not declared` | **Consequences of the first row.** The XAML compiler rejected `MainPage.xaml`, so `MainPage.g.vb` was never generated — no partial class, therefore no `x:Name` fields and no generated entry point. | Same fix. |
| `Value '128274' cannot be converted to 'Char'` (×2) | `ChrW(&H1F512)` / `ChrW(&H1F513)`. Those are supplementary-plane code points; a `Char` is 16 bits. | `Char.ConvertFromUtf32`. |
| `'Localization' is not declared`; `Type 'IBrowserEngine' / 'BrowserSession' / 'TridentEngine' is not defined`; `The referenced component 'BrowserForWP.Core' / '.Crypto' / '.Localization' could not be found` | The three library projects produced no referenceable assembly. Likely because they declared no `TargetPlatformIdentifier`, so a WP8.1 app cannot resolve them as references. | Added `<TargetPlatformIdentifier>WindowsPhoneApp</TargetPlatformIdentifier>` to all four library projects. |
| `Impossibile trovare il percorso specificato.` (no file attributed) | A project-level build step failed. Building over a Parallels **shared folder** is the prime suspect: MSBuild and the XAML/PRI compiler are unreliable on that path. | **Copy the repository to local disk in the guest and build there.** |

**The lesson worth keeping:** the two defects in rows 1 and 3 were the whole
story, and row 1's error message understates it by an order of magnitude. When a
build produces dozens of unidentified-identifier errors, look for the one
structural failure that stopped code generation before reading any of them.

### Build from local disk, not the shared folder

Do this before debugging anything else:

```cmd
xcopy /E /I /Y "C:\Mac\Home\Documents\BrowserForWP" "C:\dev\BrowserForWP"
cd /d C:\dev\BrowserForWP
msbuild BrowserForWP.sln /p:Configuration=Debug /p:Platform=ARM /v:minimal
```

Then copy the repository back (or work from git) rather than building in place.
A failure with no file path attached is almost always the toolchain, not the code.

### Round 2 — what was fixed, and what is still open

The second build removed two whole error families, confirming the first two fixes:
`The property "Content" can only be set once` and `Value '128274' cannot be
converted to 'Char'` no longer appear.

**Cause of the remaining `Sub Main` / `InitializeComponent` / `x:Name` cascade:**
`BrowserForWP.vbproj` declared `<Content Include="Polyfill\compat.js" />`, but no
such file existed in the project — the polyfill lives in `BrowserForWP.Polyfill`.
A declared content item with no file fails the build early, so the XAML compile
never runs and `App.g.vb` is never generated. This is why `Sub Main` and
`InitializeComponent` were missing *again* after the XAML was already correct.

Fixed by including the canonical file with a `Link`, so it ships at
`Polyfill\compat.js` without being duplicated in the repository. **The checker
did not catch this**, because its "declared file exists" test only covered
`.vb`/`.xaml`/`.resw`; it now covers every declared item, with a negative control.

**Still open:**

1. `The referenced component 'BrowserForWP.Core' / '.Crypto' / '.Localization'
could not be found`, and every type they define being `not defined`. This may
still be unresolved, **or** it may be a consequence of the early failure aborting
the build before the libraries were built. Round 3 settles it. If it persists,
recreate each library in the IDE as a WP8.1 Class Library and re-add the sources.
2. **The polyfill is packaged but never injected.** `compat.js` now ships in the
app, but nothing reads it: `TridentEngine` has no injection code, only
`InvokeScriptAsync`. The README claims the polyfill is "injected into every
document before scripts run" — **that is not yet true.** Either implement
injection on navigation (read from the app package, then
`InvokeScriptAsync("eval", ...)` before the document scripts run) or soften the
claim. Do not leave the README asserting a behaviour the code does not have.

### Known first-build risks

The library project files for `BrowserForWP.Crypto`, `.Core`, `.Localization` and
`.Net` were **hand-authored** and have never been validated by the WP8.1 targets.
If the build reports `MSB4019` or an unrecognised project type, recreate the
project in the IDE (File -> New -> Project -> Visual Basic -> Windows Phone Apps
-> Class Library) and add the existing `.vb` files to it. Expect to do this once
per library; it is what "the SDK has not seen these files" means in practice.

The most likely compile errors, in order of frequency:

- **WinRT API availability.** `System.Security.Cryptography.SHA256`,
  `HMACSHA256` and `RNGCryptoServiceProvider` do not exist in the
  ".NET for Windows Store apps" profile. Use `WinRtCrypto`.
  `RegexOptions.Compiled` is also unsupported.
- **`Await` on `IAsyncAction` / `IAsyncOperation`.** VB supports this directly;
  if the compiler objects, add
  `Imports System.Runtime.InteropServices.WindowsRuntime` to the file.
- **Namespace duplication.** The full name is `<RootNamespace>.` plus the file's
  own `Namespace` block. `BrowserForWP.Crypto.vbproj` sets `RootNamespace` to
  `BrowserForWP` precisely because its sources declare `Namespace Crypto`.

## The loop

Every change follows five steps, in order. The canonical version lives in
[`.agents/skills/browserforwp/SKILL.md`](../.agents/skills/browserforwp/SKILL.md);
this is the summary.

1. **Plan** the change before writing code (`superpowers:writing-plans`).
2. **Implement** the smallest change that satisfies the plan.
3. **Verify** every layer touched, with the commands above.
4. **Commit** with a Conventional Commit message.
5. **Push.** A commit that is not pushed is not done.

Then update the skill if any tool, command, file layout or constraint changed.

## Release checklist

- [ ] `node tools/gen-vectors.mjs` → `52 assertions, 0 failure(s)`
- [ ] `python3 tools/make_logo.py` → all assets regenerated, no diff
- [ ] Polyfill ES5 check passes
- [ ] Visual Studio: `Debug | ARM` → `Build succeeded`
- [ ] Test Explorer: `BrowserForWP.Crypto.Tests` all green
- [ ] Handset: TLS probe reports `TLS 1.3`
- [ ] Handset: switch the phone to Italian — **every** UI string changes; no
      English leaking through
- [ ] `README.md` and `README.it.md` still agree on the platform-limitation
      section
- [ ] `Package.appxmanifest` version bumped
- [ ] Everything committed and pushed

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `PermissionError` from `make_logo.py` | The project tree is owned by another user: `sudo chown -R "$(whoami)" BrowserForWP BrowserForWP.sln` |
| `gen-vectors.mjs` exits 1 | An algorithm and an RFC constant disagree. The failing line prints both values — fix the algorithm, not the constant. |
| TLS handshake fails on every site | Check the `supported_versions` and `key_share` extensions in `ClientHelloBuilder`; a malformed extension makes servers close the connection immediately. |
| Site fails only on the handset | Run the compatibility probe. It is almost always a missing script feature, not a transport problem. |
| UI shows English on an Italian phone | `Localizer.Initialize()` was not called in `App.OnLaunched` before the frame was created. |
