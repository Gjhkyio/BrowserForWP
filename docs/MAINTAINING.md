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
# Crypto + TLS 1.3 key schedule. Runs anywhere. Must print "53 assertions, 0 failure(s)".
# The 53rd asserts brace balance on the VB it emits: an empty RFC 5869 case once
# produced an unterminated initializer and broke the test project with BC30201.
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

# Core logic. Must print "core-logic checks, 0 failure(s)". This is a
# transliteration of tests/BrowserForWP.Core.Tests/CoreLogicTests.vb, and it is
# the only way those assertions execute at all off-device. Keep the two in step.
node tools/proto/core-logic.mjs

# The shell and delivery guards that arrived with the merged browser shell.
node tools/proto/shell-guards.mjs    # picker/tab re-entrancy, completed URL, sln registration
node tools/proto/trackerblock.mjs    # host blocklist matching
node tools/proto/pinstore.mjs        # pin normalisation and comparison
node tools/proto/useragents.mjs      # UA table and search-URL escaping
node tools/proto/lightweight.mjs     # lite defaults, caps, resource keys
node tools/proto/modern-sites.mjs    # shim markers, redirect rules, delivery wiring

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

Those vectors also produce `tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb`.
That file is **generated** — never edit it by hand; regenerate it with
`node tools/gen-vectors.mjs`. It is compiled by the guest build, but nothing
executes the project that contains it.

See "Where the tests actually are" below before assuming those vectors are being
checked by a VB test run.

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

1. **A Windows host with the toolchain installed.** An **x64** host is what
   Microsoft documents and the safe default. It is not the only host that works:
   this project builds on an **ARM64** Windows 11 Parallels guest, driving
   `msbuild.exe` directly from the command line.

   An earlier revision of this file asserted that an Arm64 VM "cannot do this
   build, at any setting", citing Microsoft's statement that pre-17.4 Visual
   Studio is unsupported on Arm. That statement is about the **IDE**; it does not
   follow that the **command-line build** fails, and nobody had tested it. It
   works. The cost of assuming otherwise was three rounds of compile errors that
   a two-minute build would have surfaced immediately.
2. **Visual Studio 2013 Update 4 or later.** Update 2 is the documented minimum
   for Windows Phone 8.1.
3. **The Windows Phone 8.1 SDK and the Windows 8.1 SDK.** The latter is required
   by `TargetPlatformVersion 8.1`.

## The build that actually works

```bash
prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \
    "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd"
```

`tools/vm-build.cmd` cds to the repository, prints the MSBuild version, checks the
three toolchain paths, then runs

```
msbuild BrowserForWP.sln /nologo /v:minimal /p:Configuration=Debug /p:Platform=ARM
```

prints a diagnostic summary, and finishes with `=== BUILD_EXIT=<n> ===`. Extra
arguments are forwarded, so `tools\vm-build.cmd /t:Rebuild` performs a clean
build.

**The script decides pass/fail, and it is stricter than MSBuild.** It exits
non-zero if an `error BC`, `error MSB` or `error APPX` line appears in the log,
even when MSBuild itself returns 0, and it reports the one allow-listed
diagnostic (`WMC9999`) by name. A clean exit code is therefore a statement about
the log, not merely about MSBuild's opinion of it.

Both halves of that are verified, not asserted:

- a normal run prints `known-noise: WMC9999 (allowed, ...)`, `Real compiler
errors: none` and `=== BUILD_EXIT=0 ===`;
- a deliberate `Return "this is not an Integer"` in a function returning `Integer`
  prints `UNEXPECTED COMPILER ERRORS` and `=== BUILD_EXIT=1 ===`. A checker that
  has never been observed to fail is not a checker.

**Quoting is load-bearing.** `prlctl exec` takes the command and its arguments as
SEPARATE argv entries. `prlctl exec <vm> "cmd /c ver"` fails *silently*, because
argv[0] becomes a program literally named `cmd /c ver`. `--current-user` and `-u`
also fail on this guest (no stored credentials); do not guess at them. That is why
the build lives in a batch file rather than a one-liner.

