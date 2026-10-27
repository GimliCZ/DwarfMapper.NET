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
    - **Confirmed on CI** — PR #11's full-ci run 40538713221 (`71908b0`), all 23 jobs green, scores unchanged:

      | Leg | Largest test host | Lowest available | Leg time |
      |---|---|---|---|
      | generator | 8.1 → **0.9 GB** | 1.5 → 11.3 GB | 1:33:45 → 1:15:33 |
      | pipeline | 8.7 → **0.9 GB** | 0.6 → 11.3 GB | 1:36:38 → 1:11:26 |
      | runtime | 6.8 → 1.0 GB | 3.2 → 11.7 GB | 0:10:52 → 0:16:09 |
      | doctooling | 6.6 → 0.8 GB | 6.3 → 11.9 GB | 0:08:03 → 0:10:05 |
      | testing | 5.9 → 0.8 GB | 7.5 → 12.1 GB | 0:27:09 → 0:29:01 |
      | codefixes | 4.7 → 0.9 GB | 8.8 → 12.1 GB | 0:10:32 → 0:09:51 |

      Before is the 2026-10-27 nightly (run 40477294097, `b58c6e3`). The two long legs are 19 % and 26 % faster;
      the short legs' times moved by single minutes, both ways, which is runner noise at their length.
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

  **B3 done — the AOT gate's two blind checks.** CI judges the published NativeAOT binary by its exit code alone,
  and the sample's narrow-overflow and string-to-int checks printed `ERROR:` and carried on, so they could never fail
  the gate. Both return 1 now. `AotSampleGateScanTests` holds every `ERROR:` line to a non-zero return (RED on
  `Program.cs:32` and `:54`, GREEN after). The stale "36 `return 1` sites" in ci.yml and CORRECTNESS.md (39 now)
  became the rule the scan holds.

  **B4 done — round 30's Task 30.4, the reason-required scans** (the owner asked on 2026-10-27 for the whole
  inventory in this PR). `SuppressionReasonScanTests` holds five rules, each with a positive control:
  - every `<NoWarn>` id is named by the comment directly above it, and that comment names no id the element
    does not suppress;
  - no project suppresses a DWARF id in `<NoWarn>` (the 2026-09-27 ruling);
  - every real `#pragma warning disable` (parsed, so string literals and doc comments do not count) has a `//`
    reason on its line or the line above, as Task 30.4 specified;
  - every `[SuppressMessage]` has a non-empty Justification;
  - every `.globalconfig` downgrade has a comment directly above it.

  They found 60 `<NoWarn>` findings, 5 DWARF ids, and 6 pragmas. Before writing reasons, `nowarn-audit.ps1`
  (fixed first, `dd47ae2`) separated live suppressions from dead ones: 8 of 105 suppressed nothing and were
  deleted (CA5394 in Benchmarks, Conformance and Gallery; DWARF044 in AotSample and IntegrationTests; CA2227 in
  ProviderA; CA1002 and CA2227 in ProviderB). Every surviving id got its reason from the sites that raise it.
  DWARF038 moved from three `<NoWarn>` lists to per-project `.globalconfig` files with their reasons. Stale
  comment ids went (the empty CodeFixes `<NoWarn>` among them), and `samples/.globalconfig`'s premise that every
  sample suppresses CA1050 was corrected: CA1050 fires on none of them. The six pragmas got reasons; one
  file-header pragma suppressing five ids, IDE0079 included, shrank to the two that fire, scoped to the two types
  that raise them. Whole solution 0 warnings; every test project green.

  **B5 done — `DwarfMapper.Testing`** (inventory item 4, the owner's choice of record-and-report for SafeTypes):
  - CA1510 is fixed rather than suppressed: fourteen `if (x is null) throw new ArgumentNullException(nameof(x))`
    became `ArgumentNullException.ThrowIfNull(x)` (same exception, same parameter name), and the id left the
    project's `<NoWarn>`.
  - `GraphOracleComparer`'s two catch blocks were not swallows; each now says why: an overflow falls back to the
    scalar comparison and still records a difference, and an element whose ToString throws can only cause a loud
    false difference, never a false pass.
  - `ObjectFactoryV2.SafeTypes` records what did not load instead of dropping it. When no loadable type implements
    an abstract type and an assembly loaded from a file declares one that failed to load (read through its
    metadata), the factory throws, naming the type, its assembly and the loader's reason, instead of leaving the
    member null. An assembly loaded from memory stays recorded but unreported: naming it on a guess would fail
    tests that have nothing to do with it (`IUnimplemented` and the dangling test's own assembly showed how).
    `DanglingDependencyTests` holds it: RED (no exception), GREEN after; Testing.Tests 140/140.
  - The mutation-scope audit table is re-measured in the same commit: the testing leg's share fell 18.2 % -> 17.0 %.

  **B6 done — the emitted pragma documented** (`docs/CORRECTNESS.md`), and DocTooling's swallowed catch states its
  reason (`fc0ae37`).

  **B7 done — `Directory.Build.targets` no longer claims an `.editorconfig` section that does not exist.** The
  `generated_code = false` section is deliberately not committed (round 24, `d38cda1`). Round 24's blocker was
  re-tested on SDK 10.0.101: per-rule `none` under that section silences nothing a generator emitted (1,404 CA1822
  and 32 others, identical with and without it), so the always-on gate stays blocked. The two FILED findings,
  CA1852 (4) and CA1508 (2), stand; CA1019 (8) is new. Recorded in `Issues/round24/ROUND24-GENERATED-CODE-AUDIT.md`.

  **B8 done — scripts and the tools they need** (inventory items 14 and 15):
  - `mutation-battery.sh` ends by proving src/ and docs/ are clean again: every restore swallowed its own failure.
    `MutationMethodologyScanTests.M1c` holds it (RED without the check, GREEN with it).
  - `corpus-diagnostic-density.ps1` prints "revision UNKNOWN" when git fails, instead of an empty revision
    labelled "(src/ clean)". Not under test: it is the provenance line of a manual measurement script.
  - `gate-checks.ps1`'s ledger reader says what a json block that does not parse costs: the band alone, never a
    passed regression.
  - ci.yml's Codecov comment no longer says the deleted inline gate still runs, and says the status check blocks a
    merge only where master requires it, which today it does not.
  - CONTRIBUTING.md declares `pwsh` and Python 3 (standard library only), and that `full-ci` takes effect from the
    next push.

  **B9 done — two scans that passed over nothing, and ReSharper suppressions held to a reason** (items 19, 20):
  - `ConformanceArtifactScanTests` returned when `conformance/results/` was missing ("the gate creates it on first
    run"), which stopped being true on 2026-07-26: six artifacts are committed. Both tests now fail on a missing or
    empty directory (checked by moving it away: 2 of 3 red; restored: 3/3 green).
  - Round 30's Task 30.6 asked for the 19 InconsistentNaming headers in one reasoned place. Each already carries its
    own specific reason line; one place would have traded 19 reasons for one generic one. Instead
    `SuppressionReasonScanTests` holds every `// ReSharper disable` and `disable once` (40 today, all reasoned) to
    a reason line directly above, with a positive control.

  **B5, what the first full-ci run caught (`fc0ae37`, run 40558858454).** Two of its three reds were B5's. The third,
  `perf-tests`, was not (A3).
  - deep-test: `DwarfMapper.Testing` line coverage fell to 99 % against its 100 % floor. The run's coverage report
    put every gap in B5's metadata reader. No test made it name a type definition, a generic instantiation, a
    nested definition, a type nested in another assembly's type or a global-namespace type, or list two missing
    implementations at once. One fallback, `FullName ?? Name`, could not be taken. Loaded types are now matched by
    metadata token, which drops that fallback and compares exactly, and `TypeName` lost its default branch, which no
    base type or interface can reach. `DanglingDependencyTests`' probe declares every shape, all in sources compiled
    at run time (as test-assembly types they tripped CA1040 and CA1034). Measured: `Testing.Tests` alone leaves no
    line or branch of the new code uncovered.
  - package-size: the `DwarfMapper.Testing` package went from 53 to 56 KB, and B5 did not re-measure: invariant R1
    missed a fourth time, stated rather than backdated. Re-measured in its own commit (B5c).

  **B10 done — the complete fuzz proof and the ISA matrix run in CI** (inventory item 13). Until now both ran only
  on a maintainer's machine.
  - `FeatureCombinationFuzzTests.Full_power_set_compiles_on_every_emit_path` returns at once unless
    `DWARF_FUZZ_FULL=1`, and no CI job set it: housekeeping `-Nightly`, which deep-test runs, skips the exhaustion
    stage. Its own comment said CI ran it. A nightly job, `fuzz-exhaustion`, now sets the variable and runs that one
    fact. The test asserts that every subset compiled on every path, then writes a line that the job's vacuity step
    requires. Measured 2026-10-27: 65,536 subsets × 4 emit paths = 262,144 compilations, 0 failures, 10.5 min on
    6 cores / 12 threads. The job's 90-minute limit is an estimate for the first full-ci run to pin. The old
    "~6 min" figures in the test and in housekeeping were corrected.
  - `scripts/isa-matrix.ps1` ran by hand only. A nightly job, `isa-matrix`, now runs it. The script leaves out
    `Category=Perf`, like every other test run CI reaches, and it shows a failed build's errors. Piped to `Out-Null`,
    its first run for this item said only that the build had failed (two analyzer errors in an edited test file).
    `IsaMatrixScriptTests` drives the real script with a planted failing `dotnet`: RED, the error is not shown;
    GREEN, it is. Locally all five configurations pass, 1,017 integration and 70 differential tests each, in 50 s.
  - Two scans had to learn about the new jobs:
    - `CiGateScanTests` gained two rows.
    - `CiToolPrerequisiteScanTests` now exempts a job whose every test run selects tests by name, provided none of
      the names can reach the ILVerify tests. The first version of that check looked for "IlVerif", which
      `EmittedIlIsVerifiableTests` does not contain, so it would have waived the install for a job that selects
      those tests by name. Review caught it. A theory over nine filters failed 6 of 9 against that version.
  - CONTRIBUTING's list of the expensive tier gained `perf-tests` (missing since round 31) and the two new jobs.

  **B11 done — three non-blocking jobs block** (inventory item 6; the Ubuntu canary by the owner's choice,
  2026-10-27). Their job-level `continue-on-error` is gone.
  - `roslyn-forward-compat` and `preview-sdk-canary` set their own criterion: blocking once seen green, and for
    the preview canary on pushed master, with the run cited in the flip commit. Both were green on every master
    nightly read for the flip, 2026-10-22 to 10-27. The run cited is 40477294097 (`b58c6e3`).
  - `ubuntu-26-canary`'s comment planned to move every pin to 26.04 and delete the job. The owner chose instead to
    make the canary blocking and keep every other Linux job on 24.04. It was green on the same six nightlies, and
    the header note on the pins now says so.
  - `CiGateScanTests`: the three rows no longer say "declaration only". A new theory fails if a job-level
    `continue-on-error` comes back on any of them: planted on `ubuntu-26-canary`, 1 of 3 red; removed, green.
    Step-level ones (`bench-wall-time-alert`, `build-test`'s test-analytics step) are out of its scope by design.
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

## Round 35 carry-forward (from the inventory)

The inventory's other proposed tasks, in its rank order, are in `SILENCED-CHECKS-INVENTORY.md`. The first one
round 35 should build is the ratchet that keeps the rest fixed: round 30's Task 30.4 scans, never built. They
would require a reason for every `<NoWarn>` id and every `#pragma`, both directions, plus a `Justification` on
every `[SuppressMessage]`, a comment on every `.globalconfig` downgrade, and no DWARF id in a project `NoWarn`.
Then Task 30.5 (the widening-primitive near-miss, measured), the `DwarfMapper.Testing` items, and the stale
comments.

**The owner's settings, not code:**
- `master` requires no status checks, so every CI gate, Codecov included, is advisory at merge time;
- three non-blocking jobs (roslyn-forward-compat, preview-sdk-canary, ubuntu-26-canary) are past their own
  "make blocking once green" criteria, and zizmor reports with 0 findings but blocks nothing;
- harden-runner audits all 19 jobs and blocks none; its egress baseline is what block mode would need;
- three projects suppress DWARF038/DWARF044 in `NoWarn`, against the 2026-09-27 ruling;
- `.oss-scanner/Dockerfile` runs the suite with `|| echo WARNING`: stated, but the scanning agent is never told the
  suite may have been red.
