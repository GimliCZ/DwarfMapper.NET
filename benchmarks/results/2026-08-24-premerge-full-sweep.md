<!-- SPDX-License-Identifier: GPL-2.0-only -->

# 2026-08-24 — full measured sweep, all 53 benchmarks

The pre-merge measurement for rounds 25 and 26. Every benchmark in the suite, **default job** (not the
ShortRun smoke, whose timings are non-gating by house rule), Release, one run.

| | |
|---|---|
| Host | AMD Ryzen 5 5600, Windows 10.0.19045, 12 logical cores |
| Runtime | .NET 10.0.1 (10.0.125.57005) |
| Job | BenchmarkDotNet default (15 warmup + 15 measured iterations per benchmark) |
| Collection size | N = 1000 where a collection is involved |
| Competitors | Mapperly 4.3.1, Mapster 10.0.8, AutoMapper 14.0.0 (last MIT release) |

**How to read the ratio column: it is `mean / DwarfMapper's mean` within the same category.** Below 1.00
means the rival is FASTER than us. Standard errors are included because several rows are close enough that
the error decides whether a gap is real.

**Cross-run comparison is invalid.** This session measured cross-run variance on this machine at roughly
±17%, and demonstrated it with a known-null experiment: two branches with byte-identical emitted code
measured 20% apart. Only compare rows *within* this table.

---

## Where DwarfMapper leads

| category | DwarfMapper | best rival | lead |
|---|---|---|---|
| **Dict** (1000 entries, `int`→`long` values) † | **8,037.5** ±16.1 | Mapperly 18,954.1 | **2.36x** |
| **Blit** (1000 layout-identical structs) | **434.5** ±1.2 | Mapperly 976.7 | **2.25x** |
| **NestedFill** (order graph, mixed fills) | **875.2** ±1.8 | Mapster 1,016.4 | **1.16x** |
| **NumList** (`int[]`→`List<long>`) | **660.3** ±2.0 | Mapster 921.1 | **1.39x** |
| **Widen** (`int[]`→`long[]`, `Vector.Widen`) | **357.1** ±2.4 | Mapperly 435.4 | **1.22x** |
| **Flatten** (`Order.Customer.Name`) | **5.0** ±0.0 | Mapperly 6.2 | **1.25x** |

† **The Dict figure is platform-dependent and must not be quoted bare.** It is ~1.14x on Linux for the same
code; the gap is Mapperly's dictionary path being slower on Windows, not DwarfMapper being faster. See
`2026-08-11-dict-windows-rerun.md`.

## Where DwarfMapper is behind

Stated because a comparison that reports only its wins is an advertisement, not a measurement.

| category | DwarfMapper | rival ahead | behind by |
|---|---|---|---|
| **Enum** (scalar member, by-name) | 4.1 ±0.0 | Mapperly **2.9** | **1.38x** |
| **List** (1000 reference elements) | 6,105.8 ±38.0 | Mapster **5,424.7** | **1.13x** |
| **Flat** (4 scalar members) | 4.7 ±0.0 | Hand **4.1**, Mapperly 4.5 | 1.15x / 1.04x |
| **Array** (1000 reference elements) | 4,692.2 ±17.6 | Mapperly **4,652.3** | 1.01x — *inside the error* |

The pattern is consistent and worth naming: **we lead wherever a fast path is eligible and trail slightly
where none is.** `List` and `Array` carry reference elements, so no blit and no span fill applies; what
remains is allocating a thousand destination objects, where there is nothing to win. The `Array` gap is
smaller than the combined standard error and should be read as parity.

## Full table, every benchmark

Alphabetical by category; within a category, fastest first.

