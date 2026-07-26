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

# id | file | sed expression | guard (test filter) | what the mutation simulates
#
# The guard column is the POINT of the catalogue: it records which test is supposed to notice. A mutant killed
# by some other test still counts, but one whose named guard sleeps through it is worth knowing about.
MUTANTS=(
"M01|src/DwarfMapper.Generator/Pipeline/AmbientValidator.cs|s/new SortedSet<(string, string)>(OrdinalPair)/new SortedSet<(string, string)>()/g|DeterminismSourceScanTests|culture-sensitive ordering reaching emitted text"
"M02|src/DwarfMapper.Generator/Pipeline/AmbientValidator.cs|s/string.CompareOrdinal(a.Item1, b.Item1)/string.Compare(a.Item1, b.Item1, StringComparison.CurrentCulture)/|DeterminismSourceScanTests|the ordinal pair comparer quietly becoming culture-sensitive"
"M03|src/DwarfMapper.Generator/Core/StableHash.cs|s/h ^= c;/h ^= (uint)(c + 1);/|StableHashTests|a hash tweak silently renaming every generated helper"
"M04|src/DwarfMapper.Generator/Core/StableHash.cs|s/h \\*= Prime;/h *= Prime + 2u;/|StableHashTests|the FNV prime drifting off the published constant"
"M05|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/nameConvention == 1$/false/|ProjectionRuntimeParityTests|an option reaching the runtime resolver but not projection"
"M06|src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs|s/if (!string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal)) continue;/if (true) continue;/|SelfMapDiagnosticTests|DWARF076 no longer flagging a same-type map"
"M07|src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs|s/SourceIsValueType ? \"src.GetValueOrDefault()\" : \"src\"/\"src\"/|ValueTypeSourceCollectionTests|a value-type source collection emitting uncompilable code again"
"M08|src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs|s/Count = sourceIsValueType ? CountKind.None : count;/Count = count;/|ValueTypeSourceCollectionTests|src.Count emitted on a Nullable<T> source"
"M09|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (extra.HasNullSub)/if (false)/|ProjectionRuntimeParityTests|NullSubstitute silently dropped by projection"
"M10|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (extra.When is not null)/if (false)/|ProjectionRuntimeParityTests|When= silently dropped by projection"
"M11|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (skipNullSourceMembers$/if (false/|ProjectionRuntimeParityTests|SkipNullSourceMembers silently dropped by projection"
"M12|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (allowNonPublic$/if (false/|ProjectionRuntimeParityTests|a non-public source member reported as simply missing"
"M13|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (use is not null)/if (false)/|EndpointContractTests|Use= silently dropped by projection"
"M14|src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs|s/if (global::System.Threading.Interlocked.Exchange(ref __registered, 1) != 0) return;//|AutoValidateRuntimeTests|the run-once guard lost, re-registering and corrupting IsAmbiguous"
"M15|src/DwarfMapper.Generator/Pipeline/MapEmitter.cs|s/ArgumentNullException.ThrowIfNull/ArgumentNullException_DISABLED.ThrowIfNull/g|GeneratedCodeIsWarningFreeTests|the emitted null guard disappearing"
"M16|src/DwarfMapper.Generator/Pipeline/NumericConverter.cs|s/CreateChecked/CreateTruncating/g|NumericConversionTests|checked narrowing becoming a silent wrap"
"M17|src/DwarfMapper.Generator/Pipeline/ParsableConverter.cs|s/InvariantCulture/CurrentCulture/g|ParsableConversionTests|emitted parsing becoming culture-dependent"
"M18|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.cs|s/DiagnosticDescriptors.UnmappedMember/DiagnosticDescriptors.AmbiguousMatch/|DiagnosticTests|the completeness gate reporting the wrong diagnostic"
"M19|src/DwarfMapper.Generator/Pipeline/EnumConverter.cs|s/ArgumentOutOfRangeException/InvalidOperationException/g|EnumStringTests|string->enum failing with the wrong exception type"
"M20|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/needs a null decision/is fine actually/|BacklogCTests|the nullable-to-non-nullable projection refusal losing its reason"
"M22|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (explicitOnly)/if (false)/|OptionContractTests|the mass-assignment trust boundary (AutoMatchMembers=false) not applying at projection"
"M23|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (!autoNest)/if (false)/|OptionContractTests|projection auto-nesting despite AutoNest=false"
"M24|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/if (ignoreObsolete)/if (false)/|OptionContractTests|IgnoreObsoleteMembers silently dropped by projection"
"M25|src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs|s/if (explicitOnly)\n                {\n                    diagnostics.Add(new DiagnosticInfo(\n                        DiagnosticDescriptors.ExplicitOnlyNotElementWise/if (false)\n                {\n                    diagnostics.Add(new DiagnosticInfo(\n                        DiagnosticDescriptors.ExplicitOnlyNotElementWise/|OptionEndpointParityTests|the explicit-only trust boundary silently not applying to span/async element pairs"
"M21|docs/diagnostics.md|s/\*\*Fix:\*\* disambiguate with/**Fix (optional):** disambiguate with/|Scan8|an ERROR diagnostic downgrading its remedy to optional advice"
)

echo "== mutation battery: ${#MUTANTS[@]} mutants (mode: $MODE) =="
echo

SURVIVORS=(); STALE=(); KILLED=0

restore() { git checkout -- "$1" 2>/dev/null || true; }

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

# Belt and braces: a half-applied mutation left behind would read as a real regression later.
git checkout -- src/ 2>/dev/null || true

echo "== killed $KILLED/${#MUTANTS[@]} =="

if (( ${#STALE[@]} > 0 )); then
  echo
  echo "STALE mutants (the catalogue no longer matches the source):" >&2
  for s in "${STALE[@]}"; do echo "  - $s" >&2; done
  echo "  Update the expression, or drop the mutant if the behaviour is gone." >&2
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
(( ${#SURVIVORS[@]} == 0 && ${#STALE[@]} == 0 )) || exit 1

echo "All mutants killed — every catalogued defect is caught by at least one guard."
