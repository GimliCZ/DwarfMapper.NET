#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# DwarfMapper gate-band checks — the R2 "mandatory raise" half of the ratchet invariant
# (Issues/round22/RESEARCH-97-PERCENT-GATES.md §3), dot-sourced by scripts/housekeeping.ps1.
#
# The repo has always had the floor half of the ratchet: a measured value below the floor fails. R2 adds
# the FORCING half: a measured value a full quantum ABOVE the floor also fails, with a message that says
# to raise the floor to the measured value in the same commit. Improvement becomes irreversible by
# construction — slack cannot be banked, and every kill-list round mechanically drags the floor behind it.
#
# THE QUANTUM IS ONE UNIT OF THE GATE'S OWN PRECISION (R1: floors equal the measured value truncated to
# the gate's stated precision): 1.0 pp for the one-decimal coverage floors, 1 point for the integer
# Stryker `break` values. The research's Q5 default names "one mutant's score-worth" for a mutation leg;
# an integer `break` cannot express a sub-point raise, so the enforceable quantum is 1 point — at the
# current populations (201 / 284 / 113 scoreable) a single Killed mutant already crosses the next integer
# from each leg's measured score, so the two formulations coincide today. Deviation from the literal Q5
# wording stated here deliberately; the maintainer's Q5 answer supersedes it.
#
# These live in their own file, with no top-level execution, so the band logic is unit-testable: the
# SelfValidation suite (GateBandLogicTests) dot-sources this file alone and drives every function through
# fake inputs in both failure directions. Do not add script-level statements here.
#
# R4 note: every check in this file reads a DETERMINISTIC oracle — line-coverage counts and mutant-status
# counts. Branch percentages and wall-clocks stay informational (no gate on a nondeterministic oracle).
# ─────────────────────────────────────────────────────────────────────────────────────────────────────

