<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Spec: when map fusion must REFUSE

`Issues/round29/SPIKE-map-fusion.md` closed the measurement question and named this as the blocking design
task, in its own words: *"Fusion must be refused wherever `B`'s construction is observable, and enumerating
those cases is the first design task, not the emitter."*

The measurement is settled and re-verified against generator output: inside a per-element loop, fusing
`A -> B -> C` removes one allocation per element (0.55x allocation, 1.79x faster at N = 1,000); in
straight-line code the JIT already elides the intermediate and a fused emission is dead weight.

**This document is the refusal list.** It is written before the emitter because the emitter is the easy half
and a wrong refusal list is a silent correctness bug — fusion that skips an observable `B` changes behaviour
with no diagnostic and no test failure.

## The rule the list derives from

> Fusion is legal **iff** the only thing `B` ever does is carry values from `A` to `C`, within one call, in
> a chain nobody else can observe or intercept.

Anything that (a) runs code when `B` is built, (b) can see `B`'s identity, (c) can see `B`'s *type*, or
(d) gives a consumer a second way to reach the `A -> B` edge, defeats it.

## Status vocabulary

* **REFUSE** — fusion changes observable behaviour. Do not emit; no diagnostic needed (the chained code is
  correct, it is simply not fused).
* **SAFE** — `B` demonstrably carries values only.
* **PROVE** — plausible either way; must be settled by a test against emitted output before the emitter
  ships. Listed so none is quietly assumed safe.

Every row marked SAFE or REFUSE below on the strength of a code reading names what was read. Rows marked
PROVE are the honest remainder, and **the emitter does not ship while any row in its path is PROVE.**

## 1. Code that runs when `B` is built

| feature | status | why |
|---|---|---|
| `[BeforeMap]` / `[AfterMap]` on the `A -> B` pair | **REFUSE** | The hook is user code that runs at `B`'s construction. Fusing deletes the call. This is the headline case. |
| `[BeforeMap]` / `[AfterMap]` on the `B -> C` pair | **REFUSE** | The hook receives `B` as its source argument. There is no `B` to hand it. |
| A member of `B` that `C` does not consume, whose production runs user code | **REFUSE** | **MEASURED — see below.** This one rule subsumes the three that used to be separate PROVE rows. |
| A user-declared converter on a member `C` DOES consume | **SAFE** | The fused form still has to produce the value, so the call still happens, once. |
| `B`'s constructor (a `[MapConstructor]` / positional record) | **REFUSE** | A fused `A -> C` constructs `C`, never `B`, so a user constructor on `B` does not run at all. Whether its effects were observable is not decidable here, and a constructor that validates is the common case. |

## 2. Code that can see `B`'s IDENTITY

| feature | status | why |
|---|---|---|
| `ReferenceHandlingStrategy.Preserve` anywhere in the chain | **REFUSE** | `DwarfRefContext`'s identity table is keyed by SOURCE reference with `ReferenceEqualityComparer`. In the `B -> C` map the source is `B`, so the table is keyed on instances fusion would never create. Two `A`s that legitimately share a `B` would produce one `C` today and two under fusion. |
| `OnCycleStrategy` other than the default, where the cycle routes through `B` | **REFUSE** | The cycle guard is on-stack/depth state about the object being mapped. Removing `B` from the graph removes the node the guard is tracking. |
| `[MapShare]` / `[Reinterpret]` on a member of `B` | **PROVE** | Sharing means the destination aliases the source's storage. If `C` shares from `B` and `B` is elided, `C` would have to share from `A` directly — which may or may not be the same object. |
| `[MapCollectionKey]` upsert into an existing `B` | **REFUSE** | Upsert mutates a destination the caller supplied; there is no such `B` in a fused chain. |

## 3. Code that can see `B`'s TYPE

| feature | status | why |
|---|---|---|
| `[RoundTrip]` declared over `B` | **REFUSE** | The verifier's whole contract is that a `B` exists and round-trips. Named by the spike. |
| The ambient registry (`IDwarfMapper`, module-init self-registration) carrying `A -> B` or `B -> C` | **REFUSE** | A registered map is reachable by any assembly at run time without a reference. Fusing does not remove that reachability; it adds a second, differently-behaving path. This is caveat 4 generalised beyond "public". |
| `[MapDerivedType]` on the `A -> B` pair | **REFUSE** | The arm chosen depends on `A`'s runtime type and produces a `B` *subtype*. `C` may be selected from `B`'s type; fusion would have to re-derive that selection, which is a different algorithm rather than a shortcut. |
| `[ProvidesMap]` / hand-written provide for `A -> B` | **REFUSE** | The consumer supplied the implementation. It is not ours to inline. |

