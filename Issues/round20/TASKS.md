<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The task list

**Standing rule, adopted 2026-08-16: every issue found goes in here, as a task, when it is found.**
Not into a ledger line, not into a report addendum, not into a category table. Here. One store.

This file supersedes the issue-tracking role of `CARRY-FORWARD.md` (which stays as the written-up *reasoning*
behind these items — the detail is worth keeping, it just should not be where work is tracked).

**Status:** `TODO` · `WIP` · `DONE` · `DECIDE` (blocked on a maintainer ruling, not on work) ·
`DROP` (considered and rejected — kept so nobody re-raises it).

**Where the detail lives:** `CF §n` = `Issues/round20/CARRY-FORWARD.md` section n · `Dn` = an entry in
`tests/DwarfMapper.Generator.Tests/Contracts/DeclaredDivergences.cs` · `R21` =
`Issues/round21/RESEARCH.md` · ledger = `.superpowers/sdd/2026-08-16-round20-generator-defects/progress.md`
(git-ignored; rulings and per-task history live there).

---

## NOW — state of the work

Everything below this line is a backlog. This section is what is *actually happening*, so it can be read
without asking.

**Branch:** `feat/surface-coverage-architecture`, worktree `C:/Users/Jouda/RiderProjects/DwarfMapper-surface`.
Nothing pushed. Surface matrix green at 865/865; whole solution builds 0 warnings / 0 errors.

| | |
|---|---|
| **In flight** | **A9a review** — D8, D11 and D13 implemented (three commits), under task review. |
| **Next** | **A9b** (D12, D14, D15), then **A10 last among the refusal tasks**, then A11. (A5–A8 and A9a are done; A6 is `PARTIAL` and A8 narrowed `D9` — both wait on A10.) |
| **Blocked on a human** | **D-e only.** ~80 diagnostics predate the CHANGELOG and have never been announced; `Scan9` guards only *new* ids. Harmless until the first tag, then not. |
| **Round 19** | Complete — 10 tasks + 5a/5b/5c, final whole-branch review returned *merge after must-fixes*, and those must-fixes were **A0**. Merge itself is **F1** below and needs your say-so. |