# The one R2 raise message, verbatim in both gates; RatchetInvariantScanTests pins the phrase.
$script:RaiseFloorMessage = 'raise the floor to the measured value in this commit'

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Coverage: returns $null when the measured line coverage sits inside [floor, floor + 1.0), a failure
# STRING otherwise (the caller accumulates across assemblies, matching the housekeeping loop's style).
# Comparison in integer tenths so binary-float representations of one-decimal values cannot flip a band
# edge (78.3 stores as 78.2999…; multiplying and rounding first makes the arithmetic exact).
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Test-CoverageWithinBand {
    param(
        [Parameter(Mandatory)][string]$AssemblyName,
        [Parameter(Mandatory)][double]$Measured,
        [Parameter(Mandatory)][double]$Floor
    )
    $measuredTenths = [long][math]::Floor([math]::Round($Measured * 10, 6))  # truncate: the floors' own rule
    $floorTenths = [long][math]::Round($Floor * 10)
    $shownMeasured = $measuredTenths / 10.0

    if ($measuredTenths -lt $floorTenths) {
        return "coverage: $AssemblyName line coverage $shownMeasured% fell below the measured floor $Floor%"
    }
    if ($measuredTenths -ge ($floorTenths + 10)) {
        return ("coverage: $AssemblyName line coverage $shownMeasured% is >= 1.0 pp above the floor $Floor% - " +
                "the floor no longer equals the measurement (invariant R2): $script:RaiseFloorMessage " +
                "(scripts/housekeeping.ps1 `$coverageFloors, with the re-measurement's date in its comment).")
    }
    return $null
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Mutation: recomputes the leg's RAW score from the JSON report's status strings (same out-of-process
# regex idiom as Assert-MutantsWereTested — the report embeds every mutated file's source, so a full
# ConvertFrom-Json buys nothing). detected = Killed + Timeout, scoreable = Killed + Survived + Timeout +
# NoCoverage; Ignored never enters (ruling (b): the only Ignored population is the H7 progress guard).
# Throws below `break` (Stryker's own exit code fails first in a real run — this makes the check provable
# against a fake report, and catches a leg whose exit code lied) and throws on floor(score) >= break + 1
# with the R2 raise message. Passes inside the band.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-MutationScoreWithinBand {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][int]$Break
    )
    if (-not (Test-Path -LiteralPath $ReportPath)) {
        throw "mutation ($Leg): no report at $ReportPath - the R2 band check has nothing to measure"
    }
    $raw = Get-Content -Raw -LiteralPath $ReportPath
    $killed = ([regex]::Matches($raw, '"status"\s*:\s*"Killed"')).Count
    $timeout = ([regex]::Matches($raw, '"status"\s*:\s*"Timeout"')).Count
    $survived = ([regex]::Matches($raw, '"status"\s*:\s*"Survived"')).Count
    $noCoverage = ([regex]::Matches($raw, '"status"\s*:\s*"NoCoverage"')).Count
    $scoreable = $killed + $timeout + $survived + $noCoverage
    if ($scoreable -eq 0) {
        # Assert-MutantsWereTested owns the vacuity diagnosis; this is only the divide-by-zero guard.
        throw "mutation ($Leg): 0 scoreable mutants in $ReportPath - vacuous run, no score to band-check"
    }
    $detected = $killed + $timeout
    $score = $detected / $scoreable * 100
    $scoreFloored = [int][math]::Floor([math]::Round($score, 6))
    $shown = [math]::Round($score, 2)

    if ($score -lt $Break) {
        throw ("mutation ($Leg): RAW score $shown% ($detected/$scoreable) is below break $Break - " +
               "regression (check the Timeout bucket first: a Timeout counts as detected, so a " +
               "re-classification moves the score with no test having changed).")
    }
    if ($scoreFloored -ge ($Break + 1)) {
        throw ("mutation ($Leg): RAW score $shown% ($detected/$scoreable) floors to $scoreFloored, a full " +
               "point above break $Break - the floor no longer equals the measurement (invariant R2): " +
               "$script:RaiseFloorMessage (break AND low together - Stryker 4.16 refuses low < break with " +
               "exit 0 - citing this run in the config's comment).")
    }
    Write-Host "   ${Leg}: RAW score $shown% ($detected/$scoreable) within [$Break, $($Break + 1)) - floor equals measurement" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Newest mutation-report.json written since $Since, or $null. Mirrors the lookup inside
# Assert-MutantsWereTested (scripts/housekeeping.ps1) — kept separate so that function's contract, which
# three config comments cite by name, stays untouched.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Find-LatestMutationReport {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][datetime]$Since
    )
    return Get-ChildItem -Path $Root -Recurse -Filter 'mutation-report.json' -ErrorAction SilentlyContinue |
           Where-Object { $_.LastWriteTime -ge $Since } |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# The composition housekeeping's three mutation legs call: locate this run's report, read the config's
