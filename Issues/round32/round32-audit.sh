#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
# Round-32 audit for DwarfMapper.NET. Run from the repository root.
#   Issues/round32/round32-audit.sh baseline   print the figures the T00 entry of TASK-LOG.md records
#   Issues/round32/round32-audit.sh static     per-task completion checks, no build (seconds); exits 1 on any TODO
#   Issues/round32/round32-audit.sh tests      run the round-32 acceptance tests (needs a prior Release build)
# Unlike round 31's audit, `static` exits non-zero while any task is TODO (round-32 finding, item 4), and it
# keeps no baseline under $HOME: the baseline lives in the TASK-LOG, where a new container can read it.
set -uo pipefail
[ -d src/DwarfMapper.Generator ] || { echo "run from the DwarfMapper.NET repository root"; exit 2; }
PY="$(command -v python3 || command -v python)" || { echo "python3 or python is required"; exit 2; }
export PYTHONUTF8=1

todo=0
mark() { # id, condition-exit-code, note
  if [ "$2" -eq 0 ]; then printf "  %-6s DONE  %s\n" "$1" "$3"; else printf "  %-6s TODO  %s\n" "$1" "$3"; todo=$((todo+1)); fi; }
has() { grep -qE -- "$2" "$1" 2>/dev/null; }

bom_configs() { "$PY" - <<'PY'
import glob
print(sum(1 for p in glob.glob('stryker-config*.json') if open(p,'rb').read(3) == b'\xef\xbb\xbf'))
PY
}
pragma_count() { grep -rn --include=*.cs '#pragma warning disable' src | grep -v '/obj/' | wc -l | tr -d ' '; }
probe_count() { find src tests samples benchmarks -name 'ZZ*.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null | wc -l | tr -d ' '; }
# Text of one job in a workflow: from "  <job>:" to the next two-space key.
job() { awk -v j="  $2:" '$0==j{on=1;next} on&&/^  [A-Za-z0-9_-]+:[[:space:]]*$/{on=0} on' "$1"; }
r1_rows() { grep -cE 'InlineData\("stryker-config[a-z.]*\.json"\)' tests/DwarfMapper.Generator.Tests/SelfValidation/RatchetInvariantScanTests.cs; }

case "${1:-static}" in
baseline)
  echo "head=$(git rev-parse --short HEAD)"
  echo "sdk=$(dotnet --version 2>/dev/null || echo none)"
  echo "bom_configs=$(bom_configs)"
  echo "pragma_src=$(pragma_count)"
  echo "probes=$(probe_count)"
  echo "r1_rows=$(r1_rows)" ;;

static)
  echo "Round-32 static audit @ $(git rev-parse --short HEAD)"
  echo "── global ──"
  p=$(probe_count); mark G-probe "$([ "$p" -eq 0 ]; echo $?)" "no ZZ*.cs probe files (found $p)"
  unpinned=$(grep -rhE '^\s*-?\s*uses:\s' .github/workflows 2>/dev/null | grep -vE '@[0-9a-f]{40}' | grep -vE 'uses:\s*\./' | wc -l | tr -d ' ')
  mark G-pins "$([ "$unpinned" -eq 0 ]; echo $?)" "workflow actions pinned to a 40-hex SHA (unpinned: $unpinned)"
  echo "── tasks ──"
  b=$(bom_configs)
  mark T01 "$([ "$b" -eq 0 ] && [ -f tests/DwarfMapper.Generator.Tests/Round32/StrykerConfigEncodingTests.cs ]; echo $?)" "no stryker config starts with a BOM ($b do); encoding test present"
  mark T02 "$([ -f Issues/round32/FINDING-T02-ci-phantom-kills.md ]; echo $?)" "research: CI's generator leg above its proven ceiling"
  mut=$(job .github/workflows/ci.yml mutation)
  rel=$(job .github/workflows/release.yml release)
  mark T03 "$(echo "$mut" | grep -q 'dotnet-ilverify' && echo "$rel" | grep -q 'dotnet-ilverify' && has tests/DwarfMapper.Generator.Tests/SelfValidation/CiToolPrerequisiteScanTests.cs 'dotnet stryker'; echo $?)" "ilverify in the mutation and release jobs; the scan counts dotnet stryker"
  mark T04 "$(echo "$mut" | grep -q 'Assert-LegScoreWithinBand'; echo $?)" "the mutation job runs the band check (needs D2)"
  nb=$("$PY" - <<'PY'
import glob,json
print(sum(1 for p in glob.glob('stryker-config*.json')
          if json.loads(open(p,'rb').read().decode('utf-8-sig'))['stryker-config'].get('break-on-initial-test-failure') is not True))
PY
)
  mark T05 "$([ "$nb" -eq 0 ]; echo $?)" "break-on-initial-test-failure in every config ($nb without; needs D5)"
  mark T06r "$([ -f Issues/round32/RESEARCH-T06-dashboard-413.md ]; echo $?)" "research: what the dashboard upload sends, and the options"
  mark T06 "1" "the dashboard upload fix (owner decision D1; not grepped until decided)"
  mark T07r "$([ -f Issues/round32/FINDING-T07-package-growth.md ]; echo $?)" "research: package growth measured and accounted"
  mark T07 "1" "the package-size ceilings (owner decision D3; not grepped until decided)"
  r=$(r1_rows); mark T08 "$([ "$r" -ge 6 ]; echo $?)" "R1 reads all six stryker configs ($r rows)"
  mark T09 "$([ -f tests/DwarfMapper.Generator.Tests/Round32/ProbeFileScanTests.cs ]; echo $?)" "the ZZ*.cs probe rule is a test"
  mark T10 "$(git check-ignore -q .claude/worktrees/x; echo $?)" ".claude/worktrees/ is ignored"
  mark T11 "$(! has tests/DwarfMapper.NegativeCases/README.md 'is how you opt out'; echo $?)" "NegativeCases README no longer calls the pinned list an opt-out"
  mark T12 "$(! has Issues/ledgers/README.md 'rev-parse'; echo $?)" "ledgers README names its capture commit"
  mark T13 "1" "DCO sign-off (owner decision D4; not grepped)"
  mark T14 "$([ -x scripts/cloud-toolchain.sh ] && has Issues/ROUND-PROTOCOL.md 'cloud-toolchain.sh'; echo $?)" "cloud toolchain script, referenced by the protocol"
  echo "── $todo TODO ──"
  [ "$todo" -eq 0 ] ;;

tests)
  for proj in tests/DwarfMapper.Generator.Tests tests/DwarfMapper.IntegrationTests; do
    dotnet test "$proj" -c Release --no-build --nologo --filter "FullyQualifiedName~.Round32.|FullyQualifiedName~RatchetInvariantScanTests.R1|FullyQualifiedName~CiToolPrerequisiteScanTests" 2>&1 | grep -E "Passed!|Failed!|No test matches|\[FAIL\]"
  done ;;

*) echo "usage: $0 baseline|static|tests"; exit 2 ;;
esac
