<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: a collection element routed through a USER-DECLARED map pays a null ternary the synthesized path does not

**Found 2026-09-09**, while sweeping the collection surface across seven decades at the owner's request.
Filed rather than built, on the precedent of `Issues/round26/FINDING-list-fill-strategy.md`: this changes
emission for every collection whose element map is user-declared, and it changes OBSERVABLE BEHAVIOUR on a
null element, so it needs its own safety analysis and test pass rather than a closing commit.

## The gap it explains

The `Array` category is the one place DwarfMapper trails a rival at every element count. Measured
2026-09-09, all four libraries in one process, 5 warmup / 10 iterations (`CollectionSweepBenchmarks`):

| N | 1 | 10 | 100 | 1,000 | 10,000 | 100,000 | 1,000,000 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Mapperly ÷ DwarfMapper | 0.71x | 0.85x | 0.92x | 0.86x | 0.72x | 0.88x | 0.92x |

Reproduced in an earlier ShortRun pass of the same suite (0.81–1.02x). `List` and `Blit` show no such
consistent trend, so this is specific to the shape, not to the machine or the run.

## The mechanism, read from both generators' output

Same file, `DwarfM.g.cs`, two collection helpers over reference elements whose element type is
non-nullable-annotated in a `<Nullable>enable</Nullable>` project:

```csharp
// element mapped by a USER-DECLARED partial method (ArraySrc.Items, ListSrc.Items -> MapFlat)
__r.Add((__item is null ? null! : (global::FlatDst)MapFlat(__item)));

// element mapped by a GENERATOR-SYNTHESIZED helper (NfOrder.Lines)
__r.Add(__DwarfMap_Obj_global__NfLine_global__NfLineDto_BDE0B74A(__item));
```

**The two paths disagree about the same question.** One tests every element for null and stores `null!` on
the null arm; the other calls straight through and lets the helper's own guard answer. Mapperly, for the
identical shape, emits neither test:

```csharp
for (var i = 0; i < source.Length; i++) { target[i] = MapFlat(source[i]); }   // MapFlat has NO null guard
```

## Measured: which of the two checks actually costs

`NullCheckProbeBenchmarks`, four hand-written arms over one payload draw, same process. `Current` replicates
the emitted shape line for line; `Guarded` mirrors the emitted public partial method (`ThrowIfNull` then an
object initializer); `Core` is what a non-validating inlinable core would be.

| N | Current | CoreCall (loop test kept, callee guard removed) | GuardOnly (loop test removed) | Unguarded (neither) |
|---:|---:|---:|---:|---:|
| 100 | 564 ns | 567 (**1.007x**) | 514 (**0.911x**) | 513 (**0.911x**) |
| 1,000 | 6,119 | 6,610 (1.080x) | 5,737 (**0.938x**) | 5,529 (0.904x) |
| 10,000 | 70,846 | 71,843 (1.014x) | 65,316 (**0.922x**) | 62,424 (0.881x) |

**Two results, and the first one refutes the hypothesis this probe was built for.**

1. **The callee's `ThrowIfNull` is free.** `Current` - `CoreCall` is inside the combined standard error at
   all three sizes (-3.7 ns of 15.8; -491 of 849; -997 of 9,400), and the sign is the wrong way round. A
   private non-validating core would buy NOTHING. That was the proposed fix; it is dead.
2. **The loop's own ternary is the whole cost.** `GuardOnly` (loop test removed, callee guard kept) and
   `Unguarded` (neither) measure the same 0.911x at N=100. Removing the ternary recovers **~9 %** — which is
   the size of the Mapperly gap, so the mechanism accounts for the finding rather than merely accompanying
   it.

## Why this is a correctness question before it is a performance one

The null arm stores `null!` into `FlatDst[]` — an array whose element type forbids null. A consumer who
receives a null element gets **a silent null in a collection typed as non-null**, discovered later and
somewhere else. The synthesized path, for the identical situation, produces an `ArgumentNullException`
naming the parameter at the moment the null is seen. Two paths, two behaviours, and the quiet one is the
one that is also slower.

That is a de-silencing item, not only an optimization.

## The lead, stated as unconfirmed

There are **two different definitions of "this reference may be null"** in the pipeline:

| site | predicate | treats an OBLIVIOUS (`None`) annotation as |
|---|---|---|
| `CollectionConverter.cs:296`, `:405` | `NullableAnnotation == Annotated` | not nullable |
| `MapperExtractor.Conversions.cs:1156` (`SourceMayBeNullRef`) | `NullableAnnotation != NotAnnotated` | **nullable** |

The user-declared element path reaches its `NullHandling` through the second; the synthesized path does not
emit a test at all. That asymmetry is the obvious suspect and it is **not confirmed** — no measurement here
establishes which annotation the element symbol actually carries at that call site. Confirm it before
changing either predicate; a generator test that asserts the emitted element expression for
`FlatSrc[] -> FlatDst[]` under `enable`, `disable` and an explicitly `FlatSrc?[]` source is the instrument.

## What a fix must decide, and why it is not a closing commit

1. **Does a null element throw, or pass through as null?** Today: user-declared path passes null through,
   synthesized path throws. They must agree, and choosing which one changes observable behaviour for
   existing consumers either way.
2. **Does the annotation decide it?** `Child?[]` genuinely may hold nulls and the lift is right there. The
   question is only what happens for `Child[]` and for oblivious contexts.
3. **The golden manifest moves.** Every collection with a user-declared element map re-emits, so the
   byte-identity corpus and its snapshots move with it — a large, reviewable diff that wants its own commit.
4. **A regression test per branch**, RED first, per the standing rule.

## What is NOT the fix

* A private non-validating core — measured above, buys nothing (result 1).
* `SetCount` + span fill for reference elements — already measured as a non-win in
  `Issues/round26/FINDING-list-fill-strategy.md` (1.00x, then 0.92x). The `List` category needs no work:
  the seven-decade sweep puts it at parity with both rivals and AHEAD at N = 10^6 (1.24x Mapperly,
  1.32x Mapster). The "1.13x behind Mapster" in `2026-08-24-premerge-full-sweep.md` does not reproduce.
