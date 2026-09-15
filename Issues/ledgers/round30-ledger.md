<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 30 ledger

Round 30 ran without an SDD ledger in `.superpowers/sdd/`, so this one is written here directly. It records what
committed history cannot: corrections to claims already made in commit messages. **History is never rewritten** —
a wrong claim stays in its commit, and the correction lives here, next to the evidence for it.

## Corrections to committed history

### `85b7eb0` — "the compiler drops a named argument … before it reaches NamedArguments" is half wrong

**The claim** (`refactor(ambient): GetRootConfig reads AutoValidate through TryGetNamedArgument`, 2026-09-13):

> The compiler drops a named argument that binds to nothing, or has the wrong type, before it reaches
> NamedArguments.

The claim was used to argue that the old loop's "value is not a bool" outcome was unreachable.

**Measured** (2026-09-14, a throwaway probe compiling `[assembly: DwarfMapperValidationRoot(…)]` against the
generator test harness's reference set and reading `Assembly.GetAttributes()`):

| Named argument | Compiler | `NamedArguments` | `Value is bool` | `Equals(Value, true)` |
|---|---|---|---|---|
| `AutoValidate = true` | clean | `AutoValidate`, `Primitive`, `True` | true | true |
| `AutoValidate = 1` | **CS0029** | **`AutoValidate`, `Kind = Error`, `Value = null`** | false | false |
| `Nope = true` | CS0246 | empty | — | — |

- **Binds to nothing — true.** `Nope = true` does not reach `NamedArguments`.
- **Wrong type — false.** `AutoValidate = 1` is a compile error, but the argument is **not** dropped. It reaches
  `NamedArguments` as an error-kind constant with a null value. The old `is bool` false arm therefore did have an
  input, just one that only a build already failing with CS0029 produces.

**Consequence: none for behaviour.** `85b7eb0` replaced `value is bool b && b` with `Equals(value, true)`, and
both give `false` for the error-kind constant. So the refactor was behaviour-equivalent on every input, the
compile-error one included. Only the stated reason was wrong. Nothing to change in code; the correction is the
record.

### `caef954` — "the scoreable denominator is unaffected" was checked against the wrong lines

**The claim** (`refactor(codefixes): thread the pair-scoped generic name so RestateBase carries no unreachable
non-generic arms`, 2026-09-14), under MUTATION IMPACT:

> Every mutant on the removed lines (245-248, 319-320, 325-329) was CompileError under Stryker Safe Mode, so the
> scoreable denominator is unaffected.

The first sentence is true. The conclusion does not follow from it, because the refactor also CHANGED lines it did
not remove, and the codefixes leg has zero headroom: 154/177 = 87.01 % against `break` 87.

**Measured**, each run through `scripts/housekeeping.ps1 -MutationLeg 'code fixes'`:

| run | tree | scoreable | killed | score | what it showed |
|---|---|---:|---:|---:|---|
| `2026-09-14.21-01-28` | af59cee, before the refactor | 177 | 154 | 87.01 % | baseline |
| `2026-09-14.21-30-01` | first cut of the refactor | 157 | 137 | 87.26 % | **Safe Mode dropped every mutant in `WithRestatement`** |
| `2026-09-14.21-56-25` | caef954 | 179 | 155 | **86.59 %** | below break; one new survivor |
| `2026-09-15.18-27-07` | 7f96555 | 179 | 156 | 87.15 % | passes; the 23 undetected are the 23 ledger rows |

- **The first cut passed by accident.** It tested with `if (PairScopedName(attribute) is not { } generic) continue;`.
  Stryker negates that pattern, which leaves `generic` unassigned (CS0165). Stryker cannot attribute that compile
  error to a single mutant, so Safe Mode removes every mutation in the enclosing method. About 20 mutants vanished
  from `WithRestatement`, four ledger rows among them, and the score went UP. caef954 is the amended commit: a plain
  local and an `is null` test, with a comment saying why.
- **On the lines caef954 changed:**
  - three killed mutants no longer exist (line 126's LogicalNot on `IsPairScoped`, and the two Boolean `return false`
    flips at 248/254);
  - the new `PairScopedName` ternary and `Retarget`'s ternary, which is now scoreable, added five.
  - Net: killed +1, survived +1, giving 155/179. The new survivor is `true ? generic : null`: the existing
    three-type-argument look-alike names the source first, so no one-argument reading could match it.
- **7f96555** kills it with a look-alike that names the base TARGET first (RED against the planted mutant).

**Consequence:** no behavioural error, and the leg now stands at 87.15 %. The records carry the new denominator:
`legs.codefixes` in `equivalent-mutants.md`, the per-leg summary, and `codefixes-mutation-survivors.md`. **The
lesson** is recorded for the rest of the sweep: before a refactor on a leg with no headroom, diff the mutation
report on CHANGED lines, not only on removed ones. Grep the leg log for `Safe Mode` on every file touched.

## Owner rulings

### Runtime mutation floor, and `DwarfMapperRegistry.Key` (2026-09-14)

**Ruling:** `DwarfMapperRegistry.Key` becomes a `readonly record struct`. The runtime mutation leg's `break` stays
**97**. It is not lowered.

**Why it came up.** The runtime leg measured **95.45 %** (126/132, `StrykerOutput/2026-09-14.19-15-03`) against a
`break` of 97. A position-tolerant diff against the last pinned run (`2026-09-07.06-30-16`, 97.60 %, 122/125)
found three things:
- one real survivor from `77dc345` (`DwarfMapperRegistry.cs:222`, `candidates ??= …` → `=`), killed by
  `5fe0d76`;
- two `DwarfRefContext` depth-clamp boundary mutants gone Killed → Survived with no commit to the file. Both are
  provably equivalent. Their 2026-09-07 "kills" were `static`, `coveredBy = 0`, and credited only to a
  docs-generation test (`GeneratedDocsAreCurrentTests.The_api_reference_matches_the_public_surface`). They were
  accidental, and the pin had counted them;
- two permanently undetected mutants in the hand-written `Key` equality: `Equals(object)` NoCoverage, and
  `Equals(Key)`'s `&&` → `||`, ruled-in-practice.

With `Key` hand-written, the honest ceiling was 127/132 = 96.21 %, so 97 was unreachable.

**Options put to the owner:**
- **(A)** make `Key` a record struct. Compiler-generated equality has no source to mutate, so the two `Key`
  mutants leave the population: 127/130 = 97.69 %, floor held. Behind the registry's allocation and cost gates,
  because it is hot-path code.
- **(B)** re-pin the floor at 96.

**(A) was chosen.** Equality stays field-wise over the same two `Type` references, so lookup behaviour is unchanged;
the runtime leg's re-measure confirms the score.

### `MapConfig` has no runtime coverage, by design (2026-09-14)

**Ruling:** the only runtime test that instantiated `MapConfig<TSource, TTarget>` —
`IntegrationTests/MapConfigRuntimeTests.MapConfig_surface_compiles_and_chains` — is retired. `MapConfig`'s runtime
line coverage is **0 by design**, not a gap to close.

**Why.** `MapConfig` is compile-time-only surface. Its constructor is private, and every member is `=> this`. The
generator reads configuration method bodies syntactically and never executes them, so in a real application no
`MapConfig` member ever runs. The retired test built its instance with
`Activator.CreateInstance(typeof(MapConfig<S, T>), true)`: reflection bypassing a private constructor, which the
standing rules forbid, and which predates round 30. It covered six members and could never reach the other five
(the converter `Map`, `MapWhen`, the reference-type `MapOr`, the computed `Value`, `Construct`) without extending the
same bypass. The members can't be deleted either: `PublicAPI.Shipped.txt` ships them.

**Options put to the owner:**
- **(1)** retire the reflection test, and rely on the generator and compile tests that already prove every
  operation type-checks and is read;
- **(2)** exempt `MapConfig` from runtime coverage but keep the test;
- **(3)** add an `EditorBrowsable(Never)` factory so a test can instantiate without reflection.

**(1) was chosen.** (3) would grow public API only to serve a test.

**What still guards the surface:** `Generator.Tests/MapConfigGeneratorTests` and
`Coverage/MapConfigOperationCoverageTests` compile every operation, and assert what the generator emits for it. Every
other test in `MapConfigRuntimeTests` runs the generated mappers.

### Roslyn null-return guards in the code fixes are a named exemption (2026-09-15)

**Ruling:** the code fixes' guards against a Roslyn workspace API returning null stay in the source. They are one
named exemption class from the 2026-09-10 rule that every branch is reachable-and-tested or removed. **No `!`, no
throw, no behaviour change.** Their uncovered lines are the exemption, not a gap.

**The class** (line numbers as of the commit that adds `IsTypeHandle`, which shifted `ConvertToRecordStruct` by +16):

| provider | lines | guard |
|---|---|---|
| `AddMapIgnoreCodeFixProvider` | 36-38 | `root is null` after `GetSyntaxRootAsync` |
| `AddReverseMapInverseCodeFixProvider` | 38-40 | `root is null` after `GetSyntaxRootAsync` |
| `ResolveExplicitOnlyMemberCodeFixProvider` | 47-49 | `root is null` after `GetSyntaxRootAsync` |
| `RestateBaseConfigurationCodeFixProvider` | 51-53 | `root is null` after `GetSyntaxRootAsync` |
| `ConvertToRecordStructCodeFixProvider` | 265-267 | `compilation is null` after `GetCompilationAsync` |
| `ConvertToRecordStructCodeFixProvider` | 326-328 | `documentId is null` after `Solution.GetDocumentId(tree)` |
| `ConvertToRecordStructCodeFixProvider` | 352, 356-358 | `target is null` after `GetDocument`; `documentRoot is null` after `GetSyntaxRootAsync` |
| `ConvertToRecordStructCodeFixProvider` | 313, 389 | `GetDocumentationCommentId() ?? model.Name` |

**Why they are unreachable here.** Each API's contract permits null, but not for the inputs a C# code fix is handed.
- A code fix is registered only against a diagnostic in a C# source document. Such a document always supports
  syntax trees, and its project always supports compilation.
- A declaring syntax tree of a source symbol belongs to a document of the solution that compiled it, and an id the
  solution just returned always names one of its documents.
- A named type resolved through `DocumentationCommentId.GetFirstSymbolForDeclarationId` has an id by construction.

**Why they stay, not `!`.** The round-27 ledger already recorded the reasoning (`codefixes-mutation-survivors.md`, "The
`root is null` early return"): this is a defensive check on an API whose contract genuinely permits null, not dead
code that no input can reach. A `!` would trade a dead line for a `NullReferenceException` inside the consumer's
lightbulb, on the day a host meets that contract.

**Options put to the owner:**
- **(1)** a named exemption;
- **(2)** replace each guard with `!` plus a reason line, retiring the four `root is null` proven-equivalent ledger
  rows and their R3 pins;
- **(3)** throw `InvalidOperationException` instead of returning. Loud, but still unreachable, so coverage would not
  close.

**(1) was chosen.** Mutation is unaffected: the four `root is null` returns stay proven-equivalent rows in the codefixes
leg, and `ConvertToRecordStructCodeFixProvider` is not in that leg's mutate globs. **Scope:** only the rows above. A
new null guard is not covered by this ruling; it needs its own proof that no input reaches it.

### A `DWARF103` handle without its `T:` prefix is refused at registration (2026-09-15)

**Ruling:** the record-struct fix offers no action for a `TransferModelId`, or any nested id, that does not start with
`T:`. This replaces `9317064`'s pin, which recorded today's behaviour: one action, titled by the last segment,
rewriting nothing.

**Why.** `DocumentationCommentId.GetFirstSymbolForDeclarationId` resolves only a prefixed id, so an offered action for
a prefix-less handle is a lightbulb that promises a rewrite and silently delivers none. The generator never writes
that shape, so only a hand-built or foreign diagnostic carries it. Like the generic refusal beside it, the check reads
the id STRING: it costs no compilation, and an unoffered fix is a non-event.

**Options put to the owner:** refuse at registration, or keep the pinned no-op. **Refusal was chosen.**
