# Build Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out `2026-09-28-build-verification.md` — settle the one intermittent build diagnostic with evidence, make the build assert its own health, and delete every documentation reference to a file, project or command that does not exist.

**Architecture:** Three independent, separately shippable changes. (1) A measurement task: characterise the `WMC9999` diagnostic with a repeatable experiment script and either fix it or prove it cannot affect output. (2) A tooling task: `tools/vm-build.cmd` stops being a shell around MSBuild and starts failing when a real compiler error appears, while allow-listing the one known noise diagnostic by name. (3) A truth task: the docs currently promise a `BrowserForWP.Crypto.Tests` project and a `tests/BrowserForWP.Core.Tests/` harness, and neither exists.

**Tech Stack:** Batch (`tools/vm-build.cmd`), Bash (`prlctl` host-side scripting), VB.NET / WinRT 8.1, MSBuild 12.0.

## Starting state (measured, not assumed)

Read this before Task 1. Every number here was produced by running the build in the guest.

**There are no outstanding compile errors.** The solution builds:

```
=== BUILD_EXIT=0 ===
BrowserForWP.Core         -> ...\bin\ARM\Debug\BrowserForWP.Core.dll
BrowserForWP.Crypto       -> ...\bin\ARM\Debug\BrowserForWP.Crypto.dll
BrowserForWP.Localization -> ...\bin\ARM\Debug\BrowserForWP.Localization.dll
BrowserForWP.Net          -> ...\bin\ARM\Debug\BrowserForWP.Net.dll
BrowserForWP             -> ...\bin\ARM\Debug\BrowserForWP.exe
BrowserForWP             -> ...\AppPackages\BrowserForWP_1.0.0.0_Debug_Test\BrowserForWP_1.0.0.0_arm_Debug.appxbundle
```

Twelve consecutive `Rebuild` runs: `exit 0` every time, zero `BC` errors.

What remains is exactly three things, all residue rather than defects:

| Item | Severity | Count | Status at the start of this plan |
| --- | --- | --- | --- |
| `BC40000` deprecated `ResourceLoader` constructor | warning | 2 | Deliberate and documented: `ResourceLoader.GetForCurrentView` returns a cached loader, which would silently break the runtime language override in `Localizer`. Not this plan's job — see "Deliberately out of scope". |
| `WMC9999` XAML internal error | error-level text, non-fatal | 0 or 1, varying | Unexplained. Task 1. |
| Docs naming a `BrowserForWP.Crypto.Tests` / `BrowserForWP.Core.Tests` project | documentation defect | 5 operational sites, plus 2 rows in `2026-09-28-browserforwp.md` | Both projects are absent from disk and from `BrowserForWP.sln`. Task 3. The exact sites were enumerated by grep at plan-writing time, not recalled. |

## Global Constraints

- **Target platform is fixed:** `TargetPlatformVersion 8.1`, `AppContainerExe`, Windows Phone 8.1 WinRT/XAML. Do not raise it.
- **The language is VB 12 (Visual Studio 2013).** No implicit line continuation after `.`, no `NameOf`, no string interpolation. `tools/check-vb.mjs` enforces the first of these.
- **Never hand-edit `BrowserForWP.Crypto/X25519.vb` or `BrowserForWP.Net/Tls13/*`.** Change `tools/proto/w25519.mjs` / `tools/proto/tls13.mjs` first and re-run them. No task here needs to touch either, and that is not a coincidence.
- **`.NET for Windows Store apps` profile.** `Encoding.ASCII`, `RegexOptions.Compiled` and all of `System.Security.Cryptography` are absent. Use `WinRtCrypto`.
- **Guest build command, verbatim:**
  `prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd"`
  Command and arguments are **separate argv entries**; a single quoted string fails silently.
- **Verification floor before every commit:** `node tools/check-vb.mjs` exits 0 with `0 finding(s)`, and `node tools/gen-vectors.mjs` prints `52 assertions, 0 failure(s)`.
- **Never report a green static check as "it compiles".** Run the guest build.
- **Always push after committing.** Updating `.agents/skills/browserforwp/SKILL.md` is part of the loop, not an optional extra.

## File Structure

| File | Action | Responsibility |
| --- | --- | --- |
| `tools/wmc9999-probe.sh` | Create | One repeatable measurement of the `WMC9999` diagnostic across build modes, including an output hash. Exists so the question is answerable with numbers instead of another hypothesis. |
| `BrowserForWP/App.xaml` | Modify (Task 1 only, reverted unless it helps) | Test whether the unused `xmlns:local` declaration is the trigger. |
| `BrowserForWP/MainPage.xaml` | Modify (Task 1 only, reverted unless it helps) | Same, plus the unused design-time `d:`/`mc:` declarations. |
| `tools/vm-build.cmd` | Modify | Owns the build's pass/fail decision. After Task 2 it fails on any real compiler error and reports the known-noise diagnostic by name. |
| `docs/MAINTAINING.md` | Modify | Canonical record: the `WMC9999` verdict, the allow-list rationale, the warning policy, and a release checklist that names only commands that exist. |
| `.agents/skills/browserforwp/SKILL.md` | Modify | The verification table and the worked "add a feature" example must stop referencing project paths that were never created. |
| `docs/superpowers/plans/2026-09-28-build-verification.md` | Modify | Mark its steps according to what actually happened, so nobody re-executes a superseded task. |

