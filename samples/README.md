<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Samples

| Project | What it is | How to run it |
|---|---|---|
| [`DwarfMapper.Gallery`](DwarfMapper.Gallery) | Start here. One runnable example per feature, grouped from basics to testing, with an [illustrated index](DwarfMapper.Gallery/README.md) whose code blocks are quoted from the examples. | `dotnet run --project samples/DwarfMapper.Gallery` |
| [`DwarfMapper.Unions`](DwarfMapper.Unions) | C# 15 union mapping. Needs the .NET 11 SDK (its own `global.json` selects it), so it is not in the solution. | `cd samples/DwarfMapper.Unions && dotnet run` |
| [`DwarfMapper.AotSample`](DwarfMapper.AotSample) | A NativeAOT app that publishes with warnings as errors and checks its mappings at run time; CI publishes and runs it on Linux and Windows as the AOT gate. | `dotnet publish samples/DwarfMapper.AotSample -c Release -r linux-x64` |
| [`DwarfMapper.AotBench`](DwarfMapper.AotBench) | A NativeAOT stability harness: SIMD widen and blit bit-exactness around the vector boundary, repeated runs, timing variance. Built with the solution; run it by hand. | `dotnet publish samples/DwarfMapper.AotBench -c Release -r linux-x64`, then run the binary |
| [`DwarfMapper.Conformance`](DwarfMapper.Conformance) | A self-checking program that maps a long list of feature cases and returns a non-zero exit code on any wrong result; CI runs it (`scripts/conformance-gate.sh`) and fails if the assertion count ever drops. It predates several newer features, so the Gallery is the complete tour. | `dotnet run --project samples/DwarfMapper.Conformance` |
