<!-- SPDX-License-Identifier: GPL-2.0-only -->

# T3 — the two sibling mutation legs, run to completion for the first time

Round 21, task T3. Branch `feat/round21-depth-tier`, base `51a9ebd`. Stryker 4.16.0, .NET SDK 10.0.101,
12 logical cores, Windows 10. **Machine state: quiet** — ~2 % CPU and no concurrent build or test run at
launch (checked with `Win32_Process` command lines, not just a process count), and the two legs were run
strictly sequentially. This matters: NOTE 5 of `stryker-config.runtime.json` records a 44-minute figure that
was mostly contention, and every wall-clock below feeds T5's deep-tier budget.

Both configs had **never completed a run** before today. Their `break: 70` was inherited and unvalidated —
`comment` sat inside the `stryker-config` object (which Stryker 4.16 rejects outright) until 2026-08-16, and
the `mutate` globs were repo-root-relative, which matches nothing and produces a run that mutates **nothing
and exits 0**. Both were parse-fixed in round 20 and are measured here for the first time.

## Summary

| Leg | Wall-clock | Killed | Survived | Timeout | NoCoverage | Scoreable | Score | `break` set |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Generator (`stryker-config.json`) | **21:02** | 144 | 46 | 0 | 11 | 201 | **71.64 %** | **71** |
| DocTooling (`stryker-config.doctooling.json`) | **05:57** | 233 | 11 | 3 | 37 | 284 | **83.10 %** | **83** |

The generator's `break` falls from the inherited 70 to a **measured** 71 and DocTooling's rises to **83** — both
ratchets tighten, and neither number is now a guess. Together the two legs cost **26 min 59 s**; with the
runtime leg's 12:25 the three mutation legs alone are **39 min 24 s** of T5's 44-minute deep-tier budget. See
*What this means for T5* at the end.

Non-vacuity, both legs: the guard's own regex — `"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"`,
re-implemented out of process against the run's `mutation-report.json` — returns a count well above the `> 0`
that `Assert-MutantsWereTested` demands, and the files carrying scoreable statuses are **exactly** the files
each config names.

---

## Generator leg — `stryker-config.json`

**Measured 2026-08-19. 5,798 tests discovered. 21 min 02 s. Score 71.64 % = 144 detected / 201 scoreable.
`break` set to 71.**

### The first run scored 72.64 % and that number is inflated — do not restore it

| | Run 1 (no `additional-timeout`) | Run 2 (`additional-timeout: 120000`) |
|---|---:|---:|
| Wall-clock | 22:01 | **21:02** |
| Killed | 144 | **144** |
| Timeout | 2 | **0** |
| Survived | 44 | **46** |
| NoCoverage | 11 | **11** |
| Scoreable | 201 | **201** |
| **Score** | 72.64 % | **71.64 %** |

Both timeouts were **static** mutants (`"static": true`, empty `coveredBy`), which Stryker runs against the
entire suite, and **neither can hang**:

- `BlittableProof.cs:28` — `if (!a.IsUnmanagedType || !b.IsUnmanagedType)` → `&&`. No loop; `LayoutIdentical`
  recurses only through **struct** fields, and C# forbids a struct cycle (CS0523), so the depth is finite.
- `BlittableProof.cs:58` — one `or` → `and` inside `IsPrimitive`'s `SpecialType` pattern. A single enum
  pattern match. There is no loop to run away with.

This is the mechanism `stryker-config.runtime.json` already documents: `total timeout = initialTestTime +
additional-timeout`; the initial serial run here is **203 s**, the default cushion is a handful of seconds, and
Stryker **counts a Timeout as detected**. Raising the cushion to 120 s resolved both honestly as Survived,
**zero mutants gained detection**, and the entire 1.00 pp delta is those two re-classifications. No test was
excluded, and none may be.

### Where the 21 minutes goes — the number T5 has to budget

| Phase | Cost |
|---|---:|
| Analysis + solution build | 0:45 |
| Initial (serial) test run, 5,798 tests | 3:23 |
| Mutate + compile + rollback of the WHOLE project | 0:46 |
| Coverage capture | 1:29 |
| **Mutation testing, 190 tested mutants** | **14:39** |
| **Total** | **21:02** |

The dominant cost is **85 of the 201 scoreable mutants being `static`**, so Stryker cannot attribute per-test
coverage and each re-runs the whole 5,798-test suite: ~493,000 test executions, against ~1,580 for every
covered mutant put together. Per file, static/scoreable: `ConstructorSelector` 50/87, `BlittableProof` 27/88,
`LocationInfo` 5/5, `EquatableArray` 3/21.

Two facts that follow, and neither is a licence to narrow the leg:

1. Stryker mutates and **compiles the whole project** regardless of `mutate` — 11,389 mutants created, 7,016
   dropped by the mutate filter, 4,142 rolled back as compile errors, 30 dropped by the block-already-covered
   filter, 190 tested plus 11 uncovered. That is why ~6:23 elapses before the first mutant is tested, and it
   is why the JSON report lists every file in the project.
2. Narrowing `mutate` would cut the *tested* set but not most of that fixed cost, and narrowing the *test* set
   is ruled out (`stryker-config.runtime.json`, and the maintainer's ruling recorded in
   `Issues/ledgers/E3-E1-report.md`).

### Per file

| File | Scoreable | Killed | Survived | NoCoverage | CompileError (excluded) |
|---|---:|---:|---:|---:|---:|
| `Collections/EquatableArray.cs` | 21 | 19 | 2 | 0 | 2 |
| `Diagnostics/LocationInfo.cs` | 5 | 5 | 0 | 0 | 0 |
| `Pipeline/BlittableProof.cs` | 88 | 46 | 36 | 6 | 24 |
| `Pipeline/ConstructorSelector.cs` | 87 | 74 | 8 | 5 | 16 |

`LocationInfo.cs` is **fully pinned** — every one of its mutants dies.

### Every undetected mutant, with a judgement

Grouped by the member it lives in. "Equivalent" is used only where a case analysis shows the original and the
mutant agree on every reachable input — the bar `E3-E1-report.md` set with the `maxDepth < 1` clamp.