No new source file is created in this plan. Nothing in `BrowserForWP*/` changes its behaviour.

---

### Task 1: Settle `WMC9999` with evidence

**Why this task exists:** the diagnostic is currently described in `docs/MAINTAINING.md` as "intermittent and not fully explained". That is an honest sentence but not a finished one, and it leaves every future reader to re-investigate. The goal is to end up with either a fix or a measurement strong enough that the allow-list in Task 2 is justified.

**Files:**
- Create: `tools/wmc9999-probe.sh`
- Modify (temporarily): `BrowserForWP/App.xaml`, `BrowserForWP/MainPage.xaml`
- Modify: `docs/MAINTAINING.md`

**Interfaces:**
- Consumes: the guest build path established in `docs/MAINTAINING.md`.
- Produces: `tools/wmc9999-probe.sh`, invoked as `RUNS=4 bash tools/wmc9999-probe.sh`, printing one line per run of the form `<label> run <n> WMC9999=<count> <hashes>`; and a verdict paragraph in `docs/MAINTAINING.md` that Task 2 quotes as its justification.

- [ ] **Step 1: Test the untried hypothesis — the app's XAML namespace declarations**

Four hypotheses have already been eliminated (`.resw` `PRIResource` items, `/p:GenerateProjectPriFile=false`, `/p:BuildingInsideVisualStudio=true`, the Release configuration). The one lead never tested: `CompileXaml` resolves every `xmlns` declaration against `@(ReferencePath)`, and the app declares `xmlns:local="using:BrowserForWP"` while **all four referenced assemblies also declare types under `BrowserForWP`** (`BrowserForWP.Core.*`, `.Crypto.*`, `.Localization.*`, `.Net.*`). That declaration is unused — no XAML in this project names a `local:` type.

Make the app contribute only its own namespace, and drop the design-time-only markup-compatibility declarations while you are there (this project uses no `d:` attributes, so `mc:Ignorable="d"` has nothing to ignore).

In `BrowserForWP/App.xaml`, remove this line:

```xml
    xmlns:local="using:BrowserForWP">
```

so the opening tag reads:

```xml
<Application
    x:Class="BrowserForWP.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

</Application>
```

In `BrowserForWP/MainPage.xaml`, the header becomes:

```xml
<Page
    x:Class="BrowserForWP.MainPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
```

- [ ] **Step 2: Build three times and count**

Run:

```bash
for i in 1 2 3; do
  prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \
    "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd /t:Rebuild" \
    > /tmp/ns-$i.txt 2>&1
  printf 'run %s  WMC9999=%s  exit=%s\n' "$i" \
    "$(LC_ALL=C tr -d '\r' < /tmp/ns-$i.txt | LC_ALL=C grep -ac WMC9999)" \
    "$(LC_ALL=C tr -d '\r' < /tmp/ns-$i.txt | LC_ALL=C grep -a BUILD_EXIT | LC_ALL=C sed 's/.*BUILD_EXIT=//; s/ ===//')"
done
```

Expected if the hypothesis is right: `WMC9999=0` three times out of three.
Expected if it is wrong (the likelier outcome): `WMC9999=1` in all three.

- [ ] **Step 3: Revert, unless it fixed the build**

If all three runs reported `WMC9999=0`, **keep the change** — then this task is a fix, not a measurement, and skip to Step 6, recording the cause as the namespace declarations.

Otherwise restore both files exactly:

```bash
git checkout -- BrowserForWP/App.xaml BrowserForWP/MainPage.xaml
git diff --stat BrowserForWP/App.xaml BrowserForWP/MainPage.xaml   # expect: no output
```

Never leave an unused-declaration cleanup in place while claiming it fixed something it did not.

- [ ] **Step 4: Write the measurement script**

Twelve builds in this plan's earlier rounds were not enough to characterise an intermittent diagnostic, because they were not run in a controlled matrix and the outputs were not hashed. Write the harness once, properly. Create `tools/wmc9999-probe.sh`:

