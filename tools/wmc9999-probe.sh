#!/usr/bin/env bash
# ───────────────────────────────────────────────────────────────────────────
#  Characterise the WMC9999 diagnostic emitted by the VS2013 XAML compiler.
#
#  Why: docs/superpowers/plans/2026-09-28-build-closure.md Task 1. The
#  diagnostic is "Xaml Internal Error error WMC9999: ... the given key was not
#  present in the dictionary", it appears in some builds and not others, and it
#  does not fail the build. This script answers two questions with numbers:
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
