<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Spike: map fusion — the JIT elides the intermediate in straight-line code and **not inside a loop**

Phase 4. The plan's instruction was explicit about the order:

> when `A→B` and `B→C` are both `[GenerateMap]` pairs in one class, emit `A→C` composed; **measure whether
> the JIT already elides B (escape analysis in .NET 10 may) before building it.**

Measured, and the answer has a **boundary in it** rather than being a flat yes or no:

> **Straight-line, one element: the JIT elides `B`. Inside a per-element loop: it does not.**

That boundary is the whole result, because **every collection map the generator emits is a per-element
loop** — so fusion pays exactly where a mapper spends its time and buys nothing for a single-object map.

**The first version of this document claimed the flat "does not elide", and it was wrong.** All three
original arms allocate inside a `for` loop, N=1 included (the JIT cannot see that `r.Length` is 1), so the
measurement could not distinguish "the JIT will not do this" from "the JIT will not do this in a loop". The
straight-line arms below were added for exactly that reason and they reversed the claim. Recorded rather
than quietly amended: it is the same "right number, wrong population" error this round has now made five
times, caught this time before it left the machine.

## Why allocation and not time

This repository's timing runs are three iterations and several have error bars approaching their means —
the `Chained` row below carries ±15.1 ns on a 13.6 ns mean. Its allocation numbers are exact bytes and are
gated as such. So the question was posed as an allocation question:

- If the JIT elides `B`, the chained path allocates **only** the final `C`, and its byte count **equals**
  the fused path's.
- If it does not, the chained path allocates `B` as well, and the difference is **exactly** `sizeof(B)`.

A third arm, `ChainedEscaping`, deliberately stores each `B` in an array that outlives the loop, so the JIT
*cannot* elide it. Without that control, two numbers being equal would prove nothing about *why*.

## Measured

BenchmarkDotNet 0.14.0, ShortRun (3 iterations), .NET 10.0.11, Ryzen 5 5600 / Windows, in-process emit
toolchain. `FuseA/B/C` are identical four-member classes (`int`, `long`, `double`, `int`).

| Method | N | Mean | Ratio | Allocated | Alloc ratio |
|---|---:|---:|---:|---:|---:|
| `Chained` | 1 | 13.64 ns | 1.00 | **112 B** | 1.00 |
| `Fused` | 1 | 10.10 ns | 0.74 | **72 B** | 0.64 |
| `ChainedEscaping` | 1 | 17.58 ns | 1.29 | 144 B | 1.29 |
| `Chained` | 1000 | 8,679 ns | 1.00 | **88,024 B** | 1.00 |
| `Fused` | 1000 | 5,428 ns | 0.63 | **48,024 B** | 0.55 |
| `ChainedEscaping` | 1000 | 10,887 ns | 1.25 | 96,048 B | 1.09 |

## The harness was ruled out as a confounder

BenchmarkDotNet's in-process emit toolchain runs the benchmark through a generated delegate, which is a
plausible reason a JIT might decline to stack-allocate. So the whole probe was re-run **out of process**,
against a normal JIT in its own process:

| Method | N | Mean (in-proc) | Mean (out-of-proc) | Allocated (both) |
|---|---:|---:|---:|---:|
| `Chained` | 1 | 13.64 ns | 9.06 ns | **112 B** |
| `Fused` | 1 | 10.10 ns | 6.47 ns | **72 B** |
| `ChainedEscaping` | 1 | 17.58 ns | 11.99 ns | **144 B** |
| `Chained` | 1000 | 8,679 ns | 6,424 ns | **88,024 B** |
| `Fused` | 1000 | 5,428 ns | 3,929 ns | **48,024 B** |
| `ChainedEscaping` | 1000 | 10,887 ns | 8,245 ns | **96,048 B** |

**Allocation is byte-identical across the two toolchains** — all six rows. The times move (the emit harness
costs ~3 ns of overhead per op) and the ratios barely do. The measurement is about the runtime, not about
BenchmarkDotNet.

## Every byte is accounted for, which is why this is a fact rather than a ratio

On x64 a `FuseC` is 16 B of header plus `int`+`long`+`double`+`int` = 24 B of fields = **40 B**. A
`FuseC[1]` is 16 B header + 8 B length + 8 B element = **32 B**.