```bash
#!/usr/bin/env bash
# ───────────────────────────────────────────────────────────────────────────
#  Characterise the WMC9999 diagnostic emitted by the VS2013 XAML compiler.
#
#  Why: solutions/2026-09-28-build-closure.md Task 1. The diagnostic is
#  "Xaml Internal Error error WMC9999: ... the given key was not present in
#  the dictionary", it appears in some builds and not others, and it does not
#  fail the build. This script answers two questions with numbers:
#
#    1. Does the count depend on the build mode?  (three modes, RUNS each)
#    2. Does it change the compiled XAML?         (md5 of App.xbf/MainPage.xbf)
#
#  If the hashes are identical across every run, the diagnostic provably
#  cannot affect the shipped package, and tools/vm-build.cmd may allow-list it
#  by name. That is the whole point of the measurement.
#
#  Usage:  RUNS=4 bash tools/wmc9999-probe.sh
#  Exit:   0 if every run produced identical XBF hashes, 1 otherwise.
# ───────────────────────────────────────────────────────────────────────────
set -uo pipefail

VM="{66a2f493-162c-4b3f-ba40-0a26020cc818}"
MSB='C:\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe'
REPO='C:\Mac\Home\Documents\BrowserForWP'
RUNS="${RUNS:-4}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/BrowserForWP/bin/ARM/Debug"

fail=0
declare -a hashes

xbf_hashes() {
  # The shared folder is visible from the host, so hash here rather than in the guest.
  printf 'App.xbf=%s MainPage.xbf=%s' \
    "$(md5 -q "$BIN/App.xbf" 2>/dev/null || echo MISSING)" \
    "$(md5 -q "$BIN/MainPage.xbf" 2>/dev/null || echo MISSING)"
}

run_mode() {
  local label="$1" args="$2" i out log
  for i in $(seq 1 "$RUNS"); do
    log="/tmp/wmc9999-$label-$i.txt"
    prlctl exec "$VM" "cmd.exe" "/c" \
      "cd /d $REPO && \"$MSB\" $args /nologo /v:minimal" > "$log" 2>&1
    out="$(LC_ALL=C tr -d '\r' < "$log" | LC_ALL=C grep -ac WMC9999 || true)"
    printf '%-16s run %s  WMC9999=%s  %s\n' "$label" "$i" "$out" "$(xbf_hashes)"
    hashes+=("$(xbf_hashes)")
  done
}

run_mode sln-rebuild     "BrowserForWP.sln /t:Rebuild /p:Configuration=Debug /p:Platform=ARM"
run_mode app-rebuild     "BrowserForWP\\BrowserForWP.vbproj /t:Rebuild /p:Configuration=Debug /p:Platform=ARM"
run_mode app-incremental "BrowserForWP\\BrowserForWP.vbproj /p:Configuration=Debug /p:Platform=ARM"

printf '\n── verdict ─────────────────────────────────────────────────\n'
uniq_hashes="$(printf '%s\n' "${hashes[@]}" | sort -u | wc -l | tr -d ' ')"
printf 'distinct XBF hash pairs across %s runs: %s\n' "${#hashes[@]}" "$uniq_hashes"

if [ "$uniq_hashes" = "1" ]; then
  echo 'RESULT: compiled XAML is invariant. The diagnostic cannot affect the package.'
else
  echo 'RESULT: compiled XAML DIFFERS between runs. WMC9999 is NOT harmless — investigate before allow-listing it.'
  fail=1
fi

exit "$fail"
```

Make it executable:

```bash
chmod +x tools/wmc9999-probe.sh
```

- [ ] **Step 5: Run the matrix and read the verdict**

Run:

```bash
RUNS=4 bash tools/wmc9999-probe.sh
```

Expected, based on the twelve builds already recorded: a mix of `WMC9999=0` and `WMC9999=1` lines in all three modes (i.e. the count is **not** determined by build mode), and then

```
distinct XBF hash pairs across 12 runs: 1
RESULT: compiled XAML is invariant. The diagnostic cannot affect the package.
```

Two outcomes are both acceptable here, and they lead to different wording in Step 6:

- **Counts are all 0 in one mode and all 1 in another** → the trigger is the dominant build configuration. Record which.
- **Counts are mixed within the same mode** → the trigger is not a property of the build inputs at all. Record that it is non-deterministic and that only harmlessness is established.

If the script exits 1, **stop**: the hashes disagree, which means the diagnostic is accompanied by different output, and Task 2's allow-list would be hiding something real. Report that instead of proceeding.

- [ ] **Step 6: Record the verdict in `docs/MAINTAINING.md`**

Replace this paragraph (it begins after the "Remaining warnings, all understood and accepted" paragraph):

```
**`WMC9999` is intermittent and not fully explained.** It appears in most solution
builds and not in others, with byte-identical sources; with
`/p:BuildProjectReferences=false` it was absent once and present on a later
identical run. What *is* established:

- it is emitted from the XAML compiler's second pass (`XamlPreCompile`);
- it never changes the exit code, and never prevents `App.xbf`, `MainPage.xbf`,
  `BrowserForWP.exe` or the packages from being produced;
- `App.xbf` and `MainPage.xbf` are **byte-identical** across every rebuild that
  was hashed (`b7af0673a52d230302275b6c60fa2a64`, `817580f71c93802ca8818c328074ea85`);
- four hypotheses were tested and eliminated: the `.resw` `PRIResource` items,
  project-level PRI generation (`/p:GenerateProjectPriFile=false`),
  `BuildingInsideVisualStudio`, and the Release configuration.

Treat it as noise from the VS2013 XAML toolchain, not as a signal about the code.
Do not "fix" source to chase it.
```

with the measured result. Fill in the bracketed values from Step 5's actual output — do not leave them as brackets. The template:

````markdown
**`WMC9999` is a non-deterministic diagnostic from the VS2013 XAML compiler.**

```
Microsoft.Windows.UI.Xaml.Common.targets(327,9): Xaml Internal Error error
WMC9999: La chiave specificata non era presente nel dizionario.
```

*(the given key was not present in the dictionary)*

Reproduce and measure it with `RUNS=4 bash tools/wmc9999-probe.sh`, which runs
three build modes four times each and hashes the compiled XAML from every run.
Measured result: **[counts per mode]** — the diagnostic is [deterministic /
not deterministic] with respect to the build mode.

It is allow-listed by name in `tools/vm-build.cmd`, on this evidence:

- `App.xbf` and `MainPage.xbf` are **byte-identical across all 12 runs**
  (`App.xbf` = `b7af0673a52d230302275b6c60fa2a64`,
  `MainPage.xbf` = `817580f71c93802ca8818c328074ea85`), asserted by the probe's
  non-zero exit code, so it cannot affect the shipped package;
