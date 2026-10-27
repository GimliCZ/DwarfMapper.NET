# Round 34 — tasks

Branched from master `4a169a4` (round 33 merged as PR #9 `b58c6e3`, then PR #10) on 2026-10-27. Round 31's evidence
rule stands: a performance change becomes a task at **≥ 1.3x or ≥ 20 % allocation, consistently, measured on the
emitted shape**, not on a model of it. Every fix ships its RED→GREEN regression test in the same commit, and every new
branch is reachable and tested or removed (2026-09-10 full-coverage ruling).

## Tasks

The owner's pick on 2026-10-27, out of five candidates: all of them, as four tasks. Lettered, because round 33's T03
is still open under its own name.

- **A. Nightly reliability.**
  - **A1. Memory headroom on the mutation legs** (see "Next steps — CI"): in round 33's Docker rig and in Stryker's
    serial mode, the mode the legs run in, find what the test host holds at its peak (a gcdump), then try the fix
    the dump points at. Any heap-limit arm reports the suite's time as well as its peak.

    **Done — `3144bab`; the evidence is in `benchmarks/results/2026-10-27-round34-a1-testhost-memory.md`.**
    - **Not the GC heap.** The host's working set reached 6.8 GB while the GC's committed heap never passed 0.41 GB,
      and the gcdump found 214 MB of live objects at 4.2 GB RSS.
    - **Native memory.** The rest was anonymous memory in glibc malloc arenas. It rose in steps while the
      symbol-level unit tests ran: about fifty sites called `MetadataReference.CreateFromFile` on every compile.
      That call copies the whole image into native memory, and the copy is freed only at finalization.
    - **The fix.** `GeneratorTestHarness.ReferenceTo` creates one reference per file for the host.
      `MetadataReferenceReuseScanTests` holds the rest of the project to it: 52 sites before, RED; none after.
    - **A/B in Stryker's serial mode** (three trials per arm, alternating):
      - peak host 5.63 / 6.92 / 8.33 GB before, against 0.83 / 0.85 / 0.82 GB after (8.3x lower at the median);
      - lowest available memory 5.97-8.38 GB before, 13.26-13.48 GB after;
      - suite 1 m 43 s - 2 m 1 s before, 1 m 28 s every time after.
    - **The GC heap limit is moot.** It would not have touched memory outside the GC heap.
    - **The `2f3b290` PLINQ arm** measured default mode, which build-test and deep-test already have headroom for.
      It is not pursued.
    - **Open: CI.** The first nightly after this reaches master has to show the mutation legs' largest host near
      1 GB. That is a prediction from the rig, not yet a measurement.
  - **A2. The dashboard upload retries once** on a 5xx or a failed connection, after a pause; never on a 4xx, which
    the step already explains as configuration.

    **Done.** The PUT moved out of the workflow into `Send-DashboardReport` (`scripts/gate-checks.ps1`), with its
    transport and its clock injectable. `DashboardUploadTests` drives the real function through a pwsh battery
    (200 and 201 once; 504 then 200, and 000 then 201, once more after 120 s; 413 once and fails; 503 then 502 twice
    and fails; no status once and fails), and holds the mutation job to calling it with no `curl` of its own. Both
    facts were RED before the change (the function missing, the step still sending its own PUT); SelfValidation
    632/632 after. The step's comment that the `?module=` spelling was unverified is corrected: the six badges read
    six separate scores.

    **Then the real transport.** The battery's scripted transport left curl's own failed-connection path untested.
    With PowerShell's native-command preference on, curl's non-zero exit throws before the policy sees the 000 it
    exists to retry: a case running the real curl against a closed local port read `Program "curl.exe" ended with a
    non-zero exit code` (RED). The default transport now turns that preference off for itself (GREEN): pause, a
    second send, then `HTTP 000 after 2 attempt(s)`.
- **B. The silenced-checks inventory** ("Owed between rounds"), diffed against round 30's.

  **Inventory taken** (`Issues/round34/SILENCED-CHECKS-INVENTORY.md`, agent-produced, headline claims spot-checked).
  Round 30's de-silencing plan, including Task 30.4's reason-required scans, was never built. Its 23 proposed tasks
  wait for the owner's pick; the top three are shipped code: four directive readers drop a `null` name without a
  diagnostic, a probe for a misspelled `[MapProperty]` destination under `[MapTo]`, and Task 30.5. Also verified:
  the AOT sample's two checks cannot fail the gate, and master requires no status checks (owner's setting).

  **B1 done — null directive names are reported.** `[MapIgnore<T>(null)]`, method-level `[MapValue(null!, …)]`,
  `[Flatten(null!)]` and `[FlattenGraph(null!, …)]` vanished with a green build while an empty name was reported.
  Their readers now read null as the empty name, as `d474e9f` and `fc58fb0` did for the three pair-scoped siblings
  on 2026-09-13, so each reports what an empty name does (DWARF095/056, DWARF042, DWARF016, DWARF034; DWARF090 and
  DWARF092 at element-wise and update-into endpoints), and no message renders the argument as null. Three tests had
  pinned the silence (`PairScopedIgnoreReaderCoverageTests`, `FlattenAndMapValueReachTests`,
  `DirectiveEndpointReachTests`); they now assert the report, and `MethodDirectiveNullNameTests` is new: RED before,
  GREEN after. Whole solution 0 warnings; every test project green.

  **B2 done — DWARFR17.** Under `[MapTo]`, a `[MapProperty("Nmae")]` naming a destination member the target does not
  have was skipped in silence (`MapToGenerator`'s `if (w.Symbol is null) continue;`). The probe confirmed the
  inventory's inference: with `Name` mapped by name, nothing at all was reported. DWARFR17 is the registry's DWARF008,
  an Error at the directive; a member's own name matching nothing is still just an unused source member.
  `RegistryDiagnosticsGenTests` holds both (RED before, GREEN after); documented in `docs/diagnostics.md` and
  release-tracked. Whole solution 0 warnings; every test project green.
- **C. DwarfMapper's share of a large build** — a `-p:ReportAnalyzer=true` build of a fresh copy of FusedChat's
  validation tree (32 projects, 8 mappers), repinned to master packed under a version nobody will reuse: the
  generator's time, cold and warm, against the compiler's and the whole build's. The generator alone is measured
  (round 23 S3, repeated in round 33: linear, ~0.5 ms per mapped pair warm); what is new is its share of a real
  build, which decides whether compile-time work earns a round. No earlier FusedChat build timing exists in
  `benchmarks/results` or `Issues/`; ReportAnalyzer was used before only on this repository's own analyzers
  (rounds 21-22).

  **Done — `benchmarks/results/2026-10-27-round34-c-fusedchat-build-share.md`.** Six `--no-incremental` Release builds
  of a fresh copy (SDK 10.0.400, `1.1.0-r34val.1`), cold and warm alternating. DwarfMapper's generators run in 20
  compiler calls per build, summing 2.75-2.94 s cold and 1.93-2.19 s warm: about 5 % of the compiler's 40-57 s.
  Cold start is worth ~0.8 s a build, which is everything ReadyToRun could recover, so the cold-JIT lead is closed.
  In the same builds `Microsoft.VisualStudio.Threading.Analyzers` costs 12.1 s and Razor 8.3 s: a slow FusedChat
  build is mostly not DwarfMapper.
- **D. Mutation-testing accuracy** (owner decision 4): can `SurfaceProbe.Baselines`, a process-wide memo of generator
  output, make Stryker credit a kill to the wrong mutant or hide one? Stryker 4.16's source first (how mutants are
  grouped into a session, whether a test host outlives one), a narrowed Stryker A/B only if that leaves it open. The
  deliverable is the answer and a proposal; the memo's design stays the owner's call.

  **Answered: no, not with today's coverage.**
  - How Stryker 4.16 runs mutants, read from its source:
    - A session tests several mutants only when no test covers two of them (`MutationTestProcess.BuildMutantGroupsForTest`,
      `!assessingTests.ContainsAny(…)`).
    - A mutant whose tests are "every test" (the static ones) gets a session to itself.
    - Every session starts its own test host: `StrykerVsTestHostLauncher` starts a process per `RunTestsWithCustomTestHost`.
    - xUnit runs serially in Stryker's settings.

    So the memo lives for one session, and each key is filled by the first test, in a fixed order, that needs it,
    under that test's mutant. A test reads a baseline computed under another mutant only if two conditions hold. A
    mutant covered by one memo test shares a session with a mutant covered by another memo test. And the second test
    does not cover the first mutant.
  - Measured on the six legs' reports of run 40477294097 (`b58c6e3`):
    - 1,003 tests read the memo: the per-cell theory's 966 rows, plus 37 others.
    - They cover 13 non-static mutants in the generator leg, 25 in the pipeline leg, and none in the other four.
    - No such pair exists in any leg.
    - The screen is not blind: two planted mutants, each covered by a single cell row, give 810 pairs.
  - The memo's key leaves out `types`, but both callers derive it from the key's `probeKey` (`SurfaceFixtures.Get`).
    So no two cases share a baseline they should not.
  - **Proposal (owner decision 4):** keep the memo, which roughly halves the matrix. What protects it is today's
    coverage, not a rule: a new memo-reading test whose coverage shares nothing with another's would open the hazard.
    The same screen could run over every nightly's reports and fail, naming the pair.