#### `EquatableArray.GetHashCode` — 2 survivors, real but low-value

| Line | Mutation | Judgement |
|---|---|---|
| 53 | `hash * 31 + (…)` → `hash * 31 - (…)` | **Real hole.** Nothing asserts the hash *mixing*. |
| 53 | `hash * 31` → `hash / 31` | **Real hole**, same cause. |

The existing `GetHashCode_different_elements_typically_differ` does not discriminate: under either mutant
different element sets still produce different hashes. Only a distribution/collision assertion catches this.
This is the exact shape of hole #5 in `E3-E1-report.md` (`Key.GetHashCode`'s `* 397` → `/ 397`), which argues
for one shared property test over both hash functions rather than two bespoke ones.

#### `BlittableProof.LayoutIdentical` / `IsPrimitive` — 13 survivors + 2 uncovered, **all equivalent**

| Line | Mutation | Judgement |
|---|---|---|
| 28 | `!a.IsUnmanagedType \|\| !b.IsUnmanagedType` → `&&` | **Equivalent — do not attempt.** |
| 29 | `IsPrimitive(a) \|\| IsPrimitive(b)` → `&&` | **Equivalent — do not attempt.** |
| 58 | 12 × one `or` → `and` in the `SpecialType` pattern | **Equivalent — do not attempt.** |
| 30 | `a.SpecialType == b.SpecialType` → `a.SpecialType == SpecialType.None` (NoCoverage) | Unreachable branch. |
| 32 | `na.TypeKind != TypeKind.Struct` → `true` (NoCoverage) | Unreachable branch. |

The proof, because it settles thirteen mutants at once:

- **L28.** The mutant differs from the original only when *exactly one* operand is managed; when both are
  managed both forms return `false` immediately. Managed-ness always enters at some leaf whose type is a
  reference type, and every such leaf returns `false` anyway: a reference type is `TypeKind.Class`, so it fails
  the `TypeKind != Struct` check, and it is not primitive, so it cannot return `true` through L30 either.
  The mutant therefore never returns `true` where the original returns `false`, and it agrees everywhere else.
- **L29 and L58.** The primitive branch returns `true` only when `a.SpecialType == b.SpecialType` **and** the
  two symbols are *not* the same type — the identity check at L26 already returned `true` for that. Two
  distinct symbols sharing one non-`None` `SpecialType` cannot occur inside a single compilation, so the
  branch's `true` return is unreachable and every mutation of the set it tests changes nothing observable:
  a primitive is always a metadata symbol, so the fall-through path rejects it at `IsSourceSequential`.

**This is a finding, not just a verdict.** L29-L30's `true` return appears to be unreachable in principle.
Either the branch is dead and should say so, or it was meant to catch a case the identity check above it is
already swallowing. That is a question for the maintainer, in the shape `E3-E1-report.md` used for
`DwarfMapperRegistry.ResetForTests` — not a coverage hole, and not something to chase with a test.

#### `BlittableProof.InstanceFields` — the field sort, 20 survivors + 4 uncovered

| Line | Mutation | Judgement |
|---|---|---|
| 78 | `fields.Sort(…)` → `;` (statement deletion) | **Real hole — the top candidate in this file.** |
| 80, 81 | 5 each: conditional-true, conditional-false, `Length < 0`, `Length >= 0`, `?? string.Empty` removed | **Real hole**, same cause as L78. |
| 83 | `byFile != 0` → `byFile == 0` | **Real hole**, same cause. |
| 85, 86 | 4 each: conditional-true/false, `Length < 0`, `Length >= 0` | **Probably equivalent, low priority** — see below. |
| 80, 81 | `string.Empty` → `"Stryker was here!"` ×4 (NoCoverage) | Defensive fallbacks for a symbol with no location; never taken. |

The sort exists for exactly one reason, and the code says so: `GetMembers()` returns declaration order, which
for a struct split across **partial files** depends on the order the compiler happened to see the files, and
the blit proof compares fields **positionally**. No test declares a struct across two files, so the whole
comparator — up to and including deleting the sort outright — is unpinned. **One fixture kills the file-path
half of this block**: the same struct pair declared across two partial files, compiled with the file order
reversed, must give the same `CanReinterpret` verdict.

Honest caveat on L85/L86: within a single file `GetMembers()` is already position-ordered, so the
`SourceSpan.Start` tie-break is a no-op there and a test cannot easily distinguish it. Treat those eight as
probably-equivalent rather than as work, and do not count them towards a raised `break`.

#### `BlittableProof.IsSourceSequential` — 2 survivors, one of them the most consequential in the leg

| Line | Mutation | Judgement |
|---|---|---|
| 97 | `l => l.IsInSource` → `l => true` | **Real hole, highest consequence.** |
| 97 | `t.Locations.Any(…)` → `t.Locations.All(…)` | **Real hole, low consequence.** |

`IsInSource` → `true` makes **metadata (BCL) structs qualify as source-sequential**, which is precisely the
gate the code exists to hold: the comment says auto-blit requires a source struct *so that an absent
`[StructLayout]` reliably means the C# default*. A BCL struct carries no such guarantee. There is already a
test named for this case — `CanReinterpret_bcl_non_primitive_struct_returns_false_no_source` — and it covers
the mutant (`coveredBy = 1`) **without discriminating it**, because the pair it uses also differs in field
names. The missing case is a BCL struct that is field-compatible with a user struct, where the mutant would
return an unsafe `true`.

`Any` → `All` differs only for a type with **zero** locations; that is rare enough to rank last, but it is not
equivalent.

#### `ConstructorSelector` — 8 survivors + 5 uncovered

