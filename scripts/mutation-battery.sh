#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
#
# Mutation battery — Phase 4.3 of the test-hardening programme (spec 2026-07-26).
#
# NON-DEFAULT LANE. Every mutant rebuilds the generator, so this is minutes rather than seconds. Run it before
# a release, or when changing a guard — not in the inner loop.
#
# WHY THIS EXISTS
# The suite proves the generator does the right thing. Nothing proved the SUITE fails when the generator does
# the wrong thing, and those are different claims — the second is the one that matters when a refactor lands.
# Until this script, every guard here was trusted on the strength of being green, which is precisely the
# evidence a vacuous test also provides.
#
# Each mutant is a defect this project actually shipped or narrowly avoided, so a SURVIVOR is not hypothetical:
# it names a behaviour that regressed once and that nothing would now notice.
#
# CATALOGUE INVARIANTS, enforced by this script since the round-27 repair. Each exists because it was
# violated silently for a long time:
#   * every entry APPLIES     -- a sed matching nothing is reported STALE and fails the run
#   * every entry COMPILES    -- a mutant that does not build is reported COMPILE-BREAK and fails the run
#   * the suite is GREEN first -- a red baseline marks every mutant "killed" and measures nothing
#
# What the repair found (2026-08-27, at cb14993): 9 of 33 entries were stale and 8 did not compile, so 16
# measured nothing while the run still printed a score. Six of the stale ones were broken by round 27's
# parameter bundling -- the anchors matched loose locals that had become record fields, e.g.
# `elementPairsOwedCoverage` -> `acc.ElementPairsOwedCoverage`, `explicitOnly` -> `options.ExplicitOnly`.
#
# IF YOU ADD AN ENTRY, note that `if (false)` is NOT a usable mutation form in this repository: the body
# becomes unreachable and CS0162 is an error here. Invert the condition instead -- it plants the defect
# without the unreachable branch. Two entries were blocked by analyzers rather than CS0162: CA1309 refuses
# a culture-sensitive comparison outright (M02, now via StringComparer.CurrentCulture) and CA1822/MA0140
# refuse a ternary that stops touching instance state or has identical arms (M07, now inverted).
#
# History and per-id evidence: Issues/round27/FINDING-mutation-battery-catalogue-rot.md
#
# Usage:  scripts/mutation-battery.sh [--full]
#           (default) run the named guard first, and only fall back to the whole suite if it passes
#           --full    always run the whole suite per mutant (slower; catches "killed by something else")
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

MODE="${1:-fast}"
TESTS="tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj"

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "FAIL: working tree is dirty. This script edits source in place and restores with git checkout," >&2
  echo "      which would destroy uncommitted work." >&2
  exit 1
fi

