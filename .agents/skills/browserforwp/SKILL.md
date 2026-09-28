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
`WinRtCrypto` (`Windows.Security.Cryptography.Core`). `RegexOptions.Compiled` and
`Encoding.ASCII` are unsupported too, and `CryptographicEngine` exposes
`VerifySignature`, never `Verify`. Before introducing any `System.*` type, confirm
it exists in that profile — do not assume a desktop API is available.
`tools/check-vb.mjs` flags this family; the list is not exhaustive, so the flag is
a floor, not a ceiling.

**Second-and-a-half — the language is VB 12, not VB 14.** The toolchain is Visual
Studio 2013. Implicit line continuation after a `.` arrived in **VB 14 (VS2015)**,
so the JavaScript-style fluent chain that reads so naturally is a syntax error
here:

```vb
Dim w = New TlsWriter().
    U8(1).
    ToArray()
```

That is `BC30203` on the trailing dot, and then `U16`, `Bytes`, `Vec8` and
`ToArray` all report "not declared" — 24 errors in one file, none of which names
the real cause. Use a `With` block with one call per line; it reads the same and
compiles. `tools/check-vb.mjs` catches this now, because it cost a whole round.

**Second-and-three-quarters — VB is case-insensitive, so a local can shadow a
type or a member.** `Dim extensionType = ...` next to the `ExtensionType` enum
makes every `ExtensionType.X` in the file report "'X' is not a member of
'Integer'", pointing at the enum and never at the local. The same trap produced
`shared`/`Shared`, `supported`/`Supported`, `tag`/`Tag` (a `Page` property),
`Default`/`DefaultTag` (a keyword), and `value` inside `Function Value()`
(`BC30290`). Name locals after what they *hold*, not after the type.

**Third — never edit `X25519.vb` or `BrowserForWP.Net/Tls13/` by hand.** Both
are transliterations of executable prototypes, and those prototypes are the only
real checks available off-Windows. Change the prototype first, watch it pass, then
port the change:

- `X25519.vb` -> `tools/proto/w25519.mjs` -> `18 checks, 0 failure(s)`
- `BrowserForWP.Net/Tls13/*` -> `tools/proto/tls13.mjs` -> `31 checks, 0 failure(s)`
- `BrowserForWP.Net/Tls13/PinStore.vb` -> `tools/proto/pinstore.mjs` -> `0 failure(s)`

`PinStore.vb` is pure host/pin logic rather than protocol code, so its mirror is
a plain logic mirror like `core-logic.mjs`, not a wire-format prototype. It still
falls under the rule: change the mirror, watch it pass, then port.

**Fourth — a self-consistent TLS client proves nothing.** Sealing and opening your
own records will round-trip any bug that is symmetric. `tools/proto/tls13.mjs`
completes handshakes with real servers precisely so that field-order and
length-prefix mistakes cannot hide; three such bugs were found this way. If you
change record framing or ClientHello layout, re-run it against a live host.

