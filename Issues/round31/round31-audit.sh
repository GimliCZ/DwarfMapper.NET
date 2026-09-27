#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
# Round-31 audit for DwarfMapper.NET. Run from the repository root.
#   ./round31-audit.sh baseline   record counts the other checks compare against ($HOME/.round31-baseline)
#   ./round31-audit.sh static     per-task completion checks, no build (seconds)
#   ./round31-audit.sh tests      run the round-31 acceptance tests (needs a prior Release build)
set -uo pipefail
BASE="$HOME/.round31-baseline"
[ -d src/DwarfMapper.Generator ] || { echo "run from the DwarfMapper.NET repository root"; exit 2; }
# Round 31 second pass: `python3` does not exist on the Windows dev box (only `python`), and the scripts below
# read UTF-8 sources that the default cp1250 codec cannot decode. Without these two lines T05/T08/T20 reported
# a false TODO from an interpreter error, which is worse than no check.
PY="$(command -v python3 || command -v python)"
export PYTHONUTF8=1
python3() { "$PY" "$@"; }

wide_count() { python3 - <<'PY'
import re,glob
n=0
for p in glob.glob('src/DwarfMapper.Generator/Pipeline/**/*.cs',recursive=True):
    s=open(p,encoding='utf-8-sig').read()
    for m in re.finditer(r'(?:private|internal) static [^(\n]+ (\w+)\(([^)]*)\)',s,re.S):
        c=m.group(2).count(',')+1 if m.group(2).strip() else 0
        if c>6: n+=1
print(n)
PY
}
pragma_count() { grep -rn --include=*.cs '#pragma warning disable' src | grep -v '/obj/' | wc -l | tr -d ' '; }
nowarn_audit_count() { grep -rlE --include=*.csproj --include=*.props --include=*.targets '<NoWarn>[^<]*NU190[1-4]' . 2>/dev/null | grep -v '/obj/' | wc -l | tr -d ' '; }

mark() { # id, condition-exit-code, note
  if [ "$2" -eq 0 ]; then printf "  %-5s DONE  %s\n" "$1" "$3"; else printf "  %-5s TODO  %s\n" "$1" "$3"; fi; }
has() { grep -rqE "$2" $1 2>/dev/null; }

case "${1:-static}" in
baseline)
  { echo "wide=$(wide_count)"; echo "pragma=$(pragma_count)"; echo "nowarn=$(nowarn_audit_count)"; echo "head=$(git rev-parse --short HEAD)"; } > "$BASE"
  echo "baseline written to $BASE:"; cat "$BASE" ;;