| Line | Mutation | Judgement |
|---|---|---|
| 288 | `ctor.Parameters.Any(p => p.RefKind is Ref or Out)` → `.All` | **Real hole — top candidate in this file.** |
| 55 | 3 × `&&` → `\|\|` in `hasExplicitNonParameterlessCtor` | **Real hole.** |
| 58 | `c.Parameters.Length > 0` → `>= 0` | **Equivalent in practice** (see below). |
| 88 | `useObjectInitializerOnly = true` → `false` | **Real hole *or* a dead out-parameter — investigate.** |
| 230 | `param.HasExplicitDefaultValue \|\| param.IsParams` → `&&` | **Real hole.** |
| 243 | `src.IndexOf('.') >= 0` → `> 0` | **Equivalent.** |
| 230 | `continue` → `;` (NoCoverage) | The optional/`params` exemption is never taken. |
| 246 | `return false` after a failed `TryResolvePath` → `true` (NoCoverage) | A dotted `[MapProperty]` path that does not resolve is never seen at selection time. |
| 250 | `return false` when `!exact.Contains(src)` → `true` (NoCoverage) | An explicit map naming a non-existent source member is never seen at selection time. |
| 281 | `if (ctor.IsStatic) return false;` → `true` (NoCoverage) | **Dead code** — `InstanceConstructors` never contains a static constructor. A question, not a hole. |
| 285 | record copy-ctor `return false` → `true` (NoCoverage) | Never reached: a record's copy constructor is `IsImplicitlyDeclared` and is rejected one line earlier. A question, not a hole. |

Notes on the three judgements that are not obvious:

- **L288** guards a *stated* invariant — `ref`/`out` parameters cannot be emitted as named arguments (CS1620).
  Every existing ref/out test uses a constructor whose parameters are **all** `ref`/`out`, where `Any` and
  `All` agree. One constructor of the shape `Dst(int a, ref int b)` kills it, and the failure it prevents is
  emitted code that does not compile — the same family as T6's `EmittedInvalidCode` work.
- **L58 `> 0` → `>= 0`** cannot change the outcome: the predicate also requires `!c.IsImplicitlyDeclared`, so
  the only constructor the widened comparison newly admits is an *explicitly declared parameterless* one — and
  for that constructor `anyParameterless` still matches, because its `(!c.IsImplicitlyDeclared || …)` clause
  is satisfied by the same fact. Equivalent.
- **L88** is the interesting one. `useObjectInitializerOnly` is set on the parameterless path and consumed by
  the emitter; flipping it to `false` survives the whole 5,798-test suite. Either no test asserts the
  object-initializer path at all — implausible, given 74 kills in this file — or the flag is **not
  load-bearing when the selected constructor has zero parameters**, i.e. it is a redundant out-parameter. That
  is worth one look before anyone writes a test to kill it.

### Kill-first ranking for this leg

1. **`BlittableProof.IsSourceSequential` L97** (`IsInSource` → `true`) — the auto-blit safety gate; the mutant
   admits metadata structs. A test exists for the case and does not discriminate. Highest consequence: an
   unsafe *accept*.
2. **`BlittableProof.InstanceFields` L78 + the file-path comparator** (~12 mutants) — the determinism
   guarantee has no test at all, and deleting the sort outright survives. One partial-file fixture.
3. **`ConstructorSelector` L288** (`Any` → `All`) — a stated CS1620 invariant, one mixed `ref`/non-`ref`
   constructor to kill it.
4. `ConstructorSelector` L55 — the struct explicit-constructor predicate (3 mutants).
5. `ConstructorSelector` L230 — the optional/`params` exemption in `AllParametersHaveASource`.
6. `EquatableArray.GetHashCode` L53 (2 mutants) — pair with the runtime leg's `Key.GetHashCode` hole.

**Do not attempt:** the thirteen `LayoutIdentical`/`IsPrimitive` mutants (L28, L29, L58 ×12) and
`ConstructorSelector` L58 and L243 — proven equivalent above. The eight `InstanceFields` position-comparator
mutants (L85, L86) are probably equivalent and are not worth a bespoke test.

---

## DocTooling leg — `stryker-config.doctooling.json`

**Measured 2026-08-19. 4,779 tests discovered. 5 min 57 s. Score 83.10 % = 236 detected / 284 scoreable.
`break` set to 83.** One run; no re-run was needed, for the reason immediately below.

### All three timeouts are real, so no `additional-timeout` was added

This is the deliberate contrast with the generator leg, and it is the reason the same remedy is **not** applied
twice. Applying the E3-E1 test — *can this mutant hang?* — to each:

| Mutant | Replacement | Can it hang? |
|---|---|---|
| `DocSnippetInjector.cs:39` | `i++` → `;` inside `while (i < lines.Length)` | **Yes** — unconditional infinite loop. |
| `SnippetScanner.cs:138` | `kept.RemoveAt(0)` → `;` inside `while (kept.Count > 0 && IsNullOrWhiteSpace(kept[0]))` | **Yes** — the loop condition can never become false. |
| `SnippetScanner.cs:139` | `kept.RemoveAt(kept.Count - 1)` → `;`, same shape | **Yes.** |

Their covering sets are 10, 1 and 1 tests — not the whole-suite geometry that manufactured the generator
leg's spurious timeouts. All three are genuine detections, the default ceiling is correct here, and adding a
120 s cushion would only make three real hangs cost two extra minutes each. If a Timeout ever appears here on
a mutant that cannot hang, raise `additional-timeout`; never filter the test set.

### Why this leg is 6 minutes and its sibling is 21

**Zero of its 284 scoreable mutants are `static`.** Stryker attributes per-test coverage to every one, and the
covering sets total 1,603 test executions. The generator leg's 85 static mutants each re-run the whole suite,
for ~493,000. The difference is coverage attribution, not test quality — and it is the single most useful
number T5 has, because it says which leg can be made cheaper and which cannot.

| Phase | Cost |
|---|---:|
| Analysis + solution build | 0:35 |
| Initial (serial) test run + whole-project mutate/compile/rollback | 1:24 |
| Coverage capture | 1:28 |
| **Mutation testing, 247 tested mutants** | **2:29** |
| **Total** | **5:57** |

### Per file