**Fifth — the profile and the project files have their own failure families.** The
Round 5 merge review (PR #2) added four, every one of which passed
`tools/check-vb.mjs` and still failed the real build:

- **`List(Of T).AsReadOnly()` does not exist in the profile.**
  `ReadOnlyCollection(Of T)` is missing, so the call is `BC30456`, not a silent
degradation. Copy the list instead. Same family as the `SHA256` gap, and the
checker does not know this member.
- **A `Configuration|Platform` pair with no `PropertyGroup`.** A project added to
  `BrowserForWP.sln` with `Debug|ARM.Build.0 = Debug|ARM` whose `.vbproj` defines
  only `Debug|AnyCPU` fails the *entire* build inside
  `Microsoft.Common.CurrentVersion.targets` with "The OutputPath property is not
  set for project … Configuration='Debug' Platform='ARM'". The message names the
  pair, never the missing group. Every library here defines all six.
- **Generated code needs its own invariant, not just checked inputs.**
  `gen-vectors.mjs` emitted `New Byte() { _` with no closing brace for a
  zero-length vector, so RFC 5869 case 3 (empty salt *and* empty info) produced a
  file that did not compile (`BC30201`). The generator now asserts brace balance
  on its own output and refuses to write an unbalanced file.
- **Deprecated WinRT APIs are `BC40000` here.** `WebView.NavigationFailed` and
  `DataPackage.SetUri`. Use `NavigationCompleted`'s `IsSuccess` /
  `WebErrorStatus` — which carry the reason the deprecated event does not — and
  `DataPackage.SetWebLink`. A build with zero warnings is the goal; a new
  `BC40000` is a design question, not noise to allow-list.

**Sixth — a merged pull request is unreviewed code until the guest build says
otherwise.** PR #2 arrived as twenty commits that had never been compiled on the
real toolchain; the first guest build failed on four defects at once. A PR's own
verification section is a claim, not evidence, and "the author says it is
tested" is exactly the assumption this repository exists to stop making. Run
`tools\vm-build.cmd` before treating a merge as done, and diff the fork against
`docs/MAINTAINING.md` § "Still open" — that is where a merge usually contradicts
the rest of the repository.

## Step 4 — Verification commands

Run the checks for the layer you actually changed. Do not claim a layer is
verified if you skipped its command.

| You changed | Run | Expected |
| --- | --- | --- |
| Anything in `BrowserForWP.Crypto/` | `node tools/gen-vectors.mjs` | `53 assertions, 0 failure(s)` |
| `X25519.vb` (or its prototype) | `node tools/proto/w25519.mjs` | `18 checks, 0 failure(s)` |
| Anything in `BrowserForWP.Net/Tls13/` | `node tools/proto/tls13.mjs example.com` | `31 checks, 0 failure(s)` |
| `PinStore.vb` / pin comparison | `node tools/proto/pinstore.mjs` | `0 failure(s)` |
| Test vectors themselves | `node tools/gen-vectors.mjs` | every line prefixed `✓`, exit code 0 |
| The vector emitter itself | `node tools/gen-vectors.mjs` | `emitted VB braces balanced`, else it refuses to write, exit code 1 |
| `BrowserForWP.Core/` logic | `node tools/proto/core-logic.mjs` | `core-logic checks, 0 failure(s)` (53 assertions) |
| UA table / settings | `node tools/proto/useragents.mjs` | `0 failure(s)` |
| Tracker blocklist | `node tools/proto/trackerblock.mjs` | `0 failure(s)` |
| Lite defaults / caps / resources | `node tools/proto/lightweight.mjs` | `0 failure(s)` |
| Shim delivery / redirect rules | `node tools/proto/modern-sites.mjs` | `0 failure(s)` |
| Picker/tab re-entrancy, sln registration | `node tools/proto/shell-guards.mjs` | `0 failure(s)` |
| `BrowserForWP.Polyfill/compat.js` | `node tools/check-polyfill.mjs` | `is valid ES5` |
| Any `.vb`, `.vbproj`, `.xaml` or `.resw` | `node tools/check-vb.mjs` | `0 finding(s)`, exit code 0 |
| `BrowserForWP/Assets/**` | `python3 tools/make_logo.py` | one line per generated PNG, exit code 0 |
| UI / XAML / VB app code | Build in the guest: `tools\vm-build.cmd /t:Rebuild` | `BUILD_EXIT=0`, no `BC` errors; only the two deliberate `ResourceLoader` warnings |
| Unexplained build diagnostics | `RUNS=4 bash tools/wmc9999-probe.sh` | `distinct XBF hash pairs across 12 runs: 1` |
| TLS / DoH / sockets | Deploy to handset, run **Diagnostics → TLS probe** | reports negotiated `TLS1.3` |

The two VB test projects under `tests/` are compiled by the guest build but
**nothing executes them** — an ARM class library cannot run on the desktop and
there is no handset or emulator. `node tools/proto/core-logic.mjs` is the
executable half of `CoreLogicTests.vb`; keep the two in step. Never report the
`tests/` projects as "tests passing".

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
  Diagnostics/TlsProbeRunner.vb  ← app-layer glue: Net's TLS 1.3 stack → Settings UI
BrowserForWP.Core/            ← engine abstraction, tabs, history, address bar,
                                 settings/history/favourites stores, reading and
                                 night modes, tracker blocklist, lite redirects
BrowserForWP.Net/             ← TLS 1.3, DoH, HTTP client, certificate pin store
BrowserForWP.Crypto/          ← HKDF, X25519, AES-GCM (no ChaCha: one suite, see below)
BrowserForWP.Localization/    ← language resolution + string lookup
BrowserForWP.Polyfill/        ← compat.js, packaged AND injected at DOMContentLoaded
                                 and again on navigation completed
tests/*.Tests/                ← VB logic checks; compiled by the guest, NOT executed
docs/ARCHITECTURE.md          ← design + the platform laws
docs/MAINTAINING.md           ← build/run/extend recipes
tools/gen-vectors.mjs         ← crypto verification + the VB vector emitter
tools/proto/*.mjs             ← runnable prototypes and logic mirrors
                                 (w25519, tls13, core-logic, pinstore, useragents,
                                 trackerblock, lightweight, modern-sites,
                                 shell-guards)
tools/make_logo.py            ← regenerates every image asset
tools/check-vb.mjs            ← 12 categories / 50 check groups of static
                                 VB/XAML/project/resw checks
tools/check-polyfill.mjs      ← ES5 validity of the shim
tools/vm-build.cmd            ← the real build, run inside the Windows guest
tools/wmc9999-probe.sh        ← characterises the WMC9999 diagnostic + XAML drift
```

Rule of thumb: **crypto knows nothing about TLS; TLS knows nothing about the
UI; Core knows nothing about crypto.** A change that violates a layer boundary
is a design bug, not a shortcut.

## How to add a new feature

Worked example: *add a "desktop site" toggle.*

1. **Plan.** `docs/superpowers/plans/2026-09-28-desktop-site-toggle.md`.
2. **Decide the layer.** This is browser state + UI, so it lives in
   `BrowserForWP.Core` and `BrowserForWP/`, and touches neither crypto nor TLS.
3. **Decide how this change can be verified, and be specific.** The `tests/`
   projects are compiled by the guest build but nothing runs them, so "add a unit
   test" is still not an executable option — see `docs/MAINTAINING.md` § "Where
   the tests actually are". Pick one of these two, and write down which:

   - **The logic is pure and has no WinRT dependency** (like
     `AddressNormalizer`, or a user-agent table). Extract it into
     `BrowserForWP.Core` behind a function whose inputs and outputs are plain
     strings or integers, then assert its behaviour from a Node script in
     `tools/` that mirrors that function. This is what
     `tools/proto/w25519.mjs` does for X25519, and it is the only pattern in
     this repository that has caught real bugs before they shipped.
   - **The logic touches XAML, the WebView, or a WinRT API.** Nothing off-device
     can check it. Say so in the commit message, build in the guest, and write
     out the exact handset steps a reviewer should repeat. Do not describe this
     as "tested".

4. **Write the check before the implementation**, whichever you chose. For a pure
   function that means the Node script, and it must fail first:

   ```bash
   node tools/proto/useragents.mjs
   # Expected BEFORE the implementation: FAIL — no such function: EffectiveUserAgent
   ```

   For UI work it means writing the manual verification steps down *now*, while
   you still remember what "correct" looks like, so step 9 has something concrete
   to check against.

   **Superseded step.** This walkthrough used to say: write a `<TestMethod>` into
   `tests/BrowserForWP.Core.Tests/`, then run it and watch it fail. That project
   now exists and compiles, but no runner executes it off-device, so watching it
   fail is still impossible. Mirror the logic in `tools/proto/` instead — exactly
   what `core-logic.mjs` does for `CoreLogicTests.vb` — and keep the two in step.
5. **Implement the minimum.** This feature has since shipped, so the real code is
   the reference: the UA strings live in
   `BrowserForWP.Core/Browser/UserAgents.vb` as constants plus a pure
   `EffectiveUserAgent(desktopMode As Boolean)`, and `BrowserSession` delegates:

   ```vb
   Public Property DesktopMode As Boolean = False

   Public ReadOnly Property EffectiveUserAgent As String
       Get
           Return UserAgents.EffectiveUserAgent(DesktopMode)
       End Get
   End Property
   ```

   Keep the lookup in the pure class so the Node mirror can call it; a `If`
   inline in `BrowserSession` would put it out of `useragents.mjs`'s reach.

6. **Re-run the check from step 4.** Expected: it passes now — the Node script
   for a pure function, or the guest build plus the manual handset steps for UI
   work.
7. **Localize any new user-visible string.** Add the key to **both**
   `BrowserForWP/Strings/en-US/Resources.resw` and
   `BrowserForWP/Strings/it-IT/Resources.resw`. A key present in only one
   language is a bug — see "Adding a language" below.
8. **Wire the UI.** Add the control to `MainPage.xaml`, its handler to
   `MainPage.xaml.vb`, and bind the label to the resource key.
9. **Verify.** Build in the guest, then deploy:

   ```bash
   prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \
       "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd /t:Rebuild"
   ```

   Expected: `=== BUILD_EXIT=0 ===`. Then deploy to a handset and toggle the
   setting; confirm the server sees the desktop user agent.
10. **Commit and push.**

   ```bash
   git add BrowserForWP.Core/Browser/BrowserSession.vb \
           BrowserForWP/MainPage.xaml BrowserForWP/MainPage.xaml.vb \
           BrowserForWP/Strings/en-US/Resources.resw \
           BrowserForWP/Strings/it-IT/Resources.resw \
           docs/superpowers/plans/2026-09-28-desktop-site-toggle.md
   git commit -m "feat(core): add desktop-site user agent toggle"
   git push origin HEAD
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
- Reporting `tools/check-vb.mjs` passing as "it compiles". It is a static checker,
  not a compiler, and its own output says so. Run `tools\vm-build.cmd`.
- Asserting that something "cannot be built" or "is not supported" without having
  tried it. This repository has already paid for that mistake once: the README
  claimed the ARM64 guest could not build, for three rounds, and it can.
- Describing a component as a shipped feature when nothing calls it. Before
  listing anything as a feature, `grep` for a caller. The live catalogue is
  `docs/MAINTAINING.md` § "Still open": `Tls13Client` / `HttpClient13` /
  `DohResolver` are reachable only through **Diagnostics → TLS probe**, never
  through a page load, because the `WebView` navigates via Schannel; and both
  `tests/` projects compile while no runner executes them. Compiled is not
  reachable; reachable is not verified on a handset.
- Adding a project to `BrowserForWP.sln` without the matching
  `Configuration|Platform` groups in its `.vbproj` (see the fifth constraint).
- Reporting the `tests/` projects as passing, or claiming `BrowserForWP.Core` is
  covered because they compile. `node tools/proto/core-logic.mjs` is what runs.
- Implying the `WebView`'s own traffic is pinned, or that it ever uses TLS 1.3.
  Pins apply to the app's transport; the page load goes through Schannel at
  TLS 1.2 max.

## The loop — do all five steps, in order, every time

1. **Plan.** Write or update the plan for the change first, using
   `superpowers:writing-plans`. No code before the plan exists. If the change
   invalidates part of an existing plan, update that plan in the same commit.
2. **Implement.** The smallest change that satisfies the plan. Apply the
   prototype rule: anything under `BrowserForWP.Net/Tls13/` or `X25519.vb` is
   changed in `tools/proto/` first, verified there, then transliterated.
3. **Verify.** Run the commands in the table above for every layer touched.
   Report which commands were run and their real output. Never claim a layer is
   verified because a different layer passed.
4. **Commit.** Conventional Commits, imperative mood, subject <= 72 characters.
5. **Push.** `git push origin HEAD`. A commit that is not pushed does not count
   as done. Confirm with `git ls-remote origin refs/heads/main` matching
   `git rev-parse HEAD`.

**Then update this file.** If the change added, removed or altered any tool,
command, file layout or constraint, that fact belongs here before the turn ends.
A skill describing a previous version of the project is actively harmful, because
it is what the next contributor trusts.

## How to build — there IS a compiler here

The development host is an Apple silicon Mac, so the sources are written
off-platform. They are not uncompiled: an **ARM64 Windows 11 Parallels guest**
hosts the whole VS2013 toolchain and builds the solution for real.

```bash
prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \
    "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd"
```

Expected tail: `=== BUILD_EXIT=0 ===`, with no `BC` errors. Add `/t:Rebuild` for a
clean build; the batch file forwards extra arguments to MSBuild.

Three things about that command, all of which cost time to learn:

- **The command and its arguments must be separate argv entries.**
  `prlctl exec <vm> "cmd /c ver"` fails *silently* — argv[0] becomes a program
  named `cmd /c ver`. `--current-user` and `-u` fail on this guest too. This is
  why the build is a batch file.
- **Do not trust this guest's Italian build log for the first reading.**
  `error BC30203: È previsto un identificatore` is "identifier expected";
  `La chiave specificata non era presente nel dizionario` is "the given key was
  not present in the dictionary". Grep for `error BC` / `error MSB` / `WMC`
  rather than reading the prose.
- **`WMC9999` is allow-listed known noise, and it is deterministic here.** It
  appeared in **12 of 12** measured builds under `vm-build.cmd`, with
  byte-identical `App.xbf` and `MainPage.xbf` across all of them — asserted, not
  assumed, by `tools/wmc9999-probe.sh`, which exits non-zero if the compiled XAML
  ever varies. It is non-fatal and never changes the exit code. An earlier
  revision of this file called it "intermittent"; the measurement says otherwise,
  and the earlier zeros were `obj` state. Do not edit source to chase it.
- **Count warnings from `/t:Rebuild`, never from an incremental build.** The
  incremental path sees unchanged projects as up to date and skips their compile
  entirely, so it produces a *cleaner* log than a clean build does. This is not
  hypothetical: an incremental run reported "Warnings: none" while
  `BrowserForWP.Localization` still had two `BC40000`s that a rebuild shows. Two
  other `BC40000`s were removed in Round 5 — `WebView.NavigationFailed` and
  `DataPackage.SetUri` — and the only warnings that should remain are the two
  deliberate `ResourceLoader` ones documented in `docs/MAINTAINING.md`.

A previous revision of this section claimed the ARM64 guest "cannot host this
build" and that a real build needs an x64 host. **That was wrong.** It was
inferred from Microsoft's "Visual Studio does not support Arm processors"
documentation instead of from trying it; the documentation is about the IDE, not
the command-line build. It is recorded here because the failure mode was
assuming rather than testing, and that is the one this repo is supposed to be
immune to.

**How to use the two verification paths together.** `tools/check-vb.mjs` is fast
and runs anywhere; the guest build is authoritative and slow. Run the checker
first, then the build. But a green checker proves *nothing* about compilation —
every error family in rounds 3 and 4 passed it. Before the guest round trip was
discovered, every non-trivial decision had to be backed by an executable
prototype; that method found four real TLS bugs and is still worth keeping for
protocol work. It is no longer a substitute for compiling.
