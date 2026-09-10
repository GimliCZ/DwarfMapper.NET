<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 30 item G — refactor for full-surface testability

**Owner ruling, 2026-09-10:** *"the partial code coverages does not suffice, we want to have full coverage,
sanity and safety of code"* and *"systematically refactor all functions in the way, so they are testable at
full surface."*

This supersedes the standard recorded in `CODECOV-round29-patch.md`, which permitted leaving a guard
uncovered when its input was unreachable, on the argument that the honest record beats a contrived fixture.
That position is withdrawn. See §4.

## 1. The measurement, which is the spec

Taken with `roslyn-lens get_complexity_metrics` over `DwarfMapper.Generator`, threshold 15.

**Cyclomatic complexity is the lower bound on the number of test cases needed for branch coverage of a
method.** That makes "testable at full surface" a measurable property rather than a taste, and it makes the
worklist objective: a method at complexity 84 needs at least 84 cases aimed at *one* method, each of which
must set up the whole surrounding state to reach one branch. Decomposed into named units, the same branches
need a handful of cases each and every unit is independently assertable.

| # | method | cyclo | cognitive | LOC | nesting |
|---:|---|---:|---:|---:|---:|
| 1 | `MapEmitter.EmitMethod` | **84** | **128** | 322 | 4 |
| 2 | `MapperExtractor.HandleCollectionConversion` | 58 | 56 | 205 | 3 |
| 3 | `MapperExtractor.HandleDictionaryConversion` | 35 | 61 | 221 | **5** |
| 4 | `BlittableProof.TryExplainNearMiss` | 31 | 28 | 115 | 2 |
| 5 | `AggregateEmitter.EmitAmbientRegistration` | 30 | 44 | 175 | 3 |
| 6 | `BlittableProof.LayoutIdentical` | 29 | 22 | 79 | 2 |
| 7 | `CollectionConverter.Synthesize` | 29 | 13 | 63 | 1 |
| 8 | `MapEmitter.AppendValueExpression` | 28 | 50 | 106 | 3 |
| 9 | `CollectionConverter.TryResolve` | 26 | 41 | 117 | 2 |
| 10 | `MapperExtractor.TryResolveConversion` | 25 | 28 | 161 | 3 |

Twenty more sit between 15 and 24. The full list is reproducible with the command above; it is not copied
here so it cannot go stale against the code.

**Item 1 is the round's work.** `EmitMethod` alone carries more branches than items 5–10 combined, and it is
the method that decides what every generated map looks like — the highest-consequence code in the
repository sits at the worst measured testability.

## 2. What is NOT wrong, measured rather than assumed

* **No dead code.** `find_dead_code` over the generator and runtime returns exactly one entry,
  `System.Runtime.CompilerServices.IsExternalInit` — the netstandard2.0 shim that makes `init` accessors
  compile. It is referenced by the compiler, not by code, and removing it breaks the build. So the
  testability problem is **concentration, not cruft**: there is nothing to delete, only things to separate.
* **Low nesting throughout.** Max nesting is 4–5 at the worst and 1–3 typically. The methods are not
  tangled; they are *long*, built of many flat sequential arms. That is the easier shape to decompose —
  each arm is close to a method already — and it is why this is tractable at all.

## 3. The rule that makes it mechanical

> **Every branch is either reachable by some caller — then it gets a test — or reachable by none — then it
> is deleted, or replaced by a failure that IS reachable.**

A guard whose input no caller can produce cannot be tested, which under the owner's ruling means it cannot
stay. The three disposals, in preference order:

1. **Make the state unrepresentable.** A guard against a null that a constructor could refuse, or against a
   combination a type could forbid, is better removed by fixing the type than by testing the guard. This is
   the `RegistryTable` move from round 28 — two loose statics became one type and the "mark the wrong table"
   bug became unwritable.
2. **Promote to a reachable failure.** If the state is genuinely impossible, say so where a test can see
   it: a thrown exception a unit test can drive by calling the private surface directly, not a silent
   early-`return` no caller can produce.
3. **Delete.** If neither applies, the guard was speculative.

Each disposal ships its own regression test naming the finding, in the same commit — the standing rule from
2026-09-03 applies to deletions exactly as to fixes. For a deleted guard the test proves the input cannot
arrive.

## 4. The eight lines round 29 deliberately left

`CODECOV-round29-patch.md` §"What is deliberately left" recorded eight uncovered lines as defensive and
documented why. Under the new ruling they become worklist items 1–8 of this section, to be resolved by §3's
ladder rather than re-argued:

| file | lines | disposition to determine |
|---|---:|---|
| `MapEmitter.SpanMap.cs` | 4 | span map over a recursion-capable element — reachable; needs the fixture, not a deletion |
| `DictionaryConverter.cs` | 2 | `SourceKeyIsNullableRef`'s "implements no generic collection interface" arm |
| `MapperClassModel.cs` | 1 | `cut < 0` on a declaration with no space in it |
| `ConvertToRecordStructCodeFixProvider.cs` | 1 | null `AccessorList`; round 29 PROVED it unreachable through the diagnostic — so §3 rule 3, delete |

That last one is the clean case: round 29 already did the experiment showing the classifier declines a
computed property before the fix is offered. Proven unreachable is a deletion warrant, and the surviving
refusal test already pins the real behaviour.

## 5. Sequencing, and why it is not one commit

The generator is ratcheted: 63 pinned benchmarks keyed by BDN method name, a golden emission manifest, six
mutation legs, and coverage floors that fail the build when exceeded by ≥1.0 pp. **R1 (no ratchet moves
without a re-measure in the same commit) and R2 (a floor must EQUAL its measurement) apply to every commit
here.** A big-bang refactor of `EmitMethod` would move the golden manifest, the coverage floors and possibly
the mutation pins simultaneously, and nothing would be attributable.

Cadence: **one extracted unit per commit.** Each carries the extraction, the unit's own tests, and any
re-measured pin, and each leaves the manifest byte-identical or explains the diff. `scripts/extracted-reach.ps1`
before citing the manifest for moved code — byte-identity only locks what the corpus executes, and moved
code is exactly the case where reach and identity diverge.

Order: item 1 first and alone (it is the round), then 2–3 (same file, same shape, adjacent), then 4/6
(`BlittableProof`, one file), then the rest by descending cognitive complexity — cognitive, not cyclomatic,
because it tracks what a reviewer must hold in their head to approve the extraction.

## 6. Exit criterion

Not a coverage percentage. **No method in `src/` above cyclomatic 15 without a documented reason**, every
surviving branch reachable by a named test, and the eight round-29 lines dispositioned. A coverage number
rises as a side effect; chasing the number directly is how it stops meaning anything, which is the one part
of the round-29 standard that survives intact.
