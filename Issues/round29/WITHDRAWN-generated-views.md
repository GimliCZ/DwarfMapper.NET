<!-- SPDX-License-Identifier: GPL-2.0-only -->

# `[GenerateView<TSource, TTarget>]` — built, measured, and withdrawn on principle

**Owner ruling, 2026-09-07.** *"Dwarf mapper is safe by design and this pathway exposes concurrency and
disposal issues that can be close to impossible to diagnose."*

This file exists so the feature is not re-proposed without the argument that removed it. **A removed feature
with no recorded rationale gets rebuilt.**

## What is being recorded is not a failure

Phase 1 shipped **green**: eleven commits (`c27ec58`..`9f937b8`), the full tier passing, `View_Dwarf` pinned
at **0 B/op**, and the measured behaviour matching what the round-29 research predicted. Two real defects
were found and fixed inside it (`CS7036` from a nested view constructed without its owner; a parent left
naming a nested view that was refused), and the escape rework replaced a curated keyword list with the
language's own `@`. None of that is why it is going.

**It is going because its failure mode is wrong for this project**, and that is a design judgement about
what DwarfMapper is for, not a verdict on the code.

## The argument

1. **A view is a window, not a snapshot, and the window is per-member.** Each property evaluates *on
   access*. A source mutated between two reads therefore yields a combination of values that never existed
   at any single instant — a record of a thing that never was. In a library whose headline value is making
   **silent mislinking impossible**, shipping a second, subtler way to produce silently wrong data is a
   contradiction. The failure is not a crash, not a diagnostic, not a wrong type: it is a plausible-looking
   DTO with fields from two different moments.

2. **`ref struct` gives the lifetime half of a Rust borrow and none of the exclusivity half.** The compiler
   stops a view being stored in a field, boxed, captured or held across an `await`, so it cannot *outlive*
   its source. It says nothing at all about who else may **write** to that source while the view is alive.
   Rust's safety comes from `&` vs `&mut` — the exclusivity — and C# has no equivalent. C++'s
   `ranges::views` took exactly this half-guarantee and produced a documented hazard class (dangling and
   surprising-recomputation bugs) rather than a safe abstraction.

3. **A disposed source is still a live reference.** `ref struct` scoping cannot help here at all: the view
   reads a disposed object and does whatever that type does with a read after disposal — often nothing
   visible, sometimes an `ObjectDisposedException` from a line the consumer never wrote, in a `.g.cs` they
   cannot edit.

4. **The accepted precedents do not transfer.** `Span<T>`, `Utf8JsonReader` and `string_view` are all views
   over a **buffer read one element at a time**. Ours is a **multi-member projection over a mutable object
   graph**, and it is named after a DTO — `OrderDtoView` — which invites exactly the reading that is wrong:
   that it is the DTO, only cheaper. The name and the shape both promise a snapshot the type cannot deliver.

Note that 1–3 are not diagnosable by anything this repository can build. A generator cannot see the
consumer's threading, and cannot see a `Dispose` that happens in another method. There is no `DWARF###` that
closes this; that is the whole point of the ruling.

## If it is ever re-proposed

It needs an answer to the mutation question that does not rely on the consumer being careful — the
project's standing position is that "hold it correctly" is not a safety property. Candidates that would
change the argument, none of them cheap:

- a **materialising** endpoint that reads every member once into a `readonly struct` (that is `Map`, and
  `Map` already exists — which is the honest conclusion most of the time);
- a source type the generator can prove immutable, so there is no window to be wrong about;
- a language feature that expresses exclusivity, which C# does not currently have.

## What survived the removal, and why

- **`Identifiers.EscapeTypeName`** (`src/DwarfMapper.Generator/Core/Identifiers.cs`). Nothing about it was
  view-specific: it encodes the measured fact that a contextual keyword is legal as a *member* name and not
  as a *type* name, which is why it is a **sibling** of `Escape` rather than a widening of it — widening
  `Escape` would churn every golden file for no gain. It has **no production call site** today and is kept
  anyway: every type-declaration position the generator writes has the same exposure (a consumer may write
  `public partial class @record`, and `ISymbol.Name` hands back `record`), and task 1.y is queued to enforce
  its use. Its measurement is now pinned by
  `tests/DwarfMapper.Generator.Tests/Core/IdentifiersTests.cs`, which executes it directly rather than
  through an endpoint.
- **The size-gate reasoning** in `scripts/gate-checks.ps1`: that two package rows with identical RAW sizes
  and differing compressed ones are **not** "compression noise" — deflate is deterministic — but the
  baseline having been packed from a different worktree path, which leaks into the PE. That is a general
  correction to how a package-size diff is read and it survives the numbers changing.
- **The corpus blind spot** both view defects hid behind, carried to
  `Issues/round30/BLIND-INSTRUMENTS.md`. It blinds far more than views.

## What the ids do now

`DWARF102` (member cannot be viewed without allocating) and `DWARF108` (`[GenerateView(Name = …)]` is not a
usable type name) were **unshipped** — neither ever appeared in `AnalyzerReleases.Shipped.md`, so no
consumer can be suppressing or documenting either. They are removed rather than deprecated. `DWARF102`
returns to the reserved block in `AssemblyScanTests` with a note saying why; `DWARF108` is simply
unallocated again.
