<!-- SPDX-License-Identifier: GPL-2.0-only -->

# RESEARCH — test velocity without losing coverage

Round 29 · researched 2026-09-05 · **research and measurement design only.** Nothing in the repository was
modified, no build or test run was started, no gate was executed. Every number labelled MEASURED below was
either read out of an artefact already committed/produced in this working tree (Stryker reports, config
comments) or quoted from a primary source; every number labelled PLAUSIBLE is an estimate with its
reasoning shown, and is not evidence.

The owner's constraint is the frame for everything here: **keep full-scale coverage, increase speed.**
Nothing is dropped, sampled, or filtered away. The target is the work that proves nothing — and the first
finding of this report is that the brief's own estimate of where that work is was off by more than an order
of magnitude, in a way that changes which intervention is worth doing first.

---

## 0. Executive correction to the brief's premise

The brief states, from a prior in-repo analysis, that the generator mutation leg's waste is
"91 tests do all the killing while 5,592 run: a ~61× waste factor, estimating 44 min → ~1 min if scoped",
and that `stryker-config.json` "specifies no `coverage-analysis`, so test discovery is wide."

Three of those four claims do not survive contact with the artefacts.

**(a) `coverage-analysis` is already `perTest`.** Its documented default *is* `perTest`
([Stryker.NET configuration][cfg]). The report proves it empirically: in
`StrykerOutput/2026-09-02.07-23-06/reports/mutation-report.json` every one of the 254 non-static scoreable
mutants carries a populated `coveredBy` array (median 2 tests, max 147). Coverage analysis is running.
`stryker-config.runtime.json`'s explicit `"coverage-analysis": "perTest"` restates the default; adding the
same line to `stryker-config.json` would change nothing.

**(b) The cost is the static mutants, and `perTest` cannot touch them.** MEASURED from the same report:
**137 of 391 scoreable mutants (35.0 %) are `"static": true`, and all 137 have an EMPTY `coveredBy`
array.** Stryker's documented behaviour for these is to run them against the whole suite:

> "When using `perTest` mode, mutants that are executed as part of some static constructor/initializer are
> run against all tests as Stryker cannot reliably capture coverage for those." — [Stryker.NET
> configuration][cfg]

**(c) Scoping to test projects saves ~19 %, not 61×.** The waste-factor arithmetic assumed the killing tests
are a tiny minority of a large foreign suite. MEASURED from the report's own `testFiles` roster
(6,603 tests, 485 files):

| project prefix | tests | share |
|---|---:|---:|
| `DwarfMapper.Generator.Tests` | 5,346 | 81.0 % |
| `DwarfMapper.IntegrationTests` | 856 | 13.0 % |
| `DwarfMapper.NegativeCases` | 120 | 1.8 % |
| `DwarfMapper.Testing.Tests` | 80 | 1.2 % |
| `DwarfMapper.DifferentialTests` | 70 | 1.1 % |
| `DwarfMapper.CompilerTests` | 52 | 0.8 % |
| consumer / corpus / registry hosts | 79 | 1.2 % |

The generator's own test project **is** the suite. Excluding every other project — the maximal
`test-projects` intervention — removes at most 1,257 of 6,603 tests, i.e. **19 %**. The 61× number is only
reachable by naming individual killing tests (`test-case-filter`), which is precisely the exclusive-test-list
the maintainer already ruled out in `stryker-config.json`'s NOTE 2, and §2.5 below shows measured evidence
that such a list would drift.

The one claim that survives is that the leg is slow and that most of its work proves nothing. It does. The
lever is different from the one proposed.

---

## 1. The cost model — where the time actually goes

### 1.1 Why a nanosecond mapper needs minute-scale tests

DwarfMapper's *product* is a compile-time artefact. The unit under test is not a function that maps a DTO
in 40 ns; it is **the C# compiler's behaviour on the text the generator emits**. Asserting that requires,
per test case, some subset of:

1. parse a fixture program (`CSharpSyntaxTree.ParseText`) — ~0.1–1 ms;
2. construct a `CSharpCompilation` over ~50 metadata references;
3. run one or two `IIncrementalGenerator`s through `CSharpGeneratorDriver` — this binds symbols, so it pulls
   real metadata decoding;
4. for a large minority of tests, **bind the resulting compilation again** via `GetDiagnostics()` to prove
   the emitted code is error-free *and warning-free under `nullable enable`* (the repo builds
   warnings-as-errors, and a warning inside a `.g.cs` is unsuppressible by the consumer — see the
   `generated-code-warnings-unsuppressible` finding);
5. for the runtime/fuzz layer, `Emit()` to a `MemoryStream` and `Assembly.Load` the bytes.

Steps 4 and 5 are compiler back-end work. There is no way to assert "the generated code compiles clean"
that is cheaper than compiling it. That is the irreducible floor, and it is why 7,386 generator tests cost
~75 s rather than ~2 s. The suite is not slow because it is badly written; it is slow because each test is
a small compilation.

### 1.2 The measured budget

MEASURED, quoted from `scripts/housekeeping.ps1`'s own comments (2026-08-21, Release, `--no-build`):

- fast tier, whole solution: **74.5 s**
- same run with `--collect:"XPlat Code Coverage"`: **92.9 s** (+18.4 s, +24.7 %)

MEASURED, quoted from `tests/DwarfMapper.Generator.Tests/GeneratorTestHarness.cs`: offering the whole
trusted-platform-assembly set instead of the five named framework assemblies takes the generator test
project from **62 s to 83 s (+34 %)**, "because every one of the thousands of compilations then binds
against ~200 references instead of ~50."

That last figure is the most important calibration in this document. It says the suite's wall-clock is
**roughly linear in per-compilation reference-binding work**. A 4× increase in reference count cost +34 %;
therefore reference binding is on the order of a third of the suite's time, and the rest is parsing,
generator execution, and re-binding of generated output.

Harness call-site census (MEASURED, `grep` over `tests/`):

| harness entry point | call sites | what it costs |
|---|---:|---|
| `GeneratorTestHarness.Run(` | 659 | parse + create compilation + run generator, **text assertions only** |
| `RunAndGetCompilationErrors(` | 60 | the above **+ full `GetDiagnostics()` of the output compilation** |
| `EmitAssembly(` | 46 | the above **+ `Emit()` + `Assembly.Load(byte[])`** |
| `GeneratedCodeWarnings(` | 18 | full `GetDiagnostics()`, nullable-enable, filtered to `.g.cs` |
| `RunAll(` | 15 | two generators, all outputs concatenated |

Call sites are not test counts — the 60 `RunAndGetCompilationErrors` sites include `[Theory]` bodies and
fuzz loops, so their share of the 7,386 executed tests is much larger than 60/798. Establishing that split
is measurement task M1 in §7.

### 1.3 The mutation legs: a one-term cost model

Stryker runs the full test suite once per **static** mutant, and only the covering tests per non-static
mutant. MEASURED from the 2026-09-02 generator report:

- static term: 137 mutants × 6,603 tests = **904,611 test executions**
- non-static term: sum of `|coveredBy|` over 254 mutants = **2,675 test executions**
- **static mutants are 99.7 % of the leg's test executions.**

So, to a first approximation:

> **leg wall-clock ≈ (static mutant count × suite wall-clock) ÷ concurrency + baseline run**

Sanity check against the repo's own measurements: 137 × 74.5 s ÷ 6 (Stryker's default concurrency is
"half your logical processor count" [cfg]; the machine is 12 logical cores) = **28 min**. The brief reports
51 min for this leg and `stryker-config.json` records 21 min for an earlier, smaller mutant set. The model
lands in the right order of magnitude; the residual is per-mutant test-host startup, the instrumented
(non-`--no-build`) rebuild per mutant, and CPU oversubscription (§3.3).

The model makes a falsifiable prediction. MEASURED static counts per leg, from the reports on disk:

| leg / config | scoreable | static | static share | model says |
|---|---:|---:|---:|---|
| pipeline (`MapperExtractor.Members.Phases.cs`) | 239 | **157** | 65.7 % | slowest per mutant |
| generator (`stryker-config.json`) | 391 | 137 | 35.0 % | second |
| runtime (`stryker-config.runtime.json`) | 125 | 24 | 19.2 % | ~5× cheaper than generator |
| codefixes | 177 | 3 | 1.7 % | nearly free |
| doctooling | 289 | **0** | 0 % | should be the fastest leg by far, despite having *more* mutants than the runtime leg |

If leg wall-clock does not track the static column far better than it tracks the scoreable column, this
model is wrong and everything ranked on it needs re-ranking. That is measurement task M4.

### 1.4 What is *not* in the 74.5 s