# EQUIVALENT MUTANTS — deliberately NOT catalogued, because no test can kill them and a permanent survivor
# would train everyone to ignore the report:
#   * ExampleCatalogue using GetExportedTypes() instead of GetTypes(). Catalogued as M33 and it SURVIVED:
#     every [DocExample] type is public, so the two calls return the same set and no test can tell them
#     apart. Rather than leave a permanent survivor, the equivalence is now enforced by
#     DocReconciliationTests.Every_declared_example_is_public — if a non-public example is ever added the
#     calls diverge and that test fails, which is the assertion the mutant was reaching for.
#   * update-into passing `false, false` for isPreserveMode/isSetNullMode instead of the real values. The
#     hardcoded literals contradict the six other call sites and were worth removing, but preserve threading
#     is driven by the class-level nested synthesis path, so the call site makes no observable difference.
#     Verified by mutating it back with UpdateIntoPreserveParityTests in place: still green.
#
# id | file | sed expression | guard (test filter) | what the mutation simulates
#
# The guard column is the POINT of the catalogue: it records which test is supposed to notice. A mutant killed
# by some other test still counts, but one whose named guard sleeps through it is worth knowing about.
MUTANTS=(
"M01|src/DwarfMapper.Generator/Pipeline/AmbientValidator.cs|s/new SortedSet<(string, string)>(OrdinalPair)/new SortedSet<(string, string)>()/g|DeterminismSourceScanTests|culture-sensitive ordering reaching emitted text"
"M02|src/DwarfMapper.Generator/Pipeline/AmbientValidator.cs|s/string.CompareOrdinal(a.Item1, b.Item1)/StringComparer.CurrentCulture.Compare(a.Item1, b.Item1)/|DeterminismSourceScanTests|the ordinal pair comparer quietly becoming culture-sensitive"
"M03|src/DwarfMapper.Generator/Core/StableHash.cs|s/h ^= c;/h ^= (uint)(c + 1);/|StableHashTests|a hash tweak silently renaming every generated helper"
"M04|src/DwarfMapper.Generator/Core/StableHash.cs|s/h \\*= Prime;/h *= Prime + 2u;/g|StableHashTests|the FNV prime drifting off the published constant"
"M05|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/options.NameConvention == 1$/options.NameConvention != 1/|ProjectionRuntimeParityTests|an option reaching the runtime resolver but not projection"
"M06|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Phases.cs|s/if (!string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))/if (string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))/|SelfMapDiagnosticTests|DWARF076 no longer flagging a same-type map"
"M07|src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs|s/SourceIsValueType ? \"src.GetValueOrDefault()\" : \"src\"/SourceIsValueType ? \"src\" : \"src.GetValueOrDefault()\"/|ValueTypeSourceCollectionTests|a value-type source collection emitting uncompilable code again"
"M08|src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs|s/Count = sourceIsValueType ? CountKind.None : count;/Count = count;/|ValueTypeSourceCollectionTests|src.Count emitted on a Nullable<T> source"
"M09|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (extra.HasNullSub)/if (!extra.HasNullSub)/|ProjectionRuntimeParityTests|NullSubstitute silently dropped by projection"
"M10|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/return extra.When is not null/return extra.When is null/|ProjectionRuntimeParityTests|When= silently dropped by projection"
"M11|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (options.SkipNullSourceMembers)$/if (!options.SkipNullSourceMembers)/|ProjectionRuntimeParityTests|SkipNullSourceMembers silently dropped by projection"
"M12|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (options.AllowNonPublic /if (!options.AllowNonPublic /|ProjectionRuntimeParityTests|a non-public source member reported as simply missing"
"M13|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (use is not null)/if (use is null)/|EndpointContractTests|Use= silently dropped by projection"
"M14|src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs|s/if (global::System.Threading.Interlocked.Exchange(ref __registered, 1) != 0) return;//|AutoValidateRuntimeTests|the run-once guard lost, re-registering and corrupting IsAmbiguous"
"M15|src/DwarfMapper.Generator/Pipeline/MapEmitter.cs|s/ArgumentNullException.ThrowIfNull/ArgumentNullException_DISABLED.ThrowIfNull/g|GeneratedCodeIsWarningFreeTests|the emitted null guard disappearing"
"M16|src/DwarfMapper.Generator/Pipeline/NumericConverter.cs|s/CreateChecked/CreateTruncating/g|NumericConversionTests|checked narrowing becoming a silent wrap"
"M17|src/DwarfMapper.Generator/Pipeline/ParsableConverter.cs|s/InvariantCulture/CurrentCulture/g|ParsableConversionTests|emitted parsing becoming culture-dependent"
"M18|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs|s/DiagnosticDescriptors.UnmappedMember/DiagnosticDescriptors.AmbiguousMatch/|DiagnosticTests|the completeness gate reporting the wrong diagnostic"
"M19|src/DwarfMapper.Generator/Pipeline/EnumConverter.cs|s/ArgumentOutOfRangeException/InvalidOperationException/g|EnumStringTests|string->enum failing with the wrong exception type"
"M20|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/needs a null decision/is fine actually/g|BacklogCTests|the nullable-to-non-nullable projection refusal losing its reason"
"M22|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (options.ExplicitOnly)/if (!options.ExplicitOnly)/|OptionContractTests|the mass-assignment trust boundary (AutoMatchMembers=false) not applying at projection"
"M23|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (!autoNest)/if (autoNest)/|OptionContractTests|projection auto-nesting despite AutoNest=false"
"M24|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (options.IgnoreObsolete)/if (!options.IgnoreObsolete)/|OptionContractTests|IgnoreObsoleteMembers silently dropped by projection"
"M25|src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs|s/if (explicitOnly)/if (!explicitOnly)/|OptionEndpointParityTests|the explicit-only trust boundary silently not applying to span/async element pairs"
"M27|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Phases.cs|/Source-side completeness for projection/,+3 s/policy.RequiredMapping == 1/policy.RequiredMapping != 1/|GeneratedDocsAreCurrentTests|RequiredMapping source coverage lost at projection"
"M26|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Phases.cs|s/foreach (var owed in acc.ElementPairsOwedCoverage)/foreach (var owed in new System.Collections.Generic.List<(ITypeSymbol Src, ITypeSymbol Tgt, LocationInfo? Loc, List<string> IgnoreSources)>())/|GeneratedDocsAreCurrentTests|RequiredMapping source coverage lost at the span and async-stream endpoints"
"M28|tests/DwarfMapper.Generator.Tests/Contracts/OptionCatalog.cs|s/v => !v.Equals(def)/v => true/|OptionContractTests|the derived enum probe collapsing to the DEFAULT value, making every enum cell measure nothing"
"M21|docs/diagnostics.md|s/\*\*Fix:\*\* disambiguate with/**Fix (optional):** disambiguate with/g|Scan8|an ERROR diagnostic downgrading its remedy to optional advice"
"M29|src/DwarfMapper.DocTooling/SnippetScanner.cs|s/l\[prefix.Length..\]/l.TrimStart()/|SnippetScannerTests|dedent flattening relative indentation instead of removing the common prefix"
"M30|src/DwarfMapper.DocTooling/SnippetScanner.cs|s/if (result.TryGetValue(region.Id, out var first))/if (false \&\& result.TryGetValue(region.Id, out var first))/|SnippetScannerTests|a duplicate snippet id silently resolving to whichever file was scanned first"
"M31|src/DwarfMapper.DocTooling/OptionTableRenderer.cs|s/prose.TryGetValue(p.Name, out var t) ? t : \"\"/prose.TryGetValue(p.Name, out var t) ? t : \"TBD\"/|DocsAreSnippetCurrentTests|a newly added option rendering placeholder prose instead of failing the build"
"M32|tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs|s/preceding?.StartsWith(ExemptMarker, StringComparison.Ordinal) != true/preceding?.StartsWith(ExemptMarker, StringComparison.Ordinal) == true/|DocFenceScanTests|the fence ratchet exemption test inverted, so genuine offenders go unrecorded"
"M34|src/DwarfMapper.DocTooling/DocSnippetInjector.cs|s/LongestBacktickRun(region.Body) + 1/System.Math.Min(LongestBacktickRun(region.Body) + 1, 3)/|DocPipelinePropertyTests|a snippet containing \`\`\` closing its own fence and leaking code into the document as prose"
)