# `break`, band-check the raw score. Separate from the core assert so the core stays drivable by a fake
# report in the unit battery.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-LegScoreWithinBand {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [Parameter(Mandatory)][string]$StrykerOutputRoot,
        [Parameter(Mandatory)][string]$ConfigPath,
        [Parameter(Mandatory)][datetime]$Since
    )
    $report = Find-LatestMutationReport -Root $StrykerOutputRoot -Since $Since
    if (-not $report) {
        throw "mutation ($Leg): no mutation-report.json since $Since - the R2 band check has nothing to measure"
    }
    $configBreak = (Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json).'stryker-config'.thresholds.break
    Assert-MutationScoreWithinBand -Leg $Leg -ReportPath $report.FullName -Break ([int]$configBreak)
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# I4 — the DECONTAMINATION sweep, widened from "the leg's own target DLL" to every product assembly in
# every test bin.
#
# Stryker has no test sandbox and backs up only the files it OVERWRITES. The analyzer-referencing test
# projects reference DwarfMapper.Generator with ReferenceOutputAssembly="false", so their bins hold no
# pre-run copy of it — Stryker plants a MUTATED DwarfMapper.Generator.dll there during the mutate/compile
# phase, no `*.stryker-unchanged` marker is written beside it, restore never touches it, and
# RepoWriteGuard's leftover-backup signal structurally cannot fire. P5 found six such bins after a run
# that was otherwise clean (ConsumerTests/CleanCorpus, ConsumerTests/Host, CorpusTests, DifferentialTests,
# IntegrationTests, Testing.Tests). The hazard is not the leftover file: it is the NEXT incremental build,
# which keeps the newer mutant, after which those suites exercise a mutated generator and go silently
# green.
#
# `git diff --exit-code` cannot see this (bin/ is git-ignored) and neither can the leg's score. So the
# sweep is a content scan: the reference set of product assembly NAMES comes from what src/ actually
# builds, and every file of one of those names under tests/**/bin must not carry Stryker's marker.
#
# WHY THIS AND NOT RepoWriteGuard (the in-task ruling, recorded with its alternative): RepoWriteGuard is a
# WRITE-time guard inside the test process — it can refuse a write the tests themselves perform, which is
# how H1's doc-overwrite was closed. It is structurally the wrong instrument here: Stryker plants these
# DLLs from OUTSIDE any test process, before the tests run, so there is no write for the guard to
# intercept. The sweep belongs where the other post-leg proofs live (Assert-MutantsWereTested,
# Assert-LegScoreWithinBand) — after the leg, reading the tree. T3-H1 NOTE 3 is that precedent.
#
# It FAILS rather than deletes, deliberately: silently deleting the evidence would turn a Stryker
# behaviour change into a no-op and leave the next reader with nothing to read. The message names every
# offending file and the remedy.
#
# R4/H7 clean: the oracle is file CONTENT, never a timestamp and never a clock. P5's original detection
# used "timestamped inside the run's mutate/compile phase", which is exactly the wall-clock oracle H7
# forbids — a slow run, a clock skew or a re-run would both miss mutants and invent them.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Remove-PlantedMutants — the "Delete the listed files and rebuild" remedy that
# Assert-NoMutatedProductBinaries prescribes, performed instead of merely instructed.
#
# WHY THIS EXISTS. Stryker backs up only the files it OVERWRITES. A test project that references the
# generator as an ANALYZER (ReferenceOutputAssembly=false, no CopyLocal) gets a mutant planted with no
# *.stryker-unchanged beside it, so Stryker's own restore cannot put it back. Every local
# `housekeeping.ps1 -Mutation` therefore ends with mutated product assemblies under tests/**/bin, and the
# assertion fires EVERY time. CI never noticed because each nightly leg runs in a throwaway container.
#
# WHY THE ASSERTION STAYS, AND IS NOT MADE VACUOUS BY THIS. This function deletes; the assertion that
# follows still has to pass on its own terms. It fails if anything mutated SURVIVES the delete - a locked
# file, a copy outside tests/**/bin, or a mutant carrying a backup that restore should have handled and
# did not. What changes is that the expected, well-understood residue no longer stops the run; what does
# not change is that an unexpected mutated binary still fails the gate loudly.
#
# The deleted files are BUILD OUTPUT of an analyzer-only reference; the next build restores them. Nothing
# in src/ or tests/ source is touched. Every removal is printed, because a silent cleanup is how a real
# contamination would hide.
function Remove-PlantedMutants {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [Parameter(Mandatory)][string]$Root
    )
    $marker = 'Stryker'
    $binSegment = [System.IO.Path]::DirectorySeparatorChar + 'bin' + [System.IO.Path]::DirectorySeparatorChar
    $srcBin = Join-Path $Root 'src'
    $productNames = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@(Get-ChildItem -Path $srcBin -Recurse -File -Filter 'DwarfMapper*.dll' -ErrorAction SilentlyContinue |
                    Where-Object { $_.FullName.Contains($binSegment, [System.StringComparison]::Ordinal) } |
                    ForEach-Object { $_.Name }),
        [System.StringComparer]::OrdinalIgnoreCase)

    $removed = 0
    Get-ChildItem -Path (Join-Path $Root 'tests') -Recurse -File -Filter 'DwarfMapper*.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.Contains($binSegment, [System.StringComparison]::Ordinal) -and
                       $productNames.Contains($_.Name) } |
        ForEach-Object {
            $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
            $text = [System.Text.Encoding]::ASCII.GetString($bytes)
            if ($text.Contains($marker)) {
                Write-Host "   ${Leg}: removing planted mutant $($_.FullName)" -ForegroundColor DarkGray
                Remove-Item -LiteralPath $_.FullName -Force
                $removed++
            }
        }
    if ($removed -gt 0) {
        Write-Host ("   ${Leg}: removed $removed planted mutant(s) (analyzer-only references Stryker " +
                    "cannot restore); the next build restores them") -ForegroundColor DarkGray
    }
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Blit ratio gate (round 25 T4). Reads the SAME smoke report the allocation gate reads and checks that
# each blit emission still beats its scalar twin.
#
# This is a STRUCTURAL check wearing a stopwatch. It does not measure performance and does not assert any
# absolute time - which is what keeps it honest against the house rule that smoke TIMINGS are non-gates.
# It answers one question: is the fast path still being emitted? A blit that stopped being emitted
# collapses its pair to roughly 1.0x. Nothing else in the suite would catch that, because the mapping
# stays CORRECT either way - which is exactly what makes a performance regression silent.
#
# A separate function so it can be driven against a doctored report without paying for a ~7-minute run.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-BlitRatiosHold {
    param(
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][string]$BaselinePath
    )
    if (-not (Test-Path $ReportPath)) {
        throw "blit ratio: no JSON report at $ReportPath - the run produced no parseable results"
    }
    $baseline = Get-Content -Raw -LiteralPath $BaselinePath | ConvertFrom-Json
    $report = Get-Content -Raw -LiteralPath $ReportPath | ConvertFrom-Json
    $failures = @()

    $mean = @{}
    foreach ($b in @($report.Benchmarks)) {
        if ($null -ne $b.Statistics) { $mean[$b.Method] = [double]$b.Statistics.Mean }
    }

    foreach ($pair in @($baseline.pairs.PSObject.Properties)) {
        $fast = $pair.Value.fast
        $scalar = $pair.Value.scalar
        # Vacuity guard: a renamed or deleted scenario must FAIL the gate, never fall silently out of it.
        if (-not $mean.ContainsKey($fast)) {
            $failures += "blit ratio: '$($pair.Name)' fast arm '$fast' is missing from the report - the gate cannot see it"
            continue
        }
        if (-not $mean.ContainsKey($scalar)) {
            $failures += "blit ratio: '$($pair.Name)' scalar arm '$scalar' is missing from the report - the gate cannot see it"
            continue
        }
        if ($mean[$fast] -le 0) {
            $failures += "blit ratio: '$fast' reported a non-positive mean - it crashed or was skipped"
            continue
        }
        $ratio = $mean[$scalar] / $mean[$fast]
        if ($ratio -lt $baseline.minRatio) {
            $failures += ("blit ratio: $($pair.Name) is {0:F2}x, floor is {1:F2}x. The blit is no longer " -f $ratio, $baseline.minRatio) +
                          "beating its scalar twin, which usually means the fast path stopped being EMITTED " +
                          "for this shape - check the proof gate before assuming the machine is noisy."
        }
        else {
            Write-Host ("   {0}: {1:F2}x (floor {2:F2}x)" -f $pair.Name, $ratio, $baseline.minRatio) -ForegroundColor DarkGray
        }
    }

    if ($failures.Count) { throw ($failures -join "`n") }
    Write-Host "   blit ratio gate: every pinned pair still beats its scalar twin" -ForegroundColor Green
}


