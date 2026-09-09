<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Design: the collection benchmark suite is one point on every axis

Raised by the owner, 2026-09-09: *"The benchmark is missing better collection of tests, especially classes
and blitabilities. We should explore the lists and arrays issues. I think our testing list and variants are
insufficient."*

Correct on all three counts, and the gap is sharper than "more benchmarks would be nice". Stated plainly:

> **Every collection claim this repository publishes rests on a single element count, a single class shape
> and a single blittable struct.** Three axes, one point each. A single point cannot distinguish a property
> of the design from a property of N = 1000.

## 1. What the suite measures today

63 benchmarks, `[Params(1000)]`, one class (`MapperBenchmarks`).

| category | rows | source to destination | element |
|---|---:|---|---|
| `Array` | 4 | `FlatSrc[]` to `FlatDst[]` | class, 4 members |
| `List` | 4 | `List<FlatSrc>` to `List<FlatDst>` | class, 4 members |
| `Seq` | 1 | `IEnumerable<FlatSrc>` to `FlatDst[]` | class, 4 members |
| `NumList` | 4 | `int[]` to `List<long>` | value, widening |
| `NestedFill` | 4 | order graph | mixed |
| `Blit` | 4 | `Vec3Src[]` to `Vec3Dst[]` | struct, 12 B |
| `BlitRatio` | 4 | blit vs `[StructLayout(Auto)]` twin | struct, 12 B |
| `SpanBlit` | 2 | span to span | struct, 12 B |

### The three collapses

**(a) N. Every row above is N = 1000 and nothing else.** `[Params(1000)]` -- one value. So "we lead Blit
2.25x" and "we trail Mapster on List 1.13x" are both statements about one array length, published without
one. Round 29 already learned what that hides: `Issues/round29/sweep-results.md` swept 10^1..10^6 and found
the earlier two-point reading of the blit ("0.21x at 1k, 0.98x at 100k -- a win that evaporates") was an
artifact; swept properly the blit never loses. The same instrument has never been pointed at the
comparative rows.

**(b) Classes. There is exactly ONE class shape in the whole suite**: `FlatSrc` to `FlatDst`, four members
(`int`, `string?`, `int` to `long`, `bool`). `Array`, `List`, `Seq` and `Nested` all use it. So every
reference-element conclusion is a conclusion about four members, one of them a string, with settable
property setters. Not measured at all: a wide DTO, a class whose elements own nested objects, a
constructor/record destination -- and that last one is **different emitted code** (the constructor-selection
path) which has never been timed inside a collection at any size.

**(c) Blittability. One blittable pair, at one size, with one negative control.** `Vec3` is 12 B of `float`.
The blit's eligibility rule has several ways to fail and only one of them (`[StructLayout(Auto)]`) is ever
exercised as a benchmark. Element SIZE is not an axis at all, yet size is exactly what decides whether a
block copy beats a per-element loop.

## 2. Where each new measurement lives, and why it is not one class

Hard constraints, read from the gate rather than assumed:

* `scripts/housekeeping.ps1:277` keys allocation pins by **`$b.Method`**, and `:264` compares the row count
  to `allocation-baseline.json`'s `totalBenchmarks: 63`. Putting `[Params(1, 10, ..., 1_000_000)]` on
  `MapperBenchmarks` would produce **seven rows per method name** -- the pin dictionary would keep whichever
  came last, and the count check would fail 441 against 63.
* The gate reads `MapperBenchmarks-report-full.json` **by class name**, so a second class is invisible to it
  -- which is the property we want, not a problem to solve.
* `Program.cs` calls `BenchmarkRunner.Run<MapperBenchmarks>` directly, so a second class is currently
  unreachable.
* `-BenchSmoke` passes **no filter**. Whatever the entry point runs under `DWARF_BENCH_SMOKE=1` is what the
  nightly pays for, every night.

**Ruling: two classes, and smoke mode never sees the second.**

| | `MapperBenchmarks` (existing) | `CollectionSweepBenchmarks` (new) |
|---|---|---|
| N | `[Params(1000)]`, unchanged | `[Params(1, 10, 100, 1_000, 10_000, 100_000, 1_000_000)]` |
| allocation pins | exact, gated nightly | **none** -- allocations are N-proportional by construction |
| run by `-BenchSmoke` | yes | **no** |
| purpose | regression gate | usage-space measurement |

`Program.cs` keeps `BenchmarkRunner.Run<MapperBenchmarks>` when `DWARF_BENCH_SMOKE=1` and becomes a
`BenchmarkSwitcher` otherwise. The nightly's cost and the pinned count do not move.

This also supersedes the round-29 workaround: `sweep-results.md` ran its decade sweep as a **standalone
probe outside the repository** for exactly this reason, and paid for it with an in-process-toolchain caveat
that bounds every number in that file. In-repo, out-of-process, with rivals in the same run, removes it.

## 3. The axes

### Axis 1 -- N, on the shapes the owner named

`1, 10, 100, 1k, 10k, 100k, 1M` across {`Array`, `List`, `Blit`} across {Dwarf, Mapperly, Mapster,
AutoMapper}.