Verified toolchain in that guest (`BIOS type: efi-arm64`, Windows 11, Parallels
Tools 27.0.0-58628):

| Component | Path | Version |
| --- | --- | --- |
| MSBuild | `C:\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe` | 12.0.40629.0 |
| Visual Studio | `...\Microsoft Visual Studio 12.0\Common7\IDE\devenv.exe` | 2013 |
| Windows Phone SDK | `C:\Program Files (x86)\Microsoft SDKs\Windows Phone\v8.1` | 8.1 |
| Windows SDK | `C:\Program Files (x86)\Windows Kits\8.1` | 8.1 |
| Repository | `C:\Mac\Home\Documents\BrowserForWP` (Parallels shared folder) | |

`devenv.exe` is present but is not the build driver: every build and every
measurement in this file comes from `msbuild.exe` on the command line.

### Status of the build

**The solution builds, cleanly, for `Debug|ARM` and `Release|ARM`.** Each build
produces the four library DLLs, `BrowserForWP.exe`, `App.xbf`, `MainPage.xbf` and
a package set (`*.appx`, `*.appxbundle`, `*.appxupload`) under
`BrowserForWP\AppPackages\`. Repeated `Rebuild` runs are consistent: 12
consecutive builds, `BUILD_EXIT=0` every time, zero `BC` errors.

The sections below are the round-by-round record. They are kept in full because
the error taxonomy is the reusable part — every one of these families looked like
something other than what it was. Read the two `Status of the build` entries in
order.

### Round 1 and Round 2

Approximately 78 errors, but they were the product of two defects:

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

### Shared folder or local disk?

Both work. Building in place over `C:\Mac\Home\Documents\BrowserForWP` is what
produced every artefact described in this file. Copying to local disk is worth
trying when a failure carries *no file path attached at all*, but it is a
diagnostic of last resort rather than a prerequisite — and on this repository it
was never the cause. The advice below it was, in hindsight, a plausible guess
that the next round's evidence did not support.

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

### Round 3 — the libraries compile, and fifty-four new errors

With the early abort gone, the build reached actual compilation and reported the
real state of `BrowserForWP` and `BrowserForWP.Net`. `BrowserForWP.Core`,
`.Crypto` and `.Localization` built there and then; `BrowserForWP.Net` produced
**54 errors across 6 files**.

They came from five distinct causes. None of them is visible to
`tools/check-vb.mjs`, and four of the five are *VB-specific* traps that a
transliteration from JavaScript walks straight into.

| Symptom | Real cause | Fix applied |
| --- | --- | --- |
| `BrowserForWP.Net` failed wholesale: `IDisposable`, `List`, `Encoding`, `ArgumentException`, `Math`, `Array`, `InvalidOperationException` all "not defined"; `BC36948` on every `Async`; `BC30665` on every `Throw` | `BrowserForWP.Net.vbproj` had **no `<Import Include>` ItemGroup** and none of the `OptionStrict`/`OptionInfer`/`OptionCompare` PropertyGroups, and closed with `Microsoft.VisualBasic.targets` instead of the XAML targets. The other three libraries have all of them. Every source file still *parsed*, so it read as broken code rather than a broken project. | Rewrote the project file on the `.Crypto` template: 16 `<Import Include>` entries plus the option groups and the XAML targets import. |
| 24 errors in `ClientHelloBuilder.vb`: `BC30203` "identifier expected", then `U16`, `Bytes`, `Vec8`, `ToArray` "not declared" | **Leading-dot method chains** — `New TlsWriter().` at end of line, next call on the following line. That is legal from **VB 14 (VS2015)**; this is **VB 12 (VS2013)**, which has no implicit line continuation after a period. One syntax mistake, twenty-four errors, all naming the wrong thing. | Rewrote each chain as a `With` block with one call per line. |
| `'SupportedVersions' is not a member of 'Integer'`, `'KeyShare' is not a member of 'Integer'`, `'Alpn' is not a member of 'Integer'` | A local variable named `extensionType` **shadowed the `ExtensionType` enum** — VB is case-insensitive. The compiler blames the enum member, not the local. | Renamed the local to `extType`. Same class of bug as `supported`/`Supported`, `tag`/`Tag`, `Default`/`DefaultTag`, `value`/`Value()`, `shared`/`Shared`. |
| `'SubReaderVec24' is not a member of 'BrowserForWP.Net.Tls13.TlsReader'`, followed by a wall of `BC30574` late-binding errors | `TlsReader` only had `SubReaderVec16`. `Certificate`'s `certificate_list` is a 3-byte length (`RFC 8446 §4.4.2`). | Added `SubReaderVec24()`. |
| `BC30390`: `WinRtCrypto.ToBuffer ... is not accessible in this context because it is 'Friend'` | Assembly-scoped `Friend` used from a *different* assembly. | Made `ToBuffer` (and `ToArray`) `Public`. |
| `BC30002: Type 'CertificateVerifyInfo' is not defined`, in `CertificateValidator` | The class was **nested inside `ServerMessageParser`**; the referencing file named it unqualified. | Moved it to namespace level. |
| `BC30456: 'Verify' is not a member of 'CryptographicEngine'` | The WinRT type exposes `VerifySignature`, not `Verify`. | Corrected. |
| `BC30456: 'ASCII' is not a member of 'System.Text.Encoding'` | `Encoding.ASCII` is genuinely **absent** from the .NET for Windows Store apps profile (it needs `ASCIIEncoding`, which the profile removes). | Replaced with a local `AsciiBytes` helper that also rejects non-ASCII labels instead of silently transcoding them. |
| `BC32006`: cannot convert `Char` to `Integer` | VB has no `Char`-to-`Integer` conversion under `Option Strict`. | `AscW`, as the error itself suggests. |
| `BC30512: Long to Integer`, `BC30311: String to Windows.Networking.HostName`, `BC30290` local shadows its function, `BC30201` comment inside a continued array literal | `UInteger - Integer` widens to `Long`; `StreamSocket.ConnectAsync` needs a `HostName`; `Dim value(...)` inside `Function Value()`; a trailing `'` comment on a continued line inside `New Byte() {...}`. | `CInt(received)`, `New HostName(host)`, renamed the local, moved the comment out of the braces. |

