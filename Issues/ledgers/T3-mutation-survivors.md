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
