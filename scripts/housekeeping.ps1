#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# DwarfMapper LOCAL housekeeping — the heavy integrity checks kept OUT of CI on purpose (time/cost).
# Run before a release or a large push. Each stage fails fast.
#
#   pwsh scripts/housekeeping.ps1                 # suite + exhaustion + AOT execute
#   pwsh scripts/housekeeping.ps1 -SkipExhaustion # skip the ~6 min full power-set
#   pwsh scripts/housekeeping.ps1 -Mutation       # also run Stryker mutation testing (very slow)
#   pwsh scripts/housekeeping.ps1 -Heal           # regenerate AnalyzerReleases rows (self-heal) then test
#   pwsh scripts/housekeeping.ps1 -Coverage       # collect coverage during stage 1, enforce per-assembly floors
#   pwsh scripts/housekeeping.ps1 -ILVerify       # ILVerify the shipped runtime + a generated-consumer assembly
#   pwsh scripts/housekeeping.ps1 -Deep           # DWARF_DEEP=1 for stage 1: multiplied fuzz/property/torture counts
#   pwsh scripts/housekeeping.ps1 -BenchSmoke     # benchmark smoke (ShortRun) + allocation exact-pin gate (~7 min)
#   pwsh scripts/housekeeping.ps1 -Nightly        # EXACTLY what CI's nightly deep-test job runs: -Deep -Coverage
#                                                 # -ILVerify -BenchSmoke, exhaustion and AOT skipped. Mutation stays
#                                                 # behind -Mutation (its own cost class; CI runs those legs as own jobs).
#                                                 # Needs: dotnet-reportgenerator-globaltool + dotnet-ilverify.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipAot,
    [switch]$SkipExhaustion,
    [switch]$Mutation,
    # One or more leg names instead of all six; see the selector in the mutation stage.
    [string[]]$MutationLeg,
    [switch]$Heal,
    [switch]$Coverage,
    [switch]$ILVerify,
    [switch]$Deep,
    [switch]$BenchSmoke,
    [switch]$Nightly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# The R2 mandatory-raise band checks (invariant R2, Issues/round22/RESEARCH-97-PERCENT-GATES.md §3) live
# in their own function-only file so GateBandLogicTests can drive them against fake inputs in both
# failure directions without executing this script's stages.
. (Join-Path $PSScriptRoot 'gate-checks.ps1')

# -MutationLeg runs ONE leg through the mutation stage's exact block — the same fuse, the same four
# post-leg proofs — instead of the two and a half hours all six take together. It exists because a leg's
# FIRST pin must come from a housekeeping-driven run: a bare Invoke-StrykerLeg produces a score but runs
# neither Assert-MutantsWereTested (which is what tells a real score from a VACUOUS one) nor either
# decontamination sweep. Round 29's testing leg was pinned from such a run and had to be re-measured;
# this switch is so the next one does not have to be.
#
# Resolved HERE, before any stage runs, for the reason Assert-StrykerConfigSane states about thresholds:
# a typo should fail in milliseconds with its cause named, not after the restore, the build and the whole
# self-test suite have spent ten minutes earning the right to discover it.
$allLegs = @('generator', 'doc tooling', 'runtime', 'code fixes', 'pipeline', 'testing')
$legs = if ($MutationLeg) { $MutationLeg } else { $allLegs }
$unknown = @($legs | Where-Object { $allLegs -notcontains $_ })
if ($unknown.Count) {
    throw ('mutation: unknown -MutationLeg value(s) [' + ($unknown -join ', ') + ']. Known legs: ' +
           ($allLegs -join ', ') + '.')
}

