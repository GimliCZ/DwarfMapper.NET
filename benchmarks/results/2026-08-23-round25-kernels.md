<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 25 T0-A — local kernel measurements

**Why this file exists.** The round-25 RFC's v2 numbers were measured in a cloud container on hardware this
repository does not have, and the harness it says is "preserved for re-runs" (`/home/claude/bench/Program.cs`)
does not exist here. Every constant round 25 ships had to come from a measurement someone can repeat. This is
that measurement.

## Environment

| | |
|---|---|
| Host | Windows 10.0.19045, 12 logical cores |
| Runtime | .NET 10.0.1, Release, workstation GC |
| Vectors | `Vector.IsHardwareAccelerated = true`, `Vector<byte>.Count = 32` (256-bit) |
| Method | each candidate against its **scalar twin in the same process**, so shared noise cancels; 200 warmup iterations for tier-up, then median of 15 trials of 50 iterations. The correction under point 2 re-ran the large sizes with 21 trials and 10 inner iterations, to keep Large Object Heap traffic out of the timed region |
| Runs | executed twice; both runs reported below where they differ materially |

Ratios are the finding. Absolute nanoseconds carry machine noise and are not comparable across runs.

## Results

Ratio > 1 means the fast path wins. All four kernels allocate their destination, which is what the generator
emits.

**The two `n=65536` cells in bold below are SUPERSEDED — do not cite them.** They were produced by a harness
that allocated 1 MB inside the timed region, i.e. on the Large Object Heap, 50 times per trial. The
correction under point 2 has the re-measured figures; every large size is a win. The rest of the table stands
and was reproduced across two runs.

| kernel | n=4 | n=8 | n=16 | n=32 | n=64 | n=128 | n=256 | n=1024 | n=16384 | n=65536 |
|---|---|---|---|---|---|---|---|---|---|---|
| A `array → List<T>`, 16-byte struct | 1.08 / 1.00 | 1.48 / 1.55 | 1.83 / 1.82 | 2.22 | 2.66 | 2.86 | 3.61 | **13.47** | 1.23 / 1.19 | ~~0.93 / 0.92~~ |
| B `List<T> → array`, 16-byte struct | 0.84 / 1.02 | 1.82 / 1.55 | 1.83 / 1.92 | 2.48 | 2.66 | 3.02 | 10.29 | 8.88 | 1.22 / 1.22 | ~~1.16 / 0.79~~ |
| C enum array (T1, as shipped) | 0.76 / 0.82 | 1.19 / 1.60 | 1.89 / 2.35 | 2.53 | 3.33 | 4.36 | 5.44 | 6.89 | 7.91 / 9.25 | 1.39 / 1.49 |
| D `List<T> → List<T>`, 16-byte struct | 0.95 / 0.92 | 1.82 / 1.10 | 2.16 / 2.52 | 2.63 | 2.67 | 3.55 | 3.49 | **12.44** | 1.48 / 1.94 | 1.25 / 1.15 |

## What this changes

**1. The container's `Count >= 32` List guard does not reproduce, and no guard ships.** The RFC recorded
`Add` winning below about 32 elements and specified a threshold expressed as a `Vector<T>.Count` multiple.
Measured here on the shape that is actually emitted (a fresh destination), the crossover sits between
**n=2 and n=4**: n=2 is 0.76 / 0.79x, n=4 is already 1.08 / 1.14x, and it climbs from there. So the only
losing size is a two-element collection, at a cost of about **20 ns**.

A guard would mean emitting BOTH strategies at every blittable collection site and choosing at run time —
double the emitted code, a second path to test, and a runtime answer to "which one ran" — to recover 20 ns on
collections of two. Declined on that trade, with the numbers above as the reason rather than a hunch.

