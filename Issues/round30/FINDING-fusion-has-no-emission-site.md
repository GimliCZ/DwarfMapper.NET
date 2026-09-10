<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: map fusion has no emission site — it is an analyzer, not an emitter

**2026-09-10**, on returning to round 30 to build the emitter the refusal spec had just authorised.

The spec's §6 lists what the emitter needs and §7 declared it authorised once no row remained PROVE. Both
were written without ever asking the question that decides whether an emitter is the right shape at all:

> **Does the generator ever emit `MapBC(MapAB(x))`?**

It does not. And nothing measured so far would have revealed it, because every arm of every fusion
experiment called the chain **by hand**.

## The evidence

**1. No generated file chains two maps.** Scanning all `*.g.cs` under `tests/`, `samples/`, `benchmarks/`
and `src/` — 624 generated files, the whole corpus plus the gallery plus the clean-corpus consumer — for a
map call whose ARGUMENT is another map call:

```
generated files scanned: 624
nested-call occurrences: 52
```

and all 52 are `Add(__DwarfMap_Obj_…(item))` — one element map inside a collection's `Add`. That is a
container transform wrapping a single map, not `A -> B -> C`.

**2. It is structural, not incidental.** `MemberMap.ConverterMethod` is a single `string?`. A member carries
**one** converter. There is no representation in the model for "convert, then convert again", so no emitter
can produce one however the consumer's types are shaped.

## What that means for the chain the spike measured

`Issues/round29/SPIKE-map-fusion.md` measured `MapBC(MapAB(a))` against `MapAC(a)` and found the intermediate
is not elided inside a per-element loop. `FusionProbeBenchmarks` re-measured it against real generated maps
and got byte-identical numbers. **Both are correct and neither is emitted code.** In both, the chain is
written by the probe:

```csharp
for (var i = 0; i < r.Length; i++) { r[i] = _m.MapBC(_m.MapAB(_src[i])); }   // the BENCHMARK writes this
```

That is a call site in a CONSUMER'S method body. A source generator contributes new source; it does not
rewrite the consumer's existing statements. So there is nothing for a fused emission to replace.

## What the feature actually is

An **analyzer plus code fix**, not an emitter:

* **Analyzer** — the consumer writes `MapBC(MapAB(x))`, and especially writes it inside a loop. Report that
  the intermediate is allocated per element and that a direct `A -> C` map would remove it.
* **Code fix** — declare `partial C MapAC(A a);` on the same mapper and rewrite the call site to use it.
  This repository already has the machinery: `ConvertToRecordStructCodeFixProvider` does a
  multi-declaration, solution-wide rewrite with a title that warns about call sites.

Everything already built survives the reframing, which is why this is a redirection rather than a loss:

| artefact | still valid because |
|---|---|
| the measurement (0.55x allocation, 1.79x at N = 1,000 in a loop; nothing straight-line) | it quantifies what the CONSUMER gains by taking the suggestion |
| the six resolved refusal rows | they become the analyzer's SUPPRESSION rules — when not to suggest it |
| `FusionObservabilityTests`' criterion | it is the sharpest suppression rule of all: if `C` ignores a member of `B` whose production runs user code, suggesting the direct map would delete that work |
| `MapFusionEquivalenceTests` | the fix must not change behaviour, and this is the oracle that says so |

The straight-line finding also becomes an analyzer rule rather than a caveat: outside a loop the JIT already
stack-allocates the intermediate (both arms allocate 40 B), so **the analyzer must not fire there** — a
suggestion that buys nothing is noise.

## The lesson, and it is the round's fourth of this shape

The refusal spec was careful, measured, and answered the wrong question. Six PROVE rows were driven to zero
by real experiments, and every one of them asked *"is fusion SAFE here?"* — none asked *"is there anywhere
to apply it?"* The check that settles it is one regex over the generated corpus and one look at
`MemberMap.ConverterMethod`, and it was available on the first day.

**Before designing how to do a thing safely, establish that the thing has a place to happen.**

## Status change

* `SPEC-fusion-refusal-list.md` §7 said "the emitter is now authorised". **Withdrawn** — there is no emitter
  to authorise. The table's rows are retained as the analyzer's suppression list.
* Round 30 item B becomes: *design a DWARF diagnostic + code fix for a consumer-written chain*, with the
  refusal rows as suppressions and the straight-line case as a non-firing rule. Not started; the design
  question is which diagnostic id and whether the analyzer can see enough of a loop to be sure.