**Live counts, read from the code rather than from a report** (I had been repeating 23 / 162 from Task 5c's
report; the source says otherwise — see the ledger's controller-error entry):
**9 divergence findings · 34 declared cells · 293 unjudged-but-counted cells across four ratchets.**

**Layer 0 is complete** (C2, B2, B1, B9 — the instrument repairs). Measurements from here are trustworthy in
a way they demonstrably were not before: B2 alone found **70** cells that had been reading the right answer
for the wrong reason, and the B1/B9 sweep found a **seventh** vacuous mechanism nobody had flagged.

### Why A10 runs late — the one ordering fact worth knowing

Every DWARF **Error** suppresses emission, so a newly-refused cell reports `CS8795` and stays
`NotCompilable` rather than becoming `Refused`. Task A1 proved this empirically: its fix moved a cell from
one `NotCompilable` sub-population to another and **no ceiling moved at all.** A10 reclassifies that whole
population once, so running it before the refusal tasks means measuring the same ceilings twice.

Standing ruling from the same finding: **no task may lower a ceiling it did not re-measure in the same
commit.** Predicted movement has proven unreliable.

---

## A. Generator defects — the round-20 plan

Fixing any of these turns the build **red** until its `DeclaredDivergences` entry is deleted and the three
ceilings (findings / declared cells / structural — **now 18 / 93 / 12**) are lowered to their newly measured
values.

| # | Status | Task | Closes |
|---|---|---|---|
| A0 | `DONE` | CHANGELOG must-fixes + `Scan9` (every diagnostic id must be announced) | CF §1.1–1.3 |
| A1 | `DONE` | Duplicate `[FlattenGraph]` destination emitted uncompilable code → `DWARF087` | N4 |
| A2 | `DONE` | The missing arity checks — under review. `DWARFR04` reused for D4 (**no new id**: the brief was wrong that its descriptor was dead; it checks *stacked-attribute* arity, a different thing). `DWARF088` added for D5+D21, one check, two call sites. | D4, D5, D21 |
| A3 | `DONE` | **Closed by A2, unplanned.** D3's cells are `ctor(1)` + a property initializer — named arguments ride on the one-argument constructor — so A2's check fires on them. The narrower alternative was rejected: it would leave `[MapProperty("Id")]` refused and `[MapProperty("Id", Use=…)]` silent. | D3 |
| A4 | `DONE` | Co-located host reads no member-level directives. Fixed: `MapperExtractor` reads them off the host's own members through the one `MemberDirectives` parser the registry already used; the method forms written there are refused as the new **`DWARF089`**. Measured: **2 Honoured, 18 Refused** — the twenty split 6 (the directive ACTS: 2 Honoured + 4 labelled Refused only by a pre-existing `DWARF038`) / 6 (named arguments refused on their merits) / 8 (`DWARF089`). Ceilings 19/113 → **18/93**. Found four issues, filed as **B15–B18**. | D20 |
| A5 | `DONE` | `MapToGenerator` ignored assembly-level config. Fixed **structurally**: the lookup is hoisted into `Pipeline/AssemblyConfiguration`, and all three front doors read it — the class model for its defaults layer, `DwarfGenerator` for `PublicExtensions` (an inline copy until now), and the registry for both. **One change closed two of three.** D19: the by-name wire is refused as the new **`DWARFR10`**, asked with `MapperExtractor.ReadAutoMatchMembers` itself, not a copy. D18: the registry extension class is now `internal` unless the assembly opts in — what `PublicExtensions` always documented as its default; **BREAKING**, in `CHANGELOG.md`. **D17 did NOT close and its stated reason was measurably false** — the `[MapTo]` front door emits an extension class and *no* ambient registry rows, so `RegisterCollectionShapes` has nothing there to withhold; three ways out recorded, none of them A5's to take. Ceilings 15/84 → **13/82**; no other ratchet moved, none raised. Re-measured: exactly 2 of 854 rows differ. | D18, D19 (D17 stands) |
| A6 | `PARTIAL` | `[MapNullSkip]`'s two forms were exact inverses, and **neither was wrong** — both declare `AppliesTo = All` and their docs describe one option at two scopes. The implementation had **three** readers of it: the method endpoints read the method form and never the pair-scoped one, the `[GenerateMap]` and synthesized pairs read the pair-scoped form and had no method, and projection was handed the bare class value and saw neither. Folded into one `MapperExtractor.ResolveNullSkip`, **most-specific-wins** (method → pair → mapper → assembly), which every front door calls; `ResolvePairNullSkip` is gone. **4 cells Silent → Honoured** (the pair-scoped form at CreateMap/UpdateInto), **2 cells Silent → Refused** (`DWARF090` now covers `[MapNullSkip]` element-wise, remedy with the value repeated). **Projection did NOT close and is not deferred by choice:** threading the value there is one line and produces the right behaviour — the same blocking `DWARF028` the class-level option already gets — but its `CS8795` cascade measured all three cells `NotCompilable`, raising `NotCompilableCellCeiling` 99 → **102**. Forbidden, and a closure by relocation. D6/D7 **narrowed** to their Projection cells. `DivergentCellCeiling` 82 → **76**; finding ceiling stays **13**; all five other populations re-measured unchanged. | D6, D7 |
| A7 | `DONE` | Element-wise endpoints do not inherit method-level directives — **two root causes, not one.** D1+D2 are one shape: the twice-written `DWARF077` check is now a single element-wise gate (`ReportElementWiseDirectiveGaps`) reporting the new **`DWARF090`**, whose message names the pair-scoped remedy (`[MapIgnore<TTarget>]`, `[MapProperty<TSource,TTarget>]`) — measured `Honoured` at those endpoints, so the refusal has a working replacement. **8 cells Silent → Refused.** D16 needed its own fix, the new **`DWARF091`**: `CollectHooks` accepted the partial mapping method as a hook, which at UpdateInto emitted `Update(s, d);` *inside* `Update` — shipped infinite recursion **the matrix scored `Honoured`** (filed as **B19**). **1 cell Silent → Refused, 8 more `NotCompilable` → Refused.** D2's filed evidence was wrong (`DWARF038` is `ImplicitConversionApplied`, not a placement refusal); corrected in its section. Ceilings 18/93 → **15/84**, `NotCompilable` 107 → **99**. | D1, D2, D16 |
| A8 | `DONE` | Projection does not honour member directives — **two directives, two different answers, and one finding whose own evidence was false.** **D10 closes outright** and the record it leaves is a correction: `[Flatten]` was filed as "honoured at CreateMap and UpdateInto" against `nested-pair`, and it was not — that fixture's `Dst` still declares the NESTED member, so the flatten found leaf `X`, matched it to nothing, and emitted **byte-identical output at all five endpoints**; the two `Refused` readings were an incidental `DWARF044` nullable-hop warning. New fixture `flattenable-nested-member` (struct root, destination carries the LEAF, `DWARF001`-by-design baseline) poses the question; `ResolveFlattenInfos` is now one walk both resolvers call, so **projection honours the flatten** (`__s.Child.X`) and refuses an invalid root with the same `DWARF016`, and the element-wise endpoints refuse as **`DWARF090`** naming the dotted `[MapProperty<Src, Dst>("Child.<leaf>","<leaf>")]` remedy — measured `Honoured` there before it was prescribed, because `[Flatten]` has no pair-scoped twin. **D9 narrows to its 4 Projection cells:** 8 element-wise cells close as `DWARF090` (remedy `[MapValue<Dst>("Name","x")]`, measured `Honoured` at both). Projection did not, and was measured rather than argued — `ctor(2)` becomes `Refused (DWARF064)` but the three malformed axes earn `DWARF042`/`DWARF041`, Errors, so `NotCompilableCellCeiling` measured **99 → 102**. Reverted, recorded at the call site, waiting on **A10**. Also corrected in the entry: only `ctor(2)` was ever honoured at Create/Update; the other three are `CS8795` there too. **Object-initializer question, settled with measurement: it reaches NEITHER.** A constant assignment and a leaf pull-up read nothing from the destination, and a threaded directive that measures `Honoured` at Projection is by construction not structurally inapplicable — `StructurallyExcusedCellCeiling` stays **12**. Ceilings 13/76 → **12/62**; `NotCompilable` **99**, `UnhonouredButLoud` **14**, `Unaskable` **44**, `NoSuchSite` **137**, `StructurallyExcused` **12** all re-measured unchanged. One non-ceiling baseline raised deliberately: `SurfaceProbeTests`' fixtures-without-a-member-slot count 17 → **18**, as that assertion's own message instructs. Found two issues, filed as **B25–B26**. | D9 (narrowed), D10 |
| A9a | `DONE` | Directives that act at CreateMap and are silent at the other four — **one shape, one gate, three commits.** The new **`DWARF092`** (a Warning, so the cells land as `Refused` and not in A10's population) is reported by `MapperExtractor.ReportCreateMapOnlyDirectives`, ONE function called from the update-into, projection, span-map and async-stream branches, reading through the create-map branch's own readers. **28 cells Silent → Refused.** Two of the three entries had **wrong evidence**: `D8` claimed the directive "acts at CreateMap in BOTH forms" and the OPEN form acted nowhere — the flat pair declares no hierarchy, so the sampled arguments were `typeof(Dst), typeof(Dst)` and the cell was `DWARF035` behind `CS8795`; the new `polymorphic-hierarchy` fixture fixes that and moved one cell OUT of `NotCompilable` (99 → 98). `D13` misstated the mechanism — `[ReverseMap]` does not GENERATE an inverse, it makes a separately-declared one inherit inverted renames, and a missing one is `DWARF052`. `D11`'s evidence held exactly as filed. Element-wise remedies were MEASURED (`d[__i] = Map(s[__i]);` adopts the declared create map) for `[FlattenGraph]` and `[MapDerivedType]`; `[ReverseMap]` deliberately claims no transfer. Ceilings 12/62 → **9/34**, `NotCompilable` 99 → **98**; four others re-measured unchanged. One non-ceiling baseline raised deliberately: fixtures-without-a-member-slot 18 → **19**. | D8, D11, D13 |
| A9b | `TODO` | The rest of A9 — directives acting at exactly one endpoint, but not the create-map-only shape (**one directive per commit**). `D12` acts at three and is silent element-wise; `D14` acts at UpdateInto alone; `D15` is refused at CoLocatedHost and silent at all five mapper endpoints. None of the three fits `DWARF092`'s sentence, which is about the create map specifically. | D12, D14, D15 |
| A10 | `TODO` | `CS8795` read as `NotCompilable` where it means `Refused` — 96 cells judged for the first time | G4/R4 |
| A11 | `TODO` | No struct / constructor slot in the endpoint templates — 21 cells unmeasurable | G5 |

## B. Test-infrastructure holes

| # | Status | Task | Source |
|---|---|---|---|
| B1 | `DONE` | **`Scan6a` passes by construction** — searches for an enum's members in a corpus containing the enum's own declaration. `Scan2` already excludes its own defining file; copy that. Check `Scan3`, `Scan6b`, `Scan5`-options for the same shape, and the `>=40` floors sitting against actual 59 and 82. | CF §5b |
| B2 | `DONE` | **`Property`/`Field` share one `BuildAt` arm that discards `site`** — every Field cell is byte-identical to its Property cell, so a field-only divergence is invisible while reading as measured. ~11 of 162 cells. Deserves a declared **G6** entry, not a silent fix. | CF §5b.3 |
| B3 | `TODO` | **The option matrix's excuse class cannot go stale-red** — `OptionContractTests` accepts `NotApplicable` on a non-blank *reason* alone, never re-measured, never counted (8 of 18 `ProjectionCells`). The surface matrix re-classifies live; this does not. Also `DeclaredDivergences.CoversOption` is endpoint-blind. | CF §5b.4 |
| B4 | `TODO` | **No test pins that same-source, different-destination `[FlattenGraph]` stays accepted** (`("Entry","NodesA")` + `("Entry","NodesB")`). The one shape a source-keyed refusal would wrongly reject. Guards a brand-new build-breaking Error. | Task 1 review |
| B5 | `TODO` | Fixture-baseline rule is a comment, not a gate. **Trap:** a naive "baseline must compile" check fires on four legitimate `DWARF001`-by-design fixtures. | CF §3.2 |
| B6 | `TODO` | `NoSuchSite` ratchet gates the total only — offsetting per-cause drift passes silently. | CF §3.1 |
| B7 | `TODO` | `CrossAssembly` obligation is placement-blind — a row confined to one project satisfies the one category whose claim is that it is only observable *across* assemblies. | CF §3.4 |
| B8 | `TODO` | "Shrink-only" is prose on both `PredatesTheChangelog` and `DiagnosticTestAllowlist`. One guard covers both. | CF §3.8 |
| B9 | `DONE` | `Scan9`'s control proves the *corpus* is real, not that the *assertion* is. Sweep the scan family for the same shape. | CF §3.9 |
| B10 | `TODO` | `CorpusFor`'s throwing default arm is unreached by any test. | CF §3.3 |
| B11 | `TODO` | `DiagnosticCoverageRatchetTests` claims "holds by construction"; `PredatesThisProject` is a hatch no test blocks. | CF §3.6 |
| B12 | `TODO` | Nothing forbids a cell being in both `Reasons` and `StructurallyInapplicable` (double-count). Theoretical today. | CF §3.5 |
| B13 | `TODO` | `SurfaceParityTests` checks the evidence link's *shape*, not that file and anchor resolve. Latent. | CF §5b.5 |
| B14 | `TODO` | `IsGeneratorAuthored`'s remarks omit the `*.g.cs` collision case (permissive-only, undocumented). | CF §3.7 |
| B15 | `TODO` | **The registry accepts `[MapIgnore("x")]` on a member and discards the argument** — a silent discard of exactly the shape this round hunts, and now an asymmetry: `DWARF089` refuses the identical text at the co-located host. Decide one way for both. No cell measures it (the matrix's Registry `MapIgnore ctor(1)` cells are refused by `DWARFR02` for an unrelated reason), which is why it survived. | A4 |
| B16 | `TODO` | **`DWARF088`'s message calls a co-located `[GenerateMap]` host "this mapper class".** It is one, in the sense `ExtractCore` means; it is not one to the reader who wrote a plain DTO. Pre-existing from A2, not made worse by A4. | A4, A2 |
| B17 | `TODO` | **The `StringFormat` path leaves the converter it replaced in the synthesized-helper table**, so an unused `private static` helper is emitted next to the formatted one. Cosmetic, affects every endpoint that honours `StringFormat`, harmless but it is generated code nobody asked for. | A4 |
| B18 | `TODO` | **A member-form `[MapProperty]`/`[MapIgnore]` on a MEMBER of a `[DwarfMapper]` class is swallowed** — `DWARF088` is raised off the class and method symbols, never off a member, so the one placement left is silent. Predates A4 (mode-1 `[GenerateMap]` included) and **no cell measures it**: the matrix's member sites at the five mapper endpoints splice into the DTO pair, never into the mapper class. `CoLocatedHostMemberDirectiveTests.A_DwarfMapper_class_declaring_a_pair_does_not_read_its_own_members` currently sits over this shape and says in its remarks that it does not bless it. | A4 review |
| B19 | `TODO` | **`Honoured` proves an element had an effect, not that the effect is right — so a cell can be GREEN while the generated code is broken.** `SurfaceProbe.Classify` returns `Honoured` when the emitted text merely *differs* between the with-and-without compilations. Nothing inspects what it differs *into*. **Evidence, from A7:** `[AfterMap]` on `public partial void Update(Src s, Dst d)` made the generator emit `Update(s, d);` as the last statement *of `Update`* — unconditional infinite recursion, shipped, compiling. The matrix scored that cell **`Honoured`** and the claim-parity theory passed it, at both the claimed and (had it been unclaimed) the honest reading. It was found only because D16's *neighbouring* cell at SpanMap was `Silent` and someone dumped the generated body while chasing that one. This is a different class of failure from everything else in this file: every earlier finding was the matrix failing to **measure** something, and was therefore visible as a red or unaccounted cell. This one **graded broken behaviour as working**, and left no trace anywhere in the counts. A `Refused` cell has the same blind spot in weaker form — it proves a diagnostic was added, not that it was the right diagnostic (see D2, where a `DWARF038` about an `int → string` conversion was filed for four rounds as a refusal of `[MapProperty]`'s placement). **Do not scope the remedy here** — "how does a cross-product of 854 cells assert *correctness* and not just *difference*" is a design question, not a fix, and the cheap answers (golden output per cell, a runtime execution leg, a recursion/self-call check on generated bodies) differ enormously in cost and in what they actually catch. What this item asks for is that the limitation is written down where the ceilings are read, so the next person does not mistake a green matrix for a correct generator. | A7 |
| B20 | `TODO` | **A `[MapIgnore]` that names nothing is silently inert at every endpoint.** The destination-name set is matched with `MapperExtractor.IgnoreNameComparer` (ordinal), so `[MapIgnore("id")]` against a property `Id` — or `[MapIgnore("Typo")]` against nothing at all — is accepted, excludes nothing, and produces **no diagnostic anywhere**. The caller believes they excluded a member; the completeness gate goes on demanding it, and `DWARF001` (if it fires at all) names the member rather than the dead directive. Same silent-discard shape as **B15**, and it is what made A7's comparer question ambiguous enough to get wrong: with no diagnostic to anchor on, "does this name work?" had to be answered by reading the set's construction. A `DWARF056`-style "matched nothing" report is the obvious candidate — that diagnostic already exists for the *pair-scoped* forms (`[MapIgnore<T>]` naming no pair), so the unscoped form being exempt is an asymmetry, not a policy. **Check first whether any test or sample relies on a dead `[MapIgnore]`** before making it loud. | A7 review |
| B21 | `TODO` | **`[DwarfMapper(CaseInsensitive = true)]` versus the always-ordinal ignore set — measure, do not assume.** Member *matching* can be made case-insensitive by that option, but `IgnoreNameComparer` is ordinal unconditionally. So under `CaseInsensitive = true` a source `id` maps to a destination `Id`, yet `[MapIgnore("id")]` plausibly *should* exclude `Id` and does not. **Not asserted as broken** — a defensible reading is that a directive names a member exactly while auto-matching is fuzzy, and CLAUDE.md records that the repo's one `CaseInsensitive` doc fence needs a deliberately lower-cased fixture, so the shape is under-exercised generally. What is missing is that **nothing establishes either answer**: no test pins ignore behaviour under that option, and the surface matrix cannot see it (the option is a `[DwarfMapper]` axis, the ignore is a separate element, and no cell varies both). Decide it, then pin it — the comparer now has one home (`MapperExtractor.IgnoreNameComparer`) so the change would be one line plus tests. | A7 review |
| B22 | `TODO` | **No `DWARFR##` message or remedy is pinned anywhere — the whole registry family, not one id.** Every `DWARF0xx` id has its wording held in place: `DiagnosticMessageContractTests` and `DiagnosticProseIsCurrentTests` pin message shape and prose, `Scan9` forces a `CHANGELOG.md` entry per id, and `DiagnosticCoverageRatchetTests` wants a `NegativeCases` row. The `DWARFR` family is **excluded from all of them by written convention** — `AssemblyScanTests.cs:74-80` states the `Scan9` exclusion explicitly (the family is announced as a *range*), and `docs/diagnostics.md` declares `[MapTo]` a prototype tier exempt from the DWARF0xx self-validation scans. So the family's only gate is `RegistryDiagnosticsGenTests`, which checks that each id is **triggered** by some test and **mentioned** in `docs/diagnostics.md`. Neither is a check on WORDING: a `DWARFR` message can be rewritten, have its remedy inverted, or stop naming a fix at all, and nothing fails. **Surfaced by A5**, which added `DWARFR10` and followed the convention correctly — the id's message names two remedies and both are unpinned. Two concrete instances to settle while deciding the mechanism: (a) `DWARFR10` offers `[MapIgnore]`, which at the registry ignores the *source* member and so leads straight to `DWARFR02` — the registry has no destination-side skip, unlike `[MapIgnore("Dest")]` in the class model, so one of the two offered fixes takes two steps (disclosed and accepted at A5 review, since the `[MapProperty]` remedy does terminate in one); (b) `DWARFR02`'s own text says "add a source member, a `[MapProperty]` binding, or remove it", which is the fix a `DWARFR10` reader has just been sent away from. **The decision is whether to fold `DWARFR##` into the DWARF0xx scheme** (`RegistryDiagnostics.cs` has said "a later unification could fold them in" since it was written) **or to give the family its own wording gate.** Folding is the larger change and settles it once. | A5 review |
| B23 | `TODO` | **`ApiReferenceRenderer` silently eats a space between two adjacent inline doc tags.** `ApiReferenceRenderer.cs:189` calls `XDocument.Load(xmlPath)` with default `LoadOptions.None`, which **discards whitespace-only text nodes**. So wherever a doc comment separates two inline elements by nothing but a space — `</b> <c>`, `</c> <see …/>`, `</see> <c>` — the space is gone before `Flatten` ever runs, and `docs/generated/api-reference.md` renders `classes.AutoMatchMembers` for source that plainly reads `classes.</b> <c>AutoMatchMembers</c>`. `Flatten`'s own `Regex.Replace(@"\s+", " ")` cannot restore what the loader dropped. **Found by A5** — twice: once in the generated output, then again because A5's report claimed a reword to `<c>` had fixed it when re-reading the regenerated file showed it had not. Only the *authored* comment was reworded (real text now sits between the tags), so the renderer defect is untouched and latent for every future doc comment. Fix is one argument — `LoadOptions.PreserveWhitespace` — but it is shared doc tooling whose output is byte-compared by `GeneratedDocsAreCurrentTests`, so it must be applied with the whole regenerated diff reviewed, which is why A5 filed it rather than folding it in. **No test would have caught this**: the docs test compares the generated file against the renderer's own current output, so a renderer bug is self-consistent and invisible to it. | A5 review |
| B24 | `TODO` | **The same pair named twice with CONTRADICTING `[MapNullSkip<S,T>]` values silently discards the second.** `MapNullSkipAttribute<TSource, TTarget>` is `AllowMultiple = true`, so `[MapNullSkip<Dto, Entity>(true)]` beside `[MapNullSkip<Dto, Entity>(false)]` on one class compiles clean. `MapperExtractor.ResolveNullSkip` returns the **first** match by declaration order and the second directive is dropped without a word — a caller's explicit, opposite statement of intent, discarded, which is the exact shape this round hunts. Distinct from the method-vs-pair contradiction A6 DID define (most-specific-wins, pinned both directions): there the two forms have a defensible ranking, here they have identical scope and only source order separates them, so there is nothing to rank. Deferred rather than fixed because refusing it is a new diagnostic and needs the full five-file sync; `DWARF056`'s "matched nothing" pass is the closest existing shape. Found while unifying the readers, not measured by a test — the surface matrix cannot reach it, because its `×2` axis renders two IDENTICAL applications on purpose (a `bool` samples to `true` for both variants), so no cell in the 865 poses this question. | A6 |
| B25 | `TODO` | **`[MapValue<T>]`'s probe arguments are still the nonsense shape the arity-0 form was corrected out of.** `MapValueAttribute<TTarget>` declares `[DwarfSurfaceProbe(… Arguments = "{Id}, \"probe\"")]` — a string constant assigned to an `int` — so all 28 of its cells measure the generator's reaction to a **type error** (`DWARF040`) rather than to a constant assignment. Measured while working D9: `[MapValue<Dst>("Id", "probe")]` reads `NotCompilable (CS8795)` at CreateMap, SpanMap and AsyncStream, and `Refused (DWARF056)` at UpdateInto and Projection; with `{Name}` and a string it reads `Honoured` at SpanMap and AsyncStream (that measurement is what pinned `DWARF090`'s remedy). Its arity-0 twin was fixed exactly this way in an earlier round and the generic form was left behind. **Out of A8's cell list** — D9 is arity 0 only — but it is the same stale-evidence pattern this round exists to find, and the DWARF056 readings at UpdateInto/Projection are an unexamined claim of their own: the pair-scoped form apparently does not reach those two endpoints. | A8 |
| B26 | `TODO` | **`DWARF044` fires for a `[Flatten]` root whose leaves land nowhere.** `ResolveFlattenInfos` warns about a nullable-reference root as soon as the root resolves, before anything asks whether a single leaf matches a destination member — so a flatten that maps nothing at all still warns that "a null value throws at runtime when its flattened members are read", about members nobody reads. Harmless in isolation; not harmless as evidence, because it is precisely what made **D10** read `Refused` at CreateMap and UpdateInto for four rounds while the emitted output was byte-identical, and therefore what let the finding claim the directive was honoured there. A diagnostic that fires on a directive with no effect is a cell that looks measured and is not. Cheap fix (warn only when `flatMatches` actually consumed a leaf), but it changes an existing warning's trigger, so it is filed rather than folded into A8. | A8 |

## C. Tooling and environment

| # | Status | Task | Source |
|---|---|---|---|
| C1 | `TODO` | **Stryker runs in no CI job at all.** The 66.95% is a manual leg, free to regress silently. | CF §5b.1 |
| C2 | `DONE` | **`.git`-as-a-file breaks the doc tests in any worktree** — and forced Task 1 to hand-render a generated file. Fix `RepoRoot` to accept both. | CF §4.1 |
| C3 | `TODO` | Both sibling Stryker configs are parse-fixed but never run; `break: 70` is inherited and unvalidated. | CF §4.2 |
| C4 | `TODO` | `docs/research/testing-conformance-REPORT.md:22` still says "Mutation testing — none". | CF §5b.2 |
| C5 | `TODO` | Drift: `ci.yml` says "854 cells" (actual 861/865); a ratchet open-codes `AssertRatchet`; `AssemblyScanTests` keeps a private repo-root walk `RepoPaths` exists to replace. | CF §5b.6 |
| C6 | `TODO` | Kill the top mutation survivors: `DwarfMapperRegistry.cs:76` (duplicate `Register` → `InterfaceMaps`, stated invariant, **zero tests**), `DwarfMappingDepthException.cs:32`, `DwarfMapExceptions.cs:95`. **Do not** attempt `:291` — equivalent in practice, see CF §5.4. | CF §5.3–5.4 |

## D. Decisions — **ruled**, now ordinary work

These were parked awaiting a maintainer. Under the execution rule adopted 2026-08-16 they were ruled on
instead, each with what it costs if the ruling is wrong. **Every one is reversible in a single commit.**
Full reasoning is in the ledger under `Ruling:`.

| # | Status | Ruling | Cost if wrong |
|---|---|---|---|
| D-a | `TODO` | **`NullCollections`@`Projection`: keep today's behaviour, keep the divergence entry, and document it** in `docs/options.md` as *projection's collection null-semantics are `AsNull` by nature*. Option (a) risks failing inside a translated query at runtime — worse than a documented divergence. Option (b) was implemented and reverted after breaking seven tests. | Nothing changes at runtime; the entry stays recorded and (a)/(b) remain open to a later maintainer. |
| D-b | `TODO` | **Delete `ResetForTests`.** Zero callers; its IVT targets a project whose registry tests do not use it; the mutation leg excludes that project — so round-19's additions to it are unverifiable dead code *by construction*. | A future test wants a reset hook and re-adds ~8 lines. |
| D-c | `TODO` | **Delete the stale items from `CLAUDE.md`'s working note** — the file's own rule is to delete each once decided, and one describes a fence-allowlist mechanism that no longer exists. ⚠️ This edits the agent's own instructions, so it is called out rather than done quietly. | Two historical notes lost — both preserved here and in `CARRY-FORWARD.md`. |
| D-d | `DONE` | **Keep `internal` + `[InternalsVisibleTo]`.** The four meta-attributes need it; public would grow the shipped API for test-only metadata, and a separate assembly breaks the single-package delivery story. The CRA objection was about what the IVT *exposes* — **D-b resolves it**, leaving only inert metadata with zero runtime reads. | If the CRA posture later demands zero IVT, the meta-attributes move to their own assembly — a contained refactor. |
| D-e | `TODO` | **Keep deferring the ~80 unannounced diagnostics** to a first-release-notes task; `PredatesTheChangelog` is the worklist. The project has never shipped, so nothing is currently mis-announced. | First release notes ship incomplete. `Scan9` guards only *new* ids, so **this one needs a human before the first tag.** |

## E. Research — measure before changing anything

| # | Status | Task |
|---|---|---|
| E1 | `TODO` | **Mutation leg costs 44 min because 91 tests do all the killing and 5,592 pay for it** (~61× waste). Scope to the intentional tests. See R21-1. |
| E2 | `TODO` | **Time the torture collection × 49** — I wrongly recorded this hypothesis as refuted on kill-count, which is not time-cost. Genuinely unmeasured. See R21-3. |
| E3 | `TODO` | Check from the existing JSON whether any mutant was killed **only** by an accidental toucher — de-risks E1 with no run at all. |
| E4 | `TODO` | Isolate the culture-swapping tests into their own collection so the rest of `IntegrationTests` can parallelise. **Bigger than it looks:** a second shared-state hazard (the registry static) the existing comment never mentions. See R21-3. |

## F. Branch and process — the work that is not a code fix

Previously invisible: it lived only in the SDD ledger and in my head. It belongs here like everything else.

| # | Status | Task |
|---|---|---|
| F1 | `TODO` | **Merge `feat/surface-coverage-architecture`.** Round 19's final whole-branch review returned *merge after must-fixes*; those were A0 and are done. **Needs your say-so — a merge is a side effect outside this worktree, so I do not do it on a ruling.** 30 commits, `+6.4k/−0.5k`, of which `src/` is only +729. |
| F2 | `TODO` | Decide the fate of the worktree after merge. It is a real git worktree at a sibling path, plus a git-ignored `.superpowers/sdd/` workspace holding the ledger, briefs and reports. The ledger is the only record of ~15 rulings; **capture it before deleting anything.** |
| F3 | `TODO` | Round-19 plan and spec live in `docs/superpowers/`; round-20's plan is on the branch. If the branch merges, three plan documents land in `docs/` — decide whether they stay as history or move under `Issues/`. |
| F4 | `DONE` | Pre-flight conflict scan for the round-20 plan (table + four ordering rulings) — recorded in the ledger. Was skipped before A0 and run late. |

## G. Every machine that holds work and executes steps

The inventory. Each of these holds a list and drains it — and until now most existed only implicitly, which is
how the same item ended up recorded in three places and the ceilings ended up misquoted in a fourth.

| Machine | Where | Holds | Drained by |
|---|---|---|---|
| **Round-20 task list** | `Issues/round20/TASKS.md` | this file — ~40 items | the standing rule: every issue lands here |
| **Divergence store** | `Contracts/DeclaredDivergences.cs` | **19 findings / 113 cells**, each asserting its defect *still exists* | section **A**; an entry deleted per fix |
| **`PredatesTheChangelog`** | `SelfValidation/AssemblyScanTests.cs` | **76** diagnostics never announced | **D-e** — needs a human before the first tag |
| **Mutation survivors** | `StrykerOutput/…/mutation-report.json` | **39**, kill-list ranked | **C6** |
| **Round-19 SDD workspace** | `.superpowers/sdd/2026-08-13-…/` | ledger + briefs + reports, **git-ignored** | complete; **F2** — the ledger is the only record of its rulings |
| **Round-20 SDD workspace** | `.superpowers/sdd/2026-08-16-…/` | ledger + briefs + reports, **git-ignored** | in use |
| **Worktrees** | `DwarfMapper.NET` (master), `DwarfMapper-surface` (branch) | 30 unmerged commits | **F1** merge, **F2** cleanup |
| **CI** | `.github/workflows/ci.yml`, `release.yml` | build/test legs incl. the `SurfaceMatrix` trait leg | **C1** — no mutation leg exists; **C5** — says "854 cells" |
| **Scripts** | `scripts/` — `housekeeping.ps1`, `mutation-battery.sh`, `conformance-gate.sh`, `run-aot-bench.ps1`, `git-hooks/` | the `-Mutation` legs and the non-vacuity guard | **C1**, **C3** |
| **Stryker configs** | `stryker-config{,.doctooling,.runtime}.json` | three mutation legs | **C3** — two never run to completion; `break: 70` unvalidated |
| **Research** | `Issues/round21/RESEARCH.md` | 3 items, one already measured | section **E** |
| **Reasoning archive** | `Issues/round20/CARRY-FORWARD.md` | the *why* behind these items | not a worklist — do not track work there |

### The ten shrink-only ratchets

Each is a population that may only get smaller. **No task may lower one it did not re-measure in the same
commit** — predicted movement has proven unreliable (A1 moved a cell between two `NotCompilable`
sub-populations and no ceiling changed at all).

| Ratchet | Value | Meaning |
|---|---:|---|
| `NoSuchSiteCellCeiling` | 137 | no declaration site exists — 21 of these are **A11**'s template gap |
| `NotCompilableCellCeiling` | 107 | compiler rejected the placement — **96 carry the wrong verdict**, that is **A10** |
| `UnaskableCellCeiling` | 44 | the case-space cannot pose a question |
| `UnhonouredButLoudCellCeiling` | 14 | changed nothing, but the build fails anyway |
| `DivergentCellCeiling` | 113 | cells covered by a recorded divergence |
| `DivergenceFindingCeiling` | 19 | recorded divergences |
| `StructurallyExcusedCellCeiling` | 12 | shape-based, not behavioural |
| `PredatesTheChangelog` | 76 | diagnostics never announced |
| `DirectCompileErrorCallBaseline` | 52 | direct `RunAndGetCompilationErrors` call sites |
| `MapMethodModelBoolFlagBaseline` | 15 | signature-triggered map modes |

---

## The honest caveat about this list

There are **~40 open items** here. That is a real risk in itself: a list where everything is a task is a list
nobody finishes, and it can feel like progress while nothing closes.

**Suggested cut line, if one is wanted:** everything in **A** (they are measured product defects with a ratchet
already asserting they exist), plus **B1–B4** (each is a mechanism currently reporting success while measuring
nothing — the failure mode that has now appeared **six** times), plus **C1–C2** (both make other work
untrustworthy). That is ~18 items. Everything else is genuinely deferrable, and **D** is not work at all until
it is ruled on.