| File | Scoreable | Killed | Survived | Timeout | NoCoverage | CompileError (excluded) |
|---|---:|---:|---:|---:|---:|---:|
| `SnippetScanner.cs` | 92 | 80 | 7 | 2 | 3 | 8 |
| `DocSnippetInjector.cs` | 58 | 49 | 4 | 1 | 4 | 15 |
| `OptionTableRenderer.cs` | 80 | 69 | 0 | 0 | 11 | 7 |
| `DocTableInjector.cs` | 27 | 22 | 0 | 0 | 5 | 6 |
| `ExampleCatalogue.cs` | 27 | 13 | 0 | 0 | 14 | 6 |

The shape is worth naming: **`SnippetScanner` and `DocSnippetInjector` are well pinned on behaviour and
unpinned on messages; `DocTableInjector` and `ExampleCatalogue` have no failure-path coverage at all.**

### Every undetected mutant, with a judgement

#### Family A — exception message text and line numbers: 11 survivors + ~25 uncovered, one hole

Every one of these empties a message fragment, or shifts a reported line number, inside a
`DocToolingException` that a test *does* provoke. The tests assert `Assert.Throws<DocToolingException>` and
stop there, so the message a maintainer actually reads is unasserted.

| Member | Mutants |
|---|---|
| `SnippetScanner.ScanFile` (nested-region refusal) | L84 `$""`, L84 `i + 1` → `i - 1` |
| `SnippetScanner.ScanFile` (close-without-open) | L97 `i + 1` → `i - 1` |
| `SnippetScanner.Merge` (duplicate id) | L50 `""` |
| `SnippetScanner.Dedent` (empty region) | L144 `""` |
| `SnippetScanner.Dedent` (injector marker in body) | L155 `$""`, L157 `""` |
| `SnippetScanner.ParseId` (malformed marker) | L119 statement, L120 `$""`, L121 `""` — **NoCoverage** |
| `DocSnippetInjector.Inject` (unknown id) | L46 `i + 1` → `i - 1`, L47 `$""` |
| `DocSnippetInjector.FindClose` (unclosed marker) | L120 `""` |
| `DocSnippetInjector.ParseId` (malformed marker, empty id) | L97, L98, L102 ×2 — **NoCoverage** |

**Real hole, one fix.** This is the same shape as holes #11 and #12 in `E3-E1-report.md`
(`DwarfMapMissingException.FormatMessage`, 16 survivors). The remedy is a convention rather than N tests:
every `Assert.Throws<DocToolingException>` also asserts one discriminating fragment **and** the reported line
number — the `i + 1` → `i - 1` mutants exist precisely because nothing checks the number the message quotes,
and a doc-pipeline failure whose line number is wrong is worse than one with no line number.

The `ParseId` entries are a genuine *coverage* gap on top of that: neither injector nor scanner has a test for
a marker with no closing delimiter. `SnippetScannerTests.A_marker_with_no_id_is_a_loud_failure` covers the
empty-id branch only, and `DocSnippetInjectorTests` has no malformed-marker test at all.

#### Family B — `DocTableInjector`'s two refusal paths, 5 uncovered: **the top candidate in this leg**

| Line | Mutation | Judgement |
|---|---|---|
| 26, 27 | `throw` → `;`, message → `$""` | **Real hole.** No test for a document with no `<!-- table: name -->` marker. |
| 32, 33, 34 | `throw` → `;`, messages → `$""` / `""` | **Real hole.** No test for a table marker that is never closed. |

`DocTableInjector` has **no dedicated test file**. It rewrites tracked documents, and the guard whose comment
reads *"Refusing to treat the rest of the file as table body"* is the thing standing between a malformed
marker and a truncated document. Deleting that `throw` outright is undetected today. Two string literals and
two `Assert.Throws` kill all five, and NOTE 3 of the config — this leg physically emptied `README.md` on its
first run — is why this ranks first rather than as tidy-up.

#### Family C — `ExampleCatalogue.Build`'s two refusals, 14 uncovered: the largest uncovered block

| Line | Mutation | Judgement |
|---|---|---|
| 55-57 | message fragments → `""` (the "no `public static void Run()`" refusal) | **Real hole.** |
| 65-70 | `throw` → `;`, message fragments, `matches.Count > 1` → `>= 1`, both conditional arms, three literals | **Real hole.** |

`ExampleCatalogue` is exercised only by the real Gallery corpus, which is well-formed by construction, so
neither refusal has ever run. Both are load-bearing: the first keeps an example from being indexed but never
run, the second keeps an index entry from binding to whichever file was found first. Killing these needs a
synthetic `[DocExample]` type and a controlled file list rather than the live Gallery — a small seam
(`Build` already takes its file list as a parameter) rather than new production code.

#### Family D — `OptionTableRenderer`'s formatting and fallback paths, 11 uncovered

| Line | Mutation | Judgement |
|---|---|---|
| 45 | `"—"` → `""` (the `defaults is null` fallback) | **Real hole** — an attribute with no parameterless ctor. |
| 91 | `if (cells.Length < 5) continue;` → `;` | **Real hole** — a short/ragged committed table row. |
| 109 | `TryCreate`'s `try` block removed (`{}`) | **Real hole** — the `TargetInvocationException` catch. |
| 116, 118 ×5 | `Format(null)`, `Format("")` vs `Format("x")` — literals and both conditional arms | **Real hole** — `Format` is only ever called with `bool`/`enum` defaults today. |
| 126 | `Display(Nullable<T>)`'s `"?"` suffix → `""` | **Real hole** — no nullable option exists yet. |
| 129 | `Display(typeof(string))` → `""` | **Real hole** — no `string` option exists yet. |

These are all reachable only through option shapes `DwarfMapperAttribute` does not currently have. That is
exactly the case this renderer exists to survive: the table is generated so that a *new* option appears
automatically. A synthetic attribute type with a `string`, an `int?` and a `null`-defaulted property, rendered
through `RenderRows`, kills nearly the whole family in one test.

#### Family E — one equivalent mutant

| Line | Mutation | Judgement |
|---|---|---|
| `DocSnippetInjector.cs:83` | `if (run > longest) longest = run;` → `run >= longest` | **Equivalent — do not attempt.** |