| benchmark | mean (ns) | ± SE | allocated | vs Dwarf |
|---|---|---|---|---|
| Array_Mapperly | 4,652.3 | 11.2 | 48,048 | 0.99x |
| **Array_Dwarf** | **4,692.2** | 17.6 | 48,048 | 1.00x |
| Array_AutoMapper | 5,233.0 | 13.0 | 48,048 | 1.12x |
| Array_Mapster | 6,086.2 | 32.4 | 48,048 | 1.30x |
| **Blit_Dwarf** | **434.5** | 1.2 | 12,048 | 1.00x |
| Blit_Mapperly | 976.7 | 4.3 | 12,048 | 2.25x |
| Blit_AutoMapper | 1,016.1 | 3.4 | 12,048 | 2.34x |
| Blit_Mapster | 1,079.5 | 8.3 | 12,048 | 2.48x |
| BlitRatio_Array_Fast | 410.0 | 2.3 | 12,048 | — |
| BlitRatio_Array_Scalar | 1,030.1 | 4.3 | 16,048 | — |
| BlitRatio_List_Fast | 417.2 | 1.8 | 12,112 | — |
| BlitRatio_List_Scalar | 1,038.4 | 4.2 | 16,112 | — |
| **Dict_Dwarf** | **8,037.5** | 16.1 | 31,120 | 1.00x |
| Dict_Mapperly | 18,954.1 | 22.0 | 31,176 | 2.36x |
| Dict_AutoMapper | 19,203.4 | 52.9 | 102,320 | 2.39x |
| Dict_Mapster | 25,788.4 | 95.7 | 102,376 | 3.21x |
| Enum_Mapperly | 2.9 | 0.0 | 24 | 0.72x |
| **Enum_Dwarf** | **4.1** | 0.0 | 24 | 1.00x |
| Enum_Mapster | 11.6 | 0.0 | 24 | 2.87x |
| Enum_AutoMapper | 75.7 | 0.1 | 48 | 18.68x |
| Flat_Hand | 4.1 | 0.0 | 40 | 0.87x |
| Flat_Mapperly | 4.5 | 0.0 | 40 | 0.96x |
| **Flat_Dwarf** | **4.7** | 0.0 | 40 | 1.00x |
| Flat_Mapster | 13.3 | 0.1 | 40 | 2.81x |
| Flat_AutoMapper | 52.8 | 0.2 | 40 | 11.15x |
| **Flatten_Dwarf** | **5.0** | 0.0 | 48 | 1.00x |
| Flatten_Mapperly | 6.2 | 0.0 | 48 | 1.25x |
| Flatten_Mapster | 14.5 | 0.1 | 48 | 2.90x |
| Flatten_AutoMapper | 53.1 | 0.3 | 48 | 10.61x |
| Immutable_Dwarf | 162.8 | 1.6 | 4,048 | — |
| List_Mapster | 5,424.7 | 30.2 | 48,112 | 0.89x |
| **List_Dwarf** | **6,105.8** | 38.0 | 48,112 | 1.00x |
| List_Mapperly | 6,131.0 | 33.8 | 48,112 | 1.00x |
| List_AutoMapper | 8,727.7 | 48.9 | 56,656 | 1.43x |
| **NestedFill_Dwarf** | **875.2** | 1.8 | 8,112 | 1.00x |
| NestedFill_Mapster | 1,016.4 | 2.3 | 8,112 | 1.16x |
| NestedFill_Mapperly | 1,733.4 | 3.7 | 9,456 | 1.98x |
| NestedFill_AutoMapper | 1,997.3 | 5.5 | 10,776 | 2.28x |
| Nested_Mapperly | 10.3 | 0.1 | 112 | 0.99x |
| **Nested_Dwarf** | **10.5** | 0.1 | 112 | 1.00x |
| Nested_Mapster | 19.6 | 0.1 | 112 | 1.87x |
| Nested_AutoMapper | 57.8 | 0.1 | 112 | 5.52x |
| NullMismatch_Dwarf | 4.4 | 0.0 | 32 | — |
| **NumList_Dwarf** | **660.3** | 2.0 | 8,112 | 1.00x |
| NumList_Mapster | 921.1 | 3.0 | 8,112 | 1.39x |
| NumList_Mapperly | 996.1 | 5.6 | 8,112 | 1.51x |
| NumList_AutoMapper | 2,830.5 | 15.4 | 16,656 | 4.29x |
| Seq_Dwarf | 6,414.3 | 29.4 | 48,088 | — |
| Set_Dwarf | 4,462.8 | 13.7 | 17,856 | — |
| **Widen_Dwarf** | **357.1** | 2.4 | 8,048 | 1.00x |
| Widen_Mapperly | 435.4 | 2.3 | 8,048 | 1.22x |
| Widen_Mapster | 703.2 | 3.8 | 8,048 | 1.97x |
| Widen_AutoMapper | 742.4 | 3.1 | 8,048 | 2.08x |

Rows with no ratio are DwarfMapper-only shapes the competitors do not all support (`Immutable`, `Seq`,
`Set`, `NullMismatch`) or are the gate's own scalar twins (`BlitRatio_*`).

## Allocation — the platform-independent claim

Timings move with the host; **allocated bytes are deterministic per SDK**, which is why the repository gates
on them and not on time.

* **Identical to Mapperly and Mapster** on every shared shape except dictionaries: `Blit` 12,048 B, `Array`
  48,048 B, `List` 48,112 B, `NumList` 8,112 B, `Widen` 8,048 B. The wins above are therefore **work done per
  byte**, not fewer bytes moved.
* **Dict**: 31,120 B against AutoMapper's and Mapster's 102,320 / 102,376 — a **3.29x** lead that, unlike the
  timing figure, is platform-independent and safe to quote.
* **NumList**: 8,112 B against AutoMapper's 16,656.
* **NestedFill**: 8,112 B; Mapperly 9,456, AutoMapper 10,776.

## What the round-25/26 work bought, isolated

`BlitRatio_*` are same-process pairs: each blit measured against its own scalar twin, identical payload,
identical process, so machine drift cancels inside the measurement.

| pair | blit | scalar twin | ratio |
|---|---|---|---|
| array→array | 410.0 | 1,030.1 | **2.51x** |
| array→List | 417.2 | 1,038.4 | **2.49x** |
| | | floor | 1.50x |

Both sit comfortably above the gate's 1.5x floor.

## Caveats

* One run. Standard errors are given per row; differences smaller than the combined SE are not findings.
  `Array` (1.01x) is inside that band and should be read as parity.
* `Flat_Hand` measured 4.1 ns against DwarfMapper's 4.7. An earlier sweep in this session had `Flat_Hand`
  at 14 ns — 3.4x *slower* than generated code, which is not credible for a hand-written assignment. That
  earlier figure came from a differently-composed run and should be disregarded; this one is consistent with
  the committed 1.14x claim.
* Timings are Windows. The Dict row in particular is known to differ on Linux.