echo "== mutation battery: ${#MUTANTS[@]} mutants (mode: $MODE) =="
echo

# BASELINE FIRST. Every verdict below is really "the test run failed", so a suite that is ALREADY red marks
# EVERY mutant killed and prints a perfectly vacuous 33/33. The score is only a measurement if the
# unmutated suite is green, so that is established before a single mutant is planted.
echo "== baseline: the unmutated suite must be green =="
if ! dotnet test "$TESTS" -c Debug --nologo >/dev/null 2>&1; then
  echo "FAIL: the unmutated suite is not green, so every mutant below would read as killed." >&2
  echo "      Fix the suite first; a battery run against a red tree measures nothing." >&2
  exit 1
fi
echo "   baseline green"
echo

SURVIVORS=(); STALE=(); BROKEN=(); KILLED=0

# Restores the mutated file AND any generated documentation.
#
# docs/ matters as much as src/ here, and this was found the hard way: the doc-currency tests REGENERATE
# docs/generated/*.md whenever the rendered content differs from the committed copy. Under a mutant, the
# rendered content reflects the DEFECT — so a battery run left a matrix on disk describing the mutated
# generator, and that file was then committed as if it described the real one. A harness that silently
# rewrites tracked artefacts is worse than no harness.
# The project a mutated file belongs to. Needed because a mutant that does not COMPILE is caught by the
# COMPILER, not by a test: the build fails, every test in the run fails with it, and the mutant reads as
# "killed by <guard>" for a guard that never got the chance to run. Seven catalogue entries sat in exactly
# that state undetected -- see Issues/round27/FINDING-mutation-battery-catalogue-rot.md. A non-compiling
# mutant is a CATALOGUE DEFECT and is reported as one.
project_for() {
  local d p
  d="$(dirname "$1")"
  while [[ "$d" != "." && "$d" != "/" && -n "$d" ]]; do
    p="$(ls "$d"/*.csproj 2>/dev/null | head -1)"
    if [[ -n "$p" ]]; then echo "$p"; return; fi
    d="$(dirname "$d")"
  done
  echo ""   # markdown and other non-compiled targets have no project above them
}

