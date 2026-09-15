<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The code-fixes leg — every undetected mutant, and why

Round 27. The leg was added after a scope audit found `DwarfMapper.CodeFixes` at **88.8 % line coverage and
0 % mutation coverage** — code thoroughly *executed* by tests that might assert nothing about it. It scored
**52.54 %** on its first run, which settled that question, and **87.01 %** after four kill batches.

This file carries the case analysis behind every mutant that remains. The machine-readable rows are in
[`equivalent-mutants.md`](equivalent-mutants.md); this is the prose they anchor to.

## The score, and what moved it

| run | scoreable | killed | survived | uncovered | score |
|---|---:|---:|---:|---:|---:|
| first measurement | 177 | 93 | 55 | 29 | 52.54 % |
| batch 1 — refusals and strings | 177 | 131 | 33 | 13 | 74.01 % |
| batch 2 — restatement shapes | 177 | 141 | 27 | 9 | 79.66 % |
| batch 3 — pair parsing, target selection | 177 | 150 | 20 | 7 | 84.75 % |
| batch 4 — argument-less attributes | 177 | **154** | 19 | 4 | **87.01 %** |
| round 30 — RestateBase refactor (caef954, 7f96555) | 179 | **156** | 19 | 4 | **87.15 %** |

Per provider at the end: `ResolveExplicitOnlyMember` 90.3 %, `RestateBaseConfiguration` 87.6 %,
`AddMapIgnore` 87.5 %, `AddReverseMapInverse` 80.0 %.

**`rawCeiling` is 87.70 %** — `(179 − 22) / 179`, re-measured 2026-09-15 after the round-30 RestateBase refactor
(was `(177 − 22) / 177` = 87.57 % at round 27). The measured 87.15 % therefore sits exactly one probably-equivalent
mutant below the highest score this leg can honestly reach, as the round-27 87.01 % did. That probably-equivalent
row was retired the same round (see "The one that is not proven" below), which leaves the ceiling itself reachable. The refactor removed three
killed mutants on the lines it changed and added the `PairScopedName` and `Retarget` ternaries; the one new survivor,
`true ? generic : null`, is killed by
`RestateBaseRestatementTests.A_three_type_argument_look_alike_naming_the_base_target_first_is_not_restated`.

## What the 61 kills were about

Not "more tests of the happy path". Three themes, each a whole class the existing tests could not reach.

**The refusals.** Every provider declines to offer a fix when its inputs are unusable — a dotted member name,
an empty or absent diagnostic property, a location outside the syntax the fix rewrites. Not one refusal was
exercised, and the reason is structural: the tests drove the providers through the *generator*, which by
construction only ever produces the shapes that succeed. Reaching a refusal needs a **synthetic diagnostic**
built with a chosen property bag and location.

**What the user sees.** Action titles and equivalence keys. A blanked title is not a crash — it is an empty
entry in the lightbulb menu, and `ResolveExplicitOnlyMember` offers two actions that differ *only* by their
text, so a blank one leaves the user choosing between identical-looking entries that do different things.

**Syntax a compiler would have rejected.** A code fix reads syntax, not a compiled model, so it has nothing
to tell it an attribute is unusable. `[MapIgnore<T>]` with no argument list and
`[MapProperty<S,T>(Use = …)]` with only *named* arguments are both legal things to type. Reading the
arguments without checking for their absence does not produce a wrong fix — it throws inside the user's
lightbulb.

### Two corrections to the kill program itself

Worth recording, because both were caught by mutants refusing to die rather than by review:

- **Two assertions were vacuous.** They searched the whole file for text the *base* attribute already
  contained, so they passed whether or not the fix preserved anything. Both are now pinned to the derived
  attribute specifically — `AliasCommandDto>(nameof(Command.Raw)` can only occur there.
- **One test rested on a wrong model.** It was built as a "replacement" case, but `existingByTarget` keys by
  *destination member*, and the base and derived attributes named different ones — so it was an addition and
  nothing was replaced. Reworked so both configure the same member, which incidentally made it pin the
  whitespace-insensitive comparison too, since the two texts then differ only in a value.

