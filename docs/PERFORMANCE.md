<!-- SPDX-License-Identifier: GPL-2.0-only -->
# What the struct-DTO numbers mean (and what they do not)

This page exists because round 29 produced the first genuinely large speed result in this repository, and
this project's position is **resilience first, speed second**. A large number reported carelessly costs more
than it buys: it has happened here before, and the retraction is documented below so it is not repeated.

Everything on this page is a *measurement of DTO shapes*, run to decide whether a feature was worth building.
Some of it describes what DwarfMapper now emits, some describes a change you would make to your own types,
and some describes an idea that was measured and **not** built. Those three are kept apart on purpose,
because merging them is exactly how a benchmark table becomes an advertisement.

---

## 1. How these were run, and why the ratios are the only stable part

BenchmarkDotNet 0.14.0 · AMD Ryzen 5 5600 (6 physical / 12 logical) · Windows 10 22H2 · .NET SDK 10.0.101 ·
.NET 10.0.11 X64 RyuJIT AVX2 · `Job=short`, **`IterationCount=3`**, `WarmupCount=3`, `LaunchCount=1`.

Raw output: [`Issues/round29/plan-results.md`](../Issues/round29/plan-results.md),
[`plan2-results.md`](../Issues/round29/plan2-results.md),
[`plan3-results.md`](../Issues/round29/plan3-results.md),
[`plan4-results.md`](../Issues/round29/plan4-results.md), with the reasoning in
[`RESEARCH-hardware-mode.md`](../Issues/round29/RESEARCH-hardware-mode.md). Every figure below cites one of
those; a figure that could not be traced to one has been deleted rather than rounded into place.

**Three iterations is not enough to publish an absolute number, and we are not publishing one.** The error
bars are wide enough that on several rows the reported error approaches or exceeds the mean:

| Row | Mean | Error | File |
|---|---:|---:|---|
| `Tree_Classes_FieldCopy`, N = 1,000 | 22.1 µs | **81.2 µs** | `plan-results.md` |
| `A_MapToClasses_ThenConsume`, N = 100,000 | 34.5 ms | **65.8 ms** | `plan3-results.md` |
| `Opt_Classes_To_Structs_Gather`, N = 100,000 | 1.59 ms | **3.48 ms** | `plan2-results.md` |
| `Tree_Classes_FieldCopy`, N = 100,000 | 20.0 ms | 18.7 ms | `plan-results.md` |

**The ratios are the stable part**, because both arms of a comparison run in the same process against the
same noise. At N = 100,000 the compared (non-baseline) rows report a `RatioSD` of **0.00–0.11** (the 0.11 is
`SpanCopy_Auto` in `plan4-results.md`; every row quoted on this page is at 0.08 or below), and a baseline's
own self-ratio reaches 0.13 (`A_MapToClasses_ThenConsume`). At N = 1,000 the spread is much worse and reaches
**0.26** (`Mixed_ColumnTranspose`), which is one reason the small-N rows are not used to decide anything here.

So: **an order of magnitude is a finding; a precise percentage is not.** If you quote "0.09×" from this page
and your machine gives you 0.14×, this page was wrong and your machine was right. What should reproduce is
the *direction* and the rough size — a class-per-element destination replaced by one struct array is worth
somewhere around 5–11× at 100,000 elements across the shapes measured here, not 1.2× and not 100×.

**These numbers describe one machine, one OS, x64, one runtime patch.** Nothing here has been reproduced on
Linux, on arm64, or on server GC. This matters more than it sounds: see §6.

---

## 2. What was measured, split by who has to do the work

Read the group headings before the numbers. All rows are N = 100,000 unless stated; ratios are against the
class-shaped baseline of the same category, so **lower is faster**.

### 2a. What DwarfMapper's shipped Phase 2 feature gives you

