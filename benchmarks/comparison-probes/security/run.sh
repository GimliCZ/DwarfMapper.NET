#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
# The security cells, one PROCESS each (a stack overflow ends the process and cannot be caught), then each library's
# flat map published as NativeAOT. One CELL line per pair: CELL <lib> <case> | exit=<code> | <output>.
# From a checkout, on the pinned SDK (global.json), on Linux with clang and zlib1g-dev for the AOT step:
#   bash benchmarks/comparison-probes/security/run.sh
set -u
cd "$(dirname "$0")" || exit 1
mkdir -p obj
echo "SDK $(dotnet --version); ulimit -s $(ulimit -s) KB (the main thread's stack)"
dotnet build -c Release -p:RestoreLockedMode=false -o obj/probe > obj/b.log 2>&1 || { echo "build failed"; grep -E " error " obj/b.log | head -5; exit 1; }
cell() {
  local l=$1 c=$2 n=${3:-} s rc what
  s=$(date +%s); timeout 300 dotnet obj/probe/probe-security.dll "$l" "$c" $n > obj/o.log 2> obj/e.log; rc=$?
  what=$(grep -h PROBE-SECURITY obj/o.log | cut -c16-200); [ -n "$what" ] || what="stderr: $(head -c 160 obj/e.log | tr '\n' ' ')"
  echo "CELL $l $c ${n} | exit=$rc | $(( $(date +%s) - s )) s | $what"
}
for l in DWARF MAPPERLY MAPSTER; do
  cell $l FLAT
  cell $l DEEP 100000
  cell $l CYCLE
  for n in 1000 10000 100000; do cell $l REENTRY $n; done
done
echo "== NativeAOT publish (linux-x64)"
dotnet publish -c Release -r linux-x64 -p:ProbeAot=true -p:RestoreLockedMode=false -o obj/aot > obj/p.log 2>&1; rc=$?
echo "publish exit=$rc"
grep -E " error " obj/p.log | sed 's/ \[\/.*//' | sort -u | head -5 | cut -c1-260
grep -oE "warning (IL[0-9]+|DWARF[0-9]+|RMG[0-9]+): [^[]*" obj/p.log | sed 's/ *$//' | sort | uniq -c | sort -rn | cut -c1-220 | head -12
if [ $rc -eq 0 ]; then
  for l in DWARF MAPPERLY MAPSTER; do
    timeout 60 ./obj/aot/probe-security $l FLAT > obj/o.log 2> obj/e.log; rc=$?
    what=$(grep -h PROBE-SECURITY obj/o.log | cut -c16-200); [ -n "$what" ] || what="stderr: $(head -c 160 obj/e.log | tr '\n' ' ')"
    echo "CELL AOT $l FLAT | exit=$rc | $what"
  done
fi