*(A note on reading the REUSE column below: it shows the blit losing badly at small n, but it describes a
different operation — copying into an ALREADY-ALLOCATED destination. DwarfMapper does not emit that for these
shapes; it allocates the destination, which is the ALLOC column. The REUSE numbers matter only if an
update-into blit is ever built.)*

**2. The gate targets the IN-CACHE regime, at n≈1024, where the ratio is largest and most stable.**
The plan, following v2, had specified the gate at "the large-n regime, ratio >= 1.5x". That is the wrong
place — not because the blit loses there, but because the ratio collapses toward 1.0 as both arms become
memory-bandwidth-bound, leaving no headroom between a healthy result and a de-emitted fast path. A gate needs
margin to be meaningful. At n≈1024 the measured ratio is 13–20x against a 1.5x floor; at n=65536 it is
1.1–1.2x, where noise and a real regression are indistinguishable. **T4's gate is therefore at n≈1024.**

> ### CORRECTION, same day — the large-n "inversion" was an artifact of this harness
>
> The first version of this file reported `array → List` at **0.93 / 0.92x** for n=65536 and concluded the
> blit *loses* at large sizes. **That does not reproduce and should not be cited.** The cause was the harness,
> not the code: it allocated a fresh 1 MB destination on every one of 50 inner iterations per trial. A 1 MB
> array lands on the **Large Object Heap**, so the row was measuring LOH allocation and collection rather
> than the copy.
>
> Re-measured with 21 trials and a reduced inner count, two runs, plus a REUSE arm that allocates the
> destination once:
>
> | n | ALLOC (fresh destination, what is emitted) | REUSE (copy only) |
> |---|---|---|
> | 16,384 | 1.04 / 1.18 | 3.20 / 4.91 |
> | 65,536 | 1.20 / 1.22 | 1.09 / 1.12 |
> | 262,144 | 1.07 / 1.02 | 1.17 / 1.19 |
>
> Every large size is a win. **There is no large-n regression.** The lesson is the one this round keeps
> relearning: a measurement that allocates inside the timed region is measuring the allocator, and above 85 KB
> it is measuring the LOH.

**3. T1's enum blit is confirmed on this hardware** — up to 9.25x at n=16384, and positive from n=8 —
so it earns its place independently of the container's 76x claim, which was measured against a different
scalar baseline and should not be quoted.

## R25-04 skip-if-identical (T5), measured separately

Compare-then-skip against an unconditional copy, 16-byte structs. HIT = the destination already holds the
same bytes; MISS = it differs in the **last** element, the worst case for the scan. Ratio > 1 means
compare-then-skip wins.

| n | copy ns | HIT ns | ratio HIT | MISS ns | ratio MISS |
|---|---|---|---|---|---|
| 16 | 84 | 110 | 0.76 | 114 | 0.74 |
| 256 | 124 | 326 | 0.38 | 374 | 0.33 |
| 1,024 | 294 | 1,042 | **0.28** | 1,210 | 0.24 |
| 16,384 | 13,550 | 15,214 | 0.89 | 23,354 | 0.58 |
| 65,536 | 74,198 | 17,672 | **4.20** | 92,026 | 0.81 |

**This confirms the RFC's retraction and corrects it in one place.** Through the entire range a real mapper
operates in — tens to low thousands of elements — comparing first is a **loss even when it succeeds**,
bottoming out at 0.28x. `SequenceEqual` reads two streams where `CopyTo` reads one and writes one.

The correction is at the top end: the container measured 0.49x at n=65536 and concluded skip-if-identical is
*never* faster. Locally a HIT there is **4.20x faster**, because at roughly 1 MB the write is the expensive
part and skipping it avoids the bandwidth outright. So "never faster" is not quite true — it is faster only
for very large, already-identical collections, and it remains a loss on a miss even there (0.81x).

## Is `Buffer.MemoryCopy` a better primitive than `CopyTo`?

Asked, and measured rather than reasoned about: **no.** `Span<T>.CopyTo` already bottoms out in the internal
`Buffer.Memmove`, so the public unsafe route is the same primitive reached by a longer path — and the `fixed`
pinning it requires costs a little extra.

