<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 30 — the instruments that are trusted for a dimension nobody checked they can see

Carried out of round 29 on 2026-09-07. The head item below arrived attached to the `[GenerateView]`
endpoint, which was withdrawn the same day
(`Issues/round29/WITHDRAWN-generated-views.md`) — **the finding is not view-specific and must not leave with
the feature.** It was recorded only in that task's report, which is not tracked by git; this file is its
home.

One disease, four instruments: *an instrument is cited for coverage it structurally cannot provide.* Each
one below is green, correct about what it measures, and quoted for something else.

---

## 1 (head item) — the corpus cannot express a customized NESTED pair

**Every nested pair in the entire generator corpus is an identity map.** `ViewMatrix`, `GoldenCorpus`, the
`FeatureInteractionCompileMatrix` and the snapshot suite all put customization — converters, renames,
directives — on the **OUTER** pair only, and generate identity members at every level below it.

**How it was found, and why that matters.** Two defects in *new* code, in the nested-construction seam,
survived **8,778 passing tests** and were caught by review instead:

- a nested view needing the mapper instance was constructed without it (`CS7036` in the consumer's
  `.g.cs`), because "needs the owner" was decided by the child's own resolution while the *parent* is what
  writes the constructor call;
- a parent left naming a nested view that was itself refused, so the emitted type referenced a type nothing
  declared.

Neither is subtle. Both were invisible because **no corpus case has an instance converter inside a nested
pair**. This is the repository's own recorded `test-infra-holes` pattern in its strongest instance yet: the
bug hid behind a corpus hole, not behind clever code, and the code was brand new.

**It is a SCHEMA hole, not a missing case.** Adding four more shapes does not close it — the generators
themselves only decorate the outer pair, so any number of generated cases stays blind. The fix is to teach
`CombinatorialSchema` / `GoldenCorpus` / the FIM to place customization at depth, which moves the golden
manifest and the compile matrix and therefore deserves to be its own measured change rather than a rider on
a bug fix.

**What it invalidates today:** "N warning-free cells" means *N cells of the shapes the schema generates*.
It is not a statement about nesting. Any claim of nested-mapping coverage drawn from these four instruments
should be read that way until the schema changes.

---

## 2 — the golden manifest and CompilerTests are both nullable-BLIND

`GeneratorRunner` defaults to `NullableContextOptions.Disable`, so the golden manifest pins emissions
produced with nullable reference types **off**; `DwarfMapper.CompilerTests` is nullable-disabled **and**
errors-only. Both have been cited for coverage of nullable-annotated consumer code, which neither can see.
The round-30 sweep must cover the verification **triad**, not the golden corpus alone.

---

## 3 — `DwarfMapper.NegativeCases` is the only thing pinning REMEDY WORDING

…and only for the ids that happen to have a case. Combined with the golden manifest's opaque-hash,
regenerate-on-move workflow, **a remedy sentence can be deleted from a diagnostic and the whole suite stays
green.** Under the project's own mandate — tell the user, at the exact location, what is wrong and how to
fix it — the remedy text *is* the product, and it is unprotected.

---

## 4 — the round-29 GC experiment was a NULL instrument

`Issues/round29/PlanProbe2.cs` says in its own comment that both arrays are live in both arms and the
difference is only which one the JIT keeps used. So "removing reference members did not speed the GC scan"
is **not supported by that experiment**; the honest statement is "no gain measured, and this experiment
cannot prove there is none". Recorded here because it is the same disease outside the test suite: a
measurement quoted for a question it cannot answer.

---

## The shape to fix, not just the four instances

Each of these is a *right* measurement quoted where a different population was assumed. The generalisable
rule the round should mechanise: **a number in this repository carries its population in the same breath**,
and an instrument that cannot see a dimension says so where it is cited, not only where it is defined.

## ImmutabilityProof is outside every mutation leg (found 2026-09-07, round 29 task 3.1)

`[MapShare]` decides whether a member's reference may be shared instead of copied. A wrong `true` from
`ImmutabilityProof` silently couples two object graphs the consumer believes are independent — there is no
diagnostic that recovers from it afterwards, and no test that would notice.

**It sits inside no leg's `mutate` globs**, so not one mutant probes the proof's own branch conditions. The
component whose wrong answer is worst is the one the mutation tier cannot see. That is this round's signature
failure shape, found for the sixth time.

Not fixed in round 29 because adding a file to a leg changes that leg's population and forces a re-run and a
re-pin — and the pipeline leg's `break` currently sits 0.28 pp above its measurement, which is less than one
mutant. Doing it carelessly turns a green gate red for reasons unrelated to the proof.

## The R3 ledger coupling checks consistency, not staleness (found 2026-09-09, round 29 Phase 3 gate)

`RatchetInvariantScanTests.R3_the_equivalents_ledger_counts_are_exactly_pinned_and_every_entry_is_proof_anchored`
is the guard that stops a mutation leg being re-measured without refreshing
`Issues/ledgers/equivalent-mutants.md`. Its coupling assertion is:

```csharp
Assert.True((int)Math.Floor(measured) == breakValue, ...)
```

It reads `measuredRawScore` from the ledger and `break` from the leg's Stryker config, and requires the
first to floor to the second. It caught a real defect once already (commit `fbaf1fc`, where `break` moved
76 → 78 with the ledger left at 76.99).

**But `scoreable` — the denominator — is cross-checked against nothing.** It is compared only to the
`rawCeiling` computed from itself, which is circular. So a ledger row reading

