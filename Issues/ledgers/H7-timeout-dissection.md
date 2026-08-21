# H7 phase 1 — dissection of every Timeout-classified mutant

Read-only analysis, 2026-08-21. Sources: the five Stryker JSON reports under
`C:\Users\Jouda\RiderProjects\DwarfMapper-r21\StrykerOutput\`, the ledgers
(`Issues/ledgers/T3-mutation-survivors.md`, `Issues/ledgers/E3-E1-report.md`), the three
`stryker-config*.json` comments, and the product/test sources in the r21 worktree. No repo file was
modified; no Stryker or test run was launched (one 6-second PowerShell StringBuilder throughput probe was
run to bound a timing claim; see §2.1).

## 0. Report inventory and which run is authoritative per leg

| Run dir (worktree `StrykerOutput/`) | Leg | Status counts | Timeout mutants |
|---|---|---|---|
| `2026-08-19.06-17-43` | generator (`stryker-config.json`) | K144 / S44 / T2 / NC11 | BlittableProof.cs L28, L58 (both `static: true`, coveredBy 0) |
| `2026-08-19.06-41-40` | generator, re-run with `additional-timeout: 120000` | K144 / S46 / T0 / NC11 | none |
| `2026-08-19.12-10-18` | generator, H5 post-RepoWriteGuard re-run — **authoritative** | K144 / S46 / T0 / NC11 | none — zero flips vs 06-41-40 across all 471 mutants |
| `2026-08-19.07-03-07` | doctooling (`stryker-config.doctooling.json`), pre-guard T3 run | K233 / S11 / T3 / NC37 | DocSnippetInjector.cs L39 (id 204), SnippetScanner.cs L138 (id 525), L139 (id 534) |
| `2026-08-19.11-49-34` | doctooling, post-RepoWriteGuard — **authoritative** | K191 / S54 / T2 / NC37 | SnippetScanner.cs L138 (id 525), L139 (id 534) |

**Runtime leg (`stryker-config.runtime.json`, src/DwarfMapper): no `mutation-report.json` exists anywhere on
disk** in either repo (verified by `find` over both trees). The newest evidence is ledger-only: the
2026-08-17 full-suite run documented in `Issues/ledgers/E3-E1-report.md` and in the config's own comment —
K71 / S39 / T1 / NC7 of 118 scoreable, score 61.02 %. Its single Timeout is `DwarfMapExceptions.cs` L107, a
string mutation with 4 covering tests that **cannot hang** (no loop/recursion/blocking call) and is Killed in
every other run of the same config — a live example of load manufacturing a Timeout even under a 62 % cushion.

**Bottom line: across all three legs and their entire history, exactly three mutants have ever genuinely
hung — all in DocTooling.** Everything else Timeout-classified was the clock, not the code (catalogued in §3).

## 1. The reconciliation the coordinator asked for: 3 pre-guard hangs → 2 post-guard hangs

Mutant 204 (`DocSnippetInjector.cs:39`, `i++` → `;`) flipped **Timeout → Killed** between the two same-day
DocTooling runs; 525/534 stayed Timeout in both. All three are genuine infinite loops — the flip is not a
mutant becoming detectable, it is the *same nondeterministic race resolving differently*:

- **204's runaway loop has an allocation side channel.** Each stuck iteration appends the same prose line to
  the result `StringBuilder`, so the loop terminates itself by memory exhaustion when the builder exceeds
  `int.MaxValue` chars (~2.1 G chars ≈ 4 GB). A throughput probe on this machine measured ~63 Mchars/s for
  the append pattern → the crash lands on the order of **~30–90 s** (GC pressure slows the tail). The
  DocTooling per-mutant ceiling is `initial test time (~60–90 s) + Stryker's small default cushion` — the
  same order. Whichever fires first decides the classification: pre-guard run the clock won (Timeout),
  post-guard run the crash won (Killed, `killedBy` =
  `DocReconciliationTests.No_snippet_region_outside_a_declared_example_is_orphaned` and
  `DocsAreSnippetCurrentTests.Every_snippet_marker_in_every_doc_matches_its_sample`, both of which run
  `Inject` over the real, prose-first repo documents). *Mechanism status: high-confidence inference from
  code + measured throughput, not a direct observation of the exception.*