restore() {
  git checkout -- "$1" 2>/dev/null || true
  git checkout -- docs/ 2>/dev/null || true
}

for entry in "${MUTANTS[@]}"; do
  IFS='|' read -r ID FILE EXPR GUARD DESC <<< "$entry"
  printf -- "-- %s: %s\n" "$ID" "$DESC"

  sed -i "$EXPR" "$FILE" 2>/dev/null || true

  if git diff --quiet -- "$FILE"; then
    # A no-op mutation proves nothing. Counting it as killed would inflate the score with mutants that never
    # changed anything — the catalogue would rot into a green formality.
    echo "   STALE: expression matched nothing (source moved?)"
    STALE+=("$ID — $FILE")
    restore "$FILE"; echo; continue
  fi

  CSPROJ="$(project_for "$FILE")"
  if [[ -n "$CSPROJ" ]] && ! dotnet build "$CSPROJ" -c Debug --nologo -v q >/dev/null 2>&1; then
    echo "   COMPILE-BREAK: the mutant does not build — a CATALOGUE DEFECT, not a kill"
    BROKEN+=("$ID — $DESC")
    restore "$FILE"; echo; continue
  fi

  KILLED_BY=""
  if [[ "$MODE" != "--full" ]]; then
    if ! dotnet test "$TESTS" -c Debug --nologo --filter "FullyQualifiedName~$GUARD" >/dev/null 2>&1; then
      KILLED_BY="$GUARD"
    fi
  fi

  if [[ -z "$KILLED_BY" ]]; then
    # The named guard slept through it (or --full was requested): does ANYTHING catch it?
    if ! dotnet test "$TESTS" -c Debug --nologo >/dev/null 2>&1; then
      KILLED_BY="another test (named guard $GUARD did NOT fail)"
    fi
  fi

  if [[ -n "$KILLED_BY" ]]; then
    echo "   killed by $KILLED_BY"
    KILLED=$((KILLED + 1))
  else
    echo "   *** SURVIVED *** — the suite passes with this defect present"
    SURVIVORS+=("$ID ($DESC) — expected $GUARD to fail")
  fi

  restore "$FILE"; echo
done

# Belt and braces: a half-applied mutation left behind would read as a real regression later, and a
# doc regenerated under a mutant would be committed as fact.
git checkout -- src/ 2>/dev/null || true
git checkout -- docs/ 2>/dev/null || true

echo "== killed $KILLED/${#MUTANTS[@]} =="

if (( ${#STALE[@]} > 0 )); then
  echo
  echo "STALE mutants (the catalogue no longer matches the source):" >&2
  for s in "${STALE[@]}"; do echo "  - $s" >&2; done
  echo "  Update the expression, or drop the mutant if the behaviour is gone." >&2
fi

if (( ${#BROKEN[@]} > 0 )); then
  echo
  echo "COMPILE-BREAK mutants (the catalogue entry is defective, and NOTHING about the suite was tested):" >&2
  for b in "${BROKEN[@]}"; do echo "  - $b" >&2; done
  echo "  Rewrite the replacement so it COMPILES and still plants the defect the description names." >&2
  echo "  Note that 'if (false)' is not a usable form here: the body becomes unreachable and CS0162 is an" >&2
  echo "  error in this repository. Inverting the condition plants the defect without that problem." >&2
fi

if (( ${#SURVIVORS[@]} > 0 )); then
  echo
  echo "SURVIVING MUTANTS — each names a behaviour nothing asserts:" >&2
  for s in "${SURVIVORS[@]}"; do echo "  - $s" >&2; done
  echo >&2
  echo "Add the missing assertion, or record why the behaviour is genuinely untestable." >&2
fi

# Stale entries fail too: a catalogue that silently stops mutating is the mutation-testing equivalent of a
# vacuous test, and it degrades quietly rather than loudly.
(( ${#SURVIVORS[@]} == 0 && ${#STALE[@]} == 0 && ${#BROKEN[@]} == 0 )) || exit 1

echo "All mutants killed — every catalogued defect is caught by at least one guard."