# -Nightly is an AGGREGATE, not a new stage: it pins the exact switch set the CI deep-test job runs
# (.github/workflows/ci.yml), so a maintainer reproduces the nightly locally with one switch and the two
# cannot drift apart. It skips exhaustion and AOT (exhaustion is a default local stage, AOT has its own
# aot-trim-gate CI job) and deliberately does NOT imply -Mutation — the mutation legs are their own cost
# class and their own nightly CI jobs (the 'mutation' matrix).
if ($Nightly) {
    # -BenchSmoke rides the nightly per the T8 placement rule: its measured wall-clock (6:36/6:44 across
    # the two pin-verification runs, 12-core machine) is proportionate to the deep tier now that the
    # 44-minute ceiling is raised - it does not dwarf the mutation legs (~37 min aggregate) the nightly
    # already carries on sibling jobs.
    $Deep = $true; $Coverage = $true; $ILVerify = $true; $BenchSmoke = $true
    $SkipExhaustion = $true; $SkipAot = $true
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Coverage floors — per-assembly LINE coverage, percent. MEASURED, not aspired-to: each value is the
# full-suite Release measurement (coverlet XPlat collector merged by ReportGenerator) rounded DOWN to one
# decimal place ([math]::Floor(x*10)/10), so run-to-run float noise cannot flip the gate — that truncation
# is the only slack, no cushion is added on top. The gate is line coverage; branch coverage is printed as
# informational only. Raising a floor to a new measured value is the normal move; lowering one demands a
# written reason in the commit that lowers it.
#
# Re-measured 2026-08-22 (round-22 P4, fast tier, Release) at the commit that carries the raise — line / branch:
#   DwarfMapper 91.2/77.2 · Generator 93.7/87.4 · DocTooling 95.7/91.7 · CodeFixes 92.4/68.4 · Testing 83.2/82.2
# DwarfMapper rose 78.3 -> 91.2 by DENOMINATOR HONESTY, not new tests: P4 excluded the nine 0%-covered
# compile-time-only attribute classes (the one sanctioned [ExcludeFromCodeCoverage] category, 51 by-design-
# dead lines; 282/360 -> 282/309), each justified on the attribute and exactly pinned by
# RatchetInvariantScanTests. MapToAttribute (0/4) deliberately STAYS in the denominator — its ctor's
# defensive `?? Array.Empty` arm is research Q3's unruled category. DocTooling rose 90.7 -> 95.7 from P3's
# kill-list tests (34 NoCoverage mutants killed = covered lines grew) — the co-movement the plan predicted.
# Generator, CodeFixes and Testing line values measured unchanged to this decimal (Testing's branch moved
# 81.9 -> 82.2 with no code change — the R4 wobble exhibit; branch stays informational).
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Re-measured 2026-08-23 (post-reformat) — fast tier, Release, exact covered/coverable from
# TestResults/coverage-report/Summary.json, truncated with [math]::Floor(x*10)/10 as the rule above requires:
#   DwarfMapper 91.2/77.2 · Generator 93.4/87.9 · DocTooling 96.0/91.7 · CodeFixes 88.8/68.4 · Testing 82.7/82.6
# Exact covered/coverable behind those line values: DwarfMapper 281/308 = 91.2338 ·
# Generator 10080/10787 = 93.4458 · DocTooling 414/431 = 96.0557 · CodeFixes 238/268 = 88.8060 ·
# Testing 773/932 = 82.9399 (fast) and 82.7 (deep, the pinned one).
#
# THREE FLOORS ARE LOWERED. The written reason the rule demands: a repository-wide reformat expanded
# every single-line guard clause — `if (x) return;` — onto two lines. Measured: 314 such guards existed
# across src/ at fac40f3 and ZERO remain (CodeFixes 16, Generator 221, DwarfMapper 3, Testing 74,
# DocTooling 13). Where the guarded branch is not always taken, one covered line becomes a covered `if`
# plus an uncovered body, so coverable lines grow while covered lines do not. Expression-bodied members
# expanded to block bodies the same way. This is a DENOMINATOR effect from formatting, not lost testing:
# no test was deleted or disabled, and the suite is green at 8010 (fast) and 16122 (deep) with 0 failures.
# CodeFixes moves most (92.4 -> 88.8) because it is the smallest assembly and the most guard-dense.
#
# DocTooling is RAISED 95.7 -> 96.0 (measured 96.0557) — the normal move, locking in the improvement.
# Testing is pinned to 82.7, the DEEP-tier measurement, which is BELOW the fast-tier 82.9. The two tiers
# were measured separately and agree everywhere else to the printed decimal; pinning the minimum is what
# makes the gate hold under both `-Coverage` and `-Nightly` (-Deep -Coverage) rather than only the tier
# that happened to be measured.
#
# Re-measured 2026-08-26 (round 27) — BOTH tiers, and this time they agree to the printed decimal on every
# assembly, so the minimum and the deep-tier value are the same number and the caveat above is history
# rather than a live constraint:
#   DwarfMapper 91.5/79.5 · Generator 94.5/88.9 · DocTooling 96.3/92.1 · CodeFixes 96.2/88.6 · Testing 87.1/84.6
#
# Three floors move, all upward, each in the same commit as the work that earned it:
#
#   Generator  93.4 -> 94.5   the seam-stage coverage work
#   CodeFixes  88.8 -> 96.2   the code-fix leg's kill program, 43 tests
#   Testing    82.7 -> 87.1   the object-factory graph-shape tests
#
# The Testing number is the one worth reading twice. It was 81.0 — BELOW its floor — when this gate was
# first run in round 27, because merging the two object factories added 105 lines and no coverage run had
# happened since. All four public graph-shape builders (MakeSelfLoop, MakeTwoNodeCycle, MakeOwnerGraph,
# MakeDiamond) were executed by nothing. The floor did its job; it had simply not been asked.
#
# DwarfMapper (91.5 vs 91.2) and DocTooling (96.3 vs 96.0) are inside the 1.0 pp band, so they pass without
# a mandatory raise and are left alone — moving a floor by a third of a point is churn, not a ratchet.
#
# Re-measured 2026-09-02 (round-28 audit) — fast tier, Release, exact covered/coverable from
# TestResults/coverage-report/Summary.json, truncated as the rule requires:
#   DwarfMapper 91.5/79.5 · Generator 94.6/89.0 · DocTooling 96.3/92.1 · CodeFixes 96.2/88.6 · Testing 96.4/92.6
# Exact covered/coverable: DwarfMapper 291/318 · Generator 10832/11446 · DocTooling 420/436 ·
# CodeFixes 258/268 · Testing 780/809 = 96.4153.
#
#   Testing    87.1 -> 96.4   the GraphOracleComparer sensitivity tests (negative controls)
#
# Re-measured 2026-09-02 (round-28 patch coverage — Codecov flagged 28 lines of the merge; the tests that
# execute them moved the Generator a full point, so the band rule made this raise mandatory):
#   DwarfMapper 91.5/79.5 · Generator 95.5/89.6 · DocTooling 96.3/92.1 · CodeFixes 96.2/88.6 · Testing 96.4/92.6
# Exact covered/coverable: Generator 11042/11560 = 95.5190.
#
#   Generator  94.5 -> 95.5   Round28PatchCoverageTests (the 28 lines, plus the F27 CS0612 guard they found)
#
# Two things to read twice. First, the 87.1 above was NEVER MET: the tree at the commit that pinned it
# (cb14993) measures 704/809 = 87.02, and so did every later tree in both tiers, with a byte-identical
# covered-line set — so `-Coverage` and `-Nightly` threw at this gate on every run since 2026-08-26 and
# nothing behind stage 1b (exhaustion, AOT, ILVerify, BenchSmoke, mutation) ran through this script. The
# value was written a tenth above what the tool reports; the rule is the truncated measurement, nothing
# else. Second, what the 704 -> 780 covered lines were: every violation-reporting line of the graph oracle
# (TopologyCompare's one `violations.Add`, both FlattenGraphDiff violations, every CrossTypeCompare diff,
# ValueCompare's null and count arms), `TopologyPreserved`, and the field/dictionary walks — none executed
# by any test, because every consumer asserts `Count == 0`. An oracle nobody has seen fail grades nothing;
# tests/DwarfMapper.Testing.Tests/GraphOracleSensitivityTests.cs is the set of failures it now has to
# produce. Generator (94.6 vs 94.5), DwarfMapper and DocTooling stay inside the band and are left alone.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$coverageFloors = [ordered]@{
    'DwarfMapper'            = 91.2
    'DwarfMapper.Generator'  = 95.5
    'DwarfMapper.DocTooling' = 96.0
    'DwarfMapper.CodeFixes'  = 96.2
    'DwarfMapper.Testing'    = 96.4
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Stryker EXITS 0 when its `mutate` filter matches nothing. Every mutant comes back "Removed by mutate
# filter", no score is calculated, and `if ($LASTEXITCODE) { throw }` sees success — a leg that tested
# nothing passes the gate. That is not hypothetical: it is exactly what stryker-config.runtime.json did
# on 2026-08-16, because `mutate` globs resolve against the PROJECT directory and the file listed them
# repo-root-relative.
#
# So the exit code is necessary but not sufficient. Each leg must also prove it actually scored some
# mutants. Requires the "json" reporter, which all three configs declare.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-MutantsWereTested {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [Parameter(Mandatory)][datetime]$Since
    )
    $report = Get-ChildItem -Path (Join-Path $root 'StrykerOutput') -Recurse -Filter 'mutation-report.json' `
                            -ErrorAction SilentlyContinue |
              Where-Object { $_.LastWriteTime -ge $Since } |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $report) {
        throw "mutation ($Leg): no mutation-report.json was written for this run — the leg produced no report at all"
    }
    # Counted over the raw text rather than ConvertFrom-Json: the report embeds every mutated file's full
    # source, so it runs to tens of megabytes and parsing it buys nothing this check needs.
    $raw = Get-Content -Raw -LiteralPath $report.FullName
    $scoreable = ([regex]::Matches($raw, '"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"')).Count
    if ($scoreable -eq 0) {
        throw ("mutation ($Leg): 0 scoreable mutants — the run was VACUOUS and Stryker still exited 0. " +
               "Check that the config's 'mutate' globs are PROJECT-relative ('**/Name.cs'), not " +
               "repo-root-relative ('src/Proj/Name.cs'), which silently matches nothing.")
    }
    # ${Leg} braced, not $Leg: — a bare '$Leg:' parses as a SCOPE-qualified variable reference, not the
    # variable followed by a colon, and the script fails to parse at all.
    Write-Host "   ${Leg}: $scoreable scoreable mutants" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# The OTHER way a leg dies silently (H4): Stryker 4.16 refuses to run a config whose thresholds.break >
# thresholds.low ("Threshold low must be more than or equal to threshold break") — AND EXITS 0. That is
# exactly how the doctooling leg was unrunnable from 2026-08-19 until H1 noticed: T3 raised break to 83
# and left low at 80. Checked BEFORE the run so the mistake fails in milliseconds with its cause named,
# instead of surfacing as Assert-MutantsWereTested's "no report was written". Mirrored inline in the CI
# 'mutation' matrix job (.github/workflows/ci.yml); the two must stay in step.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-StrykerConfigSane {
    param([Parameter(Mandatory)][string]$ConfigFile)
    $thresholds = (Get-Content -Raw -LiteralPath (Join-Path $root $ConfigFile) |
                   ConvertFrom-Json).'stryker-config'.thresholds
    if ($null -eq $thresholds.break -or $null -eq $thresholds.low) {
        throw "mutation ($ConfigFile): thresholds.break/low missing - the config cannot prove it is runnable"
    }
    if ($thresholds.break -gt $thresholds.low) {
        throw ("mutation ($ConfigFile): thresholds.break ($($thresholds.break)) > thresholds.low " +
               "($($thresholds.low)) - Stryker 4.16 refuses to run this config AND EXITS 0, so the leg " +
               "would be silently dead. Whenever break moves, move low with it (NOTE 9 in " +
               "stryker-config.doctooling.json).")
    }
    Write-Host "   ${ConfigFile}: break $($thresholds.break) <= low $($thresholds.low) - runnable" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Allocation exact-pin gate (round-21 T8; the RESEARCH-97-PERCENT-GATES allocation-gate row). Allocated
# bytes per op from MemoryDiagnoser are DETERMINISTIC for a fixed SDK (verified 2026-08-21: two full
# smoke runs, all 41 benchmarks byte-identical), which is what makes an exact pin honest where a timing
# gate would violate the no-nondeterministic-oracle rule. The gate fails on ANY change to a pinned
# scenario - an unexplained INCREASE is a regression, an unexplained DECREASE is also a finding - and the
# only fix is a deliberate commit that re-measures and updates allocation-baseline.json with the reason.
# Competitor rows (Mapperly/Mapster/AutoMapper, Flat_Hand) are printed as informational context, never
# gated. Timing numbers from the smoke are NON-GATES (see SmokeConfig in benchmarks/.../Program.cs).
#
# A separate function so it can be exercised NEGATIVELY against an existing report + doctored baseline
# (the T5/H4 positive-AND-negative discipline) without paying for another ~7-minute smoke run.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-BenchAllocationsPinned {
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][string]$BaselinePath
    )
    if (-not (Test-Path $ReportPath)) {
        throw "bench smoke: no JSON report at $ReportPath - the run produced no parseable results"
    }
    $baseline = Get-Content -Raw -LiteralPath $BaselinePath | ConvertFrom-Json
    $report = Get-Content -Raw -LiteralPath $ReportPath | ConvertFrom-Json
    $failures = @()

    # SDK drift is the one legitimate silent-change vector for allocated bytes: same code, new SDK, new
    # numbers. Name it instead of letting it masquerade as a code regression.
    $sdk = (dotnet --version | Select-Object -First 1).Trim()
    if ($sdk -ne $baseline.measuredWith.sdk) {
        $failures += ("bench smoke: running SDK $sdk != baseline SDK $($baseline.measuredWith.sdk) - " +
                      "allocated bytes are deterministic PER SDK; re-measure and update " +
                      "allocation-baseline.json in the same commit as the SDK change.")
    }

    # BenchmarkRunner exits 0 even when individual benchmarks crash (NA rows) or a stray --filter shrinks
    # the suite, so 'every benchmark still executes' must be counted, not inferred from the exit code.
    $benchmarks = @($report.Benchmarks)
    if ($benchmarks.Count -ne $baseline.totalBenchmarks) {
        $failures += ("bench smoke: $($benchmarks.Count) benchmarks in the report, baseline pins " +
                      "$($baseline.totalBenchmarks) - a benchmark was added/removed or a filter shrank " +
                      "the suite; update the baseline deliberately with the reason.")
    }

    $measured = @{}
    foreach ($b in $benchmarks) {
        if ($null -eq $b.Statistics -or $null -eq $b.Memory) {
            $failures += ("bench smoke: $($b.Method) has no Statistics/Memory in the report - it " +
                          "CRASHED or was skipped, and BenchmarkDotNet still exited 0.")
            continue
        }
        $measured[$b.Method] = [long]$b.Memory.BytesAllocatedPerOperation
    }

    $pins = @($baseline.pins.PSObject.Properties)
    $unstableNames = @($baseline.unstable.PSObject.Properties.Name)
    foreach ($pin in $pins) {
        if (-not $measured.ContainsKey($pin.Name)) {
            # The vacuity guard: a renamed scenario must FAIL the gate, not silently fall out of it.
            $failures += "bench smoke: pinned scenario '$($pin.Name)' is missing from the report - the gate cannot see it"
            continue
        }
        $got = $measured[$pin.Name]
        $want = [long]$pin.Value
        if ($got -ne $want) {
            $direction = if ($got -gt $want) { "INCREASE (allocation regression)" }
                         else { "DECREASE (also a finding: something changed the emitted path)" }
            $failures += ("bench smoke: $($pin.Name) allocated $got B/op, pinned $want B/op - unexplained " +
                          "$direction. Exact-pin protocol: re-measure and update allocation-baseline.json " +
                          "with the reason in the same commit.")
        }
    }

    # Every DwarfMapper scenario must be accounted for - pinned, or listed unstable with its observed
    # variance. A new *_Dwarf benchmark cannot land ungated.
    foreach ($name in @($measured.Keys) | Where-Object { $_ -like '*_Dwarf' }) {
        if ($name -notin @($pins.Name) -and $name -notin $unstableNames) {
            $failures += ("bench smoke: '$name' is a DwarfMapper scenario with neither a pin nor an " +
                          "unstable entry - verify byte-stability across two runs and pin it in the " +
                          "commit that adds it.")
        }
    }

    foreach ($b in ($benchmarks | Sort-Object Method)) {
        if ($null -eq $b.Memory) { continue }
        $tag = if ($b.Method -in @($pins.Name)) { 'pin ' } else { 'info' }
        Write-Host ("   [{0}] {1}: {2} B/op" -f $tag, $b.Method, $b.Memory.BytesAllocatedPerOperation) -ForegroundColor DarkGray
    }
    if ($failures) { throw ($failures -join [Environment]::NewLine) }
    Write-Host "   allocation pins: $($pins.Count) scenarios exact-matched, $($benchmarks.Count)/$($baseline.totalBenchmarks) benchmarks executed" -ForegroundColor DarkGray
}

Push-Location $root
try {
    # ── Stage 0: locked-mode restore (round-22 S1) ───────────────────────────────────────────────────
    # The same repeatable-restore proof CI runs (Directory.Build.props flips RestoreLockedMode on CI=true;
    # ci.yml's explicit restores pass --locked-mode): every project must resolve EXACTLY its committed
    # packages.lock.json or this fails NU1004 — local housekeeping and CI cannot drift apart on what a
    # restore means. The ordinary local inner loop stays unlocked on purpose; after an INTENDED dependency
    # change, regenerate with `dotnet restore DwarfMapper.NET.sln --force-evaluate` and commit the lock
    # files with the change. NuGetAudit (level low, mode all) runs inside this restore, so a new advisory
    # against the pinned graph also fails here first, loudly, before any stage builds.
    Write-Host "== 0 Locked-mode restore (lock-file reproducibility + NuGet audit) ==" -ForegroundColor Cyan
    dotnet restore DwarfMapper.NET.sln --locked-mode --nologo
    if ($LASTEXITCODE) { throw "locked-mode restore failed - the resolved graph differs from the committed packages.lock.json files (or a NuGet audit advisory fired); see NU1004/NU19xx output above" }

    if ($Heal) {
        Write-Host "== self-heal: regenerate AnalyzerReleases rows ==" -ForegroundColor Cyan
        $env:DWARF_SELF_HEAL = '1'
        dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release --nologo `
            --filter "FullyQualifiedName~SelfHeal_AnalyzerReleases"
        Remove-Item Env:DWARF_SELF_HEAL
    }

    Write-Host "== 1/4 Full self-test suite ==" -ForegroundColor Cyan
    if ($Deep) {
        # One knob, one reader: tests/Shared/DeepTier.cs. Multiplies every registered fuzz/property/torture
        # population per its catalog entry; the fast tier (unset) is byte-for-byte the routine counts.
        $env:DWARF_DEEP = '1'
        Write-Host "   (deep tier: DWARF_DEEP=1)" -ForegroundColor DarkGray
    }
    $covDir = Join-Path $root 'TestResults/coverage'
    # --blame-hang is the H7-f runner-level backstop (Issues/ledgers/H7-timeout-dissection.md §4): a hung
    # testhost is dumped and killed with the offending test NAMED, instead of idling until someone kills
    # the run (or, in CI, until the job-level timeout eats the evidence). VSTest infrastructure, not test
    # semantics — nothing about pass/fail changes for a test that terminates. 5m is ~3x the longest
    # observed single-test runtime in this stage; the exhaustion stage (a single multi-minute test) and
    # the Stryker legs (per-mutant ceiling plays this role) deliberately do NOT carry it.
    $testArgs = @('--blame-hang', '--blame-hang-timeout', '5m')
    if ($Coverage) {
        # Collect during the SAME stage-1 run rather than in a second pass: the suite is the cost driver
        # (measured 2026-08-21: 74.5 s plain, 92.9 s collecting, both Release/--no-build), so folding the
        # collector in prices -Coverage at ~+18 s instead of a whole extra suite run. Stale cobertura
        # files from a previous run would merge silently and corrupt the floor comparison — wipe first
        # (the coverage analog of Assert-MutantsWereTested's -Since filter).
        if (Test-Path $covDir) { Remove-Item -Recurse -Force $covDir }
        $testArgs += @('--collect:XPlat Code Coverage', '--results-directory', $covDir)
    }
    dotnet test DwarfMapper.NET.sln -c Release --nologo @testArgs
    if ($Deep) { Remove-Item Env:DWARF_DEEP }
    if ($LASTEXITCODE) { throw "self-test suite failed" }

    if ($Coverage) {
        Write-Host "== 1b Coverage floors (install: dotnet tool install -g dotnet-reportgenerator-globaltool) ==" -ForegroundColor Cyan
        # TestResults/ is git-ignored, so both the raw cobertura files and the rendered report stay out of
        # the repo tree. The HTML report is for humans; the gate reads Summary.json.
        $reportDir = Join-Path $root 'TestResults/coverage-report'
        reportgenerator "-reports:$covDir/**/coverage.cobertura.xml" "-targetdir:$reportDir" `
            '-reporttypes:Html;JsonSummary;TextSummary' `
            ('-assemblyfilters:+' + ($coverageFloors.Keys -join ';+'))
        if ($LASTEXITCODE) { throw "coverage: ReportGenerator failed" }
        $summary = Get-Content -Raw -LiteralPath (Join-Path $reportDir 'Summary.json') | ConvertFrom-Json
        $failures = @()
        foreach ($name in $coverageFloors.Keys) {
            $asm = @($summary.coverage.assemblies | Where-Object { $_.name -eq $name })
            if (-not $asm) {
                # The vacuity guard — the same reason Assert-MutantsWereTested exists: a renamed or
                # dropped assembly must FAIL the gate, not silently fall out of it.
                $failures += "coverage: assembly '$name' is missing from the merged report - the gate cannot see it"
                continue
            }
            # Same rounding rule as the floors: truncate to one decimal, computed from the raw line
            # counts rather than trusting the report's own culture-formatted percentage strings.
            $measured = [math]::Floor($asm[0].coveredlines / $asm[0].coverablelines * 1000) / 10
            Write-Host ("   {0}: line {1}% (floor {2}%), branch {3}% (informational)" -f `
                $name, $measured, $coverageFloors[$name], $asm[0].branchcoverage) -ForegroundColor DarkGray
            # Both directions gate (invariant R2): below the floor is a regression, >= 1.0 pp above it is
            # a floor that stopped equalling the measurement - raise it in this commit.
            $bandFailure = Test-CoverageWithinBand -AssemblyName $name -Measured $measured -Floor $coverageFloors[$name]
            if ($bandFailure) { $failures += $bandFailure }
        }
        if ($failures) { throw ($failures -join [Environment]::NewLine) }
    }

    if (-not $SkipExhaustion) {
        Write-Host "== 2/4 Full exhaustion (DWARF_FUZZ_FULL=1, ~6 min) ==" -ForegroundColor Cyan
        $env:DWARF_FUZZ_FULL = '1'
        dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release --nologo `
            --filter "FullyQualifiedName~FeatureCombinationFuzzTests"
        Remove-Item Env:DWARF_FUZZ_FULL
        if ($LASTEXITCODE) { throw "exhaustion (full power-set) failed" }
    }

    if (-not $SkipAot) {
        Write-Host "== 3/4 AOT publish + EXECUTE (codegen correctness/determinism) ==" -ForegroundColor Cyan
        $rid = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
        # NativeAOT on Windows shells out to build/findvcvarsall.bat in the ILCompiler package. That script
        # locates the MSVC toolchain with vswhere by ABSOLUTE path, then CALLs vcvarsall.bat with stdout sent
        # to NUL - but NOT stderr. vcvarsall itself runs a bare `vswhere` (PATH-relative) for some components,
        # so when the VS Installer directory is not on PATH it prints
        #     'vswhere.exe' is not recognized as an internal or external command
        # to stderr. MSBuild's Exec captures stderr together with stdout, that line lands FIRST in the
        # captured output, and the targets read line 1 as the linker directory - producing
        # "The filename, directory name, or volume label is incorrect" and MSB3073, with the misleading
        # vswhere text quoted back. The toolchain was never missing; the probe's output was polluted.
        # Verified on this machine: with the directory on PATH, findvcvarsall.bat x64 emits exactly its two
        # expected lines and exits 0. Prepend it when it exists and vswhere is not already resolvable.
        if ($IsWindows) {
            $vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
            if ((Test-Path (Join-Path $vsInstaller 'vswhere.exe')) -and -not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
                $env:PATH = "$vsInstaller;$env:PATH"
                Write-Host "   PATH += $vsInstaller (vcvarsall's stderr would otherwise corrupt the linker probe)" -ForegroundColor DarkGray
            }
        }
        # PublishAot is deliberately NOT passed on the command line, for the reason ci.yml's aot-trim-gate
        # states verbatim and this stage did not inherit (I6): `-p:` sets a GLOBAL property, which MSBuild
        # flows down the whole project graph - including src/DwarfMapper.Generator and
        # src/DwarfMapper.CodeFixes, which target netstandard2.0 and can never be AOT-compiled. Under the
        # pinned SDK 10.0.101 that is NETSDK1207 on both of them and the stage dies before compiling
        # anything: a gate failing to START, not a gate catching a regression. AotBench's own csproj carries
        # <PublishAot>true</PublishAot>, and a project-level property applies to that project alone.
        dotnet publish samples/DwarfMapper.AotBench/DwarfMapper.AotBench.csproj -c Release -r $rid --nologo
        if ($LASTEXITCODE) { throw "AOT publish failed" }
        # Resolved through the RID, not by a bare -Recurse over bin/Release: that would happily pick up a
        # publish/ left by an earlier run under a different RID or TFM and gate on a stale binary.
        $publishGlob = Join-Path $root "samples/DwarfMapper.AotBench/bin/Release/*/$rid/publish"
        $publishDir = @(Resolve-Path -Path $publishGlob -ErrorAction SilentlyContinue)
        if ($publishDir.Count -ne 1) {
            throw ("AOT publish: expected exactly one publish/ directory matching '$publishGlob', found " +
                   "$($publishDir.Count). The stage cannot say which binary it would be gating on.")
        }
        $publishDir = $publishDir[0].Path
        # With nothing on the command line asserting AOT-ness, a framework-dependent publish would still
        # drop a runnable apphost here and sail through the behavioural gate below, leaving it green while
        # proving nothing. NativeAOT emits a native image with NO managed assembly and NO runtimeconfig.json,
        # so assert their ABSENCE before trusting the run. Mirrors ci.yml's "Assert the publish really was
        # NativeAOT" step; the two must stay in step.
        $managed = @(Get-ChildItem -LiteralPath $publishDir -File |
                     Where-Object { $_.Name -eq 'DwarfMapper.AotBench.dll' -or $_.Name -like '*.runtimeconfig.json' })
        if ($managed) {
            throw ("AOT publish: $publishDir holds a managed assembly or runtimeconfig.json (" +
                   ($managed.Name -join ', ') + "), so this was NOT a NativeAOT publish - the behavioural " +
                   "gate below would pass without proving anything. Check AotBench's <PublishAot>true</PublishAot>.")
        }
        $bin = Get-ChildItem -LiteralPath $publishDir -File -Filter "DwarfMapper.AotBench*" |
               Where-Object { $_.Extension -eq '' -or $_.Extension -eq '.exe' } |
               Select-Object -First 1
        if (-not $bin) { throw "AotBench native binary not found under $publishDir" }
        Write-Host "   NativeAOT confirmed: no managed assembly, no runtimeconfig.json in publish/" -ForegroundColor DarkGray
        Write-Host "Running native AOT binary: $($bin.FullName)"
        & $bin.FullName
        if ($LASTEXITCODE) { throw "AotBench reported AOT instability (exit $LASTEXITCODE)" }
    }

    if ($ILVerify) {
        Write-Host "== ILVerify (install: dotnet tool install -g dotnet-ilverify) ==" -ForegroundColor Cyan
        # Resolve against the newest installed net10 REF PACK — the contract surface the compiler resolved
        # against — rather than shared/Microsoft.NETCore.App. Verified empirically 2026-08-21: the ref pack
        # resolves everything both assemblies need (ilverify 10.0.11).
        # Resolution is cross-platform (T5: the nightly deep-test CI job runs this stage on a Linux
        # runner, where $env:ProgramFiles is empty): try the packs dir of every dotnet root we can name
        # (DOTNET_ROOT, wherever `dotnet` on PATH lives, Program Files on Windows), then the NuGet cache's
        # microsoft.netcore.app.ref — both layouts share the '<version>/ref/net10.0' shape. Newest by
        # [version], not by string sort (which ranks 10.0.9 above 10.0.11); prerelease suffixes stripped
        # before the cast so a rc/preview pack dir cannot crash the stage.
        $dotnetRoots = @()
        if ($env:DOTNET_ROOT) { $dotnetRoots += $env:DOTNET_ROOT }
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnetCmd) { $dotnetRoots += (Split-Path -Parent $dotnetCmd.Source) }
        if ($env:ProgramFiles) { $dotnetRoots += (Join-Path $env:ProgramFiles 'dotnet') }
        $packRoots = @($dotnetRoots | ForEach-Object { Join-Path $_ 'packs/Microsoft.NETCore.App.Ref' }) +
                     @(Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages/microsoft.netcore.app.ref')
        $refPack = $packRoots | Where-Object { Test-Path $_ } |
                   ForEach-Object { Get-ChildItem $_ -Directory } |
                   Where-Object { $_.Name -like '10.*' } |
                   Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
                   Select-Object -First 1
        if (-not $refPack) { throw "ilverify: no net10 ref pack found (searched: $($packRoots -join '; '))" }
        $refPackGlob = Join-Path $refPack.FullName 'ref/net10.0/*.dll'

        # The excuse list and both halves of its check live in scripts/gate-checks.ps1, beside the other
        # declared gate knowledge and where the sibling pwsh battery can drive them against fake inputs.
        $matchedExcuses = [System.Collections.Generic.List[string]]::new()

        # Targets: the SHIPPED runtime, and the Gallery — a generated-consumer assembly whose IL contains
        # generator-emitted mapping code including the blit/SIMD paths (21_BlittableSimd, 22_Reinterpret).
        # The Gallery's bin dir doubles as its own dependency root (DwarfMapper.dll etc. are copied there).
        # DwarfMapper.Testing added 2026-09-09 with the "all methods are verifiable" ruling: it is a SHIPPED
        # PACKAGE, so its IL reaches consumers exactly as the runtime's does, and it was in no target. The
        # generator and code-fix assemblies are deliberately still absent — they are netstandard2.0 and run in
        # the consumer's BUILD rather than their program, so verifying them needs a netstandard reference pack
        # and is a separate piece of work, named in Issues/round30/BLIND-INSTRUMENTS.md rather than assumed
        # covered.
        $targets = @(
            @{ Dll = 'src/DwarfMapper/bin/Release/net10.0/DwarfMapper.dll'; ExtraRefs = @() }
            @{ Dll = 'src/DwarfMapper.Testing/bin/Release/net10.0/DwarfMapper.Testing.dll'
               ExtraRefs = @('src/DwarfMapper/bin/Release/net10.0/*.dll') }
            @{ Dll = 'samples/DwarfMapper.Gallery/bin/Release/net10.0/DwarfMapper.Gallery.dll'
               ExtraRefs = @('samples/DwarfMapper.Gallery/bin/Release/net10.0/*.dll') }
        )
        foreach ($target in $targets) {
            $dll = Join-Path $root $target.Dll
            if (-not (Test-Path $dll)) { throw "ilverify: $($target.Dll) not built - build the solution Release first" }
            $refArgs = @('-r', $refPackGlob)
            foreach ($extra in $target.ExtraRefs) { $refArgs += @('-r', (Join-Path $root $extra)) }
            $out = @(& ilverify $dll @refArgs | ForEach-Object { $_.ToString() })
            $exit = $LASTEXITCODE
            $out | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
            $errors = @($out | Where-Object { $_ -match '\[IL\]:\s*Error' })
            foreach ($k in (Assert-IlVerifyFindingsExpected -Target $target.Dll -ErrorLines $errors `
                                                            -Known $script:IlVerifyKnownUnverifiable)) {
                if (-not $matchedExcuses.Contains($k)) { $matchedExcuses.Add($k) }
            }
            if ($exit -ne 0 -and $errors.Count -eq 0) {
                # Nonzero exit with no [IL] error lines means the tool itself failed (bad -r resolution,
                # missing file...) — never treat that as a pass.
                throw "ilverify: exited $exit for $($target.Dll) without reporting IL errors - tool failure"
            }
            $known = $errors.Count
            if ($known -gt 0) {
                Write-Host "   $($target.Dll): $known known-unverifiable finding(s), all matched to declared entries" -ForegroundColor DarkGray
            }
        }

        # The other half, and the one this stage was missing: an excuse no finding matched. Checked after
        # every target, because a Gallery-only entry legitimately matches nothing while the runtime
        # assembly is scanned.
        Assert-IlVerifyExcusesAllUsed -Known $script:IlVerifyKnownUnverifiable `
                                      -MatchedKeys $matchedExcuses.ToArray()
    }

    if ($BenchSmoke) {
        Write-Host "== Benchmark smoke + allocation exact-pin gate ==" -ForegroundColor Cyan
        # Stale results from a previous run would let the gate pass on yesterday's numbers - wipe first
        # (the benchmark analog of the coverage wipe above and Assert-MutantsWereTested's -Since filter).
        $benchResults = Join-Path $root 'BenchmarkDotNet.Artifacts/results'
        if (Test-Path $benchResults) { Remove-Item -Recurse -Force $benchResults }
        $env:DWARF_BENCH_SMOKE = '1'
        $benchStart = Get-Date
        dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks
        $benchExit = $LASTEXITCODE
        Remove-Item Env:DWARF_BENCH_SMOKE
        Write-Host ("   smoke wall-clock: {0:mm\:ss} (NON-GATE - timing from a smoke run proves nothing)" -f ((Get-Date) - $benchStart)) -ForegroundColor DarkGray
        if ($benchExit) { throw "bench smoke: benchmark run failed (exit $benchExit)" }
        Assert-BenchAllocationsPinned `
            -ReportPath (Join-Path $benchResults 'MapperBenchmarks-report-full.json') `
            -BaselinePath (Join-Path $root 'benchmarks/DwarfMapper.Benchmarks/allocation-baseline.json')
        # Round 25 T4. Reads the same report: no extra run, and it catches the one regression the
        # allocation gate structurally cannot see — a blit that stopped being emitted. The output stays
        # correct when that happens, so only a comparison against the scalar twin notices.
        Assert-BlitRatiosHold `
            -ReportPath (Join-Path $benchResults 'MapperBenchmarks-report-full.json') `
            -BaselinePath (Join-Path $root 'benchmarks/DwarfMapper.Benchmarks/blit-ratio-baseline.json')
    }

    if ($Mutation -or $MutationLeg) {
        Write-Host "== 4/4 Mutation testing (Stryker — install: dotnet tool install -g dotnet-stryker) ==" -ForegroundColor Cyan
        # ALL SIX configs sanity-checked up front — every leg's, not only the ones this invocation will
        # run: a break > low mistake in leg 6 should fail here, not after legs 1 to 5 have spent two hours
        # proving what was already known. (The sentence read "All three" against six calls; rounds 27 and
        # 29 added legs and moved neither, the same slip as three pins this round.)
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.doctooling.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.runtime.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.codefixes.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.pipeline.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.testing.json'
        if ($legs -contains 'generator') {
            $legStart = Get-Date
            # 60, not 30. MEASURED 2026-08-27 on this machine: the leg takes 31 minutes, so the old fuse was
            # cutting it off about a minute past the finish line and reporting a HANG. The 21-minute figure
            # in stryker-config.json was taken on a quiet box; a developer machine running an IDE is not one,
            # and CI already allows this leg 200 minutes for the same reason. A fuse exists to catch a leg
            # that will never finish -- sized so tightly that a busy machine trips it, it only teaches people
            # to distrust it.
            #
            # 90, not 60, since 2026-09-06 (round-29 Phase 2 gate). The mutant population grew 258 -> 391 during
            # the round-28 audit and the leg's measured wall-clock went with it: 51 minutes at 391 mutants on a
            # quiet 12-core machine (stryker-config.json, RE-MEASURED 2026-09-02). A 60-minute fuse leaves nine
            # minutes of headroom on a QUIET box and none at all on a busy one, which is the exact failure the
            # paragraph above was written about. The re-measurement that justifies this number is in the same
            # commit as the change, per invariant R1.
            $legExit = Invoke-StrykerLeg -Leg 'generator' -TimeoutMinutes 90
            if ($legExit) { throw "mutation score below break threshold (generator)" }
            Assert-MutantsWereTested -Leg 'generator' -Since $legStart
            Assert-LegScoreWithinBand -Leg 'generator' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
                -ConfigPath (Join-Path $root 'stryker-config.json') -Since $legStart
            Remove-PlantedMutants -Leg 'generator' -Root $root
            Assert-NoMutatedProductBinaries -Leg 'generator' -Root $root
        }

        # Stryker mutates ONE project per run, so the documentation pipeline needs its own config. Without
        # this leg the doc tests are trusted on the strength of being green — the evidence a vacuous test
        # also provides.
        if ($legs -contains 'doc tooling') {
            Write-Host "== 4/4b Mutation testing (DocTooling) ==" -ForegroundColor Cyan
            $legStart = Get-Date
            $legExit = Invoke-StrykerLeg -Leg 'doc tooling' -ConfigFile 'stryker-config.doctooling.json' -TimeoutMinutes 30
            if ($legExit) { throw "mutation score below break threshold (doc tooling)" }
            Assert-MutantsWereTested -Leg 'doc tooling' -Since $legStart
            Assert-LegScoreWithinBand -Leg 'doc tooling' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
                -ConfigPath (Join-Path $root 'stryker-config.doctooling.json') -Since $legStart
            Remove-PlantedMutants -Leg 'doc tooling' -Root $root
            Assert-NoMutatedProductBinaries -Leg 'doc tooling' -Root $root
        }

        # The SHIPPED runtime assembly. Unlike the attribute surface, registry members, the IDwarfMapper
        # facade and the exception types have no derivable case-space — no AttributeUsage to decompose, no
        # endpoint matrix to cross them against — so a surviving mutant is the only non-textual proof that a
        # case is untested.
        if ($legs -contains 'runtime') {
            Write-Host "== 4/4c Mutation testing (runtime assembly) ==" -ForegroundColor Cyan
            $legStart = Get-Date
            $legExit = Invoke-StrykerLeg -Leg 'runtime' -ConfigFile 'stryker-config.runtime.json' -TimeoutMinutes 30
            if ($legExit) { throw "mutation score below break threshold (runtime)" }
            Assert-MutantsWereTested -Leg 'runtime' -Since $legStart
            Assert-LegScoreWithinBand -Leg 'runtime' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
                -ConfigPath (Join-Path $root 'stryker-config.runtime.json') -Since $legStart
            Remove-PlantedMutants -Leg 'runtime' -Root $root
            Assert-NoMutatedProductBinaries -Leg 'runtime' -Root $root
        }

        # The CODE FIXES. Added round 27 after measuring what the other three legs do NOT cover: 15.3 % of
        # src/ sits inside any leg's globs, and this project sat at 88.8 % LINE coverage with 0 % mutation
        # coverage -- the exact combination mutation testing exists to interrogate, because it describes code
        # thoroughly EXECUTED by tests that may assert nothing about it.
        #
        # It is also the most literally user-facing code here: an analyzer reports a problem, and these
        # rewrite the consumer's own source to fix it. A code fix that produces subtly wrong code is worse
        # than one that fails loudly, and until this leg existed nothing proved the tests would notice.
        if ($legs -contains 'code fixes') {
            Write-Host "== 4/4d Mutation testing (code fixes) ==" -ForegroundColor Cyan
            $legStart = Get-Date
            $legExit = Invoke-StrykerLeg -Leg 'code fixes' -ConfigFile 'stryker-config.codefixes.json' -TimeoutMinutes 30
            if ($legExit) { throw "mutation score below break threshold (code fixes)" }
            Assert-MutantsWereTested -Leg 'code fixes' -Since $legStart
            Assert-LegScoreWithinBand -Leg 'code fixes' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
                -ConfigPath (Join-Path $root 'stryker-config.codefixes.json') -Since $legStart
            Remove-PlantedMutants -Leg 'code fixes' -Root $root
            Assert-NoMutatedProductBinaries -Leg 'code fixes' -Root $root
        }

        # ── 4/4e: the ROUND-27 MEMBER-RESOLUTION PHASES ──────────────────────────────────────────
        # The seam stage moved three giant methods into 13 new Pipeline/ files, and the brief asked for
        # the moved functions to be covered 'close to 100 % from mutation'. Line coverage and reach were
        # delivered; mutation coverage of them was 0 %, because no leg's globs named any of the new files.
        #
        # ONE AREA, not all thirteen. A leg scoped at the whole extraction generated 13,129 mutants, needed
        # to test 1,353, and after 158 minutes on 12 cores had not finished — while leaking idle vstest
        # hosts (26 of 32 alive with no CPU). It cannot run nightly either: the CI matrix budgets ~10x
        # measured, and 10x158 minutes is past the six-hour ceiling GitHub Actions puts on a job. So the
        # remaining ten files are covered one area per leg, each sized to complete
        # (Issues/round27/AUDIT-mutation-scope.md).
        if ($legs -contains 'pipeline') {
            Write-Host '== 4/4e Mutation testing (member-resolution phases) ==' -ForegroundColor Cyan
            $legStart = Get-Date
            # 90, not 60: MEASURED at 37 minutes, and the repo's precedent is ~2x measured (the generator leg
            # runs 31 and is fused at 60). A 60-minute fuse was the first guess here and a contended run blew
            # straight through it, reporting a HANG for a leg that was simply still working.
            $legExit = Invoke-StrykerLeg -Leg 'pipeline' -ConfigFile 'stryker-config.pipeline.json' -TimeoutMinutes 90
            if ($legExit) { throw 'mutation score below break threshold (pipeline)' }
            Assert-MutantsWereTested -Leg 'pipeline' -Since $legStart
            Assert-LegScoreWithinBand -Leg 'pipeline' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
                -ConfigPath (Join-Path $root 'stryker-config.pipeline.json') -Since $legStart
            Remove-PlantedMutants -Leg 'pipeline' -Root $root
            Assert-NoMutatedProductBinaries -Leg 'pipeline' -Root $root
        }

        # ── 4/4f: DwarfMapper.Testing's VERIFIERS ────────────────────────────────────────────────
        # Issues/round27/AUDIT-mutation-scope.md recorded this package at 0 % mutation coverage from round 27
        # onward, on the argument that it is test-only and never AOT-published. That argument covers the
        # FIXTURE BUILDERS. It does not cover the VERIFIERS: RoundTrip.Verify, LensLaws and StructuralComparer
        # are what a CONSUMER grades their own mappers with, so one that silently passes certifies a broken
        # map — the same "instrument trusted for coverage it cannot provide" shape round 29 recorded seven
        # times.
        #
        # What forced it: round 29 Phase 4 added 189 lines here (LensLaws + LensLawException), moving the
        # repository's mutation share 11.134 % -> 11.085 %. A real regression, and caused precisely by adding
        # consumer-facing verification code to the one package no leg could see.
        #
        # ONE AREA again: the five verifier files, not the package. ObjectFactoryV2 (326 lines) and
        # GraphOracleComparer (394 lines) are fixture machinery and stay the named follow-on.
        if ($legs -contains 'testing') {
            Write-Host '== 4/4f Mutation testing (testing-toolkit verifiers) ==' -ForegroundColor Cyan
            $legStart = Get-Date
            $legExit = Invoke-StrykerLeg -Leg 'testing' -ConfigFile 'stryker-config.testing.json' -TimeoutMinutes 30
            if ($legExit) { throw 'mutation score below break threshold (testing)' }
            Assert-MutantsWereTested -Leg 'testing' -Since $legStart
            Assert-LegScoreWithinBand -Leg 'testing' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
                -ConfigPath (Join-Path $root 'stryker-config.testing.json') -Since $legStart
            Remove-PlantedMutants -Leg 'testing' -Root $root
            Assert-NoMutatedProductBinaries -Leg 'testing' -Root $root
        }
    }

    Write-Host "HOUSEKEEPING PASSED" -ForegroundColor Green
}
finally {
    Pop-Location
}