- **525/534's runaway loops are pure CPU spins.** `kept.RemoveAt(0)` deleted leaves a loop with no
  allocation, no side effect, no escape of any kind — nothing inside the process can ever fail. Only the
  wall clock can end them, so they classify Timeout in both runs, stably.

Same mutation class (Statement deletion of a loop's progress statement), two different classifications,
decided solely by whether the runaway loop happens to allocate. This is the sharpest single piece of
evidence for the maintainer's directive: **the Timeout status proves nothing about the mutant; it reports a
race between the defect's accidental side effects and Stryker's load-sensitive clock.**

**Correction to flag:** `stryker-config.doctooling.json` NOTE 1 says the post-guard 204 is *"Killed outright
before the loop is reached."* That is contradicted by the evidence: both killing tests call
`DocSnippetInjector.Inject` on real documents whose first line is prose (`DocSet.All` starts with
`README.md`); `DocRegions.All()`/`DocSet.Read` never touch the injector, and `Inject`'s pre-loop preamble
(`ThrowIfNull`, `Replace`, `Split`) cannot fail. The loop **is** reached and the kill happens *inside* it,
by memory exhaustion racing the clock. The sentence should be corrected when the config is next edited
(implementation work; repo is read-only for H7 phase 1).

## 2. The three genuine hangs, dissected

### 2.1 Mutant 204 — `src/DwarfMapper.DocTooling/DocSnippetInjector.cs:39`

- **Mutator:** Statement mutation. **Original:** `i++;` (the prose-branch advance inside
  `while (i < lines.Length)`). **Mutated:** `;`.
- **Loop and its termination variant.** The loop has exactly two non-throwing advance sites: the prose
  branch's `i++` (line 39) and the snippet branch's `i = closeIndex + 1` (line 70), where
  `closeIndex ≥ i + 1` because `FindClose` searches from `i + 1`. The variant is `lines.Length − i`,
  a non-negative integer strictly decreasing on every non-throwing iteration; every throwing path exits.
- **Why the mutation destroys it.** With `i++` deleted, any iteration that takes the prose branch is a fixed
  point of the loop state except for `sb.Append(line)`: `i` never changes, the same line is re-tested and
  re-appended forever. Every real document reaches this branch (any prose line; note even an "all-marker"
  document whose text ends in `\n` reaches it, because `Split('\n')` yields a trailing `""` prose line).
  The only inputs on which the mutant terminates are zero-prose, no-trailing-newline documents — and on
  those its output is identical to the original, so **no terminating input can distinguish the mutant**.
  The observable universe is: non-termination, plus unbounded `StringBuilder` growth ending in memory
  exhaustion at an unpredictable time.
- **Deterministic kill.** No test-side kill exists against the current code shape — honestly stated: this is
  a pure non-termination mutant (with an accidental OOM side channel whose timing is the nondeterminism the
  directive bans). The cheapest structural change that makes progress observable is a **progress guard in
  the loop** (production change, controller's review required):

  ```csharp
  var i = 0;
  var previous = -1;                       // progress guard: see below
  while (i < lines.Length)
  {
      // Stryker disable all : the guard is reachable only when the loop body itself is defective;
      // no test of correct code can trigger it, so its own mutants are untestable by construction.
      if (i <= previous)
          throw new InvalidOperationException(
              $"DocSnippetInjector stopped advancing at {docPath}:{i + 1} — injector bug, not a document error.");
      previous = i;
      // Stryker restore all
      ...existing body unchanged...
  }
  ```

  With the guard in place, the `i++`-deletion mutant dies **deterministically in microseconds** in all ten
  covering tests (the second iteration throws), no new test needed — though one cheap direct test documents
  the contract and pins the ten-cover redundancy down to a named intent:

  ```csharp
  // tests/DwarfMapper.Generator.Tests/SelfValidation/DocSnippetInjectorTests.cs
  [Fact]
  public void A_document_of_prose_and_markers_terminates_and_reproduces_every_line()
  {
      // Pins the loop's progress contract: every input line appears exactly once in the output.
      // Under an advance-loss defect the guard turns this into an instant loud failure instead of a hang.
      var regions = new Dictionary<string, SnippetRegion>(StringComparer.Ordinal)
          { ["demo"] = new SnippetRegion("demo", "var x = 1;", "F.cs", 1) };
      const string doc = "one\n<!-- snippet: demo -->\n<!-- endsnippet -->\ntwo\nthree\n";

      var result = DocSnippetInjector.Inject(doc, regions, "d.md").Markdown;

      // Assert.Single with predicate rather than Equal(1, Count(...)): xUnit2013-safe in a
      // warnings-as-errors repo.
      Assert.Single(result.Split('\n'), l => l == "one");
      Assert.Single(result.Split('\n'), l => l == "two");
      Assert.Single(result.Split('\n'), l => l == "three");
  }
  ```

  (Without the guard this test hangs rather than fails under the mutant — it is the guard that converts the
  failure mode; the test alone is not a kill.)
- **Production-hardening verdict: modest genuine merit, controller's call.** This is a parser loop with two
  distinct advance sites in separate branches; a future third branch that forgets to advance is a plausible
  human edit, and today its failure mode is a CI job that hangs (or eats 4 GB and dies of OOM) instead of a
  one-line loud failure naming the file and line. That is a real robustness improvement independent of
  Stryker — but the *proximate* motive is the mutation directive, so it is flagged, not assumed.
  Alternative if the controller declines the guard: `// Stryker disable once Statement : deleting the
  increment can only manifest as non-termination; a wall-clock kill is nondeterministic (observed Timeout
  2026-08-19.07-03 and Killed 2026-08-19.11-49 on the same day, same suite).` — placed on its own line
  immediately ABOVE the `i++;` statement (`disable once` applies to the next line; a trailing comment does
  nothing). Score effect of the disable: 204 is currently Killed, so removing it from the pool gives
  192/283 = 67.84 %, floor 67 — `break: 67` unaffected. (The `// Stryker disable once <MutatorList> :
  reason` syntax is verified against Stryker.NET's ignore-mutations docs; the mutator name `Statement` is
  inferred from the JSON report's "Statement mutation" label — confirm against the docs' mutator list
  before committing.)

### 2.2 Mutants 525 and 534 — `src/DwarfMapper.DocTooling/SnippetScanner.cs:138` and `:139` (Dedent)

- **Mutator:** Statement mutation. **Original:**
  `while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[0])) kept.RemoveAt(0);` (L138) and the mirror
  `while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);` (L139).
  **Mutated:** loop body → `;`.
- **Loop and its termination variant.** `kept.Count` is a non-negative integer strictly decreased by every
  iteration (`RemoveAt` on a non-empty list — non-emptiness is guaranteed by the `kept.Count > 0` conjunct).
  Bounded below by 0, so termination is provable in two lines. The original is **robust**; there is no
  fragile expression here.
- **Why the mutation destroys it.** With the body deleted, the loop state is frozen, so the condition is
  constant: if the region body's first (resp. last) line is blank, the condition is true forever — an
  unconditional infinite loop with **no side effects at all** (no allocation, no I/O, no growing state).
  If the first/last line is non-blank, the loop never runs in either version and the mutant is behaviorally
  identical. **Every distinguishing input hangs; every terminating input is indistinguishable.** This is the
  purest form of the problem: unlike 204 there is no OOM escape, which is exactly why these two classify
  Timeout stably in both runs.
- **Deterministic kill: impossible without a structural change — stated honestly.** The cheapest change that
  converts this mutation class from "hang" to "wrong value" is replacing the two mutable-trim loops with
  index computation and one slice (production change, controller's review required). It is behaviorally
  identical, including the empty-region refusal (`first < 0` ⇔ the old `kept.Count == 0`):

  ```csharp
  // in Dedent(...), replacing lines 137–144:
  var first = body.FindIndex(l => !string.IsNullOrWhiteSpace(l));
  if (first < 0)
      throw new DocToolingException(
          $"{relativePath}:{openLine}: snippet '{id}' is empty. An empty region renders as an empty "
          + "code fence, which reads as \"this feature needs no code\".");

  var last = body.FindLastIndex(l => !string.IsNullOrWhiteSpace(l));
  var kept = body.GetRange(first, last - first + 1);
  ```

  Under this shape no Stryker mutation can produce a loop: the mutable state machine is gone. Mutating the
  lambda (`!IsNullOrWhiteSpace` → `IsNullOrWhiteSpace` / `true` / `false`), the arithmetic
  (`last - first + 1` variants), or the `first < 0` comparison each yields a **wrong slice or an immediate
  `ArgumentException`/`ArgumentOutOfRangeException`** — value-observable, killed in microseconds by the test
  below.
- **The accompanying test** (belongs in
  `tests/DwarfMapper.Generator.Tests/SelfValidation/SnippetScannerTests.cs`, matching its existing style —
  needed regardless of the restructure, because today the *only* covering test of the trim is one CsCheck
  property, i.e. the trim has no direct example-based pin at all):

  ```csharp
  [Fact]
  public void Leading_and_trailing_blank_lines_are_trimmed_from_the_region()
  {
      // The trim is what keeps a region author free to pad the markers for readability without the
      // padding showing up inside the rendered fence. Whitespace-only lines count as blank.
      var source = "// <snippet: demo>\n\n   \nvar x = 1;\n\t\n\n// </snippet>";

      Assert.Equal("var x = 1;", SnippetScanner.ScanFile("F.cs", source)[0].Body);
  }
  ```

  Against the restructured code this kills the whole trim-mutation family deterministically. Against the
  *current* code it passes but would hang (not fail) under the RemoveAt-deletion mutants — the restructure
  is the enabling change, the test is the pin.
- **Production-hardening verdict: no hardening warranted.** The original loops are trivially provably
  terminating; adding a guard would be pure mutation-appeasement. The restructure is an equivalent
  expression-shaped formulation of equal clarity whose *only* motive is making the mutation class
  value-observable — recommended because the maintainer's directive demands a deterministic death and this
  is the cheapest one, but it should be adopted as that, not sold as a robustness fix.
  Alternative if declined: `// Stryker disable once Statement : deletion of the progress statement can only
  manifest as non-termination; the loop's termination (Count strictly decreasing, bounded by 0) is proven in
  the T3/H7 ledgers.` — one such comment on its own line immediately ABOVE each of L138 and L139
  (`disable once` applies to the next line only). Score effect: the two Timeouts currently count as
  *detected*, so disabling both gives 191/282 = 67.73 %, floor 67 — `break: 67` unaffected.
- **Config follow-ups either way:** NOTE 1 and NOTE 7 of `stryker-config.doctooling.json` are written around
  these two mutants being expected, genuine, Timeout-classified detections (NOTE 7 even warns the score
  drops "if the environment stops hanging"). After the restructure they become ordinary Killed mutants
  (score unchanged — Timeout and Killed both count as detected, 193/284 = 67.96 % either way) and both
  notes need rewriting; after a disable, NOTE 7's margin arithmetic changes (three-mutant margin becomes
  191/282 vs floor 67). Either path ends with one DocTooling re-measure to confirm **zero Timeouts**.

### 2.3 Adjacent loops audited while in the file (no action needed)

- `SnippetScanner.Dedent` L164–165 (`while (prefix.Length > 0 && !w.StartsWith(prefix)) prefix = prefix[..^1];`)
  has the same theoretical shape (deleting the assignment would hang), but Stryker's Statement mutator does
  not delete assignment statements — no such mutant exists in any report, and the loop is provably
  terminating (`prefix.Length` strictly decreasing; `w.StartsWith("")` is always true, so the exit is
  doubly guaranteed). If the Dedent restructure is done, this loop can stay as is.
- `DocSnippetInjector.FindClose` and `SnippetScanner.ScanFile`'s `for` loops: the increment-flip mutants
  (`i++` → `i--`) drive the index negative and die immediately on `IndexOutOfRangeException` — deterministic
  kills, already Killed in the reports.

## 3. The load-noise list — Timeout classifications that were the clock, not a hang (keep separate; no test work, no hardening)

### Runtime leg (2026-08-16 baseline → resolved by `additional-timeout: 120000`, per E3-E1 ledger; no JSON on disk)

Seven flipped **Timeout → Survived** (i.e. the baseline counted seven non-detections as detected):

| # | Mutant | Replacement | Why it cannot hang |
|---|---|---|---|
| 1–3 | `DwarfMapperRegistry.cs` L200, L201, L202 | `ArgumentNullException.ThrowIfNull(…)` → `;` | guard deletion; no loop/recursion/blocking call |
| 4 | `DwarfMapperRegistry.cs` L301 | `Source.GetHashCode() * 397` → `/ 397` | arithmetic in a hash mixer |
| 5–6 | `DwarfRefContext.cs` L77 (×2) | depth-clamp ternary mutations (incl. `maxDepth < 1` → `<= 1`, **provably equivalent** — no test can ever detect it, yet it was counted "detected") | single ternary evaluation |
| 7 | `DwarfRefContext.cs` L78 | conditional-false arm of the clamp | single ternary evaluation |

Four more flipped **Timeout → Killed** (real detections the slow session credited to the clock instead of a
test): `DwarfMapperRegistry.cs` L68/L69/L70 (killed by `AmbientRegistryTests`) and `DwarfRefContext.cs` L78
equality (killed by `CollectionGraphNodeRuntimeTests`, `PreserveGraphEdgeCasesRuntimeTests`,
`SetNullCycleRuntimeTests`). Mechanism for all eleven: these are effectively whole-suite mutants (registry
statics / DwarfRefContext threaded everywhere), so each session re-ran a ~193 s serial suite under a ceiling
sitting seconds above 193 s; Stryker's bail makes the *survivor* sessions the longest, so the Timeout bucket
preferentially collected survivors and reported them as detected. Residual: `DwarfMapExceptions.cs` L107
(string mutation, 4 covering tests, cannot hang) still drew a Timeout in the corrected 2026-08-17 run —
load can manufacture a Timeout even at 62 % headroom.

### Generator leg (2026-08-19.06-17-43 → resolved by `additional-timeout: 120000`, confirmed twice)

| Mutant | Replacement | Why it cannot hang |
|---|---|---|
| `BlittableProof.cs` L28 (id 1812) | `!a.IsUnmanagedType \|\| !b.IsUnmanagedType` → `&&` | no loop; `LayoutIdentical` recurses only through struct fields and C# forbids struct cycles (CS0523), so depth is finite |
| `BlittableProof.cs` L58 (id 1860) | one `or` → `and` in `IsPrimitive`'s `SpecialType` pattern | a single enum pattern match |

Both are `static: true` with empty `coveredBy` → each re-runs the whole 5,798-test suite (~203 s) under a
ceiling seconds above it. With the cushion both resolve honestly as **Survived** (zero mutants gained
detection; the 1.00 pp score drop is exactly these two re-classifications). The H5 post-guard re-run
(`2026-08-19.12-10-18`) is **clock-clean: zero Timeouts, zero status flips vs 06-41-40 across all 471
mutants** — the generator leg has never contained a genuine hang.

Verified: `additional-timeout: 120000` is present in both `stryker-config.json` and
`stryker-config.runtime.json`; `stryker-config.doctooling.json` deliberately has none (its NOTE 1 —
correct while the two genuine hangs remain; obsolete after §2.2 lands).

## 4. Test-side hang exposure

Tests that today would hang (or die of OOM) rather than fail if the product loops:

| Test | Exposure |
|---|---|
| `DocPipelinePropertyTests.A_region_survives_extraction_after_being_written_into_source` | **Sole coverer of 525/534.** A 500-iteration CsCheck property whose body generator includes blank lines — a leading/trailing blank draw spins the mutated trim forever, inside `Sample`, with no way to fail. |
| `DocReconciliationTests.No_snippet_region_outside_a_declared_example_is_orphaned`, `DocsAreSnippetCurrentTests.Every_snippet_marker_in_every_doc_matches_its_sample` | Run `Inject` over all 16 real documents; under an advance-loss defect they OOM after ~4 GB growth or hang, whichever the machine decides. On a CI runner the OOM can destabilize the whole job. |
| The other eight coverers of 204 (`DocSnippetInjectorTests` ×5, `DocPipelinePropertyTests` ×3) | Same: hang-or-OOM, never a clean assertion failure. |

**What the repo can and cannot use to bound itself:**

- `[Fact(Timeout = n)]` (xunit 2.9.3): per xunit's docs the timeout is only honored for async tests, and
  its accuracy depends on the parallelism algorithm (the conservative default gives "more accurate test
  timeouts"; disabling parallelization historically defeats it — only `DwarfMapper.IntegrationTests`
  disables it, `Generator.Tests` does not, so the attribute *would* be live there). But even where it works
  it is cooperative: a CPU-bound spin in product code never observes cancellation, so xunit marks the test
  failed while the runaway thread keeps burning a core until process exit. It is a smaller, less
  load-sensitive clock — still a clock. **Not recommended as the kill;** acceptable only as belt-and-braces
  on the property test if the controller wants one.
- **The honest fix is product-side** (§2.1 guard, §2.2 restructure): after those, every covering test fails
  by assertion or by the guard's exception, deterministically, in microseconds — no test-side clock needed.
- **Runner-level backstop (recommended regardless):** `dotnet test --blame-hang --blame-hang-timeout 5m` in
  the CI test step. This is VSTest infrastructure, not a test-semantics change: a hung testhost is dumped
  and killed, the job fails with the offending test named instead of idling until the job-level timeout.
  It bounds *every* future hang, not just these three loops. (Do not add it to the Stryker legs — there the
  per-mutant ceiling already serves that role and a second killer would fight it.)
- An in-fixture iteration guard is not applicable here: the loops are in product code and expose no
  iteration count; bounding them from the test side is impossible without threads-plus-clock, which is the
  thing being banned.

## 5. Hand-off summary for the controller (implementation order)

1. **`SnippetScanner.Dedent` restructure** (§2.2, production, behavior-identical) + new
   `SnippetScannerTests.Leading_and_trailing_blank_lines_are_trimmed_from_the_region`. Kills 525/534's
   whole class deterministically; removes the project's last two genuine-hang Timeouts. Then rewrite
   NOTE 1/NOTE 7 of `stryker-config.doctooling.json` and re-measure the leg (expect 0 Timeout, score
   unchanged at 67.96 %, `break: 67` stands).
2. **`DocSnippetInjector.Inject` progress guard** (§2.1, production, controller judgement on the hardening
   merit) + the terminates-and-reproduces test. Converts 204's class (and any future advance-loss edit)
   into a microsecond loud failure; ends the OOM-vs-clock race that flipped its classification twice in one
   day. Wrap the guard in `// Stryker disable all : … // Stryker restore all`.
3. **Correct the false sentence** in `stryker-config.doctooling.json` NOTE 1 ("Killed outright before the
   loop is reached" → the kill is memory exhaustion inside the runaway loop racing the clock).
4. **CI:** add `--blame-hang --blame-hang-timeout` to the plain test step (not the Stryker legs).
5. **Load-noise list (§3): explicitly no action** — the per-leg `additional-timeout` decisions are already
   correct and measured; the seven runtime Timeout→Survived mutants are ordinary survivors catalogued as
   holes #1–#4 in E3-E1; the two generator statics are Survived-equivalent judgements in the T3 ledger.
6. Fallback for any of 1–2 the controller declines: `// Stryker disable once Statement : <reason>` at the
   exact line, with the score arithmetic pre-computed in §2 (67.73 %–67.84 %, `break: 67` safe in all
   combinations).

## H7 phase 2 — implementation and re-measure (2026-08-21)

Controller-approved implementation of §5 items 1–3, on branch `feat/round21-depth-tier`.

### What landed

- **`DocSnippetInjector.Inject` progress guard** (§2.1 sketch, verbatim shape): `previous` declared before
  the loop; `if (i <= previous) throw new InvalidOperationException(...)` + `previous = i;` at the top of
  the body, wrapped in `// Stryker disable all` / `// Stryker restore all`. The comment states the
  invariant (two advance sites; every non-throwing branch must advance `i`), not just the mutation motive.
  New test `DocSnippetInjectorTests.A_document_of_prose_and_markers_terminates_and_reproduces_every_line`
  (house `Regions(...)` helper; `Assert.Single(collection, predicate)` — xUnit2013-safe).
- **`SnippetScanner.Dedent` restructure** (§2.2 sketch): the two mutable `RemoveAt` trim loops replaced by
  `FindIndex` / `FindLastIndex` / `GetRange`; behaviour-identical including the empty-region throw
  (`first < 0` ⇔ old `Count == 0`), message byte-identical; the variable stays `kept` so the marker check
  and dedent below are untouched. New direct pin
  `SnippetScannerTests.Leading_and_trailing_blank_lines_are_trimmed_from_the_region` (previously the
  trim's only coverer was one CsCheck property).
- **`stryker-config.doctooling.json`**: NOTE 1 rewritten to the verified mechanism — the pre-guard 204 kill
  was OOM inside the runaway loop racing the clock, NOT "Killed outright before the loop is reached" (that
  claim was falsified in §1); NOTE 7's Timeout-dependence warning replaced (no detection rides on a Timeout
  any more); NOTE 5's population numbers refreshed; header re-measure line updated.

### Sabotage demos (house rule — each mutation applied by hand, test observed failing FAST, then reverted)

- Delete `i++;` (mutant 204's exact shape) → `A_document_of_prose_and_markers_terminates_and_reproduces_every_line`
  FAILS in **25 ms** with `InvalidOperationException: DocSnippetInjector stopped advancing at d.md:1 —
  injector bug, not a document error.` No hang, no OOM.
- `var first = 0;` (leading trim lost — 525's class in the new structure) →
  `Leading_and_trailing_blank_lines_are_trimmed_from_the_region` FAILS in **31 ms** (wrong-slice assertion).
- `var last = body.Count - 1;` (trailing trim lost — 534's class) → same test FAILS in **31 ms**.
- All three reverted; `git diff` verified clean of sabotage before the re-measure.

### Re-measure (DocTooling leg, quiet machine, `dotnet stryker --config-file stryker-config.doctooling.json`)

- Wall-clock **5 min 04 s** (was 5:27); 4,789 tests discovered; 567 mutants created.
- **Killed 193 / Survived 54 / Timeout 0 / NoCoverage 37** → 284 scoreable, **score 67.96 %** — identical
  score and scoreable count to the 2026-08-19 run: the two stable Timeouts became ordinary Kills and the
  former racing mutant stayed Killed, now deterministically. 67 CompileError; the guard's 7 mutants report
  `Ignored: progress guard, not logic…` (the disable-all reason string), and the `previous = -1` literal
  outside the disable block spawned a `-1 → +1` mutant that is **Killed** (guard throws on iteration 1) —
  no guard-related survivor.
- **Floor: 67.96 % → `break: 67`, `low: 67` — unchanged, re-affirmed by measurement in this commit.**
- Post-run `git status`: only the intended edits (2 src, 2 test files, this ledger) — **RepoWriteGuard held**,
  no tracked document touched.

### Final Timeout state across legs

DocTooling **0** (this run); generator **0** (2026-08-19.12-10-18, authoritative, zero flips); runtime — the
single historical Timeout was clock noise on a mutant that cannot hang (§0), already resolved by
`additional-timeout: 120000`. **Exit criterion met: no leg's score rides on a wall-clock classification.**