**The lesson worth keeping:** when a whole project fails at once, check whether it
has the *project-level* imports and option groups that its siblings have. Every
file parsing correctly is exactly what makes this look like a code problem.

### Round 4 — the solution builds

`BUILD_EXIT=0`, no `BC` errors, and the package set is produced. Two further
defects were found and verified by experiment:

1. **A XAML theme key that does not exist on WP8.1.** `MainPage.xaml` used
   `Background="{ThemeResource TextBoxBackgroundThemeBrush}"`, a **Windows Phone
   8.0 (Silverlight)** key. WP8.1 XAML does not define it, and the failure is a
   non-fatal internal lookup error:

   ```
   Microsoft.Windows.UI.Xaml.Common.targets(327,9): Xaml Internal Error error
   WMC9999: La chiave specificata non era presente nel dizionario.
   ```

   It does not fail the build, so the brush just stays unset at runtime and the
   message survives review indefinitely. Verified by swapping the key to
   `TextControlBackground`: the diagnostic disappears, and returns when the old
   key is restored. `ApplicationPageBackgroundThemeBrush` *is* a valid WP8.1 key.

2. **Ambiguous image assets.** The packaging step warned six times with
   `APPX1621`: a mixture of `Assets\Logo.png` and `Assets\Logo.scale-240.png`
   matching the same logical name. The manifest names the *logical* path, so the
   base variants must be qualified too. Renamed every base asset to
   `.scale-100.png` (what the WP8.1 template itself generates). Zero `APPX1621`
   after the change. `python3 tools/make_logo.py` and the `.vbproj` `<Content>`
   items were updated together — keep them in step.