| pipeline | 242 scoreable | 78.28 % |

passes R3 against `break: 78` **while describing a population that no longer exists**. The pipeline leg's
scoreable population moved 242 → 304 when round 29 added `[MapShare]` and `[MapDenseEnumKeys]` to
`MapperExtractor.Members.Phases.cs`; the row survives that unchanged, because 78.28 still floors to 78.

The failure this permits is quiet and specific: the ceiling arithmetic, the survivor worklist counts, and
every "N survivors are an open worklist item" sentence in the prose go stale together, and the one test
whose job is to notice reads green. **`rawCeiling` being arithmetic over the pinned counts is not the same
guarantee as the pinned counts being current.**

The fix is a second anchor for the denominator — the leg's own most recent report JSON carries
`killed + survived + timeout + noCoverage`, which is exactly `scoreable`, and it is a file on disk under
`StrykerOutput/`. Anchoring the row to a named report (the way every adjudication entry is already anchored
to a proof file) closes it. Not done in round 29: `StrykerOutput/` is git-ignored, so the anchor needs a
retention decision — pin the report path and accept that CI cannot re-verify it, or copy the four counts
into the ledger and check *those* sum to `scoreable`. The second is cheap and is the recommended shape.

## The update-into idempotence fuzz uses a FRESH destination (found 2026-09-09, round 29 Phase 4)

`MetamorphicPropertyFuzzTests.Update_into_is_idempotent` is the suite's only property check on the
update-into endpoint. It builds its destination with

```csharp
var dst = Activator.CreateInstance(dstType)!;   // every member at its default
```

and then compares `dst` against the **source** after one write and after two.

**A fresh destination cannot distinguish a member the mapper leaves alone from a member it writes
correctly.** With every member at its default, "the mapper never touched this" and "the mapper wrote the
right value" are the same observation whenever the default coincides — and where it does not, the member
reads as a plain mismatch against the source rather than as evidence about the write set. Every partiality
defect is outside what this instrument can see: a member dropped from the write set by a resolution bug, a
directive that silently stops applying, an endpoint that quietly narrows what it assigns.

The consumer-facing half is closed — `DwarfMapper.Testing.LensLaws` (round 29 Phase 4) fuzzes both GetPut
and PutPut over a **populated** destination, and `Issues/round29/SPIKE-lens-laws.md` records the reasoning.
The generator's own fuzz was left alone deliberately: pointing it at a populated destination changes what it
measures, and any failures it then finds are real defects needing triage rather than a rider on a spike.
That is the round-30 task. Note before starting it that **PutPut does not hold for a conditional mapper**
(`[MapNullSkip]`, `When=`), so the generator fuzz can only assert GetPut across its whole synthetic schema —
PutPut needs the schema to say which cases are unconditional.

## ILVerify was pointed at an INCIDENTAL corpus (found and closed 2026-09-09)

The ILVerify stage's two targets were the shipped runtime and **the Gallery** — a corpus built to illustrate
the documentation, which happens to contain generated code. Two consequences, and the second is the one that
bit:

1. **Its coverage of emitted constructs is whatever the docs happened to need.** Nothing said the Gallery
   exercises every emission path, and nothing would have noticed if a new feature shipped without a Gallery
   example.
2. **It contains hand-written consumer code, so its findings need an excuse list** — a `stackalloc` demo in
   `18_SpanMap.cs`. And an excuse list over a mixed corpus cannot distinguish "the consumer wrote something
   unverifiable" from "we emitted something unverifiable". On 2026-09-09 an entry was added to it for
   `<PrivateImplementationDetails>::InlineArrayAsSpan` **which was caused by generator-emitted code**, and the
   entry read as reasonably as the `stackalloc` one beside it.

**Closed by `EmittedIlIsVerifiableTests`**: the golden feature corpus — which the repository already gates for
completeness — is compiled into one assembly, each case in its own namespace, and verified with **zero
permitted findings and no excuse list at all**. Its sources are declarations and partial method signatures, so
every method body in that assembly is the generator's and a finding there is ours by construction. A sabotage
control (a planted `stackalloc`) proves the pipeline reports rather than being silently misconfigured — which
it was on the first attempt, when a reference-pack mismatch turned every method into a load failure that
*looked* like a finding.

The Gallery stays a target, with its one hand-written excuse. The rule is now enforced where it cannot be
diluted.

**What this does not close:** the runtime assembly and the Gallery are verified as BUILT ARTEFACTS, so the
corpus test and the stage measure two different things — the corpus verifies what the generator emits today
from the golden sources, the stage verifies what actually shipped into a built DLL. Keeping both is
deliberate; noticing that neither alone is sufficient is the point of this file.

### Still outside every ILVerify target: the two netstandard2.0 assemblies

`DwarfMapper.Generator` and `DwarfMapper.CodeFixes` ship inside the `DwarfMapper` package (as
`analyzers/dotnet/cs/*.dll`) and are verified by nothing. They run in the consumer's **build** rather than
their program, which is why they are not in the "IL that reaches a consumer's process" argument — but they
are IL a consumer's machine loads and executes, from a package they installed.

Not added with the other targets because ilverify resolves them against a **netstandard2.0** reference set,
not the net10 pack every current target uses, and a target that cannot resolve its references reports load
failures that look like findings — the exact confusion the corpus test's control was written to catch.
Sizing that is round-30 work; recording it is not optional.