## The 23 that remain

### Reachable-but-indistinguishable, 4 occurrences each

**`ConfigureAwait(false)` → `ConfigureAwait(true)`.** Selects whether the continuation resumes on a captured
`SynchronizationContext`. It cannot change what the await *returns*, the syntax root is the only thing read
from it, and no thread-affine work follows. Distinguishing the two would mean asserting which thread
resumed.

**`getInnermostNodeForTie: true` → `false`.** Chooses between a node and its direct parent when the two share
an identical span. Every caller immediately walks upward with `FirstAncestorOrSelf<T>`; a tie means one
candidate *is* the parent of the other, so both have the same ancestors above the tied pair.

**The `root is null` early return.** Reached only when `GetSyntaxRootAsync` yields null, which happens for a
document that does not support syntax trees — and a code fix is only ever registered against a diagnostic in
a C# source document. **The guard stays in the source deliberately**, unlike the flatten `Nullable<TNode>`
branch deleted this round: that one was dead because no *input* could reach it, whereas this is a defensive
check on an API whose contract genuinely permits null. Deleting it would trade a dead line for a
`NullReferenceException` if that contract is ever met.

### Boundary comparisons on indices that cannot be zero

`dot >= 0` → `dot > 0` and `generic >= 0` → `generic > 0` in `InverseName`, over `LastIndexOf('.')` and
`IndexOf('<')` on a type's source text. They differ only at index 0 — a type written starting with `.` or
`<`. Neither is valid C# type syntax.

`cut < 0` → `cut <= 0` in `Short()`, same argument.

### A conditional that is redundant for its own guard value

`cut < 0 ? name : name.Substring(cut + 1)`. When `cut` is −1 the true branch returns `name` and the false
branch computes `name.Substring(0)`, which *is* `name`. Forcing the false branch changes nothing.

### Guards whose mutants converge downstream

`!TryGetValue("SourcePair", …) || string.IsNullOrEmpty(pair)` → `&&`. With `&&` the guard stops returning
false when the property is present but empty — and the empty string then fails the separator check
downstream, so `TryReadPair` returns false on that path anyway. When the property is absent, `pair` is null
and `IsNullOrEmpty(null)` is true, so both forms return false immediately.

`replacements.Count > 0` → `>= 0` and `additions.Count > 0` → `>= 0`. `ReplaceNodes` and `AddRange` over an
empty collection are no-ops returning an equal node.

The `return document` early exit when nothing needs restating: falling through runs both of those no-ops and
re-annotates the class for formatting, producing byte-identical text. Pinned by
`RestateBaseRefusalTests.Restating_a_pair_that_has_not_drifted_leaves_the_document_alone`.

### Out parameters written before a `false` return — **the weakest entry here**

`source = target = string.Empty` immediately before `return false`, twice. Every caller is
`if (TryRead… && TryRead…)` and reads the outs only on the true path.

This one rests on **caller discipline, not on the language**, and it is recorded as such rather than as an
absolute. The day a caller reads those outs after a false return, the mutant becomes killable and this entry
must be deleted rather than argued with.

### The one that is not proven — retired 2026-09-15

The trivia source for an added attribute list —
`classDecl.AttributeLists.LastOrDefault() ?? (SyntaxNode)classDecl`. Dropping the left operand always takes
the class declaration's trivia, and the result is re-annotated with `Formatter.Annotation` immediately
afterwards, so the normalised output has been identical in every case tried. That is evidence, not a proof:
a formatting-sensitive input may yet distinguish them. Filed **`probably-equivalent`** — explicitly
low-priority, never "do not attempt".

**Retired in round 30.** The coverage sweep never settled whether the mutant was equivalent. It showed instead that
the `??` fallback cannot run:
- an attribute list is added only for an attribute copied from `toCopy`;
- `toCopy` is filled only from `classDecl.AttributeLists`.

So the class always has a last attribute list at that point. The fallback was removed in favour of
`AttributeLists[AttributeLists.Count - 1]`. The mutant can no longer be generated, and the indexer's own
`Count + 1` mutant throws, so a test kills it. The leg's undetected mutants are now all proven.
