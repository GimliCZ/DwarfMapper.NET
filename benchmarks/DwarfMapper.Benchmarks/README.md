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

## Competitor comparison & licensing

The suite compares DwarfMapper against three peers — all **benchmark-only** dependencies, never
referenced by the shipped library or generator:

| Mapper | Version | License | Notes |
|---|---|---|---|
| Riok.Mapperly | 4.3.1 | MIT | Roslyn source-gen peer (AOT-safe) |
| Mapster | 10.0.8 | MIT | runtime/expression mode (not AOT-safe) |
| **AutoMapper** | **14.0.0 only** | **MIT** | see below |

### AutoMapper is deliberately pinned to **14.0.0** (the last MIT release)

AutoMapper **v15.0+ relicensed to RPL-1.5 + a commercial license** (with a license-key mechanism).
RPL-1.5 is **not GPL-compatible**, and we want zero risk to this GPL-2.0-only repository. So:

- We reference **only `AutoMapper` `14.0.0`** — the final **MIT**-licensed release (MIT is GPL-compatible).
- Only its classic, stable API is used (`MapperConfiguration` / `CreateMap` / `IMapper.Map<T>`); no
  v15+ API, no `LicenseKey`, nothing from the commercial editions.
- v14.0.0 carries a NuGet advisory (NU1903); it is suppressed **for this benchmark project only**
  (in-memory POCO mapping, no untrusted input, not shipped). The library/generator never touch it.

If you'd rather not have AutoMapper at all, delete the `AutoMapper` `PackageReference` and the
`*_AutoMapper` benchmark methods — the rest of the suite is unaffected.