# -----------------------------------------------------------------------------------------------------
# Stryker launcher — correct reporters for the context, and a HARD FUSE so a leg can never hang forever.
#
# THE HANG, DIAGNOSED 2026-08-24. All three stryker-config*.json files request the `progress` reporter,
# which draws a live console progress bar using ANSI cursor control. Run interactively that is fine, and six
# runs on 2026-08-23 completed normally. Run from a DETACHED process with redirected stdout there is no
# console to address: Stryker created its output directory and then blocked forever, consuming 0.1 s of CPU
# across 17 processes in 12 seconds. Two attempts hung identically, and disabling MSBuild node reuse made no
# difference -- it was never the build.
#
# So: choose the reporter from the context. Redirected output gets the non-interactive reporters, which emit
# plain lines and need no cursor. The config files keep `progress` for the interactive case.
#
# AND a fuse regardless of cause. A gate that can hang indefinitely is worse than one that fails: a failure
# is information, a hang is a machine occupied all night with nothing to show. If the deadline passes the
# process tree is killed and the leg reports WHY, including what to try next.
# -----------------------------------------------------------------------------------------------------
function Invoke-StrykerLeg {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [string]$ConfigFile,
        [int]$TimeoutMinutes = 30
    )

    $stArgs = @()
    if ($ConfigFile) { $stArgs += @('--config-file', $ConfigFile) }

    # --- msbuild-path: without it NO leg runs on a machine that has VS Build Tools -------------------
    # Stryker asks Buildalyzer for an MSBuild and gets the VS 2022 Build Tools one, which predates .NET 10
    # and cannot resolve its SDK: every project fails with MSB4236 ('the specified SDK Microsoft.NET.Sdk
    # could not be found') before a single mutant is created. `dotnet build` is unaffected, which is why the
    # rest of this repository builds happily while all four legs are dead.
    #
    # Pointed at the SDK's own MSBuild, Stryker shells out to `dotnet build` instead and the build succeeds.
    # Guarded on existence rather than hardcoded: on a runner with no VS Build Tools, Stryker already picks
    # the right one and there is nothing to correct.
    $sdkMsBuild = Join-Path (Split-Path -Parent (Get-Command dotnet).Source) `
                            'sdk' | Join-Path -ChildPath (& dotnet --version).Trim() |
                  Join-Path -ChildPath 'MSBuild.dll'
    if (Test-Path $sdkMsBuild) {
        # QUOTED. Start-Process joins -ArgumentList with spaces and quotes nothing, so the default install
        # path splits at 'Program Files' and Stryker rejects the tail as an unknown command -- a one-second
        # failure that looks nothing like a path problem.
        $stArgs += @('--msbuild-path', ('"' + $sdkMsBuild + '"'))
    }

    # --- configuration: Release, and NOT only for speed ---------------------------------------------
    # Stryker defaults to Debug. Two reasons this leg does not:
    #
    #   * An IDE holds the Debug output. Rider loads this repository's own generator as an analyzer, which
    #     locks src/DwarfMapper.Generator/bin/Debug/.../DwarfMapper.Generator.dll; Stryker's build then dies
    #     with MSB3021 ('cannot copy ... because it is being used by another process'). Every other build in
    #     this repository is Release, which is why nobody noticed. A gate that only runs when the maintainer
    #     closes their editor is a gate that does not run.
    #   * Release is what everything else here certifies -- CI, the golden manifest, every dotnet test in
    #     these scripts. Mutating a Debug build to grade a Release-validated suite is a mismatch.
    #
    # Behaviourally identical, and that was checked rather than assumed: src/ contains no '#if DEBUG', no
    # Debug.Assert, and no configuration-conditional DefineConstants, so no leg's score can move because of
    # this. If any of those three ever appear, this line stops being free.
    $stArgs += @('--configuration', 'Release')

    # --- concurrency: DELIBERATELY NOT SET, and this comment is the correction ----------------------
    # This launcher briefly raised concurrency to cores-2, on the theory that Stryker's default of half the
    # logical processors was leaving the machine idle. MEASURED, and wrong: the generator leg, whose recorded
    # time is 21 minutes at the default, then blew through its 30-minute fuse without finishing.
    #
    # The default is not conservatism, it is the right answer. Stryker's workers are xunit processes that
    # parallelise INTERNALLY, so the two settings multiply and the PRODUCT is what lands on the CPU. Raising
    # Stryker's half over-subscribes harder, and both visible symptoms get worse: wall-clock, and mutants
    # classified Timeout that are not slow at all -- noise that reads as a detection.
    #
    # So nothing is passed and the tool's own default stands. Anyone tempted again should measure the
    # generator leg first; it has enough static mutants to make the difference obvious.

    # [Console]::IsOutputRedirected is the honest test: it is false in a terminal and true under a pipe,
    # a file redirect, or a detached task -- exactly the cases where `progress` has nothing to draw on.
    if ([Console]::IsOutputRedirected) {
        Write-Host "   non-interactive stdout detected - using 'dots' instead of the 'progress' reporter" -ForegroundColor DarkGray
        $stArgs += @('--reporter', 'dots', '--reporter', 'html', '--reporter', 'json')
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    # Start-Process rather than a bare call so the fuse has something to kill: a plain invocation gives back
    # no handle, and a fuse that cannot kill what it timed out on is decoration.
    #
    # Stdin is an EMPTY FILE, not 'NUL'. -RedirectStandardInput resolves its argument as a PATH, so 'NUL'
    # becomes <repo>/NUL, which does not exist, and Start-Process throws before anything launches -- the fuse
    # then fires on a null handle and blames the timeout for a launch failure. An empty file is the portable
    # equivalent, and it closes the other way to wait forever: a console prompt nobody is there to answer.
    $stdin = Join-Path ([System.IO.Path]::GetTempPath()) 'dwarf-stryker-stdin.txt'
    Set-Content -LiteralPath $stdin -Value '' -NoNewline
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList (@('stryker') + $stArgs) `
        -NoNewWindow -PassThru -RedirectStandardInput $stdin

    if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        try { taskkill /PID $proc.Id /T /F 2>&1 | Out-Null } catch { }
        throw ("mutation leg '$Leg' HUNG - killed after $TimeoutMinutes min (no exit, no score).`n" +
               "  This is the round-26 hang: the 'progress' reporter needs a console and blocks without one.`n" +
               "  The launcher already swaps it out when stdout is redirected, so if you are seeing this the`n" +
               "  cause is something else - run the leg interactively to see where it stops:`n" +
               "    dotnet stryker" + $(if ($ConfigFile) { " --config-file $ConfigFile" } else { "" }))
    }

    $sw.Stop()
    Write-Host ("   leg '$Leg' finished in {0:mm\:ss}" -f $sw.Elapsed) -ForegroundColor DarkGray
    return $proc.ExitCode
}