| n | scalar | via `CopyTo` | via `Buffer.MemoryCopy` |
|---|---|---|---|
| 2 | 1.00 | 0.19x | 0.14x |
| 1,024 | 1.00 | **3.78x** | 3.58x |
| 16,384 | 1.00 | 3.20x | 3.20x |
| 262,144 | 1.00 | 1.17x | 1.19x |

`CopyTo` matches or beats it everywhere that matters, needs no `unsafe` block, and works uniformly over an
array, a `List<T>` span and an `ImmutableArray<T>` span. It stays.

## Is there a cheaper copy primitive for SMALL collections?

Asked directly, and the answer is **no — and the question turns out to be aimed at the wrong cost.**

`Unsafe.CopyBlockUnaligned` (raw `cpblk`, no length recomputation, no `Memmove` dispatch ladder) is the
obvious candidate for tiny copies. Measured against `MemoryMarshal.Cast(...).CopyTo(...)` with the
destination allocated once, so only the copy is timed — 2,000 iterations per trial, median of 25, two runs
that agree to within a nanosecond:

| n | bytes | `Cast+CopyTo` | `CopyBlockUnaligned` | ratio |
|---|---|---|---|---|
| 1 | 16 | **3.3 ns** | 9.8 | 0.34x |
| 2 | 32 | **3.3** | 9.8 | 0.34x |
| 4 | 64 | **3.5** | 10.0 | 0.35x |
| 16 | 256 | **6.7** | 13.2 | 0.51x |
| 64 | 1,024 | **15.7** | 21.4 | 0.74x |
| 256 | 4,096 | **38.3** | 44.5 | 0.86x |
| 4,096 | 65,536 | 5,859 | 5,872 | 1.00x |
| 262,144 | 4,194,304 | 220,440 | 208,665 | 1.06x |

`CopyTo` **wins everywhere it differs**, by up to 3x at the smallest sizes, and ties above about 4,096
elements. `Buffer.Memmove` has a small-size fast path that raw `cpblk` does not; the JIT does not turn an
unknown-length `cpblk` into anything smarter.

**The more useful finding is the absolute number: the copy costs 3.3 ns at n=1.** So the small-`n` deficit
measured on the emitted shape is not in the copy at all — it is in constructing the destination
(`new List<T>` + `SetCount` versus `Add`-ing into a pre-sized list). Any adaptive switch *between copy
primitives* would be choosing between 3.3 ns and 9.8 ns inside a ~75 ns operation, which is why the
allocation-inclusive small-`n` numbers were so noisy: they were trying to resolve a 3 ns difference underneath
a 70 ns one.

That closes the guard question from the other side. A length-gated dual path would not be selecting a better
copy — there isn't one — it would be selecting a different *destination-construction* strategy, which is a
different feature with a different (and much smaller) ceiling than the ratios above suggest.

*(A discarded intermediate measurement is worth naming so it is not repeated: comparing these primitives with
the allocation left INSIDE the timed region produced a table where `CopyBlock` appeared to win at every size,
and where the `Add` baseline was non-monotonic — 56 ns at n=1 but 25 ns at n=8. Non-monotonic cost with
increasing input is the tell that a microbenchmark is measuring something other than its subject. Isolating
the copy reversed the result completely.)*

## Instruction-level analysis — the measurement that actually resolved things

Run with BenchmarkDotNet's `DisassemblyDiagnoser`, after timing had been exhausted as an instrument.
**`Code Size` is deterministic where timing is not** — the same property that makes the allocation gate
honest. On the run below the timings carried +/- 861 ns of error on a 516 ns mean; the code sizes are exact.

| method | time | native code size |
|---|---|---|
| blit, no guard (as emitted today) | 516.5 ns | **900 B** |
| blit, with the old size guard | 483.8 ns | **957 B** |
| element loop (what a non-provable pair gets) | 6,150 ns | 443 B |