`DWARF103` names a collection element type that could be a `readonly record struct`, and the code fix
converts it — the element type plus every transfer model it inlines. The fix converts the **destination**
type. If your *source* is still a graph of classes, the number you get is the **gather** column; the **blit**
column needs the source to be structs too, which is a change on the other side of the mapping that the fix
does not make for you.

| Shape | Baseline (classes) | Gather (target converted) | Blit (both sides structs) | Allocated | Source |
|---|---:|---:|---:|---|---|
| 4-class DTO tree → nested structs | 20.0 ms | **1.80 ms** (0.09×) | 0.92 ms (0.05×) | 18.4 → 6.4 MB (0.35×) | `plan-results.md`, `D_tree` |
| DTO with an optional nested member | 14.6 ms | **1.59 ms** (0.11×) | 0.92 ms (0.06×) | 10.4 → 4.8 MB (0.46×) | `plan2-results.md`, `A_optional` |

At N = 1,000 the same two shapes are 0.30× (tree) and 0.30× (optional) for the gather — roughly 3.3× faster
rather than 9–11×. **The win grows with N** because what it removes is one heap object per element, and
allocation, zeroing and GC promotion are what scale.

`DWARF101` names a field order that packs a transfer-model struct. There is no code fix for it; you edit the
declaration. Measured on the exact shape in the message — `{bool, long, byte, double, short}` at 40 bytes
against the same five fields at 24:

| Shape | Padded | Packed | Allocated | Source |
|---|---:|---:|---|---|
| 40-byte struct vs 24-byte struct, blit | 812 µs | **500 µs** (0.62×) | 4.0 → 2.4 MB (0.60×) | `plan2-results.md`, `D_layout` |

(0.57× at N = 1,000, from the same category.) Reorder **both** sides of a pair or the block copy is lost —
`DWARF101` reports both for that reason.

### 2b. Changes to your own types that DwarfMapper does not make

These were measured to decide what to build next. **DwarfMapper emits none of them today.** They are here
because they were the largest results in the study and hiding them would misrepresent where the wins in §2a
sit in the landscape.

| Shape | Baseline (classes) | Measured variant | Allocated | Source |
|---|---:|---:|---|---|
| Order lines → one shared line array + `(offset, count)` ranges | 51.6 ms | 8.72 ms (0.17×) | 29.6 → 9.6 MB (0.32×) | `plan3-results.md`, `B_arena` |
| `Dictionary<enum,int>` per element → enum-indexed inline array | 47.0 ms | 3.08 ms (0.07×) | 31.2 → 2.8 MB (0.09×) | `plan3-results.md`, `E_counts` |

The arena shape changes what a consumer reads (`result.Lines.AsSpan(o.LineOffset, o.LineCount)` instead of
`o.Lines`), so it can only ever be an opt-in result shape, not a drop-in for `List<LineDto>`
(`RESEARCH-hardware-mode.md` §10).

### 2c. Measured, and deliberately not built

| Shape | Baseline (classes) | Measured variant | Allocated | Source |
|---|---:|---:|---|---|
| Map a DTO tree, then read 6 fields → read through a view instead | 34.5 ms | 1.40 ms (0.04×) | 18.4 MB → **0** | `plan3-results.md`, `A_view` |
| Message DTO (5 strings, `DateTime`, `bool`, enum) → view | 10.4 ms | 0.48 ms (0.05×) | 8.0 MB → **0** | `plan3-results.md`, `C_msg` |

A "view" is a `readonly ref struct` over the *source*, with a property per mapped member — the mapping never
runs, so nothing is allocated and nothing is copied. **DwarfMapper does not emit views.** They are the
strongest result in the whole study and they are also the most constrained: a `ref struct` cannot be stored
in a field, captured in a lambda, or held across an `await`, so it fits "map and immediately serialize or
render" and nothing else. They are recorded as a candidate, not as a capability.

### 2d. The losers — measured, and the reason no SIMD path was built

Same runs, same machine, same job. **Ratios above 1.00 are slower than the plain scalar loop they would
replace.**

