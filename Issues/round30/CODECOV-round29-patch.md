<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round-29 Codecov patch report: what was covered, and what is defensive

Codecov on the round-29 PR: **patch coverage 90.9 %, 86 lines missing** across nine files.

**Reproduced locally before anything was written.** Collecting coverage across the solution and intersecting
uncovered lines with the lines the PR changed gives **41 uncovered + 45 partial = 86**, matching Codecov's
count and its per-file split. So the numbers below are the same measurement rather than a proxy for it, and
the line numbers are the ones it flagged.

## The result

| file | flagged | outcome |
|---|---:|---|
| `ImmutabilityProof.cs` | 34 | **19 closed** (15 uncovered + 4 partials), re-measured |
| `DenseEnumProof.cs` | 17 | all eight enum underlying types + control |
| `ConvertToRecordStructCodeFixProvider.cs` | 17 | 1 refusal test; **4 shown defensive** |
| `LayoutHygiene.cs` | 7 | both depth guards + control |
| `MapEmitter.SpanMap.cs` | 4 | not covered — see below |
| `BlittableProof.cs` | 3 | the `Nullable<T>` refusal, both operands |
| `DictionaryConverter.cs` | 2 | not covered — see below |
| `MapperClassModel.cs` | 1 | not covered — see below |
| `GeneratedNames.cs` | 1 | `IsUserConv(null)` |

## What the exercise actually found

**These were not arbitrary uncovered lines. They were refusal and termination arms**, which is the worst
place for coverage to be missing:

* `ImmutabilityProof`'s refusals decide whether a share is ALLOWED, and a share wrongly allowed aliases
  mutable state into a consumer's object graph. Its FIELD arm had nothing walking it at all, because every
  share the generator produces goes through properties.
* `DenseEnumProof`'s underlying-type arms decide which SLOT INDEX a member writes to. A misread constant
  puts a value in a different member's slot — silently, because every slot has the same type.
* `LayoutHygiene`'s depth guards are what make the walk TERMINATE on a consumer type nobody imagined.
* `BlittableProof`'s nullable refusal is what stops `[Reinterpret]` emitting a cast the consumer's own build
  rejects (CS0453).

Every set got a CONTROL, so a passing test means the arm fired rather than that the code refuses everything
it is handed.

## The negative result, which is worth more than the coverage it replaced

`ConvertToRecordStructCodeFixProvider`'s null-`AccessorList` guard in `WithInitAccessor` looks like an
obvious gap: an expression-bodied property genuinely has no accessor list. **It is unreachable through the
diagnostic.** Two experiments, not one:

| fixture | result |
|---|---|
| `public long Doubled => Id * 2;` | no `DWARF103` (the public member also trips `DWARF001`, having no source counterpart) |
| `private long Doubled => Id * 2;` | no `DWARF103` either — so the refusal is the computed property itself, not visibility |

The classifier declines a model carrying a computed property before the fix is ever offered. The surviving
test asserts that REFUSAL, which is real behaviour and was unpinned. Three more lines in the same file are
defensive for the same kind of reason: `compilation is null` cannot happen for a valid project, and the
bail-outs for a non-class declaring syntax or a syntax tree absent from the solution describe a `Solution`
the provider is never handed.

## What is deliberately left, and why

Eight lines across four files, all partial branches in private emitter paths:

* **`MapEmitter.SpanMap.cs` (4)** — branches inside span-map emission that depend on an element converter
  needing a `(ctx, depth)` tail. Reaching them needs a span map over a recursion-capable element, which is a
  real shape and a large fixture; worth doing when someone next touches span maps, not worth a contrived
  one now.
* **`DictionaryConverter.cs` (2)** — the `KeyValuePair` interface walk in `SourceKeyIsNullableRef`, whose
  untaken arms are "a source that implements no generic collection interface".
* **`MapperClassModel.cs` (1)** — the `cut < 0` arm of a declaration with no space in it.
* **`GeneratedNames.cs`** — covered.

**The standard applied here:** a guard whose input no caller can produce is DEFENSIVE, and the honest record
is why it is unreachable rather than a fixture that manufactures the impossible state. Chasing those to
100 % is how a coverage number stops meaning anything. This repository already keeps that distinction for
equivalent mutants (`Issues/ledgers/equivalent-mutants.md`); the same discipline applies to lines.

## Two of my own fixtures were wrong, and the toolchain caught both

* `DWARF001` refused a `Dst.Id` with no source counterpart — the diagnostic doing its job on a careless
  consumer type, in a test I wrote.
* The compiler rejected a two-argument `LayoutHygiene.MeasureMember` overload that does not exist; only the
  zero-depth wrapper is public, so that guard is reached through recursion rather than by driving a
  parameter no caller can drive.
