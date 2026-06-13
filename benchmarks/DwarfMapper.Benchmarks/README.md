<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper.Benchmarks

In-repo [BenchmarkDotNet](https://benchmarkdotnet.org/) suite measuring DwarfMapper's
compile-time-generated mappers against a hand-written baseline across the core scenarios:

| Benchmark | Scenario |
|---|---|
| `Flat_HandWritten` (baseline) | hand-written flat object copy |
| `Flat_DwarfMapper` | generated flat mapper — should match hand-written |
| `Nested_DwarfMapper` | nested object graph (auto-synthesized sub-mapper) |
| `Array_DwarfMapper` | `FlatSrc[]` → `FlatDst[]` element-loop mapping |
| `Blit_DwarfMapper` | `Vec3Src[]` → `Vec3Dst[]` — the SIMD reinterpret blit fast-path |

## Running

Benchmarks require a **Release** build and are run from this directory:

```bash
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks
```

Useful filters/jobs:

```bash
# A single scenario
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks -- --filter "*Blit*"

# A fast smoke run (1 iteration; not for real numbers)
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks -- --job short
```

The suite is part of the solution so it always stays compiling, but CI only **builds** it —
it does not run benchmarks (timing is environment-sensitive). Run locally on a quiet machine for
meaningful numbers.

> Competitor comparisons (Mapperly / Mapster / AutoMapper) are intentionally omitted for now to keep
> the suite dependency-light and the baseline unambiguous (DwarfMapper vs. hand-written). They can be
> added as a separate benchmark class when desired.