The two forms differ on exactly one input, `run == longest`, and there they agree anyway: the mutant performs
the assignment `longest = run` where `run` already equals `longest`, which changes nothing. No test can detect
it. Same category as `DwarfRefContext`'s `maxDepth < 1` clamp in `E3-E1-report.md`; it belongs in an
`ignore-mutations` entry, not in a test.

### Kill-first ranking for this leg

1. **`DocTableInjector`'s two refusals** (5 mutants) — cheapest test in the list, and it guards a document
   truncation that this leg has now demonstrated is not hypothetical.
2. **`ExampleCatalogue.Build`'s two refusals** (14 mutants) — the largest single uncovered block, and both
   refusals exist to stop a silently wrong index.
3. **Assert the message, not just the type** — a convention change across `SnippetScannerTests`,
   `DocSnippetInjectorTests` and `DocPipelinePropertyTests` that kills ~30 mutants and matches the runtime
   leg's holes #11/#12.
4. `OptionTableRenderer`'s formatting family (11) — one synthetic attribute type.
5. `ParseId`'s malformed-marker branches in both files (7) — two one-line inputs.

**Do not attempt:** `DocSnippetInjector.cs:83` — proven equivalent above.

---

## Cross-leg kill-first ranking

1. **`BlittableProof.IsSourceSequential` L97**, `l.IsInSource` → `true` (generator) — the auto-blit safety
   gate admits metadata structs, and the test named for that case does not discriminate. An unsafe *accept* is
   the worst failure mode in this repository.
2. **`DocTableInjector`'s two refusal paths** (doc tooling, 5 mutants) — a document-truncation guard with no
   test, in a leg that has now emptied `README.md` for real.
3. **`BlittableProof.InstanceFields` L78 and the file-path comparator** (generator, ~12 mutants) — deleting
   the determinism sort outright survives; one partial-file fixture kills the block.