## Next steps — the owner's

1. **Enrol in Anthropic's OSS Scanner.** Nothing blocks it now. Since PR #10, master's `.oss-scanner/project.yaml`
   carries exactly the `key: value` lines of the prepared entry. On 2026-10-27, after that merge, upstream
   (`anthropics/oss-scanner`, main at `cae19c8`) had no `projects/dwarfmapper` and no DwarfMapper PR. The kit is in
   `~/.dwarf-tools/osc-enrol/`:
   1. Open the link in `new-file-link.txt`. GitHub forks the repository and fills in `projects/dwarfmapper/project.yaml`;
      commit it and open the PR.
   2. Paste `pr-body.md` (upstream's template) as the description and tick the boxes. Every box is left empty: being
      a core maintainer, accepting the terms and the eligibility claim are the owner's statements. The PR's `terms`
      check stays red until the terms box is ticked.
   3. Sign the CLA with a comment that contains only `I have read the CLA Document and I hereby sign the CLA`.

   Reports then go to `dwarfmapper@seznam.cz` and, through private vulnerability reporting (on since 2026-10-27), to a
   private advisory on this repository, both at every severity. Upstream's own test of "the scanner will likely work"
   passed on 2026-10-27: `tools/check dwarfmapper` (`cae19c8`) built master at `b58c6e3`, and in the finished image,
   offline at the agent's 2 CPUs / 8 GB, the push-time suite passed 9,621 of 9,621 in 154 s, with no out-of-memory
   event. Nothing in README or SECURITY.md may claim the scanner until the enrolment PR is merged.
2. **FusedChat A/B stats** — round 31's scripts in `Issues/round31/fusedchat-stats/`; the owner's tree and machine
   time, so the owner schedules it.

## Next steps — CI

- **The first nightly on round 33's code was red only on the dashboard.** Run 40477294097 (`b58c6e3`) started at
  08:58 UTC, 5 h 41 min after its 03:17 cron. Everything round 33 targeted passed: package-size, deep-test, and the
  initial run of all six mutation legs, with scores identical to PR #9's. The pipeline report uploaded, so the 413 is
  gone. Two of the 23 jobs went red, the runtime and codefixes legs, and only at their upload step: the Stryker
  dashboard answered both PUTs (09:09:41 and 09:09:59) with **HTTP 504** four minutes later. Doctooling's upload at
  09:08 and testing's at 09:28 succeeded, and codefixes' report is the smallest of the six, so size is not the cause.
  At 12:30 UTC all six badges showed that run's scores: the 504s cost no displayed number.
  - Offered: re-run the two failed jobs. It only turns the run green.
  - Offered: the upload step retries once on a 5xx. The workflow is the code under test, so its test is a scan in
    SelfValidation, the way `PerfCategoryIsolationScanTests` holds a workflow rule.
- **Memory headroom on the mutation legs** — the open risk that can still kill a nightly. On that run:

  | Leg | Score | Largest test host | Lowest available |
  |---|---|---|---|
  | pipeline | 95.22 % | 8.7 GB | 0.6 GB (09:23:44 UTC) |
  | generator | 97.11 % | 8.1 GB | 1.5 GB |
  | runtime | 97.48 % | 6.8 GB | 3.2 GB |
  | doctooling | 96.55 % | 6.6 GB | 6.3 GB |
  | testing | 100.00 % | 5.9 GB | 7.5 GB |
  | codefixes | 87.64 % | 4.7 GB | 8.8 GB |

  Stryker itself peaked at 0.5-1.5 GB; the test hosts are the risk (round 33, "Generator.Tests' own peak memory").
  The experiments specified there are still open: Generator.Tests without `2f3b290`'s `.AsParallel().AsOrdered()`
  under `DOTNET_PROCESSOR_COUNT=4`; a gcdump at the peak, to find what grows; a GC heap limit for the mutation job's
  test hosts (`DOTNET_GCHeapHardLimitPercent` is read as hexadecimal: 40 % is `28`). Not Stryker concurrency 1, which
  would roughly double the generator leg's run toward its 200-minute timeout.

## Carried from round 33

- **T03 Pre-size the Preserve identity map** — waits on its API surface (owner decision 1).
- **The fast-path audit's untested cells**, in its rank order: `default(ImmutableArray<T>)` as a source (needs owner
  decision 5 before a test can pin it); a FlattenGraph leaf under Preserve; `Nullable<struct>` wrappers losing Preserve
  identity; an array blit under SetNull against a method-level config on the element pair's partial.