| Idea | N = 1,000 | N = 100,000 | Source | Verdict |
|---|---:|---:|---|---|
| Column transpose, then SIMD, for mixed field widths | **2.93× slower** | **1.82× slower** | `plan-results.md`, `M_mixed` | rejected |
| 12-byte struct permuted by byte shuffle | **2.21× slower** | **1.35× slower** | `plan-results.md`, `P12_perm` | rejected |
| 16-byte struct permuted by one byte shuffle | 0.77× | 0.95× | `plan-results.md`, `P16_perm` | inside the noise; not built |
| `TensorPrimitives` widening (`float`→`double`) | 0.60× | 0.97× | `plan-results.md`, `W_widen` | helps only while the buffer is cache-resident |
| Span blit vs the element loop | 0.21× | 0.98× | `plan-results.md`, `S2_spanmap` | real for small buffers, bandwidth-bound above L2 |
| Span over a *class's* field block instead of converting it | 0.68× | 1.00× | `plan4-results.md` | ~1 ns/element, and silently wrong on an auto-layout class |
| Dropping reference members to make the GC scan cheaper | 1.04× | 1.13× | `plan2-results.md`, `C_gcscan` | **no gain measured — and see §3** |

These are the reason round 29 shipped two diagnostics and a code fix instead of a vectorized copy path. The
pattern is consistent: **SIMD wins when it replaces per-element *work* (a widening conversion) or moves a
whole block; it loses when it replaces a copy the JIT already emits as two or three register moves.** A
12-byte permute is three loads, three shuffles and two ORs standing in for three float moves, and it measures
exactly like that.

---

## 3. Two results that surprise people, stated carefully

**A single `string` leaf in an otherwise blittable gather costs 33–55%.** `plan2-results.md`, `C_gather`:
the same gather with a `string Name` member instead of an integer id is **1.55× slower at N = 1,000 and
1.33× at N = 100,000**, and allocates 1.5× as much. A reference field costs a GC write barrier on every
element store and keeps the destination array GC-scannable. This is *pairwise* — the same struct with and
without the reference. It is not a claim that any DTO containing a string is slower than any DTO without one:
in `plan-results.md`'s `D_tree` the string-leaf variant is *faster* than the plain one (1.41 ms against
1.80 ms) because it is a different, smaller shape. Two different questions, two different tables.

**We could not measure a GC-scan saving from removing reference members, and the experiment as written
cannot prove there is none.** The intent was to compare a forced Gen2 collection with a reference-carrying
struct array alive against one with a reference-free array alive. The result was `1.13×` — the reference-free
arm slightly *slower*, which is not a physical story anyone should believe. The reason is in the probe:
[`PlanProbe2.cs`](../Issues/round29/PlanProbe2.cs) holds **both** arrays in fields, so both were alive in both
arms and the only difference was which one the JIT kept "used". So the honest statement is not "removing
references does not help the GC" — it is **"this benchmark measured a full GC with the whole fixture alive,
found no gain, and could not isolate the variable it was written to isolate."** `RESEARCH-hardware-mode.md`
§10 marks that row inconclusive for the same reason. It is on this page because a null instrument published
as a negative finding is the failure mode this page exists to prevent.

---

## 4. Where the win actually comes from

**Destination storage, not clever copying.** Every attempt in round 29 to speed up the *copy* measured a loss
or noise (§2d). The gains all come from the same place: a class-element destination allocates one heap object
per element — allocation, zeroing, an object header, a reference write barrier, and eventually Gen1/Gen2
promotion — while a struct-element destination is **one** allocation for the whole collection.