**Remaining warnings, all understood and accepted:** two × `BC40000` on
`New ResourceLoader(ResourceMap)` in `Localizer.vb`. The suggested replacement,
`ResourceLoader.GetForCurrentView(name)`, returns a **cached** loader, so it would
silently stop honoring a runtime language change — which `Localizer` depends on.
The deprecated constructor is the one with the semantics this code needs, the
warning is a forward-compatibility note about a "TBD" future release that will
never ship for WP8.1, and the alternative cannot be tested on a handset from here.
A deliberate, documented trade-off.

**`WMC9999` is a diagnostic from the VS2013 XAML compiler, and it is harmless.**
It is not a defect in this codebase and it must not be chased with source edits.

```
Microsoft.Windows.UI.Xaml.Common.targets(327,9): Xaml Internal Error error
WMC9999: La chiave specificata non era presente nel dizionario.
```

*(the given key was not present in the dictionary)*

Measure it with `tools/wmc9999-probe.sh`, which runs three build modes `RUNS`
times each and hashes the compiled XAML from every run:

```
RUNS=4 bash tools/wmc9999-probe.sh
```

**Measured result (12 runs):** `WMC9999=1` in **12 of 12** runs — 4/4
`sln /t:Rebuild`, 4/4 app-project `/t:Rebuild`, and 4/4 app-project incremental
builds. `distinct XBF hash pairs across 12 runs: 1`. The probe exits non-zero if
that count is ever anything but 1.

So within a single host session the diagnostic is **deterministic**, not
intermittent, and the compiled XAML is invariant. Both artefacts agree every
time:

```
App.xbf      = b7af0673a52d230302275b6c60fa2a64
MainPage.xbf = 817580f71c93802ca8818c328074ea85
```

Earlier in the same day, isolated ad-hoc builds reported `WMC9999=0` three times
with sources that are not distinguishable from today's, including one solution
build with `/p:BuildProjectReferences=false`. Those zeros are **not reproduced**
by the matrix above. The honest reading is that the behaviour is stable *within*
a session and differed *between* sessions — per-session toolchain state, not a
property of the inputs. Treat the "intermittent" characterisation as superseded
by this measurement.

An attempt to isolate it to the app's `obj` directory was inconclusive: those
files are owned by the guest's MSBuild user, so they cannot be deleted from the
macOS host, and `/t:Rebuild` already runs a Clean. What is established without
that experiment is enough for the allow-list, because the invariance of the
`.xbf` is checked rather than argued.

**Five hypotheses were tested and eliminated:**

| Hypotheses tested | Result |
| --- | --- |
| The `.resw` `PRIResource` items — set `Condition="false"` | `WMC9999` still present. Not PRI resources. |
| Project-level PRI generation — `/p:GenerateProjectPriFile=false` | Still present. |
| `/p:BuildingInsideVisualStudio=true` | Still present. |
| The `Release` configuration (vs `Debug`) | Still present. |
| The app's unused `xmlns:local` / `mc:Ignorable="d"` declarations, on the theory that the XAML compiler's type dictionary collides because all four referenced assemblies also declare types under `BrowserForWP` | Still present, 3/3. |

**If you are tempted to chase it anyway:** do not change source to do so. The
diagnostic is emitted by a task that has already produced correct output, and the
remaining leads are inside Microsoft's toolchain. The one experiment that would
actually move this forward is a build on an x64 host at the same VS2013 update
level, to test whether running the toolchain under Arm64 emulation is the trigger
— that is an untested hypothesis, and it is recorded as one.

`tools/vm-build.cmd` allow-lists this diagnostic **by name** and fails the build
on every other `error BC` / `error MSB` / `error APPX` line.

### Round 5 — the merged fork is reviewed, and made to build

A second repository was merged into `main` as PR #2 (branch `Gjhkyio/main`, 20
commits, 42 files). It closes the three gaps Round 4 left open and adds the
browser shell: polyfill injection, a TLS probe runner, a pin store, tabs, find,
reading and night modes, tracker blocking, lite redirects, persisted settings,
history and favourites, and two VB test projects.

It was merged **without ever running the guest build.** The first guest build
after the merge failed with four distinct defects. All four are fixed, and each
one is a family worth recognising again:

1. **A profile gap in `List(Of T)`.** `HistoryStore.List()` called
   `_entries.AsReadOnly()`. `ReadOnlyCollection(Of T)` is not part of the
   ".NET for Windows Store apps" profile, so this is `BC30456` — the same shape
   as the `SHA256` / `Encoding.ASCII` gaps above, and `tools/check-vb.mjs`
   cannot see it. Replaced with the profile-safe copy,
   `New List(Of HistoryEntry)(_entries)`.
2. **A solution platform mapping with no matching conditional group.** The tests
   were added to `BrowserForWP.sln` with `Debug|ARM.Build.0 = Debug|ARM`, but
   their `.vbproj` files only defined `Debug|AnyCPU`. A solution build for ARM
   then fails inside `Microsoft.Common.CurrentVersion.targets` with "The
   OutputPath property is not set for project …" — an error that names the
   *pair*, never the missing `PropertyGroup`. Both test projects now carry the
   same six configurations (`AnyCPU`/`ARM`/`x86` × Debug/Release) as every other
   library here.
3. **An emitter that only worked for non-empty data.** `tools/gen-vectors.mjs`
   wrote `New Byte() { _` with **no closing brace** for a zero-length vector.
   RFC 5869 case 3 has an empty salt *and* an empty info, so the generated
   `Vectors.generated.vb` contained two unterminated initializers that swallowed
   the declarations after them: `BC30201` in the test project. The generator now
   has an explicit empty case **and** asserts brace balance on the emitted VB,
   refusing to write the file when it is unbalanced. Verified by negative
   control: the pre-fix file has 57 `{` and 55 `}`.
4. **Deprecated WinRT APIs.** `WebView.NavigationFailed` and
   `DataPackage.SetUri` both raise `BC40000` here. Failure is now handled through
   `NavigationCompleted`'s `IsSuccess` / `WebErrorStatus`, which carry the reason
   the deprecated event does not, and the share path uses `SetWebLink`. Both of
   those warnings are gone; the only warnings left are the two deliberate,
   already-documented `ResourceLoader` ones above.

   **Count warnings from a rebuild, never from an incremental build.** After the
   first fix round this build reported "Warnings: none" — and that was wrong, or
   rather it was measured against the wrong thing: an incremental build reuses the
   cached `BrowserForWP.Localization` DLL and never recompiles `Localizer.vb`, so
   it hides that project's two warnings. `tools\vm-build.cmd /t:Rebuild` shows
   them. A cleaner-looking log is not a cleaner tree.

Also found and fixed while reviewing, none of which a compiler could see:

- Both test projects **compiled but nothing executed them.** See "Where the
  tests actually are".
- The Settings overlay was titled **"Diagnostics"**: the two heading keys were
  swapped with the engine label. `DiagnosticsTitle` now reads "Diagnostics", the
  engine block is labelled "Rendering engine", and the previously hardcoded
  English `"compatibility layer active"` / `"native"` are catalogue keys.
- The engine status was prefixed with `DiagnosticsProbe` — the TLS probe's own
  label, "Run TLS probe" — producing "Run TLS probe: native".
  `DiagnosticsProbe` was a duplicate of `TlsProbeRun` and is deleted.
- The security-details dialog embedded English `"(TLS 1.2 max, WebView). UA="`
  in code. It is now the `SecurityWebViewCeiling` key in both languages.
- `ErrorNoConnection`, `Loading`, `LoadComplete`, `PinMismatch` and
  `SecurityTls13` had no consumer. All five are wired (failure reason, status
  line, pin verdict, probe headline). **61 resource keys, 0 unused, en/it parity
  intact** — checked with a real XML parser, not a tag count.
- `TlsProbeRunner` carried a second copy of the pin comparison; it now calls
  `CertificateValidator.VerifyPin`, so there is one implementation.
  `TlsProbeResult` gained `PinMismatch` so the UI shows the localized sentence
  instead of the English `pin-MISMATCH` token buried in the detail line.

**Unchanged on purpose:** `AddressNormalizer` refuses `localhost:8080`, because a
colon before any slash is read as the scheme `localhost`. The comment above that
branch promises localhost support; in practice only a `localhost` with *no* port
navigates. Left as-is and recorded: loopback is unreachable from an AppContainer
anyway, and that class is security-relevant parsing a merge review should not
rewrite without a device test to justify it.