- **`2a10fb3`** — the null-element store shape, 1.12x: below the bar, waiting for a quiet-machine confirmation at 100
  and 10,000 elements and the owner's call.
- **Generator cold start** — closed by task C: ~0.8 s per cold FusedChat build, not worth ReadyToRun's costs.
- **T02b** — a widen span map (`void Map(ReadOnlySpan<int>, Span<long>)`) emits a scalar loop, never `Vector.Widen`;
  unmeasured.
- **DWARF066 on a positional record's inherited member** — a known false positive, pinned on purpose by
  `WhenGuardInitializerTests`; `KeepDefault = true` answers it.
- **SECURITY.md "Supported versions"** — still `1.0.2-rc.1`; one line, when the version moves.

## Owed between rounds

- **The de-silencing and exclusion inventory** (standing mandate, 2026-09-03): pragmas, `NoWarn`,
  `ExcludeFromCodeCoverage`, Stryker ignores, skipped tests, swallowed catches and unreported generator conditions.
  Each item without a stated reason becomes a task.

## Owner decisions

1. T03's API surface: a `DwarfRefContext` constructor overload, or a capacity member.
2. `[Reinterpret]` under Preserve: it forces the block copy before the gates the Preserve fix added, so a source
   reached twice becomes two targets. Keep honouring it (today), honour it and report the identity it gives up
   (`180510e`'s shape for `[MapConstructor]`), or refuse it under Preserve.
3. `[Reinterpret]` under AsNull: the same decision on a second axis; a null source array still maps to `[]`.
4. `SurfaceProbe.Baselines` under Stryker: a process-wide memo of generator output, shared across tests by design.
   Task D answered the question: with today's coverage no test can read a baseline built under another mutant. Left
   to decide: whether a nightly gate should hold that.
5. What a `default(ImmutableArray<T>)` source maps to. The block copy, the widen and the share answer empty; the
   general path enumerates `src.GetValueOrDefault()` and should throw.

Decided and not carried: LICENSE stays as it is; the public contact is `dwarfmapper@seznam.cz`; T01's pin plus canary
landed (`c142578`, `17c5c28`); private vulnerability reporting is on; scanner reports go by email and GitHub advisory
at every severity (PR #10).
