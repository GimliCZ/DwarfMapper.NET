#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
# Every validation case x every library, one build each. One CELL line per pair:
#   CELL <case> <lib> | build=<exit> | <DWARF/RMG diagnostics> | <runtime result, or why there is no build>
# From a checkout, on the pinned SDK (global.json): bash benchmarks/comparison-probes/validation/run.sh
set -u
cd "$(dirname "$0")" || exit 1
root=../../..
mkdir -p obj/cells
echo "SDK $(dotnet --version); Mapperly $(grep -o 'Riok.Mapperly" Version="[^"]*' $root/Directory.Packages.props | cut -d'"' -f3); Mapster $(grep -o 'Include="Mapster" Version="[^"]*' $root/Directory.Packages.props | cut -d'"' -f4)"
dotnet restore -p:RestoreLockedMode=false > obj/restore.log 2>&1 || { echo "restore failed"; tail -5 obj/restore.log; exit 1; }
for c in UNMAPPED_TARGET UNMAPPED_SOURCE TYPE_MISMATCH NULL_INTO_NONNULL ENUM_MEMBER_MISSING READONLY_TARGET CTOR_PARAM_NO_SOURCE CONVENTION_FLATTEN; do
  for l in DWARF MAPPERLY MAPSTER MAPSTER_STRICT; do
    o="obj/cells/$c-$l"
    dotnet build -c Release --no-restore "-p:DefineConstants=C_$c%3BL_$l" -o "$o" > obj/b.log 2>&1; rc=$?
    diags=$(grep -oE "(error|warning) (DWARF|RMG)[0-9]+: [^[]*" obj/b.log | sed 's/ *$//' | sort -u | cut -c1-170 | head -3 | tr '\n' ';')
    if [ $rc -eq 0 ]; then
      rt=$(dotnet "$o/probe-validation.dll" 2>&1 | grep PROBE-RUNTIME | cut -c15-220)
    else
      rt="(no build: $(grep -cE ' error ' obj/b.log) errors; first non-DWARF/RMG: $(grep -E ' error ' obj/b.log | grep -vE 'error (DWARF|RMG)' | head -1 | grep -oE 'error [A-Z]+[0-9]+: .{0,90}'))"
    fi
    echo "CELL $c $l | build=$rc | ${diags:-no DWARF/RMG diagnostic} | $rt"
  done
done
