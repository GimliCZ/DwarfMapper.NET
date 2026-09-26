#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
# Round-31 audit for DwarfMapper.NET. Run from the repository root.
#   ./round31-audit.sh baseline   record counts the other checks compare against ($HOME/.round31-baseline)
#   ./round31-audit.sh static     per-task completion checks, no build (seconds)
#   ./round31-audit.sh tests      run the round-31 acceptance tests (needs a prior Release build)
set -uo pipefail
BASE="$HOME/.round31-baseline"
[ -d src/DwarfMapper.Generator ] || { echo "run from the DwarfMapper.NET repository root"; exit 2; }

wide_count() { python3 - <<'PY'
import re,glob
n=0
for p in glob.glob('src/DwarfMapper.Generator/Pipeline/**/*.cs',recursive=True):
    s=open(p).read()
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
    txt=open(p).read()
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
  mark T08 "$([ -f src/DwarfMapper.Generator/Pipeline/ExtractionContext.cs ] && [ "$w" -lt "${wide:-0}" ]; echo $?)" "ExtractionContext exists; methods >6 params: $w (baseline ${wide:-?})"
  mark T09 "$(grep -A40 'foreach (var r in collectionRegs)' src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs | grep -q 'TryGetNonEnumeratedCount' && grep -A40 'foreach (var r in collectionRegs)' src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs | grep -q 'CollectionsMarshal.AsSpan'; echo $?)" "registry collection lambdas pre-size + span fast path"
  mark T10 "$(has src/DwarfMapper.Generator/Pipeline/MapEmitter.cs '__dwarf_proj_'; echo $?)" "projection trees hoisted into static fields"
  mark T11 "$(has src/DwarfMapper/DwarfMapperRegistry.cs 'RegisterMany' && has src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs 'RegisterMany' && has src/DwarfMapper/PublicAPI.Unshipped.txt 'RegisterMany'; echo $?)" "batch registration (runtime + emission + PublicAPI)"
  mark T12 "$([ -f src/DwarfMapper/ExactPairSlot.cs ] && has src/DwarfMapper/IDwarfMapper.cs 'ExactPairSlot<' && has src/DwarfMapper/DwarfMapperRegistry.cs '_version'; echo $?)" "exact-pair slot + registry version"
  mark T13 "$(has src/DwarfMapper.Generator/Pipeline/MapEmitter.cs 'EnumerableQuery<' && [ -f tests/DwarfMapper.Generator.Tests/Round31/TreeOnlyQueryable.cs ]; echo $?)" "in-memory projection route + parity oracle"
  mark T14 "$(has src/DwarfMapper '(class|struct) Dispatch<'; echo $?)" "static per-destination dispatch (Opus-designed)"
  mark T16 "$(has src/DwarfMapper.Generator/Pipeline/MapEmitter.cs 'Expression<global::System.Func<[^>]*>> [^=]*=> __dwarf_proj_'; echo $?)" "projection expression exposed"
  mark T19 "$(grep -q 'Category!=Perf' .github/workflows/ci.yml; echo $?)" "Perf category excluded from default lane"
  hr_jobs=$(python3 - <<'PY'
import re,glob
bad=0
for p in glob.glob('.github/workflows/*.yml'):
    s=open(p).read()
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
  mark T22 "$(grep -q 'sdk-next' .github/workflows/ci.yml; echo $?)" ".NET 11 SDK leg"
  mark T23 "$([ -f tests/DwarfMapper.Generator.Tests/Round31/NullabilityAttributeProbeTests.cs ]; echo $?)" "nullability-attribute probe"
  rem=$(grep -rnE 'Assert\.(Contains|DoesNotContain)\("[^"]*__' tests/DwarfMapper.Generator.Tests/Coverage 2>/dev/null | grep -v NormalizeLocals | wc -l | tr -d ' ')
  mark T28 "$([ "$rem" -eq 0 ]; echo $?)" "assertions on raw __ locals left: $rem (optional task)"
  nocov=$(for f in tests/DwarfMapper.Generator.Tests/Coverage/*.cs; do head -15 "$f" | grep -q '^// Covers:' || echo "$f"; done | wc -l | tr -d ' ')
  mark T29 "$([ "$nocov" -eq 0 ]; echo $?)" "coverage files without // Covers: header: $nocov"
  mark T30 "$([ -f docs/MIGRATION-from-AutoMapper.md ]; echo $?)" "AutoMapper migration guide"
  echo "(T15, T17, T18, T24–T27 are Opus/human tasks — reviewed, not grepped)" ;;

tests)
  export PATH="$HOME/dotnet:$PATH"
  for proj in tests/DwarfMapper.Generator.Tests tests/DwarfMapper.IntegrationTests; do
    [ -d "$proj/Round31" ] || { echo "$proj: no Round31 tests yet"; continue; }
    echo "== $proj =="
    dotnet test "$proj" -c Release --no-build --nologo --filter "FullyQualifiedName~.Round31." 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]"
  done ;;

*) echo "usage: $0 baseline|static|tests"; exit 2 ;;
esac