function Assert-NoMutatedProductBinaries {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [Parameter(Mandatory)][string]$Root
    )
    $marker = 'Stryker'

    # Latin1 is byte-preserving, so this is a byte scan written as a string search: metadata strings in a
    # managed assembly are UTF-8 in the #Strings heap, and Stryker's injected MutantControl names land
    # there verbatim. Never Get-Content's default decoding, which would mangle bytes it cannot decode.
    $carriesMarker = {
        param([string]$Path)
        [System.Text.Encoding]::Latin1.GetString([System.IO.File]::ReadAllBytes($Path)).Contains(
            $marker, [System.StringComparison]::Ordinal)
    }

    $binSegment = [System.IO.Path]::DirectorySeparatorChar + 'bin' + [System.IO.Path]::DirectorySeparatorChar

    # 1. The reference set: the assemblies src/ actually produces. Taking the names from the tree rather
    #    than hard-coding them means a new product project is swept the day it first builds.
    $originals = @(Get-ChildItem -Path (Join-Path $Root 'src') -Recurse -File -Filter 'DwarfMapper*.dll' `
                                 -ErrorAction SilentlyContinue |
                   Where-Object { $_.FullName.Contains($binSegment, [System.StringComparison]::Ordinal) })
    if ($originals.Count -eq 0) {
        throw ("decontamination ($Leg): no product assembly found under src/**/bin - the sweep has no " +
               "reference set and would pass by looking at nothing. Build the solution before the leg.")
    }

    # 2. The scanner's own premise, checked rather than assumed (the Assert-MutantsWereTested lesson): if a
    #    clean src build already carries the marker it stops discriminating, and every later green is
    #    meaningless. Note this is also why the scan is restricted to PRODUCT assembly names - the test
    #    assemblies legitimately contain the literal "Stryker" in their own source and always match.
    $dirtyOriginals = @($originals | Where-Object { & $carriesMarker $_.FullName })
    if ($dirtyOriginals.Count -gt 0) {
        throw ("decontamination ($Leg): the src-built ORIGINALS already match '$marker' (" +
               (($dirtyOriginals | ForEach-Object { $_.FullName }) -join ', ') + "). The marker no longer " +
               "discriminates a mutant from a clean build, so this sweep cannot prove anything - either " +
               "src/ is itself contaminated (rebuild it) or the marker needs replacing.")
    }

    $productNames = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@($originals | ForEach-Object { $_.Name }), [System.StringComparer]::OrdinalIgnoreCase)

    # 3. Every copy of one of those assemblies anywhere under tests/**/bin - not just beside a
    #    *.stryker-unchanged backup, which is precisely the set Stryker never wrote a backup for.
    $planted = @(Get-ChildItem -Path (Join-Path $Root 'tests') -Recurse -File -Filter 'DwarfMapper*.dll' `
                               -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName.Contains($binSegment, [System.StringComparison]::Ordinal) -and
                                $productNames.Contains($_.Name) })
    if ($planted.Count -eq 0) {
        throw ("decontamination ($Leg): not one product assembly was found under tests/**/bin. The test " +
               "projects reference the product, so an empty scan means the layout moved and the sweep is " +
               "looking at nothing - a vacuous pass, which is the failure mode this check exists to deny.")
    }

    $offenders = @($planted | Where-Object { & $carriesMarker $_.FullName })
    if ($offenders.Count -gt 0) {
        throw ("decontamination ($Leg): " + $offenders.Count + " MUTATED product assembly/assemblies " +
               "survived the leg under tests/**/bin:" + [Environment]::NewLine +
               (($offenders | ForEach-Object { '  ' + $_.FullName }) -join [Environment]::NewLine) +
               [Environment]::NewLine +
               "Stryker backs up only files it OVERWRITES, so an analyzer-only reference (no CopyLocal, no " +
               "pre-run copy) gets a mutant planted with NO *.stryker-unchanged marker and restore never " +
               "touches it. The next incremental build keeps the newer mutant and those suites then " +
               "exercise a mutated product, silently green. Delete the listed files and rebuild.")
    }

    Write-Host "   ${Leg}: $($planted.Count) product assemblies under tests/**/bin, none mutated" -ForegroundColor DarkGray
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Package-size ceiling (round-23 S6; the RESEARCH-97-PERCENT-GATES "package/binary size ratchet" row).
#
# The shipped .nupkg has a MEASURED ceiling in whole KB, and the ceiling equals the measurement truncated
# (invariant R1: the gate's stated precision is the KB, and no cushion is added on top). A package over
# its ceiling fails; the only fix is a commit that re-measures and moves the number WITH the reason,
# exactly as $coverageFloors and allocation-baseline.json are moved.
#
# WHY A SIZE GATE AT ALL, given ApiCompat and PublicAPI.Shipped/Unshipped already guard the SURFACE:
# because a surface check cannot see what is not surface. An accidentally embedded resource, a dependency
# that started shipping into lib/, a second copy of an analyzer - all of those leave the public API
# identical and the package fatter. Size is the cheap secondary signal for exactly that class.
#
# DELIBERATELY ONE-SIDED, unlike the R2 coverage/mutation band and unlike the two-directional allocation
# pins. A package that SHRINKS is not a finding the way a smaller allocation is: allocated bytes are a
# behavioural fact whose unexplained movement means the code changed, while package size has no
# correctness meaning at all. The round-23 plan's S6 row says "raise-only-with-re-measure" and this
# follows it. If the maintainer later wants the forcing direction too, the band shape is one line away -
# Test-CoverageWithinBand above is the template.
#
# NOTE THE TIGHTNESS, so nobody is surprised by the first red: at the measured 247 KB the headroom to the
# ceiling is ~530 bytes. That is what "the ceiling equals the measurement at the gate's stated precision"
# costs, and it is the same bargain the one-decimal coverage floors and the exact allocation pins make.
# Any generator or runtime change that adds half a kilobyte of IL is expected to re-measure this number
# in its own commit; the failure message says so.
#
# R4 clean: the oracle is a file's length in bytes. No clock, no percentage, no sampling.
#
# MEASURED 2026-08-22, Windows, SDK 10.0.101, at commit 81c4ace (the round-23 branch tip when S4 and S6
# were written), CI=true, from a `git clean -xdf` tree, two independent packs each:
#   DwarfMapper.1.0.2-rc.1.nupkg          253,420 B and 253,421 B  -> floor(253421/1024) = 247 KB
#   DwarfMapper.Testing.1.0.2-rc.1.nupkg   48,508 B and  48,508 B  -> floor( 48508/1024) =  47 KB
# (The one-byte wobble is NuGet's random .psmdcp part name, not build output - scripts/repro-pack-check.py
# measured exactly that and its header records it.)
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$script:PackageSizeCeilingsKb = [ordered]@{
    'DwarfMapper'         = 247
    'DwarfMapper.Testing' = 47
}

