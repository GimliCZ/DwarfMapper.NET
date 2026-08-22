<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Generator compile cost at 1,000 mappers (2026-08-23) — round 23, S3

The measurement half of S3. **Nothing here is a gate**, and that is the point of the file: these are
wall-clock numbers, invariant **R4** forbids gating on a nondeterministic oracle and H7's discipline is that
no gate reads a clock, so the time is *recorded here* and the *deterministic* property is what
`tests/DwarfMapper.CompilerTests/GeneratorCompileCostTests.cs` actually asserts. S2 set the same split for
the runtime benchmarks (alert-only, never build-failing).

## Environment

```
Windows 10 Pro 10.0.19045 ; 12 logical cores
SDK 10.0.101 (global.json pin, rollForward disable — ISSUE-038)
Debug build of the test corpus; generator run through CSharpGeneratorDriver, no MSBuild in the loop
Branch feat/round23-product at 1a7bf3e
Corpus: K0's TypeGraphRenderer over PinnedSampling's deterministic draws (TypeGraphGen.MirroredPair),
one mapper per namespace, all in ONE compilation. No second renderer was written for this row.
```

## Time to first emit

`RunGenerators` on a **plain** driver (no `trackIncrementalGeneratorSteps` — a consumer's build does not
track steps, and the bookkeeping is not free), over a compilation that already exists; the parse/compilation
construction is timed separately because it is Roslyn's cost, not the generator's.

| Mappers | Parse + compilation | Time to first emit | Per 1,000 mappers | Generated files | Generator diagnostics |
|--------:|--------------------:|-------------------:|------------------:|----------------:|----------------------:|
| 40      | 105 ms              | 858 / 880 / 1002 ms | *see caveat*     | 42              | 4                     |
| 1,000   | 366 / 374 / 398 ms  | 3,637 / 3,502 / 3,762 ms | ~3.5-3.8 s  | 1,002           | 116                   |

**~3.5-3.8 seconds to generate a thousand mappers** (three runs, R4 spread ~7 %), plus ~0.37-0.40 s to parse them. 1,002 generated files for
1,000 mappers: one per mapper, plus the aggregate extensions facade and the ambient-registry manifest.

**The caveat, and it is the reason the fast tier does not print a normalised figure.** 880 ms at 40 mappers
normalises to **22.0 s per 1,000** — a **6× overstatement** of the ~3.6 s the deep tier actually measures,
because at N=40 almost the whole number is one-off JIT and first-run warm-up. The test therefore prints the
per-1,000 figure only at N ≥ 500 and says so at smaller sizes. A wrong number that is printed gets quoted.

## What is gated instead — and why it is the better gate

A generator's consumer-facing cost is not one cold build; it is every keystroke afterwards. Measured with a
tracked driver at **both** corpus sizes:

| Edit | Per-mapper extraction re-run | Source files re-emitted |
|---|---:|---:|
| identical re-run, 40 mappers | 0 / 40 | 0 / 44 |
| identical re-run, 1,000 mappers | 0 / 1,000 | 0 / 1,004 |
| unrelated edit (a new file touching no mapper), 40 | 0 / 40 | 0 / 44 |
| unrelated edit, 1,000 | 0 / 1,000 | 0 / 1,004 |
| **one mapper edited, 40** | **1 / 40** | **2 / 44** |
| **one mapper edited, 1,000** | **1 / 1,000** | **2 / 1,004** |

The recompute counts are **constant in the corpus size** — 1 and 2 at forty mappers and at a thousand. That
is the property a wall-clock ratio was only ever going to observe indirectly, it is deterministic, and it is
what the test asserts. The two re-emissions are the edited mapper's own file and the single aggregate facade
that lists every mapper (one file by design, so it necessarily re-emits whenever any mapper changes).

## Sabotage demo

The gate has teeth, demonstrated against the product rather than against the test: `MapperClassModel` — the
record the pipeline's per-mapper step produces — was temporarily given an `object SabotageTag` first
positional member (`new object()` at all three construction sites), which is exactly "a model that stopped
being value-equatable". Two of the three facts red, naming the counts:

- `Editing_one_mapper_recomputes_a_constant_amount_of_work`: *"editing ONE mapper re-extracted **40 of 40**
  (step 'DwarfMapperExtract'), expected exactly 1."*
- `An_unrelated_edit_leaves_every_mapper_in_the_corpus_cached`: *"step 'DwarfMapperAmbientRegistration'
  produced 1 changed output(s) of 1 after an edit that touched no mapper at all."*

Reverted by explicit pathspec; re-run green.

## Three measurement traps found while writing this

1. **`RemoveSyntaxTrees` + `AddSyntaxTrees` is not an edit.** Modelling the one-mapper edit that way appends
   the replacement at the END of the tree list, reordering every tree after it, and **all 40 mappers
   recomputed** — an artefact of the modelling, not a finding about the generator. `ReplaceSyntaxTree`
   replaces in place, which is what an editor does, and the count drops to 1. Anyone re-measuring this must
   not repeat the first mistake and file it as a regression.
2. **"Did the step run" is the wrong question, and it reads red on a healthy tree.** The first version of
   the gate counted every reason other than `Cached`, on the reasoning that `Unchanged` (ran, produced an
   equal value) still costs CPU. Measured on an *unmodified* generator, that metric reports
   `DwarfMapperExtract` re-running for **all N mappers** on any compilation change — because
   `ForAttributeWithMetadataName`'s transform receives a semantic model, so Roslyn re-executes it for every
   attributed class whenever the compilation changes, and no generator can opt out. The strict metric
   therefore measures the host and is red at baseline. What DwarfMapper controls is whether the re-run
   produces an EQUAL model, which is what decides whether anything is re-emitted — so the gate counts
   changed VALUES (`Cached` or `Unchanged` = no change), the repository's existing `GeneratorCacheAssert`
   idiom. A first, weaker sabotage attempt (wiring `.Combine(context.CompilationProvider)` into the
   per-mapper step) is invisible to both metrics for the same reason, and correctly so: it changes nothing
   the baseline was not already doing.
3. **Roslyn's own steps always re-run.** `Compilation` and
   `compilationAndGroupedNodes_ForAttributeWithMetadataName` come back non-cached on *any* compilation
   change — including the unrelated-edit case where every DwarfMapper step is cached. Asserting over them
   would assert a property of the host, so the test scopes its caching claims to `DwarfMapper*` plus
   `SourceOutput`, with a floor on how many steps must match so a rename cannot make the assertion vacuous.

## Corpus cost

`CompilerCostCorpusMappers` in the deep-tier catalog: fast **40**, deep **1,000**. The whole corpus is built
once per test class and the primed driver is re-run against each edit, so the four facts cost one parse and
one prime between them rather than four.
