# Benchmark results — realistic payloads, comparable Dict + new coverage (2026-07-25, Windows)

> ## ⚠ CORRECTION (2026-07-26): the Dict headline below does not reproduce
>
> This file calls the `Dict` row "the headline" at a ~2× lead over Mapperly (`Dict_Mapperly` 20,050 ns vs
> `Dict_Dwarf` 9,964 ns). Four independent Linux re-measurements put that lead at **~1.14× (range 1.09–1.19)**.
> `Dict_Dwarf` reproduces almost exactly (10.10 µs on the quietest run); **`Dict_Mapperly` does not** — it
> measures 11.37 µs here against 20,050 ns there.
>
> A Mapperly version change, a benchmark change and a difference in work done were all ruled out (pinned
> `4.3.1` throughout; the fixture change and this file are the *same* commit; allocation is identical on both
> platforms, so both really do copy and convert). Whether the Windows figure was an outlier or a genuine
> Windows characteristic **cannot be decided without a Windows host**.
>
> **Do not cite the ~2× Dict figure.** See `2026-07-26-full-sweep-linux.md` § *Dict re-measurement*. Every
> other row in this file stands.

Supersedes the Dict caveat in `2026-07-24-realistic-payloads-side-by-side.md`. Same payload source
(`ObjectFactoryV2`, identical instance to every mapper in a category), with four changes since that run:

1. **Dict is now a value-changing map** (`Dictionary<string,int>` → `Dictionary<string,long>`), so Mapperly can
   no longer alias the source and every mapper genuinely copies+converts. The row is finally like-for-like.
2. **New coverage categories** (DwarfMapper-only): `Set` (int[]→HashSet), `Immutable` (int[]→ImmutableArray),
   `NullMismatch` (string?→string via NullSubstitute).
3. `BenchmarkRunner` now forwards `args`, so `--filter`/`--anyCategories`/`--job` actually work.
4. A dedicated low-noise Flat run (15 warmup / 30 iterations) settles the Flat ratio.

```
BenchmarkDotNet v0.14.0 ; Windows 10 Pro 10.0.19045
Runtime=.NET 10.0.1, X64 RyuJIT AVX2 ; Job: DefaultJob
```

## ⚠ Read absolutes WITHIN this run only

This sweep ran on a **busier machine** than the 2026-07-24 run: the `Flat_Hand` baseline is 6.2 ns here vs
4.7 ns then, ~30% slower across the board, and several rows carry large StdDev (Blit_Dwarf ±369 ns on 1,116).
**Do not compare absolute ns across the two files** — that delta is machine load, not code. The cross-library
*ratios within this run* are valid (every mapper measured under the same load, same instant), and allocation
bytes are load-independent.

## Cross-library comparison (this run)

| Category | DwarfMapper | Mapperly | Mapster | AutoMapper | Within-run leader |
|---|---:|---:|---:|---:|---|
| Flat (1 obj) — see low-noise below | 6.12 ns | 5.85 ns | 16.3 ns | 57.4 ns | Dwarf/Mapperly tie |
| Nested (1 obj)  | 12.96 ns | 13.41 ns | 24.1 ns | 63.1 ns | tie |
| Flatten (1 obj) | 7.35 ns | 8.12 ns | 19.9 ns | 53.3 ns | **Dwarf** |
| Enum (1 obj)    | 4.85 ns | 3.91 ns | 13.4 ns | 81.1 ns | Mapperly |
| Array (1000)    | 7,155 ns | 7,212 ns | 8,882 ns | 8,569 ns | **Dwarf** (tie Mapperly) |
| List (1000)     | 7,330 ns | 7,093 ns | 6,469 ns | 9,859 ns | Mapster |
| Blit (1000)     | **1,006 ns** (med) | 1,528 ns | 1,343 ns | 1,446 ns | **Dwarf (~1.5x)** |
| Widen (1000)    | **438 ns** | 511 ns | 689 ns | 728 ns | **Dwarf** |
| **Dict (1000)** | **9,964 ns / 31,120 B** | 20,050 ns / 31,176 B | 31,183 ns / 102,376 B | 23,689 ns / 102,320 B | **Dwarf (~2x)** |

**The Dict row is the headline.** With identical value types the old row showed Mapperly at 11.8 ns / 104 B —
because it returned the source dictionary by reference and copied nothing. Forcing an `int→long` value change
makes every mapper do the real work, and DwarfMapper is now **~2x faster than Mapperly** and **~2.4–3x faster
than Mapster/AutoMapper**, at ~30% of their allocation (31 KB vs ~102 KB). This is the first honest dictionary
number in the suite.

Allocation is byte-identical across Dwarf/Mapperly/Mapster in the array-family categories, so those timing gaps
are compute, not GC.

## Low-noise Flat (15 warmup / 30 iterations) — #5 settled

| Method | Mean | Ratio vs Hand |
|---|---:|---:|
| Flat_Hand | 4.415 ns | 1.00 |
| Flat_Dwarf | 5.018 ns | **1.14** |
| Flat_Mapperly | 4.828 ns | 1.09 |
| Flat_Mapster | 13.383 ns | 3.03 |
| Flat_AutoMapper | 50.959 ns | 11.55 |

The full-sweep run that first showed Flat_Dwarf at 1.23 was variance: the dedicated run gives **1.14**, with
DwarfMapper and Mapperly both within ~1.1x of hand-written and indistinguishable from each other at ~5 ns. The
only structural difference from hand-written is `ArgumentNullException.ThrowIfNull(s)`. Treat Flat as "as fast
as hand-written, within measurement noise". This is the number to quote, not a full-sweep Flat row.

## New coverage categories (DwarfMapper-only)

| Category | Shape | Mean | Allocated | Note |
|---|---|---:|---:|---|
| NullMismatch | string? → string via `NullSubstitute=""` | 5.22 ns | 32 B | real `s.Name ?? ""` branch; ~15% of draws are null |
| Set | int[] → HashSet<int> | 5,069 ns | 17,856 B | hashing + dedup — distinct profile from a linear fill |
| Immutable | int[] → ImmutableArray<int> | 242 ns | 4,048 B | builder + freeze into one buffer; cheap |

These have no competitor rows by design — they guard DwarfMapper's own emitted paths against regression, the
same role `Seq` plays for ISSUE-019. Benchmark shape coverage is now 9 of the 32 combinatorial shapes, enforced
by `BenchmarkCoverageSelfValidationTests` (floor raised 6→9). The remaining 23 shapes stay declared-and-exempted
with reasons.

### A note surfaced building NullMismatch
`string?` → `string` does NOT compile with default handling: DWARF070 (a warning) is escalated to a build error
by warnings-as-errors, so the mapper refuses to silently store a null and forces the author to pick
NullSubstitute / SkipNullSourceMembers / a nullable destination. That is the "never silent" contract working as
intended — the benchmark uses NullSubstitute, which is why it measures a real coalesce branch.

## NativeAOT

Not re-measured (unchanged sample). Two operational notes now codified in `scripts/run-aot-bench.ps1`:
- **Payloads stay hand-built by design.** `ObjectFactoryV2` is reflection-based (`Activator.CreateInstance`,
  `GetProperties`), which would defeat the AOT sample's reflection-free premise. Realistic payloads cannot be
  shared into the AOT sample without breaking what it exists to prove.
- **Stale-binary guard.** A failed `dotnet publish` (e.g. `vswhere.exe` off PATH → MSB3073/123) leaves the
  previous `publish/` in place, so running the exe reports month-old numbers with no error. The script clears
  `publish/` first, checks the publish exit code, and refuses to run a binary older than the publish it started.