### Still open

Items 1–3 of the previous revision are **closed** by Round 5: injection, the
probe and the pin store all exist and are wired. What remains is this.

1. **The VB test projects compile, but nothing executes them.** Both are in
   `BrowserForWP.sln` and the guest build produces their DLLs — real progress on
   the Round 4 gap — but no runner invokes `RunAll()`. A WP8.1 ARM class library
   cannot run on the desktop, and there is no handset and no emulator, so
   **"compiled" is not "tested"**. The assertions now run off-device through
   `tools/proto/core-logic.mjs` (53 assertions), a transliteration of
   `CoreLogicTests.vb` that must be kept in step with it. That mirror exists
   because the VB suite's first defect was invisible without execution: an
   assertion naming the heavy `duckduckgo.com` search URL that the lite-first
   default had replaced.
2. **Pinning is enforced only on the app's own transport.** `PinStore` and
   `CertificateValidator.VerifyPin` are real and wired, so a stored pin is checked
   against the leaf SPKI on every probe and a mismatch is surfaced. They **cannot**
   apply to browsing: the `WebView` rides Schannel, whose validation this app
   cannot hook. A pin therefore protects the app's TLS 1.3 path, never the pages
   you visit. `README.md` says "certificate pinning for the app's transport
   layer", which is accurate — do not let it drift into implying that the
   browser's own traffic is pinned.
3. **Never run on a handset.** XAML layout, `WebView` behaviour,
   `DOMContentLoaded` injection, reading-mode fallback, lite redirects, night mode
   and 2014-hardware performance are all unverified. Compiling is not running.

### IE-adaptation is closed

"Adapt Internet Explorer instead of writing an engine" was examined and closed.
Trident **is** the platform engine, and an app cannot re-configure it. The four
levers such a plan needs, and why each is absent:

- **no API to set the WebView document mode.** `WebView` exposes no document-mode
  property, and the hosted engine is already the newest available.
- **no Trident newer than IE11 ever shipped for this OS.** There is nothing to
  move up to; `X-UA-Compatible: IE=edge` selects the engine that is already
  running.
- **no API to toggle IE11 feature flags.** WP8.1 gives an app no switch over
  which CSS/JS features Trident honours.
- **no MSHTML surface is reachable from a WinRT app.** No COM activation of
  `mshtml`, no `IWebBrowser2`, no document-mode control.

What an app *can* do is what this repository already does: inject an ES5
compatibility layer into the document (`TridentEngine.InjectPolyfillAsync`) and
report the engine's limits truthfully (`CompatibilityProbe`).

`BrowserForWP.Core/Diagnostics/IeModeProbe.vb` plus a **Diagnostics → IE mode**
tap is how a handset turns that from an argument into a measurement. Until
someone runs it, the probe is the instrument and this section is the claim — keep
the two distinct, and record the measured `documentMode` here when it happens.

### Error taxonomy

The library project files are hand-authored. If one of them stops being
recognised, recreate it in the IDE (File -> New -> Project -> Visual Basic ->
Windows Phone Apps -> Class Library) and re-add the existing `.vb` files — that
is what "the SDK has not seen this file" means in practice.

Families actually observed, in order of how misleading they are:

- **Project-level imports missing.** A whole project's types reporting "not
  defined". See Round 3.
- **VB 12 vs VB 14 syntax.** Leading-dot chains. `tools/check-vb.mjs` now flags
  them; it could not before, and this was 24 errors in one file.
- **Case-insensitive shadowing.** A local named after a type, property or
  enclosing member. The error names the *type*, never the local.
- **Reserved words as member names.** `Public Property Error As String` is
  `BC30183`, because `Error` is a reserved keyword in VB (the legacy `Error`
  statement) and there is no fallback spelling. What makes this family misleading
  is the *second*, spurious diagnostic it drags in: `BC42312`, "XML documentation
  comments must precede a member or type declaration", pointing at a doc comment
  that is perfectly correct — so the message you chase is the wrong one. Rename
  (`ErrorMessage`) rather than escape (`[Error]`): escaping compiles, but it puts
  brackets at every call site, a pattern nothing else in this repository uses.
  Found by compiling the native-engine plan's Task 3, which had never been
  compiled before it was executed.
