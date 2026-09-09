<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Spike: map fusion — the JIT does **not** elide the intermediate

Phase 4. The plan's instruction was explicit about the order:

> when `A→B` and `B→C` are both `[GenerateMap]` pairs in one class, emit `A→C` composed; **measure whether
> the JIT already elides B (escape analysis in .NET 10 may) before building it.**

Measured. **It does not.** The answer is a yes/no with no noise in it, and the reason it is trustworthy is
that every byte is accounted for.

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

## What this changes

The plan expected the opposite. .NET 10 widened escape analysis considerably — small arrays of value *and*
reference types, local struct fields, delegates — and an intermediate that is constructed, read once and
discarded is the textbook shape for stack allocation. It is not happening here.

So **map fusion is an unclaimed win, not a feature the runtime already gives away**: 0.55× allocation and
0.63× time at N=1000, 0.64× and 0.74× at N=1. Allocation is the number to quote; the timings are three
iterations and are consistent with it rather than independent evidence for it.

## What this does NOT establish, stated before anyone quotes it

1. **The probe measures hand-written `private static` methods, not generator output.** The same caveat the
   arena spike carries. A real emitted pair is a `public partial` method on a mapper class, and whether the
   inlining that makes `B` a candidate happens identically there is unmeasured. This is the exact shape of
   the "right number, wrong population" error this round made four times.
2. **Two data points, not a usage space.** N=1 and N=1000. The round's own standing rule is that a threshold
   picked between two points is invented; if fusion ever needs a size gate, it needs the decade sweep first.
   (It probably does not — the win is per-element and structural.)
3. **Nothing here says the composed map is *correct* to emit.** Fusion changes observable behaviour whenever
   the intermediate is observable: `BeforeMap`/`AfterMap` hooks on the `A→B` pair, a `[RoundTrip]` verifier
   over `B`, a converter with a side effect, a cycle-preserving reference map whose identity table is keyed
   on `B`. **Fusion must be refused wherever `B`'s construction is observable**, and enumerating those cases
   is the first design task, not the emitter.
4. **The chain must be private.** If `A→B` is a public endpoint the consumer can call, fusing `A→C` does not
   remove `B` — it adds a second path. The win only exists where the generator can see that `B` is produced
   and consumed inside one call.

## Recommendation

**Build it in round 30, gated on (3).** The measurement clears the question the plan raised — the JIT is not
doing this for us — and the payoff is a structural per-element allocation the mapper currently pays on every
chained pair. The work is not the emitter; it is the refusal list, and it should be specified before a line
of emission is written.

Re-measure against generated output before quoting any figure from this file.