This is the axis that turns two published sentences into answers:

* *"we trail Mapster on List by 1.13x"* -- at which sizes? A fixed-overhead difference and a per-element
  difference have opposite shapes across seven decades, and only one of them is worth acting on.
* *"Array is parity (1.01x, inside the SE)"* -- parity at N=1000 says nothing about N=10 or N=10^6.

### Axis 2 -- element kind (the owner's "classes")

Same collection shape (`T[]` to `T[]`), N swept, element varied. Each is one hypothesis:

| element | what it isolates |
|---|---|
| `FlatSrc` (4 members, 1 string) | today's only shape -- the control |
| **Wide** (20 members, mixed scalars) | per-element member cost vs per-element allocation cost |
| **StringHeavy** (6 strings) | reference copying with no conversion |
| **Deep** (element owns a nested object) | nested-map dispatch inside an element loop |
| **CtorDst** (record / positional destination) | the constructor path, never timed in a collection |
| **StructDst** (same 4 members, `struct` destination array) | the storage lever |

That last row is deliberate and is the honest answer to "classes and blitabilities" in one line.
`Issues/round29/RESEARCH-hardware-mode.md` already measured class blit as a **~2 % non-win** and recorded
that **destination storage is what decides** (struct DTO arrays 2-25x, -43 % memory). The suite should
*show* that rather than let it live only in a research file -- same four members, `class[]` against
`struct[]`, same N sweep.

### Axis 3 -- blittability, both directions

**Positive, by size.** `Blit` vs its scalar twin at element sizes **4, 8, 16, 32, 64, 128 B** across the N
sweep. The blit's usage space along SIZE is currently undeclared; `sweep-results.md` declared it along N
("everywhere, no crossover") for one size only.

**Negative -- the refusals, which are the user-manipulation half.** Today `[StructLayout(Auto)]` stands in
for the entire eligibility rule. Each of these must be shown NOT to blit, and to stay correct:

| case | why it must refuse |
|---|---|
| same fields, **different order** | a byte copy would silently transpose values |
| a **reference** field | copying references as bytes is unsound |
| **padding** difference (`Pack` / explicit layout) | the destination would read padding as data |
| **enum**-typed field vs its underlying type | round 29 touched this path |
| `readonly struct` / `record struct` | the shapes a consumer actually writes |

These are correctness controls with a timing column, not throughput rows. A refusal that silently became a
blit is the worst defect this design can have, and nothing currently measures the boundary.

### Axis 4 -- collection shape, pruned

Array to array, `List` to `List`, array to `List`, `IEnumerable` to array. **Not** the full type matrix --
the other axes need these four and no more.

## 4. Hypotheses this suite exists to decide

**H1 -- the List gap is `Add`, not mapping.** `ListSrc`'s own comment says the reference-element List path
is pre-sized + `Add`, while `NumList` (value elements) uses `CollectionsMarshal.SetCount` + a span fill. So
`List_Dwarf` pays `Add`'s per-element `_version++`, capacity check and `_size++` on every element and
`NumList_Dwarf` does not. If that is the whole 1.13x Mapster gap, it is a generator change, not a mystery.
Decided by ONE same-process twin -- `List_Dwarf` against a hand-written `SetCount`+span probe over the
identical payload -- before anyone touches emission.

**H2 -- three parked optimizations are all "wins that evaporate", and the N axis is what says so.**
`Issues/round30/PARKED-OPTIMIZATIONS.md` parks the 16-byte permuted blit (0.77x at 1k, **0.95x at 100k**),
spannable classes (0.68x at 1k, **1.00x at 100k**) and TensorPrimitives conversions (0.60x cache-resident,
**0.97x at 100k**). All three have the same shape, all three were measured at two points, and the reopen
criteria in that file are size-conditional. The sweep either confirms the evaporation across seven decades
or reopens them with evidence.

**H3 -- the blit's own usage space is size-conditional, and only N was ever swept.** A 4-byte element and a
128-byte element have different memcpy economics; "always on, no length gate" was concluded from 12 bytes.

## 5. Order of work

1. `CollectionSweepBenchmarks` + the switcher change; **N axis only**, existing fixture types. Proves the
   harness and leaves the gate untouched (`totalBenchmarks` does not move, smoke does not grow).
2. H1's twin probe. Cheapest decisive measurement in the list.
3. Axis 2 fixtures (element kinds), including the class-array against struct-array pair.
4. Axis 3: the size ladder, then the refusal controls.
5. One full sweep, all libraries in one process, written up as a dated results file.

**Cross-run comparison is invalid on this machine** (measured +/-17 % variance; two branches with
byte-identical emitted code once measured 20 % apart). Every comparative claim must come from rows inside
one run. Nothing in section 5 may be quoted against the 2026-08-24 sweep.

**Gate arithmetic, for whatever ever does land in the smoke class:** each new `*_Dwarf` needs a pin, a dated
`_comment` entry stating what it measures, and `totalBenchmarks` moved -- re-measured in the same commit
(invariant R1). Nothing in phases 1-4 above is expected to need one.
