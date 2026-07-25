# Benchmark results — realistic payloads, side-by-side (2026-07-24, Windows)

Measured on `bench/realistic-payloads`. Payloads are drawn from `ObjectFactoryV2` — the same fixture/fuzz source
the correctness suites use — instead of the hand-built uniform literals the suite used before. Every mapper in a
category receives the **identical payload instance** from one shared field.

Reproduce:

```
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks -- --filter '*'
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks -- --anyCategories Flat   # now actually filters
```

```
BenchmarkDotNet v0.14.0
Windows 10 Pro 10.0.19045
Runtime=.NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
GC=Concurrent Workstation ; HardwareIntrinsics=AVX2 VectorSize=256
Job: DefaultJob
```

## Cross-library comparison (this run — the genuinely comparable part)

Every row in a category maps the same instance, so the four mappers are measured against exactly the same data.

| Category | DwarfMapper | Mapperly | Mapster | AutoMapper | Fastest |
|---|---:|---:|---:|---:|---|
| Flat (1 obj)      |   5,812 ps |   4,786 ps |  13,722 ps |  51,145 ps | Mapperly (within noise, see below) |
| Nested (1 obj)    |  11,353 ps |  11,226 ps |  20,862 ps |  59,456 ps | tie |
| Flatten (1 obj)   |   5,419 ps |   5,045 ps |  13,709 ps |  51,879 ps | tie |
| Enum (1 obj)      |   4,755 ps |   3,411 ps |  12,683 ps |  78,809 ps | Mapperly |
| Array (1000)      |   5,325 ns |   5,449 ns |   6,531 ns |   5,945 ns | **DwarfMapper** |
| List (1000)       |   6,183 ns |   5,933 ns |   5,830 ns |   9,058 ns | Mapster |
| Blit (1000)       | **533 ns** |   1,054 ns |   1,073 ns |   1,114 ns | **DwarfMapper (2.0x)** |
| Widen (1000)      | **377 ns** |     494 ns |     742 ns |     804 ns | **DwarfMapper (1.3x)** |
| Dict (1000) ⚠     |   8,876 ns |    11.8 ns |  27,547 ns |  27,549 ns | not comparable — see below |
| Seq (1000)        |   6,933 ns |        n/a |        n/a |        n/a | DwarfMapper-only shape |

Allocation is byte-identical across DwarfMapper / Mapperly / Mapster in every collection category (48,048 B for
Array, 48,112 B for List, 12,048 B for Blit, 8,048 B for Widen); AutoMapper allocates 56,656 B for List. So the
timing differences above are compute, not GC pressure — except Dict, below.

**The `Dict` row is still not like-for-like.** `DictSrc.M` and `DictDst.M` are both `Dictionary<string, int>`,
and Mapperly returns the *source dictionary by reference* — 104 B is the wrapper object alone, and 1,000 entries
cannot be copied in 104 bytes. DwarfMapper, Mapster and AutoMapper all genuinely copy; among those DwarfMapper is
~3.1x cheaper in both time and allocation (31,120 B vs ~102,340 B). Making this row comparable requires forcing a
copy for every mapper (e.g. a value-type change across the dictionary), which is not done yet.

## Side-by-side: uniform vs realistic payloads (DwarfMapper rows)

Same code, **different data distribution**. This is not a regression check — the payloads deliberately changed,
so a delta mixes the cost of nulls/boundary values with the fact that a null `string?` is less work to copy than
a real one.

| Category | uniform (baseline) | realistic (this run) | Δ |
|---|---:|---:|---:|
| Array (1000)   | 5,431 ns | 5,325 ns | −2.0% |
| Blit (1000)    |   528 ns |   533 ns | +0.9% |
| Dict (1000)    | 9,025 ns | 8,876 ns | −1.7% |
| Enum (1 obj)   |  4.61 ns |  4.76 ns | +3.1% |
| Flat (1 obj)   |  4.86 ns |  5.81 ns | +19.5% — see below |
| Flatten (1 obj)|  5.32 ns |  5.42 ns | +1.8% |
| List (1000)    | 6,572 ns | 6,183 ns | −5.9% |
| Nested (1 obj) | 11.69 ns | 11.35 ns | −2.9% |
| Seq (1000)     | 7,708 ns | 6,933 ns | −10.1% |
| Widen (1000)   |   404 ns |   377 ns | −6.7% |

Allocation is unchanged in every category, which is the expected result: the shapes and sizes are the same, only
the values differ.

### The Flat delta is measurement noise, not a null-handling cost

Worth stating plainly because it is the one row that looks like a regression. It is not, on the evidence:

- **The emitted code contains no null branch.** Generated `MapFlat` is
  `ThrowIfNull(s); return new FlatDst { Active = s.Active, Id = s.Id, Name = s.Name, Score = s.Score };` — a
  plain field copy. Making `Name` nullable did not cause DwarfMapper to emit any extra work, so nulls in the
  data cannot be paying for a branch that does not exist.
- **It does not reproduce stably.** An isolated re-run of the `Flat` category alone gave a ratio of **1.16**
  against hand-written where the full sweep gave **1.23**, and Mapperly's ratio moved 1.01 → 1.08 over the same
  two runs. StdDev on `Flat_Dwarf` was 0.28–0.40 ns on a ~5 ns measurement (6–7%).
- At ~5 ns per op, the measurement is dominated by code layout and run-to-run variance. The only structural
  difference from `Flat_Hand` is the `ArgumentNullException.ThrowIfNull(s)` guard, which hand-written code
  omits — and that is present in both runs, so it cannot explain a *change*.

Treat `Flat` as "DwarfMapper and Mapperly both sit within ~1.0–1.2x of hand-written, indistinguishable from each
other at this scale". Any real conclusion needs a dedicated low-noise run.

## Harness fix landed with this run

`Program.cs` called `BenchmarkRunner.Run<MapperBenchmarks>()` **without forwarding `args`**, so every
`--filter`, `--anyCategories` and `--job` switch was silently ignored and the full ~40-minute suite ran no
matter what was requested. There was no warning; a targeted re-measurement simply appeared to hang. Fixed by
passing `args`. This is why the isolated `Flat` re-check above was possible at all.

## What is still not measured

The correctness suites cross **32** depth-one shapes; the benchmarks measure **6**. Immutable collections, sets,
`Queue`/`Stack`, every dictionary variant but one, tuples, user generics, records, polymorphic dispatch and
`nullable_ref_mismatch` have no benchmark at all. That gap is now enforced rather than implicit:
`BenchmarkCoverageSelfValidationTests` requires every shape to be mapped to a benchmark category or exempted
with a reason, and adding a shape to `CombinatorialSchema` fails the test until someone decides which it is.

ISSUE-019 is why this matters: an unknown-count source into an array target allocated two buffers on every map
for the project's entire life, and no benchmark covered the shape.

## NativeAOT

Not re-run for this branch. `samples/DwarfMapper.AotBench` builds its own fixed payloads and is untouched by
these changes, so its numbers are unchanged from `2026-07-24-full-sweep-windows.md` (all correctness and
determinism checks passing).