Ratio blit-to-element-loop: **11.97x** at n=1000, measured same-process against a declared baseline. That is
the cleanest version of this round's headline number.

**The copy is optimal and needs no further work.** `MemoryMarshal.Cast`'s length recomputation —
`length * sizeof(TFrom) / sizeof(TTo)` — folds to two instructions with NO division:

```
lea  r8,[r8+r8*2]   ; n*3
shl  r8,2           ; n*12  = byte count
call SpanHelpers.Memmove
```

**Correction, recorded because it was stated wrongly elsewhere: the deleted size guard did NOT fully fold
away.** The comparison did — no `cmp` on `SizeOf` survives — but the THROW BLOCK remained: three
instructions, **57 bytes**, six percent of the method, cold and never executed but present in every blit
helper in every consumer's binary. The justification for deleting it was always design correctness; this is
what it cost in machine code.

**What remains is `List<T>`'s own bookkeeping, not ours,** and it is O(1) rather than O(n): `_version++`, a
capacity check that cannot fail, `_items` reloaded twice because the write barrier defeats CSE, an `AsSpan`
bounds check that cannot fail, and the backing array being zeroed by `NEWARR` before being overwritten
whole. About ten instructions against a 12,000-byte `Memmove`. Removing them would mean not using
`List<T>`, which exposes no public "wrap this array" API.

For contrast the element loop pays roughly **sixteen instructions per element**, including a call, a
`_version++` and a capacity check — which is where the 11.97x comes from.

## The "regression" that was not one — run composition, not code

Chased because the logic was right: if the code is optimal and provably unchanged, a slower benchmark means
something really did change, and "noise" is a label rather than an explanation.

What changed was **how the run was composed**, and the effect is large:

| run | `Blit_Dwarf` | error |
|---|---|---|
| master, measured inside a 25-benchmark sweep | **393 ns** | +/- 861 ns on the sweep's means |
| master, measured in an isolated 4-benchmark run | **479.9 ns** | +/- 12.8, SD 36.4 |
| round 25, measured in an isolated 4-benchmark run | **463.4 ns** | +/- 9.2, SD 13.7 |

Same commit, same machine, same afternoon: **393 against 480 for identical code**, an 18% swing attributable
entirely to what else was in the run. And measured the SAME way, master and round 25 are statistically
indistinguishable, with round 25 nominally ahead.

Three separate conclusions in this file were drawn from cross-composition comparisons and are retracted by
this measurement: that the guard removal cost 17%, that round 25 made `Blit` slower than master, and that
the machine was drifting. None of them survive matched conditions.

**The rule this yields, which is the durable part:** a benchmark number is only comparable to another number
produced by the SAME run composition. Not the same machine, not the same day — the same run. Anything else
is comparing two different experiments. The repository already encodes this correctly in two places, and
both look better in hindsight: the allocation gate reads bytes rather than times, and the round-25 ratio gate
measures both arms inside a single process so composition cancels by construction.

Corollary for anyone re-measuring: filter to the categories you care about and keep the filter constant
across the runs you intend to compare. A number from `--anyCategories Blit` cannot be compared with the same
benchmark's number from a full sweep.

## What was deliberately not measured

Array→array struct blit, which has shipped since Plan 15. Benchmarking it would measure the past rather than
inform a decision. See `Issues/round25/TASKS.md`, correction 3.

## Reproducing

The harness is a standalone kernel-isolation console app, not part of the solution: it exists to answer a
question, and keeping it in the build would invite it to rot. The four kernels are reproduced verbatim in
`Issues/round25/TASKS.md` under T0-A, which is enough to rebuild it in a few minutes. The standing,
always-run comparison is the BenchmarkDotNet ratio gate in T4 — that one lives in the repository, because it
has to keep running.