- it never changes the build's exit code and never prevents any output;
- it is emitted from the XAML compiler's second pass (`XamlPreCompile`).

Five hypotheses were tested and **eliminated**: the `.resw` `PRIResource` items,
project-level PRI generation (`/p:GenerateProjectPriFile=false`),
`/p:BuildingInsideVisualStudio=true`, the Release configuration, and the app's
unused `xmlns:local` / `mc:Ignorable="d"` declarations.

**If you are tempted to chase it:** do not change source to do so. The diagnostic
is emitted by a task that has already produced correct output, and the only
remaining leads are inside Microsoft's toolchain. What would move this forward is
a build on an x64 host with the same VS2013 update level, to see whether the
issue is specific to running the toolchain under Arm64 emulation — that is a
hypothesis, and it is untested.
````

- [ ] **Step 7: Verify the docs still build-check clean**

Run:

```bash
node tools/check-vb.mjs --quiet | tail -3
```

Expected: `36 check group(s) run, 0 finding(s).`

- [ ] **Step 8: Commit**

```bash
git add tools/wmc9999-probe.sh docs/MAINTAINING.md BrowserForWP/App.xaml BrowserForWP/MainPage.xaml
git commit -m "test(build): measure the WMC9999 diagnostic instead of describing it"
git push origin HEAD
```

---

### Task 2: Make the build assert its own health

**Why this task exists:** `tools/vm-build.cmd` currently reports `BUILD_EXIT=0` and lets a human read the log. That is how `WMC9999` sat in the output looking like a failure, and how a future real error could slip past a reader who assumes the log is clean because the exit code is 0.

**Files:**
- Modify: `tools/vm-build.cmd`

**Interfaces:**
- Consumes: Task 1's verdict and hash evidence, which justify the allow-list.
- Produces: `tools/vm-build.cmd` that exits non-zero when any `error BC` / `error MSB` / `error APPX` line appears, prints a `Diagnostic summary` section, and still prints `=== BUILD_EXIT=<n> ===` as its last line (the docs and the release checklist grep that exact string).

- [ ] **Step 1: Write the new script**

Replace the whole of `tools/vm-build.cmd` with:

```bat
@echo off
rem ═══════════════════════════════════════════════════════════════════════════
rem  BrowserForWP — build the solution inside the Windows guest.
rem
rem  Run from the macOS host with:
rem
rem    prlctl exec "<VM-ID|name>" "cmd.exe" "/c" ^
rem        "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd"
rem
rem  Note the quoting: prlctl exec takes the command and its arguments as
rem  SEPARATE argv entries. Passing "cmd /c ver" as one string fails silently,
rem  which is why this is a batch file rather than a one-liner.
rem
rem  Extra args are forwarded to MSBuild, e.g.
rem    tools\vm-build.cmd /t:Rebuild
rem
rem  THIS SCRIPT DECIDES PASS/FAIL. It does not just relay MSBuild's exit code:
rem  it also fails when a real compiler error appears in the log, and it names
rem  the one diagnostic that is allowed to appear (WMC9999) so that a future
rem  reader cannot mistake known noise for a clean build, or a clean exit code
rem  for a clean log. See docs/MAINTAINING.md for the evidence behind the
rem  allow-list.
rem ═══════════════════════════════════════════════════════════════════════════

setlocal

set "REPO=C:\Mac\Home\Documents\BrowserForWP"
set "MSB=C:\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe"
set "CFG=Debug"
set "PLAT=ARM"
set "LOG=%TEMP%\browserforwp-build.log"

cd /d "%REPO%" || (echo CANNOT_CD_TO_REPO & exit /b 1)

echo === MSBuild ===
"%MSB%" /version /nologo

echo === Toolchain check ===
if exist "%MSB%" (echo msbuild12 OK) else (echo msbuild12 MISSING)
if exist "C:\Program Files (x86)\Microsoft SDKs\Windows Phone\v8.1" (echo wp81sdk OK) else (echo wp81sdk MISSING)
if exist "C:\Program Files (x86)\Windows Kits\8.1" (echo win81sdk OK) else (echo win81sdk MISSING)

echo === Building BrowserForWP.sln /p:Configuration=%CFG% /p:Platform=%PLAT% ===
"%MSB%" BrowserForWP.sln /nologo /v:minimal /p:Configuration=%CFG% /p:Platform=%PLAT% %* > "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%LOG%"

echo.
echo === Diagnostic summary ===
rem WMC9999 is allow-listed. The VS2013 XAML compiler emits it from its second
rem pass, non-deterministically; it never changes this script's exit code and
rem the compiled .xbf is byte-identical with and without it (asserted by
rem tools/wmc9999-probe.sh). Evidence: docs/MAINTAINING.md.
findstr /C:"WMC9999" "%LOG%" >nul && echo known-noise: WMC9999 ^(allowed, see docs/MAINTAINING.md^)

echo.
echo === Real compiler errors ===
rem Any of these means the build is broken, whatever MSBuild's exit code says.
findstr /R /C:"error BC" /C:"error MSB" /C:"error APPX" "%LOG%" >nul
if not errorlevel 1 (
  echo UNEXPECTED COMPILER ERRORS ^(see the log above, and "%LOG%"^)
  set "RC=1"
) else (
  echo none
)

echo.
echo === Warnings ===
findstr /R /C:"warning BC" /C:"warning MSB" /C:"warning APPX" "%LOG%" || echo none
rem Two BC40000 warnings on ResourceLoader are expected and deliberate; see
rem docs/MAINTAINING.md. Any OTHER warning here is new and worth a look.

echo === BUILD_EXIT=%RC% ===

endlocal & exit /b %RC%
```

