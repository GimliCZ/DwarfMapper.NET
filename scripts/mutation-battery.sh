#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
#
# Mutation battery — Phase 4.3 of the test-hardening programme (spec 2026-07-26).
#
# NON-DEFAULT LANE. Each mutant rebuilds the generator and runs a targeted slice, so this is minutes rather
# than seconds. Run it before a release or when changing a guard, not in the inner loop.
#
# WHY THIS EXISTS
# The suite proves the generator does the right thing. Nothing proved the SUITE fails when the generator does
# the wrong thing — and those are different claims. Every guard in this repo was, until now, trusted on the
# strength of being green, which is exactly the evidence a vacuous test also provides.
#
# Each mutation below is a real defect this project has actually shipped or narrowly avoided. A mutant that
# SURVIVES (all tests still pass) is a coverage hole with a name and an address: the mutation names the
# behaviour nothing is asserting.
#
# Usage:  scripts/mutation-battery.sh
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "FAIL: working tree is dirty. This script edits source in place and restores with git checkout," >&2
  echo "      which would destroy uncommitted work." >&2
  exit 1
fi

TESTS="tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj"
SURVIVORS=()
KILLED=0

# id | file | sed expression | the guard that should notice | what the mutation simulates
MUTANTS=(
"M1|src/DwarfMapper.Generator/Pipeline/AmbientValidator.cs|s/new SortedSet<(string, string)>(OrdinalPair)/new SortedSet<(string, string)>()/g|DeterminismSourceScanTests + CultureInvarianceFuzzTests|culture-sensitive ordering reaching emitted text"
"M2|src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs|s/nameConvention == 1$/false/|ProjectionRuntimeParityTests|an option reaching the runtime resolver but not projection"
"M3|src/DwarfMapper.Generator/Core/StableHash.cs|s/h ^= c;/h ^= (uint)(c + 1);/|StableHashTests|a hash tweak silently renaming every generated helper"
)

echo "== mutation battery: ${#MUTANTS[@]} mutants =="
echo

for entry in "${MUTANTS[@]}"; do
  IFS='|' read -r ID FILE EXPR GUARD DESC <<< "$entry"

  echo "-- $ID: $DESC"
  echo "   file:  $FILE"
  echo "   guard: $GUARD"

  if ! sed -i "$EXPR" "$FILE" 2>/dev/null; then
    echo "   SKIP: mutation could not be applied (source moved?)"
    git checkout -- "$FILE" 2>/dev/null || true
    echo
    continue
  fi

  if git diff --quiet -- "$FILE"; then
    # A no-op mutation is NOT a passing mutant — it proves nothing, and counting it as killed would inflate
    # the score with mutants that never changed anything.
    echo "   SKIP: expression matched nothing — the mutation catalogue is stale for this file."
    git checkout -- "$FILE" 2>/dev/null || true
    echo
    continue
  fi

  if dotnet test "$TESTS" -c Debug --nologo >/dev/null 2>&1; then
    echo "   *** SURVIVED *** — the suite passes with this defect present."
    SURVIVORS+=("$ID ($DESC) — expected $GUARD to fail")
  else
    echo "   killed"
    KILLED=$((KILLED + 1))
  fi

  git checkout -- "$FILE"
  echo
done

echo "== killed $KILLED/${#MUTANTS[@]} =="

if (( ${#SURVIVORS[@]} > 0 )); then
  echo
  echo "SURVIVING MUTANTS — each is a behaviour nothing asserts:" >&2
  for s in "${SURVIVORS[@]}"; do echo "  - $s" >&2; done
  echo >&2
  echo "Either add the missing assertion, or record why the behaviour is genuinely untestable." >&2
  exit 1
fi

# Restoring is not optional: a half-applied mutation left behind would look like a real regression later.
git checkout -- src/ 2>/dev/null || true
echo "All mutants killed — every catalogued defect is caught by at least one guard."
