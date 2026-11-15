#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
# Round 35 T25: the by-value enum row's two generated methods, alone and with variants of their argument check, under
# BenchmarkDotNet; any further arguments go to BenchmarkDotNet (for example --disasm for the JIT's code).
# From a checkout: bash benchmarks/comparison-probes/enum-gap/run.sh
set -u
cd "$(dirname "$0")" || exit 1
dotnet restore -p:RestoreLockedMode=false > /dev/null || { echo "restore failed"; exit 1; }
dotnet run -c Release --no-restore -- --filter '*' "$@"