## 4. A second route to the `A -> B` edge

| feature | status | why |
|---|---|---|
| `A -> B` is a **public** partial map method on the mapper | **REFUSE** | Caveat 4 in the spike: fusing `A -> C` does not remove `B`, it adds a second path. The win only exists where the generator can see `B` is produced and consumed inside one call. |
| `A -> B` reachable via `[ReverseMap]` | **PROVE** | The reverse direction is a different pair; whether declaring it makes the forward edge externally reachable needs checking. |
| `[GenerateWrapperMap]` expanding over the pair | **PROVE** | `ExpandWrapperMaps` expands over exactly the pairs already declared as map methods — noted in `6fa7308` as the reason an envelope's payload edge is *almost always* a user-declared converter. The interaction with fusion is unexamined. |

## 5. Shapes where fusion is pointless rather than wrong

| shape | status | why |
|---|---|---|
| Straight-line single-object `Map(a)` chained through `B` | **DO NOT EMIT** | Measured: the JIT already stack-allocates `B`. Both arms allocate 40 B and tie on time. A fused emission here is dead weight that duplicates what the runtime does. |
| Projection / expression trees | **REFUSE** | A projection is translated by a query provider, not executed; the intermediate is a node in an expression, not an allocation. Different question entirely. |
| Update-into `Map(S, T)` | **REFUSE** | The destination is caller-owned. There is no intermediate to elide. |

## 6. What the emitter needs, once the list is settled

1. **A shape test** that decides a call site is inside an element loop — the only place fusion pays.
2. **The refusal check**, reading this table, applied BEFORE the shape test so a refused pair costs nothing.
3. **No diagnostic on refusal.** A refused chain emits the ordinary chained code, which is correct. Warning
   about it would be noise on a shape the consumer did not ask for.
4. **A regression test per REFUSE row**, each proving the chained form still ships — the RED being a fused
   emission where the row forbids it.

## 6b. The criterion that collapsed three rows, measured 2026-09-10

Three rows above were variations on one worry — a converter, a `[MapValue]`, or a constructor on the
`A -> B` pair "may have a side effect". Phrased that way each needs its own investigation and none is
decidable at generation time. Phrased sharply they are one rule:

> The chained form computes **every** member of `B`. The fused form computes only what `C` consumes. So any
> member of `B` that `C` does NOT read, whose production runs user code, is a side effect fusion deletes.

`tests/DwarfMapper.IntegrationTests/FusionObservabilityTests.cs` proves it is real rather than theoretical,
on the mapper's own emitted code: `FoMiddle.Tag` is produced by a converter and `FoTarget` has no `Tag`.

| form | converter calls, 1 object | converter calls, 50 elements |
|---|---:|---:|
| chained `MiddleToTarget(SourceToMiddle(x))` | **1** | **50** |
| direct `SourceToTarget(x)` — what a fused emission produces | **0** | **0** |

So chained and fused are **not interchangeable** for this pair, the divergence grows with N rather than
being a one-off, and nothing in the two type declarations hints at it. No amount of measuring allocation
would have surfaced it — which is the argument for this document existing before the emitter.

**Why this is a good criterion rather than merely a true one:** it is decidable at generation time from the
two member maps alone. No interprocedural purity analysis, no attribute for the consumer to remember. The
generator already knows which members of `B` the `B -> C` map reads.

## 7. Honest status

**Three rows are PROVE** (was six; §6b resolved three by measurement). Under the rule stated at the top — the emitter does not ship while any row in its
path is PROVE — this spec does not yet authorise an emitter. The next task is those six, and each is a
generator test over emitted output, not an argument.

That is a deliberate stopping point rather than a delay: the measurement half of fusion has been finished
twice (hand-written probe, then generator output, byte-identical), and the thing standing between it and an
emitter has always been this list rather than the code.
