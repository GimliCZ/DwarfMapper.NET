<!-- SPDX-License-Identifier: GPL-2.0-only -->

# DwarfMapper.Benchmarks

In-repo [BenchmarkDotNet](https://benchmarkdotnet.org/) suite measuring DwarfMapper's
compile-time-generated mappers against a hand-written baseline **and** against Mapperly,
Mapster, and AutoMapper. `MapperBenchmarks` (in `Program.cs`) holds the comparison: each category is a
`[BenchmarkCategory]` with a `<Scenario>_Dwarf` method and, where the peer can express the shape, `_Mapperly`,
`_Mapster` and `_AutoMapper`:

| Category      | Scenario                                                                   | Peers            |
|---------------|----------------------------------------------------------------------------|------------------|
| `Flat`        | flat object copy (`Flat_Hand` is the hand-written baseline)                | all three        |
| `Nested`      | nested object graph (auto-synthesized sub-mapper)                          | all three        |
| `Array`       | `FlatSrc[]` → `FlatDst[]` element-loop mapping                             | all three        |
| `List`        | `List<FlatSrc>` → `List<FlatDst>` mapping                                  | all three        |
| `NumList`     | `int[]` → `List<long>`, a value-element list with widening                 | all three        |
| `NestedFill`  | a nested graph with mixed collection fills                                 | all three        |
| `Blit`        | an array of blittable structs — the proof-gated bulk copy                  | all three        |
| `Widen`       | same-category numeric widening conversion                                  | all three        |
| `Flatten`     | `[Flatten]` sub-member pull-up                                             | all three        |
| `EnumByName`  | enum-to-enum by name                                                       | Mapperly, AutoMapper |
| `EnumByValue` | enum-to-enum by value                                                      | Mapperly, Mapster |
| `Dict`        | `Dictionary<K,V>` → `Dictionary<K2,V2>` mapping                            | all three        |
| `Seq`, `Set`, `Immutable`, `NullMismatch` | other collection and null shapes, DwarfMapper only | —     |
| `BlitRatio`, `SpanBlit` | the bulk copy against the element loop it replaces                  | —                |
| `MapShare`, `AutoShare`, `DenseEnum` | a shared reference, or an inline array, against the copy it avoids | — |

Filter a single category with its name, e.g. `--filter "*Blit*"` or `--filter "*Flat_*"`.

The other classes measure one feature or answer one question each: the ambient facade and its registration scan
(`AmbientFacadeBenchmarks`, `AmbientScanBenchmarks`), registry collection shapes (`RegistryCollectionBenchmarks`),
the collection size sweep (`CollectionSweep`), projection and registration start-up (`Round31Benchmarks`), the
in-memory projection twin (`Round31T15Benchmarks`), widening into lists and spans (`Round33WidenListBenchmarks`,
`Round35SpanWidenBenchmarks`), and probes behind specific results (`CollectionReadProbe`, `FusionProbe`,
`NullCheckProbe`, `Round33ArrayLoopProbe`). The class names stay as the result documents in
[`../results`](../results) quote them.

C# 15 union mapping has a suite of its own, [`../DwarfMapper.UnionBenchmarks`](../DwarfMapper.UnionBenchmarks): it
needs the .NET 11 SDK, so it is not in the solution, which builds on the pinned SDK 10.

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

The suite is part of the solution so it always stays compiling. CI never gates on timings (they are
environment-sensitive), but the nightly run executes a short smoke pass and fails on any change in allocated bytes:
every DwarfMapper scenario is pinned exactly in `allocation-baseline.json` (`scripts/housekeeping.ps1 -BenchSmoke`).
Run locally on a quiet machine for meaningful timings.

## Competitor comparison & licensing

The suite compares DwarfMapper against three peers — all **benchmark-only** dependencies, never
referenced by the shipped library or generator:

| Mapper         | Version         | License | Notes                                  |
|----------------|-----------------|---------|----------------------------------------|
| Riok.Mapperly  | 4.3.1           | MIT     | Roslyn source-gen peer (AOT-safe)      |
| Mapster        | 10.0.14         | MIT     | runtime/expression mode (not AOT-safe) |
| **AutoMapper** | **14.0.0 only** | **MIT** | see below                              |

### AutoMapper is deliberately pinned to **14.0.0** (the last MIT release)

AutoMapper **v15.0+ relicensed to RPL-1.5 + a commercial license** (with a license-key mechanism).
RPL-1.5 is **not GPL-compatible**, and we want zero risk to this GPL-2.0-only repository. So:

- We reference **only `AutoMapper` `14.0.0`** — the final **MIT**-licensed release (MIT is GPL-compatible).
- Only its classic, stable API is used (`MapperConfiguration` / `CreateMap` / `IMapper.Map<T>`); no
  v15+ API, no `LicenseKey`, nothing from the commercial editions.
- v14.0.0 carries three NuGet advisories (NU1903: GHSA-rvv3-g6hj-g44x / CVE-2026-32933, GHSA-6vhm-j9h7-f9gh,
  GHSA-gpwp-5797-mjff). Each is suppressed by its advisory URL in the projects that reference AutoMapper (this one,
  the differential tests and one consumer test fixture): in-memory POCO mapping, no untrusted input, never shipped.
  The library/generator never touch it, and a new advisory still fails the restore.

If you'd rather not have AutoMapper at all, delete the `AutoMapper` `PackageReference` and the
`*_AutoMapper` benchmark methods — the rest of the suite is unaffected.