Runner-up, and the cheapest by kills per line of test: assert a message fragment in every `Assert.Throws`
across the doc pipeline (~30 mutants, same shape as the runtime leg's holes #11/#12).

## What this means for T5 — flagging the budget, not fixing it

The task's instruction was to report the number rather than narrow the `mutate` list, so:

| Deep-tier mutation component | Wall-clock |
|---|---:|
| Runtime leg (existing, nightly CI) | 12:25 |
| **Generator leg (measured here)** | **21:02** |
| **DocTooling leg (measured here)** | **05:57** |
| **Mutation total** | **39:24** |

That is **39 min 24 s of a 44-minute budget** before coverage, ILVerify, the deep test knobs and the benchmark
smoke leg are counted, and these are 12-core figures — a hosted runner with 4 cores gets Stryker's default
concurrency of 2 instead of 6. The three legs do not fit in one nightly job as they stand.

**What is not an acceptable fix:** narrowing `mutate`, filtering the test set, or excluding a test project.
The first drops measurement; the second and third were ruled out for the runtime leg and that ruling binds
(`stryker-config.runtime.json`; `Issues/ledgers/E3-E1-report.md`).

**What the measurements suggest instead**, in the order the evidence supports:

1. **Split the legs across nights.** They are independent runs with independent `break` values; nothing
   requires all three on the same night. Runtime + DocTooling is 18:22; the generator alone is 21:02. Both fit.
2. **Attack the generator leg's 85 static mutants at the cause.** They are static because Stryker cannot
   attribute per-test coverage to them, which is a property of how the generator is invoked from the tests,
   not of the tests' quality. Recovering that attribution removes the leg's dominant cost — ~493,000 test
   executions against ~1,580 for everything else. This is the generator-side twin of R21-1's covering-set
   explosion and deserves its own research item.
3. **Raise Stryker `concurrency`** only as a last resort: R21-3 already measured that it competes for the same
   cores and has the smallest ceiling.

## Housekeeping notes from running these legs

- **`Assert-MutantsWereTested` passes for both.** Verified by re-implementing the guard's own regex —
  `"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"` — out of process: **201** for the generator leg and
  **284** for DocTooling, both far above the `> 0` it demands. Neither run was vacuous, and in both the files
  carrying scoreable statuses are exactly the files the config names.
- **The DocTooling leg destroys tracked documentation when run locally.** Its run left `README.md`,
  `CONTRIBUTING.md` and `docs/diagnostics.md` emptied — 2,523 deleted lines across five files — because the
  doc-generation tests write into the repo and the mutants under test are the code that writes them. Reverted,
  not committed. This is a stronger form of the `MutantControl` pollution recorded in `E3-E1-report.md`, and
  it is now NOTE 3 of that config. `housekeeping.ps1 -Mutation` runs this leg, so check `git status` after it.
- **`StrykerOutput/` needs no `.gitignore` entry**: Stryker writes a `.gitignore` containing `*` into each run
  directory, so its output self-ignores.
- **Nothing in `src/` or `tests/` was touched.** Only the two configs and this ledger changed, so the fast
  tier is unaffected by construction.

## Open action items for the maintainer — not fixed here

1. **`scripts/housekeeping.ps1 -Mutation` is still destructive, and a config comment is not a guard.** The
   script runs the DocTooling leg unconditionally, that leg empties tracked documentation (NOTE 3 of
   `stryker-config.doctooling.json`), and the only defence today is a human remembering to run `git status`
   afterwards. This task's grant covered the two configs and this ledger, so the script was not touched —
   **the fix is a decision, not an oversight**. Two shapes are available and they are not equivalent:
   restore the doc set after the leg (`git checkout --` the five files, which silently discards a *genuine*
   regeneration a contributor was mid-way through), or make the doc-writing tests write somewhere else under
   mutation (a `DWARF_DOC_OUTPUT` redirect, which changes what the leg proves for `DocSnippetInjector` and
   `DocTableInjector` — precisely the two files whose write behaviour is under test). Pick one deliberately.
2. **The margin to `break` is one mutant on the generator leg and ZERO on DocTooling.** 143/201 still passes
   at 71; 235/284 does not pass at 83. That strictness is the house convention — `break` is the floored
   measured score in all three configs — but it means a single `Killed → Survived` flip fails the doc leg.
   Both configs now say so, and both say to check the **Timeout** bucket before assuming a regression: a
   timeout counts as detected, so a re-classification moves the score with no test having changed. On this
   leg that cuts the unusual way — its three timeouts are *genuine hangs*, so an environment that stops
   hanging on them (a test-level timeout wrapper, a different scheduler) lowers the score honestly.
3. **The generator leg's 85 static mutants deserve a research item of their own.** They are the whole cost of
   the leg and the reason the deep-tier budget does not close. `static` here is a property of how Stryker's
   coverage capture sees the generator being invoked, not of the tests — which means it may be recoverable
   without touching a single assertion. That is the generator-side twin of R21-1, and it is worth measuring
   before anyone proposes cutting what the leg mutates.

---

## T3-H1 fix — the write-back guard, and the honest DocTooling floor it exposed

Round 21, 2026-08-19, branch `feat/round21-depth-tier`. Fixes the destruction recorded above ("The DocTooling
leg destroys tracked documentation when run locally") and **closes open action item 1**: of the two shapes
offered there (restore-after vs. redirect), a third was chosen deliberately — **guarded refusal**. Every
test-side repo write now routes through one gate that refuses the write under mutation while every staleness
assertion still runs and still fails. No test is excluded from any leg (the binding ruling), nothing is
restored after the fact (nothing is written), and nothing is redirected (every writer asserts independently
of its write, so no redirect target is needed).

### The verified mechanism — the sandbox-escape hypothesis is WRONG

There is no sandbox and no escape. `StrykerOutput/<timestamp>/` holds only reports; Stryker 4.16 runs the
tests **in the real tree**: it swaps the mutated assembly into each test project's real `bin/` folder in
place, keeping the original beside it as `X.dll.stryker-unchanged` for the duration of the run (observed
directly: `tests/DwarfMapper.Generator.Tests/bin/Debug/net10.0/DwarfMapper.DocTooling.dll` carried
"Stryker was here!" and namespace `StrykernkatKqYxLiqJmLE`; nine `*.stryker-unchanged` backups sat across the
test bins). So `AppContext.BaseDirectory` is the real bin, every repo-root walk (`RepoLayout.Root`,
`RepoPaths.Root`, the two private copies) resolves the real repository **correctly**, and a mutated renderer
wrote its output over the real files. The heal-or-fail write-back in `DocsAreSnippetCurrentTests` (README.md,
CONTRIBUTING.md, docs/diagnostics.md, docs/options.md, Gallery README) and `GeneratedDocsAreCurrentTests`
(docs/generated/*) did the writing; the mutants under test were the code producing the content.

### The guard

`tests/DwarfMapper.Generator.Tests/Contracts/RepoWriteGuard.cs` — ARCH-06's registered pattern, deliberately
in the TEST project (a guard inside the mutation target could itself be mutated off). Detection is resolved
once per process, filesystem-only:

1. any `*.stryker-unchanged` beside the test assembly (the in-place swap Stryker 4.16 actually performs —
   the signal that fires for all three legs; a leftover after an interrupted run means the sibling DLLs may
   still be mutants, so refusing then is equally correct), and
2. any ancestor directory named `StrykerOutput` or `.stryker-tmp` (the copy-sandbox shape, covering the
   general "root walk escaped a sandbox upward" case).

Rewired writers (all four known repo writers; a broad-pattern sweep of `tests/` and `conformance/` found no
fifth): `DocsAreSnippetCurrentTests` and `GeneratedDocsAreCurrentTests.AssertCurrent` (refuse write, keep
compare-and-fail, failure message names the marker), `AssemblyScanTests` `DWARF_SELF_HEAL` append (refusal
leaves `missing` populated so the assert fails truthfully), `GoldenManifest.Write` (refusal throws — its
caller passes right after writing, so a silent refusal would green-light a manifest never written).
`RepoWriteGuardTests` pins detection against fabricated marker directories, pins the refusal (pre-existing
target byte-identical after a refused write; no directory even created), and carries the ARCH-06 scan: every
raw file-writing API use in the test tree must be a registered pattern with a reason and a pinned count.

### Measurements (all on this worktree, quiet machine)

- Whole solution `dotnet build`: **0 warnings / 0 errors** (after decontaminating the bins — the pre-fix
  Stryker run had left the MUTATED `DwarfMapper.DocTooling.dll` and `DwarfMapper.Generator.dll` live in the
  test bins with newer timestamps than the source outputs, so an incremental build kept them; the nine
  backups and their siblings were deleted and rebuilt, then verified string-clean).
- Full suite `dotnet test DwarfMapper.NET.sln`: **7,665 passed, 0 failed, 0 skipped**.
- **The leg was unrunnable since T3 and nobody could know from the exit code**: T3 raised `break` to 83 but
  left `low` at 80, and Stryker 4.16 refuses `low < break` outright — *with exit code 0*. `low` now tracks
  `break` (NOTE 9 of the config).
- DocTooling leg re-run (`dotnet stryker --config-file stryker-config.doctooling.json`, repo root):
  **5 min 27 s**, 4,787 tests discovered, 284 scoreable (Assert-MutantsWereTested non-vacuity holds),
  **score 67.96 % = 193 detected (191 Killed + 2 Timeout), 54 Survived, 37 NoCoverage**.
- **Post-run `git status`: byte-identical to the pre-run status.** README.md, CONTRIBUTING.md, docs/, the
  Gallery README and every other tracked file untouched. The run also restored the clean DLL on completion
  (verified string-clean) but left its `.stryker-unchanged` backup behind — the guard deliberately treats
  that leftover as mutation state (conservative), and its refusal message says how to clean it.

### Why the score fell from 83.10 and why 67 is the honest floor, not a regression

The mutant space is identical (284 scoreable in both runs; same files, same lines). Exactly **43 mutants
flipped Killed → Survived** and one Timeout became a clean Kill (`DocSnippetInjector.cs` L39 `i++` deletion —
detected either way). The 43 were never killed by an assertion about their own behaviour: with write-back
live, the first destructive mutant emptied the committed documents on disk, and every later mutant session
compared its renderer output against that polluted committed text — a mismatch regardless of the mutant. The
83.10 baseline was self-contaminated in the kill-inflating direction (the same genre as the generator leg's
inflated 72.64 first run, recorded above). Per the engagement rule, `break` moves to the re-measured floor
**in the same commit as this measurement**: 83 → **67** (67.96 floored). Margin is now three mutants
(190/284 = 66.90 % fails).

The 43 exposed survivors — all no-ops on the current documentation corpus, i.e. corpus holes of the
test-infra-holes pattern, NOT judged equivalent — by file (line/mutator from the 11-49-34 report):

- `OptionTableRenderer.cs` (15): L27, L38 (ThenBy→ThenByDescending), L45, L57, L60, L62, L81, L86, L88 (x2),
  L91, L94 (x3), L129
- `DocSnippetInjector.cs` (9): L23, L24, L26, L43, L50, L71, L83, L86, L96
- `SnippetScanner.cs` (9): L27 (OrderBy→OrderByDescending), L28, L41, L59, L67, L81, L102, L118, L164
- `DocTableInjector.cs` (7): L17, L18, L23, L25, L29, L31, L41
- `ExampleCatalogue.cs` (3): L29, L46, L59

Triage of these 43 (real hole vs. equivalent, per the house judgement format used for the original 11) is
follow-up work — raising `break` back up happens only by killing them, never by re-measuring luck.

## H5 — generator floor re-validated under the RepoWriteGuard (2026-08-19 run, closed 2026-08-21)

The H1 defect inflated DocTooling's floor by 43 artifact kills, so the generator leg's `break: 71` had to be
re-earned under the guard. Re-run `2026-08-19.12-10-18` (the analyst agent was cut off by a usage limit after
launching it; the run itself completed and the comparison was finished by the controller):

- **Wall-clock 19:41** (12:10:18 → 12:29:59), quiet machine. T3's 21:02 confirmed as the right ballpark.
- **Score 71.64% — identical**: Killed 144, Survived 46, NoCoverage 11, Ignored 204, CompileError 66.
- **Per-mutant diff vs the pre-guard run (`06-41-40`): 471 mutants, zero status flips.** The generator leg
  carried none of the corpus-self-corruption artifact class; its kills do not route through the
  doc-comparison assertions that DocTooling's 43 phantom kills did.
- **Timeout: 0** in both accepted generator runs. The two Timeouts existed only in T3's first, load-noisy
  run (`06-17-43`) on `static` mutants — load noise, not hangs. Relevant to H7: the generator leg is already
  clock-clean; the genuine-hang population lives in the DocTooling leg (2 Timeout in `11-49-34`).

**`break: 71` stands, unchanged — validated by re-measurement, not inherited.** Post-run `git status` clean:
the guard held on the generator leg too (first time it ran under it).

---

## P3 — the DocTooling kill program: families A–D + ParseId, and the 43 dispositioned (2026-08-22)

Round 22, task P3, branch `feat/round22-gates`. Baseline re-confirmed before any edit
(`StrykerOutput/2026-08-21.23-32-50`, quiet machine, 4:56): **67.96 % = 193/284, 54 Survived,
37 NoCoverage, 0 Timeout — byte-for-byte the H7 phase-2 figure**, so the kill work started from a
verified floor, not an inherited one. Kill commit `1132586` (tests + the sanctioned `Build` seam);
re-measure `StrykerOutput/2026-08-21.23-56-18` (quiet machine, **4:36**):

**Score 95.42 % = 271 detected / 284 scoreable. Survived 13, NoCoverage 0, Timeout 0.**
Per-mutant diff against baseline: **81 status flips — 44 Survived→Killed, 34 NoCoverage→Killed,
3 NoCoverage→Survived (all three adjudicated below), zero Killed→anything regressions.**
`break` 67 → **95** (95.42 floored), `low` with it, `high` 90 → 96, in the same commit as this record.

### The five families, each killed by its named test

| Family | Kill (representative `killedBy`, verified in the JSON) |
|---|---|
| **B** `DocTableInjector` refusals (5 NC) | `DocTableInjectorTests.A_document_without_the_marker_is_refused` / `An_unclosed_table_is_refused_not_truncated` — message + path pinned |
| **C** `ExampleCatalogue.Build` refusals (14 NC) | `ExampleCatalogueTests.A_type_without_a_public_static_run_is_refused` / `An_example_whose_file_was_renamed_is_refused` / `An_example_matching_two_files_is_refused_naming_both`, via the private→internal `Build` seam (+`InternalsVisibleTo`) |
| **A** message text + line numbers (~30) | the convention: every `Assert.Throws<DocToolingException>` in `SnippetScannerTests` / `DocSnippetInjectorTests` / `DocPipelinePropertyTests` now asserts a discriminating fragment AND the reported `file:line` (markers moved off line 1 so `i+1 → i-1` discriminates) |
| **D** `OptionTableRenderer` formatting/fallbacks (11 NC) | `OptionTableRendererTests` — synthetic `string`/empty-`string`/`int?`/null-default/`bool`/enum option types, both em-dash fallbacks, ragged/five-cell rows, endtable stop, header-masquerade, CRLF |
| **ParseId** malformed markers (7 NC) | `A_marker_missing_its_close_delimiter_is_a_loud_failure` in BOTH test classes + empty-id line pins |

### The 43 write-back-exposed survivors — triaged row-by-row, and one mis-filing corrected

All 43 were re-identified in the fresh baseline by expression (lines had drifted with H7 phase 2).
**None carries any residue of the corrupted-corpus story: every one survived on a stable tree under the
guard, i.e. an ordinary corpus hole, exactly as the T3-H1 record claimed.** One bookkeeping error in that
record is corrected here: its per-file list gave DocSnippetInjector 9 rows including "L83" — but old-L83
is the **family-E proven equivalent** (`run > longest`, already in the equivalents ledger), double-counted
into the 43; the actual 43rd survivor is a **fourth** `OptionTableRenderer` L94 mutant (the list said
"L94 (x3)"). Corrected split: **OptionTableRenderer 16, DocSnippetInjector 8, SnippetScanner 9,
DocTableInjector 7, ExampleCatalogue 3.** Disposition of the 43: **38 killed** by the family tests above
(CRLF normalization literals, the single-trailing-newline contract, the backtick-run reset, ThrowIfNull
deletions, prose-reader boundary/ordering/stop conditions, `IsNotBuildOutput` or-vs-and via the internal
seam, the `05_`-prefix underscore); **2 adjudicated** proven-equivalent (SnippetScanner close-branch
`continue` and dedent `prefix.Length > 0`, proofs below); **3 left with a stated reason** (below).

### Nine new proven-equivalent adjudications (ledger entries + pins moved in this commit)

The bar is T3's: original and mutant agree on every reachable input. Ids from `23-56-18`; lines current.

1. **`DocSnippetInjector.ParseId` L110, `end < 0` → `end <= 0`** (id263). `ParseId` is called only on a
   line whose `TrimStart()` begins with `<!-- snippet:`, so characters 0–2 are `<!-` and
   `IndexOf("-->")` can never return 0. The two comparisons differ only at `end == 0` — unreachable.
2. **`SnippetScanner.ParseId` L121, `close < 0` → `close <= 0`** (id516). Same shape: the line begins
   with `// <snippet:`, character 0 is `/`, so `IndexOf('>')` can never return 0.
3. **`DocTableInjector` L31, `end < 0` → `end <= 0`** (id291). `end = Array.FindIndex(lines, start + 1, …)`
   with `start >= 0` returns −1 or a value `>= start + 1 >= 1`; 0 is not in its range.
4. **`ExampleCatalogue.Build` L74, `matches.Count > 1` → `>= 1`** (id340). The ternary sits inside the
   `matches.Count != 1` throw's message, so it is evaluated only for counts {0, 2, 3, …}; `> 1` and
   `>= 1` agree on every one of those. The only distinguishing count, 1, never reaches it.
5. **`SnippetScanner.ScanFile` L105, close-branch `continue` → `;`** (id508). The only statement the
   deleted `continue` would fall through to is `if (openId is not null) body.Add(lines[i]);`, and the
   branch sets `openId = null` on its previous line — the fall-through is a guaranteed no-op.
6. **`SnippetScanner.Dedent` L170, `prefix.Length > 0` → `>= 0`** (id558). The loop's other conjunct is
   `!w.StartsWith(prefix)`; at `prefix == ""`, `StartsWith("")` is true for every string, so the
   conjunction is false either way and the loop exits identically.
7. **`OptionTableRenderer.ExistingProse` L94, `"---"` → `""`** (id421). The mutant stops skipping
   separator-shaped rows, so `"---"` can enter the prose/order dictionaries. Both consumers key those
   dictionaries by `PropertyInfo.Name` — a valid C# identifier, which `"---"` can never be — and the
   order values of real keys keep their relative order (insertion order is preserved, values shift
   uniformly), so `OrderBy` is unaffected. No observable difference. (The sibling `"Option"` arm is NOT
   equivalent — a property CAN be named `Option` — and is killed by
   `Header_and_separator_rows_never_masquerade_as_prose`.)
8. **`OptionTableRenderer.TryCreate` L109, catch-block removal → `{}`** (id426). The removed block
   contains exactly `return null;`. Stryker keeps block-removal mutants compilable by appending a
   `return default` epilogue to the method, and `default` for `object?` IS null — the mutant returns
   null on the same `TargetInvocationException` path. Behaviourally identical by the mutation tooling's
   own mechanics; confirmed empirically by `A_throwing_constructor_falls_back_to_em_dashes` covering it
   and passing.
9. **`OptionTableRenderer.Format` L118, conditional-false on the empty-string arm** (id433). The
   original is `s.Length == 0` choosing between a literal empty-quotes rendering and the interpolated
   quoted rendering; the mutant always takes the interpolated arm — which, at `s == ""`, renders the
   byte-identical text. The literal arm is a readability duplicate of the interpolated arm's empty case,
   so the one input the conditional-false changes is the one input where the arms agree. (The sibling
   conditional-true and `s.Length != 0` mutants DO diverge for non-empty strings and are Killed.)

### Three survivors left, each with its reason — filed as TASKS.md I2

`ScanAll`'s `OrderBy → OrderByDescending` (L27) and `"*.cs" → ""` (L28), and `ExampleCatalogue.Scan`'s
`"*.cs" → ""` (L29). All three live in filesystem composition roots that enumerate the REAL
`samples/` tree via `RepoLayout`. They are corpus holes, NOT equivalents: a marker-bearing non-`.cs` file
would change `ScanAll` under the pattern mutants (empty pattern returns ALL files — measured: 105 vs 44
under the Gallery), and a duplicate id split across two files would make scan order observable. But no
honest test can reach them: killing them requires planting files in the real tree (banned — the
RepoWriteGuard exists precisely to stop test-side repo writes) or redirecting `RepoLayout.Root` (banned —
the doc pipeline must prove itself against the real repository; a redirect changes what the leg proves).
The seam that would fix this honestly — the composition roots taking an injectable file list the way
`Build` already does — is a product-shape decision, filed as **I2** in `Issues/round20/TASKS.md`.

### Post-run state

- Post-run `git status`: **byte-identical to pre-run** (tracked tree; the guard held). One
  `DwarfMapper.DocTooling.dll.stryker-unchanged` backup deleted; every test-bin
  `DwarfMapper.DocTooling.dll` string-scanned clean of Stryker markers.
- Non-vacuity: 284 scoreable statuses in the JSON (271 Killed + 13 Survived), the five configured files
  and no others; 7 progress-guard mutants Ignored (the pinned in-source disable, unchanged).
- Full suite **7,747 / 0** foreground in 66 s (baseline 7,716 in 73 s — inside the fast-tier cap;
  +31 tests are the new family tests).
- The margin to `break` is now **two flips**: 270/284 = 95.07 % still passes at 95; 269/284 = 94.71 %
  fails. Every one of the 13 undetected mutants is dispositioned, so any future flip below 95 is a real
  regression or a mutant-population shift from a product edit — not an undispositioned leftover.
