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
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$coverageFloors = [ordered]@{
    'DwarfMapper'            = 91.2
    'DwarfMapper.Generator'  = 94.5
    'DwarfMapper.DocTooling' = 96.0
    'DwarfMapper.CodeFixes'  = 96.2
    'DwarfMapper.Testing'    = 87.1
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

        # Known-unverifiable methods. NEVER a blanket suppression: each entry is ONE exact method whose
        # unverifiable IL is a deliberate source construct, named here. Anything else fails the stage.
        $knownUnverifiable = @{
            # samples/DwarfMapper.Gallery/18_SpanMap.cs lines 25-26: two `stackalloc` buffers in the
            # sample's HAND-WRITTEN Run() — localloc at IL_0003/IL_002F plus the cpblk initializer copy at
            # IL_001E — demonstrating the zero-alloc span overload. stackalloc is unverifiable IL by design.
            # The generator-emitted Mapper::Map(ReadOnlySpan<int>, Span<long>) itself verifies clean.
            'DwarfMapper.Gallery.Ex18.Example::Run()' = 'stackalloc (localloc + cpblk) in hand-written sample code'
        }
        # '(?!)' never matches: with an EMPTY known map, an empty pattern would match every line and turn
        # the filter into a blanket suppression — exactly the failure mode this stage exists to prevent.
        $knownPattern = if ($knownUnverifiable.Count -gt 0) {
            ($knownUnverifiable.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'
        } else { '(?!)' }

        # Targets: the SHIPPED runtime, and the Gallery — a generated-consumer assembly whose IL contains
        # generator-emitted mapping code including the blit/SIMD paths (21_BlittableSimd, 22_Reinterpret).
        # The Gallery's bin dir doubles as its own dependency root (DwarfMapper.dll etc. are copied there).
        $targets = @(
            @{ Dll = 'src/DwarfMapper/bin/Release/net10.0/DwarfMapper.dll'; ExtraRefs = @() }
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
            $unexpected = @($errors | Where-Object { $_ -notmatch $knownPattern })
            if ($unexpected) {
                throw ("ilverify: unexpected IL errors in $($target.Dll):`n" + ($unexpected -join "`n"))
            }
            if ($exit -ne 0 -and $errors.Count -eq 0) {
                # Nonzero exit with no [IL] error lines means the tool itself failed (bad -r resolution,
                # missing file...) — never treat that as a pass.
                throw "ilverify: exited $exit for $($target.Dll) without reporting IL errors - tool failure"
            }
            $known = $errors.Count - $unexpected.Count
            if ($known -gt 0) {
                Write-Host "   $($target.Dll): $known known-unverifiable finding(s), all matched to declared source constructs" -ForegroundColor DarkGray
            }
        }
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

    if ($Mutation) {
        Write-Host "== 4/4 Mutation testing (Stryker — install: dotnet tool install -g dotnet-stryker) ==" -ForegroundColor Cyan
        # All three configs sanity-checked up front: a break > low mistake in leg 3 should fail here, not
        # after legs 1 and 2 have spent half an hour proving what was already known.
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.doctooling.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.runtime.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.codefixes.json'
        $legStart = Get-Date
        $legExit = Invoke-StrykerLeg -Leg 'generator' -TimeoutMinutes 30
        if ($legExit) { throw "mutation score below break threshold (generator)" }
        Assert-MutantsWereTested -Leg 'generator' -Since $legStart
        Assert-LegScoreWithinBand -Leg 'generator' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
            -ConfigPath (Join-Path $root 'stryker-config.json') -Since $legStart
        Remove-PlantedMutants -Leg 'generator' -Root $root
        Assert-NoMutatedProductBinaries -Leg 'generator' -Root $root

        # Stryker mutates ONE project per run, so the documentation pipeline needs its own config. Without
        # this leg the doc tests are trusted on the strength of being green — the evidence a vacuous test
        # also provides.
        Write-Host "== 4/4b Mutation testing (DocTooling) ==" -ForegroundColor Cyan
        $legStart = Get-Date
        $legExit = Invoke-StrykerLeg -Leg 'doc tooling' -ConfigFile 'stryker-config.doctooling.json' -TimeoutMinutes 30
        if ($legExit) { throw "mutation score below break threshold (doc tooling)" }
        Assert-MutantsWereTested -Leg 'doc tooling' -Since $legStart
        Assert-LegScoreWithinBand -Leg 'doc tooling' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
            -ConfigPath (Join-Path $root 'stryker-config.doctooling.json') -Since $legStart
        Remove-PlantedMutants -Leg 'doc tooling' -Root $root
        Assert-NoMutatedProductBinaries -Leg 'doc tooling' -Root $root

        # The SHIPPED runtime assembly. Unlike the attribute surface, registry members, the IDwarfMapper
        # facade and the exception types have no derivable case-space — no AttributeUsage to decompose, no
        # endpoint matrix to cross them against — so a surviving mutant is the only non-textual proof that a
        # case is untested.
        Write-Host "== 4/4c Mutation testing (runtime assembly) ==" -ForegroundColor Cyan
        $legStart = Get-Date
        $legExit = Invoke-StrykerLeg -Leg 'runtime' -ConfigFile 'stryker-config.runtime.json' -TimeoutMinutes 30
        if ($legExit) { throw "mutation score below break threshold (runtime)" }
        Assert-MutantsWereTested -Leg 'runtime' -Since $legStart
        Assert-LegScoreWithinBand -Leg 'runtime' -StrykerOutputRoot (Join-Path $root 'StrykerOutput') `
            -ConfigPath (Join-Path $root 'stryker-config.runtime.json') -Since $legStart
        Remove-PlantedMutants -Leg 'runtime' -Root $root
        Assert-NoMutatedProductBinaries -Leg 'runtime' -Root $root

        # The CODE FIXES. Added round 27 after measuring what the other three legs do NOT cover: 15.3 % of
        # src/ sits inside any leg's globs, and this project sat at 88.8 % LINE coverage with 0 % mutation
        # coverage -- the exact combination mutation testing exists to interrogate, because it describes code
        # thoroughly EXECUTED by tests that may assert nothing about it.
        #
        # It is also the most literally user-facing code here: an analyzer reports a problem, and these
        # rewrite the consumer's own source to fix it. A code fix that produces subtly wrong code is worse
        # than one that fails loudly, and until this leg existed nothing proved the tests would notice.
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

    Write-Host "HOUSEKEEPING PASSED" -ForegroundColor Green
}
finally {
    Pop-Location
}
