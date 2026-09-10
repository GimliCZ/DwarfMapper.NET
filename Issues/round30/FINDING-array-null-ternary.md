<!-- SPDX-License-Identifier: GPL-2.0-only -->

# WITHDRAWN as a defect: the per-element null ternary is a decided behaviour, and it was decided on purpose

**Filed 2026-09-09, withdrawn the same day** after the owner pointed at the git history: *"Responses have
been to this already in past of git. search commits."* They were right, and the ruling is explicit.

The measurement below stands. **The claim built on top of it does not**, and it was the important half.

## What I claimed, and why it was wrong

I claimed the generator emits two different element expressions for the same question:

```csharp
__r.Add((__item is null ? null! : (FlatDst)MapFlat(__item)));   // user-declared converter
__r.Add(__DwarfMap_Obj_global__NfLine_global__NfLineDto_BDE0B74A(__item));  // synthesized converter
```

...and concluded that "two paths disagree about one question, and the slower one silently stores `null!`
into an array whose element type forbids null" — a de-silencing item.

**Both halves are false.** Reading the synthesized helper's body, which I had not done:

```csharp
private global::NfLineDto __DwarfMap_Obj_global__NfLine_global__NfLineDto_BDE0B74A(global::NfLine s)
{
    if (s is null) return null!;      // <- the SAME null-in, null-out decision
    return new global::NfLineDto { ... };
}
```

The two paths **agree exactly**. Both preserve a null element as null. They differ only in *where the test
lives* — and that difference is forced: a synthesized helper's body is ours to shape, while a user-declared
`public partial FlatDst MapFlat(FlatSrc s)` is the consumer's own method with the consumer's own signature,
so the only place we can put the decision is the call site.

## The ruling this re-litigated

`6fa7308` — *"fix(null): guard the nested edge when its converter is a map method the USER declared —
CS8604 in the consumer .g.cs, or a throw on every failed `Result<T>`"* — introduced
`NullHandling.NullableProjectRefForgiving` for precisely this cell, and named the motivating shape:

> Three arms — destination can hold null -> the existing `NullableProjectRef`; **destination cannot and the
> source is not nullable-annotated either -> the new `NullableProjectRefForgiving`
> (`x is null ? null! : Conv(x)`, the `Result<T>`/`Outcome<T>` shape whose Fail parks `default!`, where the
> `!` is what keeps CS8601 out of the generated file)**; destination cannot and the source IS
> nullable-annotated -> UNCHANGED, because that is the shape DWARF070 already names and lifting it would
> swallow it.

So the ternary exists so that **a failed `Result<T>` maps to null instead of throwing**, and the `!` keeps
CS8601 out of a generated file where no consumer `#pragma`, `NoWarn` or `.editorconfig` can reach it. That
is the same unsuppressible-warning constraint the corpus already gates. Removing the ternary would
reintroduce the exact behaviour that commit was written to fix.

`9520b9a` settles the neighbouring cell the same way and in the opposite direction — a *nullable-annotated*
element into a non-nullable target throws loudly rather than passing null through — so the two cells are
deliberately different, not accidentally inconsistent.

The corpus already pins all of it: `feat:NestedViaDeclaredMap` covers all three arms on one pair.

## What the measurement actually showed, restated honestly

`NullCheckProbeBenchmarks`, four hand-written arms over one payload draw, same process:

| N | Current | CoreCall (callee guard removed) | GuardOnly | Unguarded (no test anywhere) |
|---:|---:|---:|---:|---:|
| 100 | 564 ns | 567 (1.007x) | 514 (0.911x) | 513 (0.911x) |
| 1,000 | 6,119 | 6,610 (1.080x) | 5,737 (0.938x) | 5,529 (0.904x) |
| 10,000 | 70,846 | 71,843 (1.014x) | 65,316 (0.922x) | 62,424 (0.881x) |

1. **The callee's `ThrowIfNull` is free.** `Current` - `CoreCall` sits inside the combined standard error at
   all three sizes and the sign is inverted. A private non-validating core buys nothing. (This was the
   hypothesis the probe was built for; it is dead either way.)
2. **The call-site test costs ~9 %** — and that is now correctly read as **the price of null-preservation,
   not as waste.** `Unguarded` is not an available option: it is Mapperly's shape, and Mapperly's
   `MapFlat` would throw a `NullReferenceException` on a null element rather than mapping it to null.

So the `Array` category's 8-29 % gap against Mapperly is **a semantic difference we chose**, measured. It is
not an inefficiency, and there is no version of removing it that keeps the behaviour: moving the test into a
wrapper just relocates the same branch and adds a call.

## What survives as work

Nothing in item A as originally written. Two smaller, genuinely open questions:

* ~~Two different predicates for "may be null" coexist in the pipeline~~ — **RESOLVED 2026-09-10, and the
  suspicion was unfounded.** `UserConverterNullGuard` (`MapperExtractor.Conversions.cs:1288`) uses the STRICT
  `== Annotated` at every branch; the loose `SourceMayBeNullRef` belongs to a different arm entirely (the
  `Nullable<U>` value-destination composition). This cell is decided by one predicate, consistently.
  `ElementNullArmTests` now pins all four cells, and the fourth is a genuine finding the annotation rules do
  not predict: under `#nullable disable` the element gets **no null test at all**, because the gate asks
  `ConverterParamIsNonNullableRef` and an oblivious parameter is not non-nullable. So the `Result<T>`-Fail
  protection is absent in a nullable-disabled consumer project — defensible (no annotations, no promises),
  undocumented until now, and pinned so a change to it is deliberate.
* **Should `docs/COMPARISON.md` say this?** The `Array` row currently reads as a plain loss. It is a
  measured trade: we map a null element to null; Mapperly dereferences it. A reader deciding between the
  two libraries would want that sentence, and it is the sort of claim this repository normally makes.

## The lesson, recorded because it is the fourth of its kind this round

I read one side of an emitted pair, found an asymmetry, and built a correctness argument on it without
reading the other side's body or searching for the commit that created it. The repository had already
answered the question twice, in commit messages that name the exact shape. **Search the history before
declaring an inconsistency** — the design memory's *"verify before declaring a limit"* applies to the
generator's own decisions, not only to the runtime's.