function Assert-PackageSizeWithinCeiling {
    param(
        [Parameter(Mandatory)][string]$PackageDir
    )
    if (-not (Test-Path -LiteralPath $PackageDir)) {
        throw "package size: no package directory at $PackageDir - the ceiling gate has nothing to measure"
    }

    # .snupkg is deliberately NOT ceilinged: symbols are not the shipped consumer payload, and their size
    # tracks debug-info settings rather than what a consumer downloads. They are printed, not gated.
    $packages = @(Get-ChildItem -LiteralPath $PackageDir -File -Filter '*.nupkg')
    if ($packages.Count -eq 0) {
        throw ("package size: not one .nupkg under $PackageDir. A ceiling check over an empty set passes " +
               "by looking at nothing, which is the failure mode this guard exists to deny - run " +
               "dotnet pack into this directory first.")
    }

    $failures = @()
    $matched = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($id in $script:PackageSizeCeilingsKb.Keys) {
        # <id>.<version>.nupkg, where the version starts with a digit - so DwarfMapper's pattern cannot
        # also swallow DwarfMapper.Testing's package and report the wrong file's size against the wrong
        # ceiling. The version itself is not pinned here; Directory.Build.props owns it, CI overrides it.
        $hits = @($packages | Where-Object {
            $_.Name -like "$id.*.nupkg" -and
            $_.Name.Substring($id.Length + 1, 1) -match '^[0-9]$'
        })
        if ($hits.Count -eq 0) {
            # The vacuity guard the allocation pins taught: a renamed or no-longer-produced package must
            # FAIL the gate, never silently fall out of it.
            $failures += ("package size: '$id' has a ceiling of $($script:PackageSizeCeilingsKb[$id]) KB " +
                          "but no $id.<version>.nupkg was produced into $PackageDir - the gate cannot see " +
                          "it. Either the package stopped shipping (delete the ceiling in the same commit, " +
                          "with the reason) or the pack step is broken.")
            continue
        }
        if ($hits.Count -gt 1) {
            $failures += ("package size: $($hits.Count) packages match $id.<version>.nupkg in " +
                          "$PackageDir (" + (($hits | ForEach-Object { $_.Name }) -join ', ') + "). Pack " +
                          "into a clean directory - the gate must not have to guess which one ships.")
            continue
        }

        $file = $hits[0]
        [void]$matched.Add($file.Name)
        $ceiling = [int]$script:PackageSizeCeilingsKb[$id]
        $kb = [int][math]::Floor($file.Length / 1024)
        if ($kb -gt $ceiling) {
            $failures += ("package size: $($file.Name) is $kb KB ($($file.Length) bytes), above the " +
                          "measured ceiling $ceiling KB. Exact-ceiling protocol (invariant R1): if the " +
                          "growth is intended, re-measure and raise PackageSizeCeilingsKb in " +
                          "scripts/gate-checks.ps1 IN THIS COMMIT, with the new measurement's date, SDK " +
                          "and commit in the comment above it. If it is NOT intended, something started " +
                          "shipping that should not - list the package with unzip -l and find it.")
        }
        else {
            Write-Host "   package size: $($file.Name) $kb KB <= ceiling $ceiling KB ($($file.Length) bytes)" -ForegroundColor DarkGray
        }
    }

    # A newly shipped package must not slip in unceilinged. Populations of ten or fewer stay exactly
    # pinned (the house rule), and the shipped-package population is two.
    $unpinned = @($packages | Where-Object { -not $matched.Contains($_.Name) })
    if ($unpinned.Count -gt 0) {
        $failures += ("package size: " + $unpinned.Count + " packed .nupkg(s) have no ceiling (" +
                      (($unpinned | ForEach-Object { $_.Name }) -join ', ') + "). A newly shipped package " +
                      "must arrive WITH its measured ceiling in the same commit, or the ratchet covers a " +
                      "shrinking fraction of what ships while staying green.")
    }

    foreach ($sym in @(Get-ChildItem -LiteralPath $PackageDir -File -Filter '*.snupkg')) {
        Write-Host "   package size: $($sym.Name) $([int][math]::Floor($sym.Length / 1024)) KB (symbols - informational, not gated)" -ForegroundColor DarkGray
    }

    if ($failures.Count -gt 0) {
        throw ($failures -join [Environment]::NewLine)
    }
}