- **`Friend` across assemblies** (`BC30390`), and **nested classes** named
  unqualified from another file (`BC30002`).
- **Profile gaps.** `System.Security.Cryptography` does not exist in the
  ".NET for Windows Store apps" profile — use `WinRtCrypto`.
  `Encoding.ASCII` and `RegexOptions.Compiled` are absent too.
  `tools/check-vb.mjs` now flags all three.
- **More profile gaps, found in Round 5.** `List(Of T).AsReadOnly()` is not in the
  profile either — `ReadOnlyCollection(Of T)` is missing, so the call is
  `BC30456` rather than a silent degradation. Check any BCL helper against the
  profile surface before using it; `tools/check-vb.mjs` flags this family.
- **`ControlChars` is not in the Store profile** (`BC30451`) — even though
  `Microsoft.VisualBasic.Strings` is, since `AscW` and `ChrW` both compile. So the
  shape of `Microsoft.VisualBasic` here is partial, and the friendly constants
  (tab, CR, LF, form feed) are exactly the part that went missing. Test whitespace
  with `Char.IsWhiteSpace`, which is what the class-attribute split in
  `SelectorMatcher` does. Found by compiling the native-engine plan's Task 5;
  `tools/check-vb.mjs` now flags it as well.
- **APIs that compile and then fail at run time.** `System.Text.Encoding.GetEncoding`
  *is* in this profile — the guest build accepts it, so it is **not**
  `BC30456` — yet Microsoft's own documentation for the method says unsupported
  code pages throw (`ArgumentException` for some, `NotSupportedException` for
  others) and that callers must catch rather than trust. Whether
  `GetEncoding("ISO-8859-1")` resolves on a WP8.1 handset cannot be settled from
  this machine, so no code here may depend on either answer: `NetDocumentFetcher`
  asks and falls back to UTF-8. Note the shape of this family — the compiler is
  *silent*, so a build-only check can never see it, and "it built" is not
  evidence about it. A plan that anticipated `BC30456` here was anticipating the
  wrong failure.
- **A `Configuration|Platform` pair with no `PropertyGroup`.** Adding a project to
  the solution with `Debug|ARM.Build.0 = Debug|ARM` while its `.vbproj` defines
  only `Debug|AnyCPU` fails the entire build with "The OutputPath property is not
  set for project … Configuration='Debug' Platform='ARM'". The message names the
  pair, never the missing group. Every library in this repo defines all six.
- **Generated code that was only ever tested on non-empty data.**
  `gen-vectors.mjs` emitted `New Byte() { _` with no closing brace for a
  zero-length vector, so the generated file failed to compile (`BC30201`) for
  RFC 5869 case 3 — a case with an empty salt *and* an empty info. Checked inputs
  are not a checked emitter: the generator now asserts brace balance on its own
  output before writing it.
- **Deprecated WinRT APIs.** `WebView.NavigationFailed` and
  `DataPackage.SetUri` are `BC40000` on this OS. Prefer `NavigationCompleted`'s
  `IsSuccess` / `WebErrorStatus` (they carry the reason, the deprecated event does
  not) and `DataPackage.SetWebLink`. Treat a new `BC40000` as a design question
  rather than as noise to allow-list.
- **XML comment hazards.** A `--` run inside a `<!-- -->` comment makes MSBuild
  refuse to load a project (`MSB4025`), which surfaces from a solution build as
  the unrelated-looking `MSB4078` "project file is not supported by MSBuild". An
  unescaped `<0..2^24-1>` copied from an RFC grammar invalidates a `'''` doc
  comment (`BC42304`). Both are now checked.