`housekeeping.ps1` stage 1 runs `dotnet test DwarfMapper.NET.sln -c Release` **without `--no-build`**, while
the 74.5 s figure was taken with `--no-build`. So every gate invocation additionally pays a full
Release build of the solution under `TreatWarningsAsErrors`, `AnalysisMode=All`, `AnalysisLevel=latest-all`,
`EnforceCodeStyleInBuild=true`, plus Meziantou.Analyzer — and that cost has never been measured or written
down anywhere in the repo. It is also paid a second time (as an up-to-date check + re-evaluation) by the
exhaustion stage and the self-heal stage, which re-invoke `dotnet test` on the generator project. Stage 1
also carries `--blame-hang --blame-hang-timeout 5m`, which routes the run through the blame data collector.

Unmeasured cost is not the same as large cost. But for a repo whose fast tier has a documented "~10 %
growth cap", having an unmeasured multi-second-to-multi-minute term inside the gate is a gap in the
instrument, and it is the cheapest thing on this list to close (M0).

---

## 2. Stryker.NET — options, what each costs, and what each does to the score's meaning

All option names, defaults and quoted wording are from the [Stryker.NET configuration page][cfg] unless
noted.

### 2.1 `coverage-analysis`

| value | behaviour (quoted) | cost | effect on score meaning |
|---|---|---|---|
| `off` | "coverage data is not captured. All unit tests are run against all mutants." | worst | none — the score is identical, just slower |
| `all` | "capture the list of mutants covered by a test. Test only the mutants covered by unit tests." | medium | none |
| `perTest` (**default**) | "capture the list of mutants covered by each test. For every mutant that has tests, only the tests that cover the mutant are used to test a mutant." | low | none *in principle*; see §2.5 |
| `perTestInIsolation` | "like 'perTest', but running each test in an isolated run." | **capture phase runs each test in its own session — "very slow"** ([#949][i949]) | none; strictly more accurate |

Two things follow for this repo.

First, **`perTest` is already in force** (§0a), so there is no free win here.

Second, `perTestInIsolation` is the *only* documented mechanism that can attribute coverage for mutants
`perTest` cannot — the maintainers recommend it exactly for the "lazy static cache initialisation" shape in
[issue #949][i949] — but its capture phase runs each of 6,603 tests in "a specific test session". At even
1 s of process startup per test that is ~110 minutes of capture before a single mutant runs. **PLAUSIBLE
conclusion: `perTestInIsolation` is a net loss here and should not be tried, unless a measured capture-phase
cost proves otherwise.** It is the one option whose *correctness* is strictly better than the status quo, so
if the owner ever wants a more honest score rather than a faster one, this is the lever — at a large cost.

### 2.2 What makes a mutant "static" here — mechanism verified from the shipped binary

Stryker's definition ([mutation-testing-elements][mte]): "A static mutant is a mutant that is executed once
on startup instead of when the tests are running." The Stryker.NET implementation ([PR #636][pr636]) "uses
detection during mutation phase as well as marker injection that are used during coverage capture phase" —
**part syntactic, part runtime-observed**. Both halves can now be quoted.

**Syntactic half** ([MutationContext.cs][mc]): the orchestrator's `InStaticValue` is documented as
"True when inside a static initializer, fields or accessor", entered via
`public MutationContext EnterStatic() => new(this) { InStaticValue = true };`. Note what is *absent*:
plain static **methods** are not listed. That matches the report — most mutants in these `internal static
class`es are not flagged.

**Runtime half** — PRIMARY SOURCE, extracted verbatim from the injected-helper resources inside the
installed `dotnet-stryker` 4.16.0 (`~/.dotnet/tools/.store/dotnet-stryker/4.16.0/…/Stryker.Core.dll`), the
exact build `housekeeping.ps1` pins:

```csharp
internal sealed class MutantContext : IDisposable
{
    [ThreadStatic] private static int depth;
    public MutantContext()  { depth++; }
    public void Dispose()   { depth--; }
    public static bool InStatic() => depth > 0;
    public static T TrackValue<T>(Func<T> builder)
    { using (MutantContext context = new MutantContext()) { return builder(); } }
}

// MutantControl:
private static void RegisterCoverage(int id)
{
    lock (_coverageLock)
    {
        if (!_coveredMutants.Contains(id))       { _coveredMutants.Add(id); }
        if (MutantContext.InStatic() && !_coveredStaticMutants.Contains(id))
                                                 { _coveredStaticMutants.Add(id); }
    }
}
```

Three consequences, all decision-relevant:

1. **`depth` is `[ThreadStatic]`.** An attractive hypothesis — that xunit's 12-way parallelism causes
   cross-thread mis-attribution and that serialising the capture run would collapse the static set — is
   **DISPROVED by the source.** Do not spend time on it. (This report originally carried that hypothesis;
   it is recorded as refuted so it is not re-invented.)
2. **The flag is sticky and per-run, not per-execution.** A mutant executed ten thousand times outside any
   static scope, and *once* inside one, is in `_coveredStaticMutants` forever for that run — and therefore
   costs a full 6,603-test suite run for the rest of the leg.
3. **Therefore the 137 static mutants are exactly those that were executed at least once, on some thread,
   inside an instrumented static initializer / static field initializer / static accessor scope in
   `DwarfMapper.Generator`.**

**What I could not identify: which initializer.** A `grep` of `src/DwarfMapper.Generator` finds no
expression-bodied static property and no `static readonly` field initializer that calls into
`BlittableProof` / `ConstructorSelector` (the callers are all ordinary static methods in
`MapperExtractor.*`, `LayoutHygiene`). So the scope is entered somewhere I have not traced. **HYPOTHESIS,
not verdict.** If it can be found and broken, 137 mutants become per-test-covered and the leg's dominant
cost term (99.7 % of its test executions) largely disappears — with the *same* files mutated, the same
mutators, the same denominator. That makes it the highest-payoff item in this report and also the one with
the least evidence behind it, which is why it is item #2 in §7 with an explicit diagnostic (M7) rather than
a recommendation to act.

What I *can* state as MEASURED is the property that matters for the gate. Matching mutants across the two
generator-leg reports on disk (`2026-08-26.23-46-17` and `2026-09-02.07-23-06`) by
`(file, mutator, source-line text)` gives 136 unambiguously matched mutants:

- **static-flag flips between runs: 0** (52 static in both). The static set is *deterministic*.
- **`coveredBy` count changed for 15 of the 84 stable non-static mutants (18 %).** The per-test coverage
  sets are *not* deterministic.
- mutant status changed for 0.

That pair is the single most decision-relevant measurement in this report. It means (i) the static cost term
is a stable property of the code, not run-to-run noise, so it can be budgeted and predicted; and (ii) any
scheme that freezes "the tests that matter" into a committed list is freezing something that demonstrably
moves — which is exactly the drift `stryker-config.json`'s NOTE 2 says the runtime leg's comment measured.
The maintainer's ruling is now backed by a second, independent measurement.

### 2.3 `test-projects`

`"test-projects": ['../X.Tests/X.Tests.csproj']`, default `null` (Stryker discovers test projects itself).
Restricts which test projects are built and run.

- Saving: **≤ 19 %** of the leg (§0c). MEASURED, from the test roster.
- MEASURED, killers: the 331 killing test ids in the 2026-09-02 report resolve to **228 distinct test
  names**, from exactly three projects — `DwarfMapper.Generator.Tests` (221), `DwarfMapper.NegativeCases`
  (5), `DwarfMapper.CompilerTests` (2).
- MEASURED, coverage: the union of every `coveredBy` entry across all scoreable mutants is **426 tests**,
  from the *same* three projects — Generator.Tests 402, CompilerTests 18, NegativeCases 6. **Not one test in
  `IntegrationTests`, `ConsumerTests`, `CorpusTests`, `DifferentialTests` or `Testing.Tests` registered
  coverage of a single generator mutant.** That is stronger than "killed nothing": they *executed* nothing.

This splits the intervention into two cases that must not be conflated.

**(a) Projects that cannot kill a generator mutant by construction — no proof is lost.** MEASURED, from
their `.csproj` files, `DwarfMapper.IntegrationTests`, `DwarfMapper.CorpusTests` and
`DwarfMapper.DifferentialTests` all reference the generator as
`<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false"/>`. The generator therefore
runs **inside the compiler during those projects' build**, and its assembly is never loaded by the test
host. Stryker activates a mutant by setting an environment variable read by `MutantControl.IsActive` **in
the test-host process** (§2.2's extracted source); the build that generated those projects' code happened
before any mutant was active. **PLAUSIBLE — following from the extracted activation mechanism plus the
reference shape, but not directly measured — those projects are structurally incapable of killing a
generator mutant, which is why their coverage contribution is exactly zero rather than merely small.** That
accounts for 856 + 20 + 70 = **946 of the 6,603 tests (14.3 %)**.

**(b) Projects that merely have no killer today.** `ConsumerTests.Host` and `DwarfMapper.Testing.Tests`
(103 tests, 1.6 %) are the residual, and there the maintainer's objection applies in full: "killed nothing
today" is not "cannot kill", and the assumption decays silently as tests are added.

- **Recommendation:** if the owner ever revisits the ruling, the defensible version is (a) only — exclude
  the analyzer-referencing projects, on the structural argument, and verify it by checking the mutation
  score is bit-identical with and without. That is ~14 % for no loss of proof. Case (b) is ~1.6 % and is not
  worth the argument.
- **Still the owner's decision** — the structural argument is PLAUSIBLE, not MEASURED, and if it is wrong
  the gate silently loses an oracle. The standing ruling is "no", and the price of keeping it is ~19 % of
  one nightly leg, which is cheap.

### 2.4 `test-case-filter`

`"test-case-filter": "(FullyQualifiedName~UnitTest1&TestCategory=CategoryA)|Priority=1"` — `dotnet test
--filter` syntax. This is the mechanism that would deliver the brief's 61×, by naming the ~228 killers.

**Do not.** §2.2 measured that per-test coverage sets drift 18 % run-to-run; a committed killer list is a
snapshot of a moving set, and every mutant it stops running comes back `Survived` or, worse, `Killed` by a
test that no longer covers it. It also inverts the purpose of the leg: mutation score exists to grade the
*suite*, and grading the suite against a list derived from the suite's current behaviour is circular.
**Weakens the score's meaning severely. Owner's decision; recommendation is no.**

### 2.5 `since` / `--since:committish`, and `with-baseline`

- `since`: "Use git information to test only code changes since the given target. Stryker will only report
  on mutants within the changed code." Sub-options `since.target` (default `master`) and
  `since.ignore-changes-in` (glob). Mutants outside the diff are **marked ignored and effectively removed
  from the report** — so the reported score is over a different, smaller denominator.
- `with-baseline`: saves the report to a storage location (`baseline.provider`: Disk, Dashboard,
  AzureFileStorage, S3) and loads it at the start of the next run, **reusing unchanged mutant results and
  producing a full report**. The Stryker project describes this "incremental" idea as: you don't re-run all
  mutants but "you do end up with a full report" ([incremental mode announcement][inc], StrykerJS wording;
  the .NET option is `with-baseline`).

The distinction matters enormously to this owner:

- **`since` changes what the score is over.** A gate with `thresholds.break` compared against a since-scoped
  score compares a number to a threshold measured on a different population. **Weakens the gate. Do not put
  it in `housekeeping.ps1 -Mutation` or CI.** It is however excellent for the *developer loop* —
  `dotnet-stryker -f stryker-config.json --since:master` while iterating on `BlittableProof.cs` — where
  nothing is being gated.
- **`with-baseline` preserves the denominator** and is therefore the one diff-based option that is a
  candidate for the gate. Its correctness rests on an assumption Stryker cannot fully check: that a reused
  "Killed" verdict is still true when the *tests* changed but the mutated file did not. For this repo that
  assumption is violated routinely — the `fixes-lock-with-regression-tests` rule means test files change on
  almost every commit. **PLAUSIBLE: `with-baseline` would reuse verdicts that a changed suite could no
  longer justify. It weakens the gate in a way that is hard to see.** Owner's decision; recommendation is
  to use it only for local iteration, never for the nightly number.

### 2.6 `concurrency`

Default: "Half your logical processor count." On the 12-core machine the configs were measured on, that is
6. Raising it to 12 is a pure scheduling change — **no effect on the score** — but interacts badly with
xunit's own parallelism (§3.3), and with per-mutant timeouts: a more loaded machine makes every mutant's run
slower, and Stryker's timeout is computed as
`(initialTestRunTime + coveringTestsTime) * timeout-ratio + additional-timeout`. `stryker-config.json`
already carries `"additional-timeout": 120000` for exactly this reason, and its NOTE 1 records that two
mutants once came back as false "Timeout" detections that inflated the score to 72.64 % — a timeout is
scored as a *detection*, so timeout pressure makes the score look better while proving less.
**Meaning-preserving in principle, but raising concurrency raises the chance of spurious Timeout
detections, which does corrupt the score.** Treat any concurrency change as requiring a report-level check
that `Timeout` count stayed 0.

### 2.7 `mutation-level`

`Basic | Standard (default) | Advanced | Complete`. Selects which mutators run, so it **changes the mutant
population and therefore the denominator**. Lowering it to `Basic` would cut the leg proportionally and make
every recorded `break` threshold meaningless (they were measured at `Standard`). **Weakens the gate.
Owner's decision; recommendation is no.** Raising it to `Advanced`/`Complete` is the opposite trade — more
proof, more time — and is worth knowing exists.

### 2.8 `ignore-methods`, `ignore-mutations`

- `ignore-methods`: `['ToString', 'ConfigureAwait', '*Exception.ctor']`, wildcards and `.ctor` notation,
  qualifiable by class name.
- `ignore-mutations`: `['string', 'logical']`, also LINQ-specific (`linq.First`, `linq.Sum`).

Both shrink the denominator. There is, however, a *principled* use: mutants that are provably unkillable are
noise, and removing noise makes the score mean more, not less. This repo already reasons this way — NOTE 1
of `stryker-config.json` argues two BlittableProof mutants were "provably incapable of hanging". A
defensible, narrow application would be `ignore-methods` on pure diagnostic-message formatting helpers
(`SizeWord`, `InlineArrayWord`, `FixedBufferWord` in `BlittableProof.cs`) *if and only if* their wording is
already pinned exactly by `DwarfMapper.NegativeCases` — in which case the string mutants there are killed
anyway and ignoring them saves nothing. MEASURED: 23 of 214 BlittableProof scoreable mutants are "String
mutation". **Small saving, changes the denominator, requires a written justification per entry. Owner's
decision.**

### 2.9 Per-mutant timeout interaction

`additional-timeout` (default 3000 ms; this repo uses 120000 ms on the generator/pipeline/runtime legs)
enters the formula `timeout = (initialTestRunTime + coveringTestsTime) * timeout-ratio + additional-timeout`.
Note the consequence for §2.3/§2.6: **anything that makes the initial test run faster also tightens every
per-mutant timeout proportionally**, because `initialTestRunTime` is a term in it. A suite speed-up is
therefore not risk-free for the mutation legs — it can convert a slow-but-passing mutant into a `Timeout`,
which Stryker scores as a *detection*. Any suite speed-up must be followed by a report check that the
`Timeout` count is still 0 (it is 0 today: MEASURED, no `Timeout` status appears in the 2026-09-02 report).

---

## 3. Roslyn generator test performance

### 3.1 What this repo already does — do not "fix" these

- **Metadata references built once and shared** (`Lazy<MetadataReference[]>` in `GeneratorTestHarness`),
  with the rationale written down: `MetadataReference.CreateFromFile` reads metadata under a lock, so
  rebuilding per test "serialised parallel compilations on metadata I/O and dominated wall-clock". This is
  the single biggest known win in generator-test harnesses and it is already banked.
- **The reference set is deliberately narrow** (~50 refs), with the 62 s → 83 s measurement recorded for the
  alternative. Any proposal to switch to `Basic.Reference.Assemblies.Net100.References.All` must reckon with
  that measurement: `.All` is a *larger* set (~160 assemblies), so it would likely reproduce the +34 %.
  [Basic.Reference.Assemblies][bra] is nonetheless worth knowing about — it ships reference assemblies as
  embedded resources for `net10.0`/`netstandard2.0`/`net472` and removes the environment dependence the
  harness comment worries about ("whether a fixture compiles depended on which tests ran first"). It solves
  a *correctness* problem this harness solved another way, and would cost speed. **Not recommended; recorded
  so it is not re-discovered as a new idea.**

### 3.2 The remaining Roslyn lever: share a baseline compilation

Every harness entry point calls `CSharpCompilation.Create(assemblyName, trees, References.Value, options)`.
Each such call constructs a fresh `ReferenceManager`, whose job is (quoting the Roslyn source's own class
comment) to "create an underlying SourceAssemblySymbol … and AssemblySymbols for referenced assemblies …
all properly linked together based on reference resolution between them" ([ReferenceManager.cs][rm]).

Roslyn shares a `ReferenceManager` between compilations "that are expected to have the same result of
reference binding" — which is what `compilation.AddSyntaxTrees(tree)` / `WithOptions(...)` produce from a
common parent. So:

```csharp
// once, static:
private static readonly CSharpCompilation Baseline =
    CSharpCompilation.Create("DwarfMapperTestAsm", Array.Empty<SyntaxTree>(), References.Value, options);
// per test:
var compilation = Baseline.AddSyntaxTrees(tree);          // reference binding reused
```

- **Mechanism:** amortises reference *symbol binding* (not just metadata decoding) across tests.
- **Expected saving: PLAUSIBLE, unknown magnitude, bounded above by the reference-binding share the 62→83 s
  measurement implies (~⅓ of suite time), and plausibly well below it** — because `ReferenceManager` also
  keeps a "global cache for metadata readers and AssemblySymbols associated with them" using
  `WeakReference`s ([ReferenceManager.cs][rm]), so some of this is *already* amortised across compilations
  even without sharing. **This is the claim in the report most likely to be smaller than it looks.** It must
  be measured, not assumed.
- **Risks to correctness, all real:**
  - The harness varies `assemblyName` per entry point (`DwarfMapperTestAsm`, `DwarfMapperMapToTestAsm`,
    `DwarfMapperCompileTestAsm`, `DwarfMapperWarnTestAsm`, `FuzzAsm_<guid>`) and varies
    `NullableContextOptions` and `allowUnsafe`. Each distinct `(assemblyName, options)` needs its own
    baseline, or `WithAssemblyName`/`WithOptions` — and changing the assembly name is exactly the kind of
    thing that can invalidate reference-manager reuse. One baseline per distinct configuration, cached in a
    `ConcurrentDictionary`, keeps this honest.
  - `EmitAssembly` deliberately uses a unique assembly name per fuzz seed "to avoid load collisions"; that
    path must keep doing so.
  - The multi-tree overload documents that **tree order is load-bearing** ("the order it lays out a partial
    struct's fields in"). `AddSyntaxTrees` on an empty baseline preserves the caller's order; this must be
    asserted, not assumed.
  - Sharing a parent compilation across parallel tests requires the parent to be immutable — it is;
    `Compilation` is immutable and `AddSyntaxTrees` returns a new instance — but the *reference manager* is
    then shared mutable state internally. It is designed for that (the compiler does it constantly), but it
    is the thing to watch if the suite starts producing order-dependent failures.
  - **UNVERIFIED and load-bearing: the exact condition under which `AddSyntaxTrees` reuses the manager.**
    A search result surfaced a `reuseReferenceManager: false` argument in the neighbourhood of
    `CSharpCompilation`'s `Update`/`AddSyntaxTrees` path, which — if it applies unconditionally to
    `AddSyntaxTrees` — would make this entire intervention worthless. My reading is that reuse holds for
    added trees unless a tree carries `#r`/`#load` directives (which change the reference set), but **I did
    not confirm the condition in the source, and M2 must not be started before someone reads
    `CSharpCompilation.AddSyntaxTrees` / `CanReuseReferenceManager` and settles it.** If reuse does not
    hold, this drops out of the ranking entirely.
- **How to verify here:** M2 in §7 — a differential run asserting byte-identical diagnostics and generated
  text for the whole suite under both constructions, plus a wall-clock comparison.

### 3.3 Narrowing the re-binding: `GetSemanticModel(tree).GetDiagnostics()`

`RunAndGetCompilationErrors` and `GeneratedCodeWarnings` call `outputCompilation.GetDiagnostics()`, which
binds **every method body in every tree**, including the fixture the test wrote. Both then discard
everything that is not in a `.g.cs` file (`GeneratedCodeWarnings` filters on `IsInGeneratedCode`;
`RunAndGetCompilationErrors` does not, and must keep its behaviour).

For `GeneratedCodeWarnings` specifically, the narrower
`outputCompilation.GetSemanticModel(generatedTree).GetDiagnostics()` binds only the generated tree.

- **Expected saving: PLAUSIBLE, potentially large on the 18 `GeneratedCodeWarnings` call sites** (fixtures
  are typically larger than the emitted mapper only in the trivial cases; in the combinatorial matrices the
  fixture is large).
- **Risk: real and subtle.** Some diagnostics are compilation-level or are reported at a location in one
  tree because of a declaration in another. The filter already drops those, so the *filtered* result should
  be identical — "should" is the operative word.
- **How to verify:** this is not verifiable by reasoning, only by a one-shot differential (M3): run both
  formulations over the entire corpus and assert the filtered diagnostic sets are equal, mutant-style. If
  they differ anywhere, abandon it. Do **not** ship it on the strength of a sampled agreement.

### 3.4 `Assembly.Load(byte[])` in `EmitAssembly` — a leak, not a cost

`EmitAssembly` loads each emitted fuzz assembly into the **default** `AssemblyLoadContext`, where it can
never be unloaded, once per fuzz seed, with a fresh GUID name. 46 call sites, and the exhaustion tier
(`DWARF_FUZZ_FULL=1`) multiplies the population. The standard fix is a collectible
`AssemblyLoadContext(isCollectible: true)` per emission, disposed after the assertions.

- **Expected saving: PLAUSIBLE and probably small in wall-clock, real in memory** — it removes steady growth
  in loaded-assembly count, metadata, and JIT'd code across a run, which is the kind of thing that makes the
  *end* of a long suite slower than the start and makes the deep/exhaustion tiers memory-hungry.
- **Risk:** the emitted assemblies self-register into the ambient cross-assembly registry via module
  initialisers (`ambient-cross-assembly-registry`). A registry entry holding a delegate into a collectible
  ALC **pins it**, so unloading may not actually happen, and worse, the registry could accumulate entries
  pointing at torn-down contexts. This needs checking before it is attempted.
- **How to verify:** M5 — instrument a deep-tier run to report `AppDomain.CurrentDomain.GetAssemblies().Length`
  and process working set at start and end. If the count grows by the fuzz population, the leak is real and
  worth fixing; if the registry pins the contexts, the fix is inert and should not be attempted.

### 3.5 `GeneratorDriver` reuse across cases — not a thing

`CSharpGeneratorDriver.Create(...)` is cheap; the expensive part is `RunGeneratorsAndUpdateCompilation`.
A driver *can* be reused, and reuse is meaningful only for **incremental re-runs against a related
compilation** — that is the entire point of `IIncrementalGenerator` step caching, and the reason
`GeneratorDriverOptions(trackIncrementalGeneratorSteps: true)` exists and is what cacheability tests assert
on (Andrew Lock's part 10, [testing pipeline cacheability][al10]; Meziantou's [testing incremental
generators][mez]). For unrelated fixtures there is nothing to reuse, and a shared driver would carry state
between tests — a correctness hazard for no gain. **Not recommended.** The repo already has
`IncrementalCachingTests` and a `CacheBatteryTests.Battery_passes_for_every_registered_generator` (it shows
up in the killer list), which is the right use of that machinery.

### 3.6 How other repos do it

- **Mapperly (riok)** — `TestSourceBuilder` builds fixture source, `TestHelper` runs the generator and
  asserts/snapshots; separate `Riok.Mapperly.Tests` (compile-time) and `Riok.Mapperly.IntegrationTests`
  (runtime), with VerifyTests/Verify + Verify.SourceGenerators for snapshots ([Mapperly tests doc][map]).
  Structurally the same split this repo has. I found **no** published measurement of their suite's cost, and
  no evidence of a shared-baseline-compilation trick.
- **`Microsoft.CodeAnalysis.Testing` / `CSharpSourceGeneratorTest<>` verifiers** — the standard analyzer-test
  harness. Its `ReferenceAssemblies` type resolves and **caches** a package-based reference set (downloaded
  once, then reused process-wide), which is the same idea as this repo's `Lazy<>` cache, plus determinism.
  Adopting it wholesale would mean rewriting 7,386 tests; the idea worth stealing is only the one already
  stolen.
- **System.Text.Json's generator tests** — I did not find a primary source describing performance techniques
  specific to them. **Claim not verified; do not cite it as precedent.**
- **Andrew Lock / Stephen Toub** — Lock's series is the standard reference for snapshot-testing a generator
  and for cacheability tests; I found **no** post by either author measuring generator *test-suite*
  wall-clock or recommending compilation reuse. **The "prior art says share the baseline compilation" claim
  is one I could not source; it is my inference from the Roslyn source comment, not a documented practice.**

---

## 4. Test-runner level

### 4.1 `--no-build` / `--no-restore`

Stage 1 runs `dotnet test <sln> -c Release` with no `--no-build`. The obvious change — one `dotnet build`
followed by `dotnet test --no-build --no-restore` — is **meaning-preserving only if the build still happens
and still fails the gate on a warning.** Done carelessly (`--no-build` with no preceding build) it turns a
warnings-as-errors gate into a stale-binary run, which is precisely the "vacuous green" genre
`Assert-MutantsWereTested` exists to prevent.

The saving is not the build itself (it must still run) but the **duplicate up-to-date checks and MSBuild
re-evaluations** in stages 1, 1-heal and 2, each of which currently re-enters MSBuild for the whole
solution. **PLAUSIBLE saving: seconds to tens of seconds per gate run; unmeasured (M0).**

### 4.2 xunit v2 parallelism

MEASURED from the repo: `xunit` 2.9.3 + `xunit.runner.visualstudio` 3.1.4 + `Microsoft.NET.Test.Sdk`
17.14.1 (VSTest), and **no `xunit.runner.json` anywhere in the tree**. So the defaults are in force
([xunit: running tests in parallel][xup]):

- `parallelizeAssembly` (parallel *assemblies*): **off** by default.
- `parallelizeTestCollections`: **on**; a test collection is one per test class by default, so classes run
  in parallel and tests within a class run sequentially.
- `maxParallelThreads`: **the number of CPU threads on the machine** (12 here).
- Collection fixtures (`[Collection]`, `ICollectionFixture<>`) merge classes into one collection and thereby
  **serialise them**. MEASURED: `grep` for collection attributes in `tests/` is worth running as part of M1 —
  a single accidental shared collection can serialise a large slice of a 5,346-test project.

With ~291 test-bearing files in the generator project, class-per-collection gives ample parallelism. There
is no obvious win here in isolation — **but see §4.4.**

### 4.3 MTP vs VSTest

Microsoft.Testing.Platform is the successor runner; it "reduce[s] orchestration overhead by avoiding some of
the older dynamic behaviors that made the VSTest model more complex" ([MS Learn: test platforms
overview][mtp]), and .NET 10's `dotnet test` has first-class MTP support ([.NET blog][mtpblog]).

The catch for this repo is concrete and expensive:

- MTP support in xunit requires **xunit v3** (`<UseMicrosoftTestingPlatformRunner>true</…>`)
  ([xunit MTP doc][xmtp]). This repo is on xunit 2.9.3.
- Migrating means: xunit v2 → v3 across 8 test projects, `Verify.Xunit` → `Verify.XunitV3`, revisiting
  `--blame-hang` (a VSTest concept; MTP has its own hang-dump extension), revisiting
  `--collect:"XPlat Code Coverage"` (coverlet.collector is a VSTest data collector; MTP uses
  `Microsoft.Testing.Extensions.CodeCoverage`), revisiting `JunitXml.TestLogger`, and revisiting **whether
  Stryker.NET drives it correctly** — [stryker-net issue #3629][i3629] documents that with the MTP runner,
  "coverage analysis assigns every covered mutant the full test suite (no per-test coverage), so each mutant
  runs against all tests."
- **That last point is decisive.** Under MTP as of that issue, Stryker loses per-test coverage entirely,
  which would turn all 254 non-static mutants into full-suite mutants and make the mutation legs
  **dramatically slower** — the exact opposite of the goal.
- Also: "You shouldn't mix VSTest-based and MTP-based .NET test projects in the same solution" ([MS
  Learn][mtp]), so this cannot be piloted on one project.

**Recommendation: do not migrate to MTP while the mutation legs matter.** Re-evaluate when #3629 is closed.
The startup-overhead saving is real but is per *test-host process*, i.e. seconds per gate run — and it would
be paid back many times over in the mutation legs, which start one test host per mutant.

### 4.4 Running several test projects concurrently — and the oversubscription finding

`dotnet test <sln>` drives MSBuild over the solution, and MSBuild's `VSTest` target runs per project.
`msbuild.exe` builds projects in parallel only when `-maxcpucount`/`-m` is given ([MSBuild: building
multiple projects in parallel][msb]), and `dotnet test --no-build -maxCpuCount:8` is used in the wild to
control test parallelism across projects ([vstest#4044][vst]).

**UNVERIFIED, and it matters: whether the `dotnet` CLI already passes a default `-maxcpucount`.** The
`-m:1` idiom exists in the wild *because* people want to serialise, which is evidence the default is
parallel; the MSBuild docs describe `msbuild.exe`'s default, not the CLI's. I could not settle this from a
primary source. **Settle it locally in seconds, without running the suite: start `dotnet test
DwarfMapper.NET.sln -c Release` and count concurrent `testhost` processes, or run
`dotnet test <sln> -v:n` and look for interleaved project banners.** If projects already run concurrently,
then item #15 below is moot *and* the artefact-write race described next is already live today — which
would make it a latent flakiness source worth naming, not a hypothetical cost of a change.

Is it safe here? The projects have separate `bin`/`obj`, but they share: `TestResults/` (stage 1 wipes and
writes `TestResults/coverage`), `StrykerOutput/`, `BenchmarkDotNet.Artifacts/`, and — critically — the
repo-writing artefact tests. `tests/DwarfMapper.Generator.Tests/Contracts/RepoWriteGuard.cs` and
`RepoPaths.cs` exist precisely because some tests write into the repo tree (golden files, AnalyzerReleases,
doc tables). Two test hosts writing the same artefact concurrently is a data race that would surface as
flaky golden-file failures. **PLAUSIBLE saving: modest (the generator project is 81 % of the tests, so
project-level parallelism can overlap at most the remaining 19 %); risk: real and specific. Not
recommended.**

The more interesting finding is the opposite direction. **Under Stryker, the machine is
oversubscribed by construction:** Stryker's `concurrency` defaults to half the logical processor count and
each of those test hosts runs xunit with `maxParallelThreads` = the full logical processor count. On the
12-core machine the configs were measured on that is 6 × 12 = **up to 72 runnable threads on 12 cores**,
each doing allocation-heavy Roslyn work against a shared GC. The 74.5 s suite figure was measured with the
machine to itself; under Stryker each mutant's suite run competes with five siblings at 12× the thread
budget. (The *ratio* is machine-specific — on a 4-core CI runner it is 2 × 4 = 8 threads on 4 cores, a
2× oversubscription rather than 6×. The intervention is "pin the product to the core count", not "use these
numbers".)

- **Mechanism:** pin xunit's `maxParallelThreads` (via `xunit.runner.json`, or `[assembly: CollectionBehavior(MaxParallelThreads = n)]`)
  so that `Stryker concurrency × maxParallelThreads ≈ logical cores`, e.g. concurrency 6 × 2 threads, or
  concurrency 3 × 4.
- **Expected saving: PLAUSIBLE, and this is the highest-variance unknown in the report — it could be
  nothing, or it could be tens of per cent of the mutation legs.** Thread oversubscription of allocation-heavy
  work typically costs 20–50 % throughput; but it could also already be self-limiting.
- **Effect on the score: none.** It is pure scheduling.
- **Caveat (§2.9):** the fast tier must keep `maxParallelThreads` at the core count, because the *gate* wants
  the suite fast when it has the machine. So this is a Stryker-only setting — an `xunit.runner.json` read
  from an env var, or a second runner config, not a global change.
- **How to verify:** M4 — run the doctooling leg (0 static mutants, so a short leg) at
  `concurrency × maxParallelThreads` ∈ {6×12 (today), 6×2, 3×4} and compare wall-clock. Cheap, and it
  answers the question for all five legs.

---

## 5. BenchmarkDotNet smoke run

### 5.1 What the gate actually pins

MEASURED, from `housekeeping.ps1`: the smoke leg gates on (a) benchmark **count** matching
`allocation-baseline.json`'s `totalBenchmarks`; (b) **exact** `Memory.BytesAllocatedPerOperation` per pinned
scenario, failing on *any* difference in either direction; (c) SDK version equality with
`baseline.measuredWith.sdk`; (d) a `blit-ratio-baseline.json` check. Timing numbers are explicitly
**non-gates** ("SmokeConfig in benchmarks/.../Program.cs"). The repo's own note records the empirical basis:
"two full smoke runs, all 41 benchmarks byte-identical."

So the question is narrow: **can the toolchain change without allocated bytes changing?**

### 5.2 In-process toolchains

The default toolchain "generates, builds and executes a new console app per every benchmark", giving
process-level isolation. `InProcessEmitToolchain` "does not generate any new executable. It emits IL on the
fly and runs it from within the process itself… useful if you want to run the benchmarks very fast"
([BDN toolchains][bdntc]). `InProcessNoEmitToolchain` is the reflection-based variant.

Allocation measurement uses `GC.GetAllocatedBytesForCurrentThread`, and BDN documents MemoryDiagnoser as
"99.5 % accurate about allocated memory" with default settings or `Job.ShortRun` or longer
([BDN diagnosers][bdndg]).

Two reasons this gate should **not** move in-process:

1. **There is a known in-process allocation-flakiness issue.** BDN issue #1925 is titled "Flaky tests because
   of AssertAllocations with InProcessEmitToolchain on Windows" — i.e. BDN's own test suite saw allocation
   flakiness specifically from the in-process emit toolchain on this platform. **I did not read the issue
   body; the title alone is enough to disqualify the change for an exact-bytes gate, but treat the mechanism
   as unverified.**
2. **In-process means the host's JIT/tiering state leaks into the measurement.** The benchmark runs in a
   process that has already JIT'd the runner, the config parser, and everything else. On .NET 10, with
   escape analysis and stack allocation of objects, *whether an object is heap-allocated can depend on
   tiering and inlining decisions* — which is exactly the thing a fresh, isolated process makes reproducible
   and a shared host process does not.

The documented "99.5 % accurate" figure is **not** a discriminator here: it is stated for MemoryDiagnoser
generally, not for a particular toolchain, so it applies to the current configuration too. The evidence that
the current configuration is exact *for these benchmarks* is the repo's own "two full smoke runs, all 41
benchmarks byte-identical" — an empirical result about this setup, which is precisely what would have to be
re-established for any other setup.

**Recommendation: leave the BenchmarkDotNet toolchain alone. It is the one leg where the measured
saving would be bought with the gate's entire meaning.**

### 5.3 The saving that is available here

The smoke leg is ~15 min for ~57 benchmarks (brief) / "6:36–6:44" (housekeeping comment) — the figures differ,
which is itself worth reconciling. If it needs to be cheaper, the honest lever is **fewer iterations, not
fewer processes**: allocated bytes from `GC.GetAllocatedBytesForCurrentThread` are per-operation and
deterministic for a fixed SDK, so a job with a minimal `IterationCount`/`WarmupCount` still yields the same
allocated bytes while collapsing the timing statistics — which are already non-gates.

**But there is a documented floor, and the repo is already sitting on it.** BDN states MemoryDiagnoser is
"99.5 % accurate about allocated memory when using default settings **or `Job.ShortRun` (or any longer job
than it)**" ([BDN diagnosers][bdndg]), and `housekeeping.ps1` records that the smoke leg *is* a ShortRun.
Going below ShortRun therefore leaves the accuracy envelope BDN documents — for a gate that fails on a
one-byte difference, that is not a trade worth making blind.

- **Expected saving: PLAUSIBLE, potentially large — but only available by going below the documented
  accuracy floor.** This demotes the item substantially: it is no longer "free time", it is "time bought by
  leaving the supported configuration".
- **Risk: (i) below-ShortRun leaves BDN's stated accuracy envelope; (ii) the timing columns become
  worthless as informational context; (iii) the `blit-ratio-baseline.json` check must be examined first — if
  it compares *timings*, this breaks it outright.** I did not read that baseline; that is M6.
- **How to verify:** run the smoke twice at reduced iterations and assert every `BytesAllocatedPerOperation`
  is byte-identical to the current `allocation-baseline.json`. If a single one moves, abandon.

---

## 6. Things the brief did not list

**6.1 The gate re-measures nothing about itself.** There is no per-stage timing in `housekeeping.ps1`. A
`Stopwatch` per stage, printed and optionally appended to a small ledger, costs nothing, cannot break a
gate, and converts every future speed claim from anecdote into data. **This should be done first, before any
optimisation, or none of the rest can be honestly reported.**

**6.2 The static-mutant share is a design property of the mutated code, and it is measurable per file
before a leg is run.** §1.3's table shows a 0 %-to-66 % spread across legs. Because the static term is 99.7 %
of the cost, **the cheapest way to make a mutation leg fast is to choose mutate targets with a low static
share** — and, conversely, adding `MapperExtractor.Members.Phases.cs` (66 % static) to a leg is 20× more
expensive per mutant than adding a DocTooling file. This is a *scheduling* insight, not a coverage
reduction: the same files still get mutated, but the owner can decide which leg they live in and how often
it runs, with a number rather than a guess. It also suggests a diagnostic worth adding to
`housekeeping.ps1` next to `Assert-MutantsWereTested`: print the static share per leg, so a change that
doubles a leg's cost is visible in the log rather than in the wall-clock.

**6.3 Whether Stryker aborts a mutant's test run at first kill — evidence is suggestive, not conclusive.**
MEASURED: `killedBy` lists for static mutants run up to **53 entries** (and 36, 26, 17, 16, 14…), and 141 of
330 killed mutants have exactly one killer. If Stryker never aborted, the long lists are expected; if it
aborts cooperatively, the long lists are *also* explainable, because with 12-way xunit parallelism many
tests are already in flight when the first failure lands and they still report. **So this does not prove
there is no early abort, and I did not find a primary source either way** (the docs cover
`break-on-initial-test-failure`, which is about the *initial* run, not per-mutant abort). If an abort does
exist, then ordering the likely killers first *would* be a lever for the 124 killed static mutants and this
paragraph's original conclusion inverts. **Resolve it by reading `VsTestRunner`'s handling in stryker-net
before pursuing or dismissing killers-first ordering.**

**6.4 Test-impact analysis is not available as a local .NET tool.** Azure Pipelines' Test Impact Analysis
maps tests to source files and runs only affected ones ([MS Learn][tia]), but it is a pipeline task tied to
the VSTest task, not something a developer loop can invoke, and its fallback behaviour ("falls back to the
full suite for any change it cannot reason about") is the correct instinct but not something you get
locally. **The practical local equivalent is `dotnet test --filter` driven by the developer, plus
`dotnet-stryker --since:master` for mutation — both developer-loop-only, never gates.**

**6.5 The brief's file counts do not match the tree.** MEASURED: `tests/DwarfMapper.IntegrationTests` has
117 `.cs` files (115 with `[Fact]`/`[Theory]`), not 514; `tests/DwarfMapper.NegativeCases` has 46 files of
which 3 carry test attributes (it is data-driven — 120 tests from 3 methods);
`tests/DwarfMapper.Generator.Tests` has 321 files. Not important in itself, but any "before" number should
be re-derived rather than inherited.

**6.6 Coverage collection is 24.7 % of the fast tier and is only needed by one gate.** MEASURED
(74.5 → 92.9 s). It is already folded into the same run rather than a second pass, which is the right call.
Nothing to do; recorded so it is not re-proposed.

---

## 7. Ranked interventions

Ranked by (expected saving ÷ risk). "Gate meaning" is a separate column on purpose: anything marked
**CHANGES** is the owner's decision, not an optimisation to be assumed.

| # | Intervention | Mechanism | Expected saving (basis) | Risk to correctness | Gate meaning | How to verify here |
|---|---|---|---|---|---|---|
| 1 | **Stage timing in `housekeeping.ps1`** | `Stopwatch` per stage + printed ledger | 0 s (it is the instrument) | none | preserved | it *is* M0 |
| 2 | **Find and break the static-initializer scope that flags 137 mutants** (§2.2) | a mutant is flagged static iff it executes once while `MutantContext.depth > 0`; the flag is sticky per run, and static mutants are **99.7 % of the leg's test executions** (MEASURED) | **the largest available saving in this report** — up to ~99 % of the mutation legs' test executions; also PLAUSIBLE only, since the responsible initializer is not yet identified | medium: the fix is a product-code shape change made for the tool's benefit, and mutants that are currently killed "by accident" (by a test that does not cover them) could flip to Survived — that is a *truer* score, but it is a score change | denominator preserved (same files, mutators, mutants); **score may move, and that must be inspected, not waved through** | M7 |
| 3 | **Fix Stryker × xunit thread oversubscription** | Stryker `concurrency` (½ cores) × xunit `maxParallelThreads` (cores) oversubscribes by ½ the core count; pin the product to ≈ cores for Stryker runs only | PLAUSIBLE, 0–40 % of every mutation leg; high-variance unknown | none — pure scheduling; watch `Timeout` count stays 0 (§2.9) | preserved | M4 |
| 4 | **Share a baseline `CSharpCompilation`** (§3.2) | reuse Roslyn's `ReferenceManager` via `Baseline.AddSyntaxTrees(tree)` | PLAUSIBLE; upper bound ~⅓ of suite time from the 62→83 s calibration, likely much less because Roslyn already weak-caches assembly symbols | medium: per-`(assemblyName, options)` baselines; tree order is load-bearing; fuzz path must keep unique names; **and the reuse condition itself is UNVERIFIED** | preserved | read `CanReuseReferenceManager` **first**, then M2 |
| 5 | **Build once, then `--no-build --no-restore` for stages 1/1-heal/2** (§4.1) | remove duplicate MSBuild evaluation and up-to-date checks | PLAUSIBLE, seconds–tens of seconds per gate run | medium: must keep an explicit build that still fails on warnings, or the gate goes vacuous | preserved *if* the build is kept | M0 will size it; then A/B one gate run |
| 6 | **`GetSemanticModel(genTree).GetDiagnostics()` in `GeneratedCodeWarnings`** (§3.3) | bind only the generated tree, not the fixture | PLAUSIBLE, only 18 call sites but they are matrix-heavy | medium-high: cross-tree diagnostics; only a full differential can clear it | preserved *only if* the differential is exactly equal | M3: full-corpus differential, abandon on any difference |
| 7 | **Collectible `AssemblyLoadContext` in `EmitAssembly`** (§3.4) | stop leaking one assembly per fuzz seed into the default ALC | PLAUSIBLE, small wall-clock, real memory | medium: ambient-registry module initialisers may pin the context and make it inert or harmful | preserved | M5: assembly count + working set at start/end of a deep run |
| 8 | **Choose mutate targets by static share** (§6.2) | static mutants are 99.7 % of leg cost; per-file static share is 0–66 % | MEASURED leverage: a 66 %-static file costs ~20× a 0 %-static one per mutant | none to the score; it is scheduling of which leg runs when | preserved (same files still mutated) | M4's per-leg numbers make it self-evident |
| 9 | `test-projects` scoping, **case (a) only**: drop the analyzer-referencing projects (§2.3) | IntegrationTests / CorpusTests / DifferentialTests reference the generator as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, so it runs at *their build time* with no mutant active; MEASURED, they contribute **0** of the 426 covering tests | MEASURED ceiling **14.3 %** of a leg | the "cannot kill by construction" argument is PLAUSIBLE, not measured; if it is wrong, an oracle is lost silently | **CHANGES — owner's decision; standing ruling is no.** Verify by requiring a bit-identical score with and without | run the leg both ways on one tree |
| 9b | `test-projects` scoping, case (b): also drop ConsumerTests / Testing.Tests | "no killer today" | ≤ 1.6 % | the maintainer's original objection applies in full | **CHANGES — recommendation: no; not worth the argument** | n/a |
| 10a | **Reduce BDN smoke iterations below ShortRun** (§5.3) | fewer timing iterations; allocated bytes are per-op and SDK-deterministic | PLAUSIBLE, large fraction of a ~7–15 min leg | **leaves BDN's documented accuracy envelope (99.5 % is stated for ShortRun *or longer*), and this gate fails on one byte**; also breaks `blit-ratio-baseline.json` if that is timing-based | preserved *only if* every pinned byte value is identical across two runs — otherwise it silently weakens the strongest gate in the repo | M6 |
| 10b | `with-baseline` on the mutation legs | reuse unchanged mutant verdicts, full report retained | PLAUSIBLE, very large on repeat runs | reused verdicts can outlive the tests that justified them — and this repo changes tests on nearly every commit | **CHANGES (subtly) — owner's decision** | run with and without on the same tree; any score difference is the answer |
| 11 | `--since` on the mutation legs | mutate only the diff | large | mutants outside the diff are marked ignored and leave the report; the score is over a different denominator than `thresholds.break` was measured on | **CHANGES — do not gate. Developer loop only** | n/a |
| 12 | `mutation-level: Basic`, `ignore-mutations`, `ignore-methods` | fewer mutators / fewer mutants | proportional | every recorded threshold was measured at `Standard` | **CHANGES — owner's decision; recommendation no** | n/a |
| 13 | `coverage-analysis: perTestInIsolation` | the only mode that can attribute static mutants | **negative** — capture runs each of 6,603 tests in its own session | none; strictly more accurate | *improves* meaning, at large cost | measure the capture phase alone before anything else |
| 14 | Migrate to MTP / xunit v3 | lower runner startup overhead | small positive on the fast tier, **large negative on mutation** ([#3629][i3629]: MTP runner loses per-test coverage) | high: v2→v3 across 8 projects, Verify, coverlet, blame-hang, logger | preserved but mutation legs get slower | do not attempt now |
| 15 | Project-level test parallelism (`-m`) | overlap test projects | ≤ 19 % (generator project is 81 % of tests) | shared `TestResults/`, repo-writing artefact tests (`RepoWriteGuard`) | preserved | not recommended |
| 16 | `Basic.Reference.Assemblies` | deterministic reference set | **negative** — larger set, cf. the 62→83 s measurement | none | preserved | not recommended; recorded so it is not re-discovered |

---

## 8. Measurement plan — establishing before/after honestly

The rule this plan enforces: **no intervention is adopted on a reasoned argument; each is adopted on a
paired measurement on the same tree, on a quiet machine, with the artefact committed.** The repo's own
`verify-before-declaring-a-limit` and `benchmarks-need-realistic-data` findings are the precedent.

**Environmental discipline** (the `stryker-config.json` comment already states the standard): quiet machine,
~2 % CPU at launch, no concurrent build, no IDE indexing. Every timing below is the **median of 3** runs,
with min/max recorded. Anything that cannot be reproduced twice is not a measurement.

### M0 — instrument the gate (prerequisite for everything else)
Add per-stage `Stopwatch` timing to `housekeeping.ps1` and print a one-line ledger: restore, build, stage-1
tests, coverage report, exhaustion, AOT, ILVerify, bench smoke, each mutation leg. Run `-Nightly` once to
populate it. **Deliverable:** the first honest breakdown of the gate's minutes, including the never-measured
build term (§1.4). Until this exists, every "we made it X % faster" claim in this repo is unfalsifiable.

### M1 — per-test cost profile of the generator suite
Run `dotnet test tests/DwarfMapper.Generator.Tests -c Release --no-build --logger "trx"` and parse the TRX
for per-test durations. **Deliverables:** (a) top-50 slowest tests and their share of the total; (b) totals
grouped by test class; (c) totals grouped by harness entry point (join the class list against the
`grep` census in §1.2); (d) a `grep` of `tests/` for `[Collection(` / `ICollectionFixture` to find any
accidental serialisation (§4.2). This tells us whether the suite is a flat 7,386 × 10 ms or a fat tail — and
those two worlds call for different interventions.

### M2 — baseline-compilation sharing (differential + timing)
Branch. Add an opt-in env var (`DWARF_SHARED_BASELINE=1`) so both constructions exist in one binary.
(a) **Correctness:** run the entire generator suite under both, capturing for every harness call the ordered
diagnostic id+location+message list and the generated text; assert byte equality. Any difference kills the
intervention. (b) **Speed:** median-of-3 wall-clock, both modes, `--no-build`, quiet machine.
**Accept only if:** differential is exactly equal AND the saving exceeds 5 % (below that it is not worth the
shared-state hazard).

### M3 — narrowed diagnostics (differential only, then timing)
Same shape as M2, for `GeneratedCodeWarnings`: compare the *filtered* `.g.cs` diagnostic sets from
`Compilation.GetDiagnostics()` versus `GetSemanticModel(genTree).GetDiagnostics()` across every call site in
the suite, including the deep tier (`DWARF_DEEP=1`) so the combinatorial matrices are in scope.
**Accept only if** the sets are equal everywhere. A sampled agreement is not evidence.

### M4 — the mutation cost model, and the oversubscription question
Use the **doctooling** leg as the probe: 0 static mutants, 289 scoreable, so it is short and it isolates the
non-static term. Then use the **runtime** leg (24 static) for the static term.
(a) Confirm the model: plot leg wall-clock against static count for all five legs from one nightly run
(M0 gives the timings). If wall-clock tracks *scoreable* better than *static*, the model in §1.3 is wrong.
(b) Oversubscription: run one leg at `concurrency × maxParallelThreads` ∈ {6×12, 6×2, 3×4, 12×1}, median of
3, and record wall-clock **and** the report's `Timeout` count (must stay 0) **and** the mutation score
(must be identical — if the score moves, something other than scheduling changed and the experiment is
invalid).

### M5 — assembly leak
Add a temporary diagnostic to a deep-tier run printing `AppDomain.CurrentDomain.GetAssemblies().Length` and
`Environment.WorkingSet` at start and end. If the count grows by ~the fuzz population, the leak is real.
Then check whether the ambient registry retains delegates from emitted assemblies — if it does, a collectible
ALC will not unload and the intervention is inert.

### M7 — locate the static-initializer scope (the highest-payoff diagnostic)
The mechanism is now known (§2.2): a mutant is flagged static iff it executes at least once while
`MutantContext.depth > 0` on its thread, i.e. inside an instrumented static initializer / static field
initializer / static accessor in `DwarfMapper.Generator`. The task is to find which one.

Three probes, cheapest first:

1. **Static, from the report.** The 137 static mutants are known by file and line. Compute the set of
   *methods* containing them and the set of methods containing the 254 non-static mutants. If a whole method
   is uniformly static (e.g. `LocationInfo`, 9 of 10) while another in the same class is uniformly
   non-static, the boundary names the entry path. This is pure analysis on artefacts already on disk.
2. **Syntactic sweep.** Search `src/DwarfMapper.Generator` for every `static` field/property initializer
   whose right-hand side is anything other than a literal, `new()`, or a framework singleton — those are the
   only places `TrackValue` can be injected — and trace each one's call graph (roslyn-lens
   `get_call_graph`) for a path into `BlittableProof` / `ConstructorSelector` / `EquatableArray` /
   `LocationInfo`. My `grep` found no such initializer, so either the sweep must be widened (static
   accessors, static local functions, module initializers) or hypothesis (3) is right.
3. **Empirical, one Stryker run.** Restrict `mutate` to a single small file with a known static mutant
   (`LocationInfo.cs`: 10 scoreable, 9 static — the cheapest possible probe), and run the leg with
   `test-case-filter` narrowed to one test class at a time. The static flag is a property of *whether any
   executed path entered a static scope*, so the filter that makes the flag disappear names the test whose
   path enters it. A handful of short runs, each far cheaper than one full leg.

**Then, before acting:** whatever is found, the fix is a shape change to product code made for the tool's
benefit. It must be justified on its own terms (a static initializer that transitively runs layout analysis
is arguably a design smell anyway), it must ship with the regression test the repo's rules require, and the
mutation score before/after must be compared mutant-by-mutant — a mutant that flips Killed → Survived was
being killed by a test that never covered it, which is information the owner needs to see, not a regression
to paper over.

### M6 — benchmark smoke iterations
First **read** `benchmarks/DwarfMapper.Benchmarks/blit-ratio-baseline.json` to determine whether it gates a
*ratio of timings* (if so, iteration reduction is off the table). If it does not: run the smoke twice at
reduced `IterationCount`/`WarmupCount` and assert every `BytesAllocatedPerOperation` equals the committed
`allocation-baseline.json` exactly, and that `totalBenchmarks` is unchanged. **Accept only on two
consecutive byte-identical runs**, matching the standard the existing pin was set by.

### Reporting standard
For each accepted intervention, record in the commit: the before and after median-of-3, the machine, the SDK,
the differential evidence, and the *unchanged* artefacts (mutation score, `Timeout` count, allocation pins,
coverage floors). Per the `fixes-lock-with-regression-tests` rule, anything that changes harness behaviour
ships with a test that fails without it. And per `solution-build-gates-emission-changes`, any harness change
gets a whole-solution build including `samples/`.

### The honesty clause
Three of this report's largest numbered items are **PLAUSIBLE, not measured**: #2 (the static-initializer
scope — mechanism verified, cause unidentified), #3 (thread oversubscription), and #4 (baseline
compilation). #4 in particular may be substantially smaller than it looks, because Roslyn already
weak-caches assembly symbols across compilations *and* because the reuse condition itself is unverified. If
M2, M4 and M7 all come back small, the correct conclusion is that this suite is already near its floor and
the remaining minutes are the price of compiling 7,386 programs — which would itself be a result worth
writing down, and would redirect the effort to items #3 and #8, which are about *scheduling* the same work
rather than doing less of it.

One hypothesis has already died this way and is recorded so it is not resurrected: that xunit's 12-way
parallelism causes Stryker to mis-attribute coverage and inflate the static set. `MutantContext.depth` is
`[ThreadStatic]` (§2.2, verified from the shipped binary). It cannot.

---

## Sources

Primary:

- [Stryker.NET — Configuration][cfg] (coverage-analysis modes and default, test-projects, since, with-baseline,
  baseline.provider, mutation-level, concurrency, ignore-methods, ignore-mutations, additional-timeout formula,
  test-case-filter, break-on-initial-test-failure)
- [Stryker — Static mutants][mte]
- [stryker-net PR #636 — Identify 'static' mutants][pr636]
- [stryker-net — `MutationContext.cs`][mc] (`InStaticValue`: "True when inside a static initializer, fields
  or accessor"; `EnterStatic()`)
- **The injected-helper sources (`MutantControl`, `MutantContext`) extracted verbatim from the embedded
  resources of the installed `dotnet-stryker` 4.16.0 binary** at
  `~/.dotnet/tools/.store/dotnet-stryker/4.16.0/dotnet-stryker/4.16.0/tools/net8.0/any/Stryker.Core.dll` —
  i.e. the exact build this repo's gate runs, not a `master` snapshot. Quoted in §2.2.
- [stryker-net issue #949 — Some mutants' coverage cannot be detected][i949]
- [stryker-net issue #3629 — MTP runner: coverage analysis assigns every covered mutant the full test suite][i3629]
- [Stryker — Announcing incremental mode][inc] (StrykerJS wording for the reuse-with-full-report idea)
- [dotnet/roslyn — `ReferenceManager.cs`][rm] (reference binding, cross-compilation symbol caching)
- [xUnit — Running tests in parallel][xup]
- [xUnit v3 — Microsoft Testing Platform][xmtp]
- [Microsoft Learn — Microsoft.Testing.Platform vs VSTest][mtp]
- [.NET Blog — Enhance your CLI testing workflow with the new dotnet test][mtpblog]
- [MSBuild — Build multiple projects in parallel][msb]; [microsoft/vstest#4044 — degree of parallelism][vst]
- [BenchmarkDotNet — Toolchains][bdntc]; [BenchmarkDotNet — Diagnosers][bdndg]
- [Microsoft Learn — Test Impact Analysis (Azure Pipelines)][tia]
- [Mapperly — Tests and linting][map]
- [Basic.Reference.Assemblies][bra]
- [Andrew Lock — Testing your incremental generator pipeline outputs are cacheable][al10]; [Meziantou — Testing Roslyn incremental source generators][mez]

In-repo artefacts read (no files modified):
`stryker-config*.json` (all five), `scripts/housekeeping.ps1`, `.runsettings`, `Directory.Build.props`,
`Directory.Packages.props`, `tests/DwarfMapper.Generator.Tests/GeneratorTestHarness.cs`,
`tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj`, the `.csproj` of every other test
project (for the generator reference shape — `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` in
IntegrationTests / CorpusTests / DifferentialTests; a plain `ProjectReference` in CompilerTests /
NegativeCases),
`src/DwarfMapper.Generator/Pipeline/BlittableProof.cs`, and the mutation reports under `StrykerOutput/`
(notably `2026-09-02.07-23-06`, `2026-08-26.23-46-17`, `2026-08-26.23-29-32`, `2026-08-26.23-24-33`,
`measured-2026-08-27.11-26-45`).

Could not verify: **which** static initializer in `DwarfMapper.Generator` puts the 137 mutants into the
static bucket (§2.2 — the *mechanism* is verified, the *cause* is not; M7); the exact condition under which
`CSharpCompilation.AddSyntaxTrees` reuses the `ReferenceManager` (§3.2 — blocks M2); whether the `dotnet`
CLI passes a default `-maxcpucount` so that `dotnet test <sln>` already runs projects concurrently (§4.4 —
settle locally); whether Stryker aborts a mutant's test run at first kill (§6.3); any published performance
technique in System.Text.Json's generator tests or in Andrew Lock's / Stephen Toub's writing that recommends
compilation reuse (§3.6); the body of BenchmarkDotNet issue #1925 (§5.2, title only).

[cfg]: https://stryker-mutator.io/docs/stryker-net/configuration/
[mte]: https://stryker-mutator.io/docs/mutation-testing-elements/static-mutants/
[pr636]: https://github.com/stryker-mutator/stryker-net/pull/636
[mc]: https://github.com/stryker-mutator/stryker-net/blob/master/src/Stryker.Core/Stryker.Core/Mutants/MutationContext.cs
[i949]: https://github.com/stryker-mutator/stryker-net/issues/949
[i3629]: https://github.com/stryker-mutator/stryker-net/issues/3629
[inc]: https://stryker-mutator.io/blog/announcing-incremental-mode/
[rm]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/ReferenceManager.cs
[xup]: https://xunit.net/docs/running-tests-in-parallel
[xmtp]: https://xunit.net/docs/getting-started/v3/microsoft-testing-platform
[mtp]: https://learn.microsoft.com/en-us/dotnet/core/testing/test-platforms-overview
[mtpblog]: https://devblogs.microsoft.com/dotnet/dotnet-test-with-mtp/
[msb]: https://learn.microsoft.com/en-us/visualstudio/msbuild/building-multiple-projects-in-parallel-with-msbuild
[vst]: https://github.com/microsoft/vstest/issues/4044
[bdntc]: https://benchmarkdotnet.org/articles/configs/toolchains.html
[bdndg]: https://benchmarkdotnet.org/articles/configs/diagnosers.html
[tia]: https://learn.microsoft.com/en-us/azure/devops/pipelines/test/test-impact-analysis
[map]: https://mapperly.riok.app/docs/contributing/tests/
[bra]: https://www.nuget.org/packages/Basic.Reference.Assemblies/
[al10]: https://andrewlock.net/creating-a-source-generator-part-10-testing-your-incremental-generator-pipeline-outputs-are-cacheable/
[mez]: https://www.meziantou.net/testing-roslyn-incremental-source-generators.htm