The clearest version of that is `RESEARCH-hardware-mode.md` §2a, which holds the destination fixed and
changes only the copy: replacing five field assignments with a 32-byte body memcpy per object measures
**0.98×–1.08×**, i.e. nothing. Change the destination to a struct array instead and the same five-field copy
runs at 0.45× / 0.11× / 0.08× at 1k / 100k / 1M. `plan4-results.md` says it a second way: spanning a class's
field block and blitting it is 0.68× at 1,000 and **1.00× at 100,000** — the technique that skips the
declaration change buys about a nanosecond per element and nothing at scale.

Two things follow, and both are load-bearing for how you should read the rest of this repository's docs:

1. **Adopting struct DTOs is what pays.** That is a change to *your* types. DwarfMapper's contribution is to
   find the types where it is safe, tell you the size you would get, and perform the rewrite.
2. **DwarfMapper's copy loop is already at the JIT floor**, and this round found no way to move it. That is
   also what [`COMPARISON.md`](COMPARISON.md) says from the other direction: the source-generator peers tie
   us on throughput, and the differentiator is correctness rather than speed.

---

## 5. What the feature will not do for you

- **The code fix does not update your call sites.** That is deliberate, not an omission: `x == null`,
  `list[i].X = v` and an aliasing assignment are meant to become **compile errors** in the files that use the
  type. Roslyn's preview shows one document, so the lightbulb title carries the warning instead. See the
  hazards table in [`diagnostics.md`](diagnostics.md#dwarf103) before you take it — three of those hazards
  are *not* compile errors.
- **The size in the `DWARF103` message is only true if the nested models convert with the outer one.** The
  size counts a nested transfer model **inlined**, at the nested struct's own size rather than at pointer
  width. Converting the outer type by hand and leaving a nested one a class gives you a type that is not the
  size you were shown. The code fix converts the whole set transitively for exactly this reason, and changes
  nothing at all if any member of the set cannot be rewritten.
- **`DWARF103` and `DWARF101` are `Info`, and ignoring them is a legitimate answer.** A field order chosen
  for readability, or a DTO you want to stay a reference type, is a decision — not a defect.
- **Neither diagnostic proves a block copy.** `DWARF103` says a pair *could* blit where that is possible and
  earned; layout identity and field-name alignment are a separate proof, and `DWARF100` is what reports a
  near miss on it.

---

## 6. What this project has already retracted, and will not restate

This section is here so nobody — including a future maintainer reading only the wins above — re-publishes a
claim this repository has already withdrawn.

- **A headline ratio that was platform-specific and described a competitor rather than this mapper.** It was
  measured on Windows and did not hold on Linux, and the effect it described belonged to the other library's
  implementation rather than to ours. The row was removed rather than corrected, so there is nothing left in
  the docs to link to — what remains is the practice it produced: every comparison row in
  [`COMPARISON.md`](COMPARISON.md) now states its OS, runtime and job in a footnote (`‡`, `†`), and the two
  re-measured rows say plainly that the rest are from an earlier run and should be reproduced locally. The
  number itself is not repeated here and must not be reintroduced.
- **"DwarfMapper is 3.98× slower than Mapperly on enums."** That was a benchmark artifact: the four libraries
  default to *different* enum strategies (name-matching vs value-casting), so the row compared a name switch
  against a raw cast and published the difference as a deficiency. Measured like-for-like the gap is gone.
  Full account in [`COMPARISON.md`](COMPARISON.md).
- **Every single-object comparison row now maps a ring of 512 distinct fixture-drawn payloads**, because
  mapping one cached object moved four rows and flipped one ranking. That rule is enforced by
  `BenchmarkPayloadRuleTests`, which fails the build if a benchmark maps a static payload.

**The numbers on this page have not been through that mill.** They are research probes
(`Issues/round29/PlanProbe*.cs`), not the repository's benchmark suite: they are not in
`benchmarks/DwarfMapper.Benchmarks`, they are not gated in CI, they are not covered by
`BenchmarkPayloadRuleTests`, and they have been run on exactly one machine at three iterations. They were
good enough to decide what to build. They are not good enough to be quoted as a performance guarantee, and
this page is the place that says so.