- **Namespace duplication.** The full name is `<RootNamespace>.` plus the file's
  own `Namespace` block. `BrowserForWP.Crypto.vbproj` sets `RootNamespace` to
  `BrowserForWP` precisely because its sources declare `Namespace Crypto`;
  `BrowserForWP.Net.vbproj` must keep `BrowserForWP.Net` because its sources
  import `BrowserForWP.Net.Tls13` and `BrowserForWP.Net.Http`.

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

## Where the tests actually are

Both VB test projects from `2026-09-28-browserforwp.md` now **exist and compile**:
`tests/BrowserForWP.Core.Tests/` and `tests/BrowserForWP.Crypto.Tests/`, both
registered in `BrowserForWP.sln` (Debug configurations only) and built by
`tools/vm-build.cmd`. They are deliberately **not MSTest projects** — they hold a
plain `Public Shared Function RunAll() As Integer` that throws on the first failed
check, so they need no test framework the guest might not have.

What is and is not covered:

| Path | What it is | Consumed by |
| --- | --- | --- |
| `tools/gen-vectors.mjs`, `tools/proto/*.mjs` | The executable prototypes. `gen-vectors.mjs` recomputes HKDF, X25519 and AES-GCM and asserts RFC 5869 / 7748 / 8448 and NIST CAVS vectors; `tls13.mjs` completes real handshakes against live servers. | `node`, on any machine. **This is the real crypto verification.** |
| `tests/BrowserForWP.Crypto.Tests/` (`Vectors.generated.vb` + `VectorsSmokeTests.vb`) | Generated VB constants from those same vectors, plus length/shape checks. | **Compiled by the guest build; never executed.** |
| `tests/BrowserForWP.Core.Tests/CoreLogicTests.vb` | Address normalisation, tab state, session/UA, settings, history, favourites, pin normalisation, hostname wildcards, language matching. | **Compiled by the guest build; never executed.** |
| `tools/proto/core-logic.mjs` | A transliteration of `CoreLogicTests.vb`. 53 assertions, exit 1 on failure. | `node`, on any machine. **This is what actually runs those assertions.** |
| `tools/check-vb.mjs` | 12 categories / 50 check groups over every `.vb`, `.vbproj`, `.xaml` and `.resw`. | `node`, on any machine. |
| `tools/vm-build.cmd` | The real compiler, and the arbiter of pass/fail. | The Windows guest. |
| `tools/wmc9999-probe.sh` | Build-diagnostic characterisation and XAML output invariance. | `bash`, on the host. |

**"Compiled" is still not "tested", and the distinction is not academic.**
`CoreLogicTests.vb` shipped an assertion naming the heavy `duckduckgo.com` search
URL that the lite-first default had replaced. It compiled cleanly, so nothing
complained; only running it would have. A WP8.1 ARM class library cannot run on
the desktop and there is no handset or emulator, which is exactly why
`tools/proto/core-logic.mjs` exists: it is the executable half of that suite.

**Keep the two in step.** If `CoreLogicTests.vb` gains a case, `core-logic.mjs`
must gain it too, and vice versa. A mirror that drifts is worse than no mirror,
because it reports green for behaviour the VB no longer has.

Crypto is the one layer covered better off-device than a VB project could cover
it, because the prototypes exercise the *same algorithm* against published RFC
vectors and live servers. Do not claim UI or XAML coverage: neither exists.

## Release checklist

- [ ] `node tools/gen-vectors.mjs` → `53 assertions, 0 failure(s)`
- [ ] `python3 tools/make_logo.py` → 12 PNGs, all `*.scale-100` / `*.scale-240`, no git diff
- [ ] Polyfill ES5 check passes
- [ ] `node tools/proto/core-logic.mjs` → `core-logic checks, 0 failure(s)`
- [ ] `tools\vm-build.cmd /t:Rebuild` in the guest → `BUILD_EXIT=0`, no `BC`
      errors, no warnings
- [ ] `node tools/proto/w25519.mjs` → `18 checks, 0 failure(s)`
- [ ] `node tools/proto/tls13.mjs example.com` → `31 checks, 0 failure(s)`
- [ ] `RUNS=4 bash tools/wmc9999-probe.sh` → `distinct XBF hash pairs across 12 runs: 1`
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