static)
  [ -f "$BASE" ] && . "$BASE" || { echo "no baseline — run: $0 baseline"; wide=0; pragma=0; nowarn=0; }
  echo "Round-31 static audit @ $(git rev-parse --short HEAD) (baseline @ ${head:-none})"
  echo "── global ──"
  probes=$(find . -name 'ZZ*.cs' -not -path '*/obj/*' | wc -l | tr -d ' ')
  mark G-probe "$([ "$probes" -eq 0 ]; echo $?)" "no ZZ*.cs probe files (found $probes)"
  p=$(pragma_count); mark G-pragma "$([ "$p" -le "${pragma:-0}" ]; echo $?)" "#pragma warning disable in src: $p (baseline ${pragma:-?})"
  unpinned=$(grep -rhE '^\s*-?\s*uses:\s' .github/workflows 2>/dev/null | grep -vE '@[0-9a-f]{40}' | grep -vE 'uses:\s*\./' | wc -l | tr -d ' ')
  mark G-pins "$([ "$unpinned" -eq 0 ]; echo $?)" "workflow actions pinned to a 40-hex SHA (unpinned: $unpinned)"
  echo "── tasks ──"
  n=$(nowarn_audit_count)
  mark T01 "$([ "$n" -eq 0 ] && grep -rq 'GHSA-rvv3-g6hj-g44x' --include=*.csproj tests benchmarks; echo $?)" "no NU190x NoWarn ($n files); advisory-scoped NuGetAuditSuppress present"
  bare=$(grep -cE '\{srcExpr\} == null' src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs 2>/dev/null || true)
  mark T02 "$([ "${bare:-1}" -eq 0 ] && has src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs 'NullGuardOperand'; echo $?)" "projection null guards via NullGuardOperand (bare sites: ${bare:-?})"
  mark T03 "$([ -f tests/DwarfMapper.Generator.Tests/Round31/DeepRecursionPocTests.cs ] && grep -q 'CVE-2026-32933' SECURITY.md; echo $?)" "PoC test + SECURITY.md claim"
  mark T04 "$(awk '/From_location_at_end_of_file/,/^        }/' tests/DwarfMapper.Generator.Tests/Coverage/LocationInfoCoverageTests.cs | grep -q 'LineSpan'; echo $?)" "end-of-file test asserts the mapped position"
  dup=$(python3 - <<'PY'
import re,glob,hashlib
norm=lambda s: hashlib.md5(re.sub(r'\s+','',s).encode()).hexdigest()
cov=set();out=set()
for p in glob.glob('tests/**/*.cs',recursive=True):
    if '/obj/' in p: continue
    txt=open(p,encoding='utf-8-sig').read()
    for m in re.finditer(r'"""(.*?)"""',txt,re.S):
        s=m.group(1)
        if len(s)<=80: continue
        before='\n'.join(txt[:m.start()].splitlines()[-2:])
        if '/Coverage/' in p and 'shared-fixture:' in before: continue
        (cov if '/Coverage/' in p else out).add(norm(s))
print(len(cov & out))
PY
)
  mark T05 "$([ "$dup" -eq 0 ]; echo $?)" "coverage sources duplicated verbatim elsewhere: $dup"
  mark T06 "$([ -f tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt ]; echo $?)" "golden manifest present (the byte-identity lock)"
  mark T07 "$([ -f tests/DwarfMapper.Generator.Tests/Round31/ResolverParameterCeilingTests.cs ]; echo $?)" "parameter-ceiling ratchet"
  w=$(wide_count)
  # Owner ruling 2026-09-27: extend ae9c7ea's bundles, not one ExtractionContext. Measured by the Roslyn ratchet
  # (ResolverParameterCeilingTests' allowance rows: 54 at T07), not by wide_count's regex, which cannot see a method
  # whose return type contains '(' - T07's log records it undercounting 40 against 54.
  rows=$(grep -cE '^\s+\["[^"]+::[^"]+"\] = [0-9]+,' tests/DwarfMapper.Generator.Tests/Round31/ResolverParameterCeilingTests.cs)
  mark T08 "$(has src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs 'sealed record ProjectionRequest' && [ "$rows" -lt 54 ]; echo $?)" "bundles extended; ratchet rows over the ceiling: $rows (54 at T07); regex count $w"
  # Accepted deviation (0af2f2e/4a6821f): one runtime helper instead of inlining, no span read (it went stale on mutation).
  mark T09 "$(has src/DwarfMapper/DwarfCollectionMap.cs 'TryGetNonEnumeratedCount' && has src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs 'DwarfCollectionMap\.'; echo $?)" "registry collection shapes pre-size through DwarfCollectionMap"
  mark T10 "$(has src/DwarfMapper.Generator/Pipeline/MapEmitter.cs '__dwarf_proj_'; echo $?)" "projection trees hoisted into static fields"
  mark T11 "$(has src/DwarfMapper/DwarfMapperRegistry.cs 'RegisterMany' && has src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs 'RegisterMany' && has src/DwarfMapper/PublicAPI.Unshipped.txt 'RegisterMany'; echo $?)" "batch registration (runtime + emission + PublicAPI)"
  # c65c4fe: only a FOUND delegate is cached, so the registry version counter is gone - and must stay gone.
  mark T12 "$([ -f src/DwarfMapper/ExactPairSlot.cs ] && has src/DwarfMapper/IDwarfMapper.cs 'ExactPairSlot<' && ! has src/DwarfMapper/DwarfMapperRegistry.cs '_version'; echo $?)" "exact-pair slot, no version counter"
  mark T13 "$(has src/DwarfMapper.Generator/Pipeline/MapEmitter.cs 'EnumerableQuery<' && [ -f tests/DwarfMapper.Generator.Tests/Round31/TreeOnlyQueryable.cs ]; echo $?)" "in-memory projection route + parity oracle"
  mark T14 "$(has src/DwarfMapper/DwarfMapperRegistry.cs 'InterfaceMapsByDestination' && [ -f tests/DwarfMapper.IntegrationTests/Round31/InterfaceBucketTests.cs ]; echo $?)" "collection dispatch flat in app size (destination buckets)"
  mark T16 "$(has src/DwarfMapper.Generator/Pipeline/MapEmitter.cs 'ProjectionExpressionName' && [ -f tests/DwarfMapper.NegativeCases/Cases/DWARF112_ProjectionExpressionNameTaken.cs ]; echo $?)" "projection expression exposed (+ DWARF112)"
  mark T19 "$(grep -q 'Category!=Perf' .github/workflows/ci.yml && grep -q 'perf-tests:' .github/workflows/ci.yml; echo $?)" "Perf category excluded from default lane; nightly perf-tests job"
  hr_jobs=$(python3 - <<'PY'
import re,glob
bad=0
for p in glob.glob('.github/workflows/*.yml'):
    s=open(p,encoding='utf-8').read()
    for job in re.split(r'\n  [A-Za-z0-9_-]+:\n',s)[1:]:
        steps=job.split('steps:',1)
        if len(steps)<2: continue
        first=re.search(r'-\s*(uses|run|name):\s*(.*)',steps[1])
        block=steps[1][:400]
        if 'harden-runner' not in block: bad+=1
print(bad)
PY
)
  mark T20 "$([ -f .github/workflows/actions-security.yml ] && [ "$hr_jobs" -eq 0 ]; echo $?)" "zizmor workflow; jobs without harden-runner first: $hr_jobs"
  mark T21 "$(grep -q 'NuGet/login@' .github/workflows/release.yml 2>/dev/null; echo $?)" "trusted publishing job"
  mark T22 "$(awk '/^  preview-sdk-canary:/,/^  bench-wall-time-alert:/' .github/workflows/ci.yml | grep -q 'TreatWarningsAsErrors=false'; echo $?)" ".NET 11 SDK leg (preview-sdk-canary, reaching its question)"
  mark T23 "$([ -f tests/DwarfMapper.Generator.Tests/Round31/NullabilityAttributeProbeTests.cs ] && has src/DwarfMapper.Generator/Core/MemberFacts.cs 'MaybeNullAttribute'; echo $?)" "nullability-attribute probe (A6 confirmed, fixed)"
  mark T24 "$([ -f tests/DwarfMapper.NegativeCases/Cases/DWARF113_UnionMemberNotMapped.cs ]; echo $?)" "C# 15 unions refused (DWARF113)"
  mark T25 "$([ -f tests/DwarfMapper.NegativeCases/Cases/DWARF114_ClosedHierarchyArmMissing.cs ]; echo $?)" "closed-hierarchy arm exhaustiveness (DWARF114)"
  mark T17 "$([ -f Issues/round31/FINDING-T17-ef-precompile.md ] && [ -f tests/DwarfMapper.NegativeCases/Cases/DWARF115_ProjectionNotPrecompilable.cs ]; echo $?)" "EF precompile measured; DWARF115"
  mark T18 "$([ -f Issues/round31/FINDING-T18-aot-size.md ]; echo $?)" "NativeAOT size measured"
  rem=$(grep -rnE 'Assert\.(Contains|DoesNotContain)\("[^"]*__' tests/DwarfMapper.Generator.Tests/Coverage 2>/dev/null | grep -v NormalizeLocals | wc -l | tr -d ' ')
  mark T28 "$(has tests/DwarfMapper.Generator.Tests/GeneratorAssert.cs 'NormalizeLocals' && [ "$rem" -le 19 ]; echo $?)" "raw __ assertions left: $rem (<= 19 kept on purpose: they pin a helper family's NAME)"
  nocov=$(for f in tests/DwarfMapper.Generator.Tests/Coverage/*.cs; do head -15 "$f" | grep -q '^// Covers:' || echo "$f"; done | wc -l | tr -d ' ')
  mark T29 "$([ "$nocov" -eq 0 ]; echo $?)" "coverage files without // Covers: header: $nocov"
  mark T30 "$(grep -q '1.10 Where DwarfMapper refuses and AutoMapper did not' docs/MIGRATION.md; echo $?)" "AutoMapper migration guide (MIGRATION.md section 1, extended in place)"
  echo "(T15, T21[H], T26, T27 are decisions — recorded in Issues/round31/TASK-LOG.md, not grepped)" ;;

tests)
  export PATH="$HOME/dotnet:$PATH"
  for proj in tests/DwarfMapper.Generator.Tests tests/DwarfMapper.IntegrationTests; do
    [ -d "$proj/Round31" ] || { echo "$proj: no Round31 tests yet"; continue; }
    echo "== $proj =="
    dotnet test "$proj" -c Release --no-build --nologo --filter "FullyQualifiedName~.Round31." 2>&1 | grep -E "Passed!|Failed!|Úspěšné!|Neúspěšné!|\[FAIL\]"
  done ;;

*) echo "usage: $0 baseline|static|tests"; exit 2 ;;
esac