- [ ] **Step 2: Verify the happy path**

Run:

```bash
prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \
    "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd /t:Rebuild" 2>&1 | tail -20
```

Expected tail, in this order:

```
=== Real compiler errors ===
none

=== Warnings ===
BrowserForWP.Localization\Localizer.vb(111,54): warning BC40000: ...
BrowserForWP.Localization\Localizer.vb(125,27): warning BC40000: ...
=== BUILD_EXIT=0 ===
```

and a `known-noise: WMC9999 (allowed, ...)` line in the `Diagnostic summary` block when the diagnostic happens to appear.

- [ ] **Step 3: Verify the failure path — negative control**

A checker that has never been seen to fail is not a checker. This is the same discipline `tools/check-vb.mjs` already documents for its own project-parity rule.

Introduce a real compile error into a library that is not protected by the "never hand-edit" rule. Append this to the end of `BrowserForWP.Localization/LanguageCatalog.vb`, before the final `End Namespace`:

```vb
    Friend NotInheritable Class DeliberateBreak
        Public Shared Function Broken() As Integer
            Return "this is not an Integer"
        End Function
    End Class
```

Build:

```bash
prlctl exec "{66a2f493-162c-4b3f-ba40-0a26020cc818}" "cmd.exe" "/c" \
    "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd" 2>&1 | tail -12
```

Expected:

```
=== Real compiler errors ===
BrowserForWP.Localization\LanguageCatalog.vb(...): error BC30311: ...
UNEXPECTED COMPILER ERRORS (see the log above, and ...)
=== BUILD_EXIT=1 ===
```

Then revert and confirm the revert is clean:

```bash
git checkout -- BrowserForWP.Localization/LanguageCatalog.vb
git diff --stat BrowserForWP.Localization/LanguageCatalog.vb   # expect: no output
```

- [ ] **Step 4: Update the documentation that describes the build's output**

In `docs/MAINTAINING.md`, in the "The build that actually works" section, the sentence that currently reads:

```
and prints `=== BUILD_EXIT=<n> ===`. Extra arguments are forwarded, so
`tools\vm-build.cmd /t:Rebuild` performs a clean build.
```

becomes:

```
prints a diagnostic summary, and finishes with `=== BUILD_EXIT=<n> ===`. Extra
arguments are forwarded, so `tools\vm-build.cmd /t:Rebuild` performs a clean
build.

**The script decides pass/fail, and it is stricter than MSBuild.** It exits
non-zero if an `error BC`, `error MSB` or `error APPX` line appears in the log,
even when MSBuild itself would have returned 0, and it reports the one
allow-listed diagnostic (`WMC9999`) by name. A clean exit code is therefore a
statement about the log, not just about MSBuild's opinion of it.
```

- [ ] **Step 5: Run the full local verification floor**

Run:

```bash
node tools/check-vb.mjs --quiet | tail -3
node tools/gen-vectors.mjs | tail -2
```

Expected: `36 check group(s) run, 0 finding(s).` and `52 assertions passed. Vectors are consistent with the RFCs.`

- [ ] **Step 6: Commit**

```bash
git add tools/vm-build.cmd docs/MAINTAINING.md
git commit -m "build: fail the guest build on real compiler errors, not on MSBuild's exit code"
git push origin HEAD
```

---

### Task 3: Documentation truth — nothing may name what does not exist

**Why this task exists:** the plan this one closes specified VB test projects, and TDD steps that write into them. They were never created. `tests/BrowserForWP.Crypto.Tests/` contains a single generated file, `Vectors.generated.vb` and no `.vbproj`; `tests/BrowserForWP.Core.Tests/` does not exist at all. Five operational sites still instruct a contributor to use them.

This is precisely the failure the skill file warns about in its own words: *"A skill describing a previous version of the project is actively harmful, because it is what the next contributor trusts."*

**Files:**
- Modify: `docs/MAINTAINING.md` (verification commands at line ~73, release checklist at line ~428, plus a new "Where the tests actually are" section)
- Modify: `.agents/skills/browserforwp/SKILL.md` (verification table line ~114, worked example lines ~174, ~209, ~211)
- Modify: `docs/superpowers/plans/2026-09-28-browserforwp.md` (status note, and two File Structure rows)
- Modify: `docs/superpowers/plans/2026-09-28-build-verification.md` (mark steps per reality)
- No change to `tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb` — it is generated; it is referenced only to state its status.

**Interfaces:**
- Consumes: nothing from Tasks 1–2; independent.
- Produces: a repository in which every mention of a `tests/BrowserForWP.*.Tests` project is accompanied by the literal marker `not created`, so that a future grep can tell disclosure from stale instruction.

**The invariant, stated precisely.** The marker is the exact string `not created`. It is applied at two granularities:

- **Operational docs** — `README.md`, `README.it.md`, anything directly under `docs/`, and `.agents/skills/**` — must carry the marker **on the same line** as the mention. These are documents people follow.
- **Plan docs** — `docs/superpowers/plans/*.md` — must carry the marker **somewhere in the file**, in a status note. These are specifications; their file lists describe intended deliverables, and one banner is the right granularity. Rewriting twenty step bodies in a historical plan to add a marker would be noise.

