# Spec: the arena result shape — a decision for round 30

Phase 4 spike. The plan's instruction was: *"prototype `MapArena` returning `(OrderRangeS[] Orders,
LineS[] Lines)` for one shape, measure against §12 B, and write the spec for a round-30 decision."* The
measurement exists. **This document exists because the performance case is settled and the contract case is
not**, and shipping it without settling the second would be the same mistake the view feature made.

## What it is

The dominant nested shape in real code is a parent owning a list of children — an order with its lines, a
message with its attachments, a customer with their addresses. Mapped conventionally, each parent allocates
its own `List<Child>`, so N parents produce N+1 allocations and the children are scattered across the heap.

An arena maps that shape into **two flat arrays**: every child of every parent, contiguous, plus a parent
array whose entries carry a `(start, length)` range into it.

```csharp
// conventional
OrderDto { …; IReadOnlyList<LineDto> Lines; }        // N+1 allocations, scattered children

// arena
(OrderRangeS[] Orders, LineS[] Lines)                 // 2 allocations, children contiguous
OrderRangeS { …; int LinesStart; int LinesLength; }   // reads as Lines.AsSpan(LinesStart, LinesLength)
```

## Measured

`Issues/round29/plan2-results.md` (`B_lines`) and `plan3-results.md` (`B_arena`), hand-written probes,
BenchmarkDotNet ShortRun, Ryzen 5 5600 / Windows / .NET 10.

| N = 100,000 | time | ratio | allocation | ratio |
|---|---:|---:|---:|---:|
| classes, `List<Line>` per order | 37,738 µs | 1.00 | 29.6 MB | 1.00 |
| per-order arrays | 11,643 µs | 0.31 | 12.0 MB | 0.41 |
| **arena** | **3,773 µs** | **0.10** | **9.6 MB** | **0.32** |

Build-and-read, same source: classes 51,577 µs → arena 8,716 µs (**0.17×**).

**Ten times faster and a third of the memory.** It is the largest single result in the round's research, and
larger than anything actually shipped.

**Read these numbers with the round's own caveat.** They come from **hand-written** arena types, not from
generator output, at two data points with three iterations. Every other figure in this round that was quoted
without checking its population turned out to describe something the reader did not assume — five times. The
arena's advantage is structural (fewer allocations, contiguity) rather than incidental, so it should survive
generation, but *should* is not *does*.

## Why this is not a build decision yet

**The arena changes the result contract, and that is a different kind of change from everything else this
round shipped.** `[MapShare]` and `[MapDenseEnumKeys]` change how a member is produced; the consumer's type
is unchanged and existing code compiles. An arena changes **what the consumer receives**:

- `order.Lines` no longer exists. Reading a child list becomes `lines.AsSpan(o.LinesStart, o.LinesLength)`.
- Anything that accepts an `OrderDto` and expects `Order.Lines` — a serializer, a view model, a mapper
  further down, an existing method signature — does not compile against an arena.
- The two arrays must travel together. Handing `Orders` to one component and `Lines` to another silently
  breaks the ranges, and nothing in the type system prevents it.

That last point is the one that most resembles the view feature's failure, and it deserves the same
scrutiny: **a range into an array nobody kept is not a compile error, it is wrong data.** Unlike the view,
there is no `ref struct` to make the mistake unrepresentable — a `(T[], U[])` tuple is freely storable and
separable.

## What would have to be true to build it

1. **A shape that binds the two arrays together**, so a range cannot outlive or be separated from its
   storage. A readonly struct holding both, with the range accessor as its only child API, is the obvious
   candidate. If the consumer can obtain a bare `OrderRangeS[]`, the feature has a silent failure mode and
   should be refused on the same grounds as views.
2. **Proof of the element shape.** The children must be blittable-eligible structs for contiguity to pay;
   that proof exists (`TransferModelShape`, round 29 Phase 2) and would gate the feature.
3. **A refusal diagnostic** naming, at the exact location, why a shape cannot be arena-mapped — a mutable
   child, a child that is itself a graph, a parent with two child collections (which needs two arenas or a
   composite, and is the first design question to answer).
4. **A consumer story for reading**, written before the emitter. If reading an arena is more awkward than the
   0.10× is worth, the feature is a benchmark result rather than a product.
5. **The measurement re-taken against generated output**, not the hand-written probe.

## The decision to take in round 30

**Option A — build it as an opt-in result shape** behind a binding struct and the refusal diagnostic above.
Highest payoff in the round's research; highest contract cost; needs (1) solved convincingly first.

**Option B — build the cheaper half.** Per-order arrays alone measured **0.31× time, 0.41× allocation**
without changing the contract at all: each parent still owns its children, they are simply an array rather
than a `List<T>`. That is two-thirds of the memory win and a third of the time win, for none of the API
disruption. **This is the option the measurement most supports and the one I would take first.**

**Option C — decline, and record why.** If (1) cannot be solved so that separating the arrays is impossible
rather than merely discouraged, the feature has the view's failure shape with none of the view's compile-time
protection, and it should be refused for the same reason.

## Recommendation

**Take Option B first and re-measure**, then decide A against the *residual* gain rather than against the
full 0.10×. The arena's headline is measured against `List<T>`-per-parent; if per-order arrays are shipped,
the arena's remaining advantage is 11,643 µs → 3,773 µs, and *that* is the number the contract disruption has
to justify — not the 37,738 µs it is usually quoted against.

That reframing is the substantive output of this spike: **the arena has been compared to the wrong
baseline**, and the right baseline is a change we could ship without any contract cost at all.