```
Fused           N=1     72 B  =  32 (array)   + 40 (one C)
Chained         N=1    112 B  =  72           + 40   ← exactly one FuseB
ChainedEscaping N=1    144 B  = 112           + 32   ← exactly the keep-array

Fused           N=1000  48,024 B = 8,024 (array) + 40,000 (1000 C)
Chained         N=1000  88,024 B = 48,024        + 40,000   ← exactly 1000 FuseB
ChainedEscaping N=1000  96,048 B = 88,024        +  8,024   ← exactly the keep-array
```

**The chained path allocates one intermediate per element, at both sizes.** And the escaping control differs
from `Chained` by *only* the keep-array — not by the `B` objects, because `Chained` was already allocating
them. The control does exactly what it was written to do: it shows the instrument can tell the two cases
apart, and that this is not one of them.

## The boundary: a loop, and nothing else

Three more arms, same probe, out of process. They do the same work as `Chained` at N=1 — one element, one
`FuseB`, one `FuseC` — with the loop removed:

| Method | Allocated | vs. its looped twin |
|---|---:|---|
| `Chained_Straight` | **72 B** | `Chained` N=1 is 112 B |
| `Fused_Straight` | **72 B** | `Fused` N=1 is 72 B |
| `Chained_StraightInlined` | **72 B** | forced `AggressiveInlining` on both helpers |

**`Chained_Straight` allocates exactly what `Fused_Straight` does.** 72 B = the array (32) + one `FuseC`
(40), and no `FuseB`. The JIT stack-allocated the intermediate. The *only* difference between this 72 B and
the looped path's 112 B is the `for` loop — same types, same helpers, same element count, same process.

The third arm rules out the other candidate explanation: forcing both helpers to inline does not move a
byte, so non-inlining was never why the looped case allocates.

So .NET 10's widened escape analysis **is** doing what the plan expected — just not across a loop body.

## What this means for the mapper, which is the useful half

**Every collection map DwarfMapper emits is a per-element loop.** `Map(A[]) → C[]`, list and set elements,
dictionary values, nested collections — all of them construct the intermediate inside the loop, which is
precisely the shape the JIT does not elide. So:

| shape | JIT elides `B`? | what fusion buys |
|---|---|---|
| single object, `Map(a)` chained through `B` | **yes** | nothing — do not emit it |
| any per-element loop (array, list, set, dictionary values, nested) | **no** | 0.55× allocation, 0.61× time at N=1000 |

That is a measured usage space with a sharp boundary rather than a threshold picked between two points, and
it says something the flat claim did not: **fusion should be emitted only inside element loops.** A fused
single-object map is dead weight that duplicates what the runtime already does.

## What this does NOT establish, stated before anyone quotes it

1. **The probe measures hand-written `private static` methods, not generator output.** The same caveat the
   arena spike carries. A real emitted pair is a `public partial` method on a mapper class, and whether the
   inlining that makes `B` a candidate happens identically there is unmeasured. This is the exact shape of
   the "right number, wrong population" error this round made four times.
2. **The usage space is a SHAPE boundary, not a size one, and only the shape was measured.** Loop vs.
   straight-line is settled. Whether the win varies by element count is two points (N=1, N=1000) and would
   need the decade sweep if fusion ever wanted a size gate — it probably does not, since the win is one
   allocation per element by construction.
3. **Nothing here says the composed map is *correct* to emit.** Fusion changes observable behaviour whenever
   the intermediate is observable: `BeforeMap`/`AfterMap` hooks on the `A→B` pair, a `[RoundTrip]` verifier
   over `B`, a converter with a side effect, a cycle-preserving reference map whose identity table is keyed
   on `B`. **Fusion must be refused wherever `B`'s construction is observable**, and enumerating those cases
   is the first design task, not the emitter.
4. **The chain must be private.** If `A→B` is a public endpoint the consumer can call, fusing `A→C` does not
   remove `B` — it adds a second path. The win only exists where the generator can see that `B` is produced
   and consumed inside one call.

## Recommendation

**Build it in round 30, scoped to element loops and gated on (3).** The measurement clears the question the
plan raised and sharpens it: the JIT is doing this for us in straight-line code and is not doing it inside a
loop, which is where every collection map lives. The payoff is one allocation per element on every chained
pair inside a loop; a fused single-object map should be refused as dead weight.

The work is not the emitter. It is (a) the refusal list above and (b) the shape test that decides a call site
is inside an element loop, and both should be specified before a line of emission is written.

Re-measure against generated output before quoting any figure from this file.