- [ ] **Step 1: Establish the facts, and enumerate the sites by grep**

Run:

```bash
ls -1 tests/
ls -1 tests/BrowserForWP.Crypto.Tests/
grep -rn "BrowserForWP.Core.Tests\|BrowserForWP.Crypto.Tests" --include=*.md .
```

Expected: `tests/` contains only `BrowserForWP.Crypto.Tests`; that directory contains only `Vectors.generated.vb`. The grep returns **24 hits**, of which exactly these 5 are operational and stale:

```
./docs/MAINTAINING.md:73:In Visual Studio, run the `BrowserForWP.Crypto.Tests` project from Test Explorer.
./docs/MAINTAINING.md:428:- [ ] Test Explorer: `BrowserForWP.Crypto.Tests` all green
./.agents/skills/browserforwp/SKILL.md:114:| Crypto unit tests | Test Explorer → run `BrowserForWP.Crypto.Tests` | all tests green |
./.agents/skills/browserforwp/SKILL.md:174:3. **Write the failing test first.** In `tests/BrowserForWP.Core.Tests/`:
./.agents/skills/browserforwp/SKILL.md:211:   git add tests/BrowserForWP.Core.Tests/DesktopModeTests.vb \
```

The other 19 are in `docs/superpowers/plans/2026-09-28-browserforwp.md` (Task file lists and `git add` lines) and in `2026-09-28-build-closure.md` itself. Line numbers will drift as you edit; match on text, not on numbers.

- [ ] **Step 1b: Fix the two `docs/MAINTAINING.md` sites**

**Site A, the verification-commands section (around line 70–74).** This text currently sits directly under the runnable command block and tells the reader to run a project that does not exist. Replace:

```
In Visual Studio, run the `BrowserForWP.Crypto.Tests` project from Test Explorer.
Those tests consume `Vectors.generated.vb`, which is **generated** — never edit
it by hand. Regenerate with `node tools/gen-vectors.mjs`.
```

with:

```
Those vectors also produce `tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb`,
which is **generated** — never edit it by hand. Regenerate with
`node tools/gen-vectors.mjs`.

That file has **no consumer**: the MSTest project it was generated for was
**not created**. See "Where the tests actually are" below before assuming those
vectors are being asserted by a VB test run.
```

**Site B, the release checklist.** Replace:

```
- [ ] Test Explorer: `BrowserForWP.Crypto.Tests` all green
```

with:

```
- [ ] `node tools/proto/w25519.mjs` → `18 checks, 0 failure(s)`
- [ ] `node tools/proto/tls13.mjs example.com` → `31 checks, 0 failure(s)`
- [ ] `bash tools/wmc9999-probe.sh` → `distinct XBF hash pairs across N runs: 1`
```

The `python3 tools/make_logo.py` line above it stays, but its expectation text must match what the generator actually prints now that every asset is scale-qualified:

```
- [ ] `python3 tools/make_logo.py` → 12 PNGs, all `*.scale-100` / `*.scale-240`, no git diff
```

- [ ] **Step 2: Fix the three `SKILL.md` sites and the stale build instruction**

**Site C, the verification table (line ~114).** Delete this row:

```
| Crypto unit tests | Test Explorer → run `BrowserForWP.Crypto.Tests` | all tests green |
```

and add these two rows in its place:

```
| Unexplained build diagnostics | `RUNS=4 bash tools/wmc9999-probe.sh` | `distinct XBF hash pairs across 12 runs: 1` |
| `BrowserForWP.Core/` logic | No automated harness — build, then verify by hand. See `docs/MAINTAINING.md` § "Where the tests actually are". | honest report, not a green tick |
```

**Sites D and E, the worked example.** Step 3 currently says *"Write the failing test first. In `tests/BrowserForWP.Core.Tests/`:"* with a `<TestMethod>` body, and step 10 commits `tests/BrowserForWP.Core.Tests/DesktopModeTests.vb`. Both paths are fictional. Step 9 is stale for a different reason: it says *"Build `Debug | ARM` in Visual Studio"*.

Replace steps 3, 4, 9 and 10 of the walkthrough with:

````markdown
3. **Decide how this change can be verified, and be specific.** There is no VB
   test project in this repository — see `docs/MAINTAINING.md` § "Where the
   tests actually are" — so "add a unit test" is not available to you. Pick one
   of these two, and write down which:

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
   node tools/proto/desktop-mode.mjs
   # Expected: FAIL — no such export: effectiveUserAgent
   ```

   For UI work it means writing the manual verification steps down *now*, while
   you still remember what "correct" looks like, so step 9 has something concrete
   to check against.

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
````

Note what changed and why: the `git add` no longer names a file that does not
exist, and the build instruction points at the command that actually builds this
project instead of at an IDE that is not the build driver.

- [ ] **Step 3: Fix the two `2026-09-28-browserforwp.md` sites**

That plan already discloses the gap, but in wording the invariant does not
recognise, and its File Structure table still lists a test project as a
deliverable. Change the status sentence:

```
Still not written, and not needed for the app to build: the two `tests/`
projects are referenced by this plan but only
`tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb` exists so far.
```

to:

```
Still **not created**, and not needed for the app to build: this plan names two
`tests/` projects — `BrowserForWP.Crypto.Tests` and `BrowserForWP.Core.Tests` —
and neither exists. Only `tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb`
was ever produced, and it has no consumer. The TDD steps in Tasks 2–13 below
refer to test files that were never written; the crypto they were meant to cover
is verified instead by `tools/gen-vectors.mjs` and `tools/proto/tls13.mjs`, and
`BrowserForWP.Core` has no automated coverage at all. See
`docs/MAINTAINING.md` § "Where the tests actually are".
```

And annotate the two File Structure rows so the table cannot be read as a
statement of what exists:

```
| `BrowserForWP.Polyfill/compat.js` | On-device compatibility layer. Written and ES5-checked; **not created** is the injection step — nothing loads it into a page. |
| `tests/BrowserForWP.Crypto.Tests/` | MSTest project consuming `Vectors.generated.vb`. **not created.** |
```

- [ ] **Step 4: Add the note that explains the absence**

Insert this section in `docs/MAINTAINING.md` immediately before `## Release checklist`:

````markdown
## Where the tests actually are

**There are no VB unit-test projects.** `2026-09-28-browserforwp.md` specified
`tests/BrowserForWP.Crypto.Tests/` and `tests/BrowserForWP.Core.Tests/` as
<TestProject> projects with `<TestMethod>` cases, and neither was created. What
exists is:

| Path | What it is | Consumed by |
| --- | --- | --- |
| `tools/gen-vectors.mjs` / `tools/proto/*.mjs` | The executable prototypes. `gen-vectors.mjs` recomputes HKDF, X25519 and AES-GCM and asserts RFC 5869 / 7748 / 8448 and NIST CAVS vectors; `tls13.mjs` completes real handshakes against live servers. | `node`, on any machine. This is the real crypto verification. |
| `tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb` | Generated VB constants from those same vectors. | **Nothing, currently.** It is written by `gen-vectors.mjs` and has no consumer until a test project is added. |
| `tools/check-vb.mjs` | 12 categories of static check over every `.vb`, `.vbproj`, `.xaml` and `.resw`. | `node`, on any machine. |
| `tools/vm-build.cmd` | The real compiler. | The Windows guest. |

**This is a real gap, not a documentation problem.** The crypto algorithms are
covered better off-device than a VB test project would cover them, because the
prototypes exercise the *same algorithm* against published vectors and live
servers — but `BrowserForWP.Core` (address normalisation, history, tab state) and
the UI have **no automated tests at all**, and the plan's TDD steps for them were
never honoured. Adding a WP8.1 Unit Test Library and running it in the guest is
specified as a follow-up plan, not attempted here.

Until that exists, do not claim test coverage for `BrowserForWP.Core`. Verify it
by building and by hand on a handset, and say so.
````

- [ ] **Step 5: Mark the old plan's steps according to what happened**

In `docs/superpowers/plans/2026-09-28-build-verification.md`, the step checkboxes are stale: 21 unchecked, including tasks that are finished and one that is superseded. Fix them so the file cannot be re-executed by mistake.

- **Task 1** (ignore generated build output): all five steps become `- [x]`. Append a one-line note under the task heading: *"Outcome: satisfied by the committed `.gitignore`; `git ls-files` shows no `bin/`, `obj/`, `*.suo` or `AppPackages/` entries. Steps marked complete retroactively — the rules were written before they were checked, and they hold."*
- **Task 3** (provision an x64 host): all six steps become `- [x]`, and the existing SUPERSEDED banner stays. Append: *"Outcome: not executed. The x64 host was not needed; the ARM64 guest already had the toolchain. See the Outcome section at the end of this file."*
- **Task 4** (the build itself): all remaining steps become `- [x]`. Append: *"Outcome: `BUILD_EXIT=0` for `Debug|ARM` and `Release|ARM`. Twelve consecutive rebuilds agreed. Error taxonomy in `docs/MAINTAINING.md`."*
- **Task 6** (skills and loop docs): all five steps become `- [x]`. Append: *"Outcome: the verification table and the loop are in `.agents/skills/browserforwp/SKILL.md`, and are kept accurate there as part of the loop. Task 3 of `2026-09-28-build-closure.md` corrected the two rows that had gone stale."*

Verify with:

```bash
grep -c '^- \[ \]' docs/superpowers/plans/2026-09-28-build-verification.md
```

Expected: `0`.

- [ ] **Step 6: Verify the invariant in the operational docs**

Run:

```bash
find . -name '*.md' -not -path './docs/superpowers/plans/*' -not -path './.git/*' -print0 \
  | xargs -0 grep -n "BrowserForWP\.\(Core\|Crypto\)\.Tests" \
  | grep -v "not created"
echo "(end)"
```

Expected: only `(end)`. Every operational mention must sit on a line that carries the marker; if a line appears above `(end)`, it is still instructing a reader to use a harness that does not exist.

- [ ] **Step 7: Verify the invariant in the plan docs**

Run:

```bash
for f in docs/superpowers/plans/*.md; do
  if grep -q "BrowserForWP\.\(Core\|Crypto\)\.Tests" "$f" && ! grep -q "not created" "$f"; then
    echo "MISSING MARKER: $f"
  fi
done
echo "(end)"
```

Expected: only `(end)`. Every plan that names a test project also discloses that it was not created. `2026-09-28-build-closure.md` satisfies this from its own Starting-state table.

- [ ] **Step 8: Run the verification floor and commit**

```bash
node tools/check-vb.mjs --quiet | tail -3
node tools/gen-vectors.mjs | tail -2
git add docs/MAINTAINING.md \
        .agents/skills/browserforwp/SKILL.md \
        docs/superpowers/plans/2026-09-28-browserforwp.md \
        docs/superpowers/plans/2026-09-28-build-verification.md
git commit -m "docs: stop promising test projects that were never created"
git push origin HEAD
```

Expected: `36 check group(s) run, 0 finding(s).` and `52 assertions passed.`

---

## Deliberately out of scope

Each of these is real, and each deserves its own plan rather than a paragraph here.

1. **Polyfill injection (main plan Task 14).** `BrowserForWP.Polyfill/compat.js` is
   written, ES5-checked and packaged, but nothing loads it into a page:
   `TridentEngine` exposes `InvokeScriptAsync` and never calls it. The READMEs now
   say so instead of claiming the opposite. Implementing it means reading the
   asset with `StorageFile.GetFileFromApplicationUriAsync(New Uri("ms-appx:///Polyfill/compat.js"))`
   and injecting on navigation — and deciding *what* "before author scripts run"
   can even mean on IE11's WebView, which has no such hook. That is a design
   question, not a code question.
2. **The two `BC40000` warnings.** `New ResourceLoader(name)` is deprecated in
   favour of `ResourceLoader.GetForCurrentView(name)`, which returns a **cached**
   loader per view and map. `Localizer` depends on re-creating the loader after
   changing `ResourceContext.QualifierValues("Language")`, so the "correct" API
   would silently break the runtime language switch — a user-visible regression
   that cannot be verified without a handset. Two warnings are the cheaper price.
   Revisit only alongside handset testing.
3. **VB test projects, run in the guest.** A WP8.1 Unit Test Library can be built
   and executed, but not from the command line alone without a device or the
   WP8.1 emulator (which needs x64). Until that is set up,
   `tests/BrowserForWP.Crypto.Tests/Vectors.generated.vb` has no consumer. Task 3
   documents the gap; closing it is separate work.
4. **Handset verification.** Nothing in this repository has ever run on a phone.
   Compiling is not running.

## Self-Review

**1. Spec coverage**

| Requirement from the request | Where |
| --- | --- |
| "Continue the plan you wrote before" | This plan finishes `2026-09-28-build-verification.md`: Task 1 closes its open diagnostic, Task 3 marks its stale steps, and its Task 3 is already flagged superseded. |
| "Fix all compilation errors" | The Starting-state table states plainly that there are none: `BUILD_EXIT=0`, zero `BC` errors, verified over twelve rebuilds. The two `BC40000` warnings are addressed by decision in "Deliberately out of scope", with the reason the suggested fix would be a regression. No task pretends to fix an error that does not exist. |
| "Always push" | Every task ends with `git push origin HEAD`. |
| "Always update the skill" | Task 3 rewrites the verification table and the worked example in `SKILL.md` — three sites, one of which (the `git add` line) was found by grepping for the paths rather than by reading the file. |

**What the request did not contain, and this plan does not invent.** "Fix all
compilation errors" is answered by demonstrating that there are none and by
saying so, not by manufacturing work. The one candidate that looks like a
compile error — `WMC9999`, which prints the word "error" — is diagnosed as
non-deterministic toolchain noise in Task 1 rather than chased with source edits.
And the deprecated-constructor warning has a suggested fix that would break a
user-visible feature; Task 2's `git checkout` hedge in Task 1 Step 3 and the
"Deliberately out of scope" entry exist so that neither is silently "fixed".

**2. Placeholder scan**

No `TBD`, no "add error handling", no "similar to Task N". Each code step shows
the complete replacement text. Task 1 Step 6 contains bracketed values — `[counts
per mode]` and `[deterministic / not deterministic]` — and this is deliberate:
they are measurements that do not exist until Step 5 runs, and the step says so
and forbids leaving them as brackets in the committed file. Task 3's steps quote
the exact current text at each of the five stale sites; the `docs/MAINTAINING.md`
and `SKILL.md` excerpts were read from disk and copied verbatim, not recalled.

**3. Type consistency**

- `tools/wmc9999-probe.sh` is introduced in Task 1 Step 4 with the interface
  `RUNS=<n> bash tools/wmc9999-probe.sh`, exit 0 iff all XBF hashes match. Task 1
  Step 6's `docs/MAINTAINING.md` template, Task 2's allow-list comment, and Task
  3's release checklist and skill row all refer to that same command and exit
  condition. No other name is used for it.
- The exit-code contract of `tools/vm-build.cmd` — last line
  `=== BUILD_EXIT=<n> ===`, non-zero on any `error BC`/`error MSB`/`error APPX` —
  is introduced in Task 2 Step 1 and consumed unchanged in Task 2 Steps 2, 3 and
  4, and in Task 3 Step 2's checklist. The literal string `BUILD_EXIT` is not
  spelled any other way.
- `App.xbf` = `b7af0673a52d230302275b6c60fa2a64` and `MainPage.xbf` =
  `817580f71c93802ca8818c328074ea85` are the same digests in the Starting-state
  table, Task 1 Step 6, and the existing `docs/MAINTAINING.md`. They match the
  values measured on disk.
