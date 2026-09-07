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
#
# RE-MEASURED 2026-09-02 (round-28 audit), Windows, SDK 10.0.101, Release, -p:EnablePackageValidation=false
# (the nightly job's own pack line), working tree of the round-27/28 diff:
#   DwarfMapper.1.0.2-rc.1.nupkg          287,189 B  -> floor(287189/1024) = 280 KB   (was 247)
#   DwarfMapper.Testing.1.0.2-rc.1.nupkg   48,769 B  -> floor( 48769/1024) =  47 KB   (unchanged)
# The main package outgrew its ceiling during rounds 24-27 and no commit re-measured it, so the nightly
# `package-size` job has been red since then: the gate did its job, and nobody read the nightly. Where the
# ~33 KB went (bytes raw / deflated, 1.0.2-rc.1 at e9d2a77 -> the rc5 pack of this tree):
#   DwarfMapper.Generator.dll   517,120 / 168,960 -> 576,512 / 189,348   the rounds' pipeline growth
#   DwarfMapper.xml             153,769 /  34,693 -> 168,571 /  38,638   doc comments on the new surface
#   DwarfMapper.dll              40,960 /  17,333 ->  42,496 /  18,082
#   README.md                    73,456 /  24,921 ->  74,062 /  25,089
#   DwarfMapper.CodeFixes.dll    24,576 unchanged
# The gate itself runs on ubuntu-latest (ci.yml package-size job), so the same tree was also packed in a
# mcr.microsoft.com/dotnet/sdk:10.0.101 container with CI=true and a locked restore, the job's exact recipe:
#   DwarfMapper.1.0.2-rc.1.nupkg          286,985 B  -> 280 KB   (Windows 287,189: the 204-byte delta is
#   DwarfMapper.Testing.1.0.2-rc.1.nupkg   48,715 B  ->  47 KB    CRLF vs LF in DwarfMapper.xml and the
#   nuspec, plus 10-20 bytes of deflate variance per DLL; every DLL is byte-identical raw on both.)
# Both environments truncate to the same KB; the ceiling is the number the gate will actually measure.
#
# AND AGAIN THE SAME DAY, after the round-28 fixes for the consumer-reported emission warnings (nullable-element
# collections and dictionary values, constructor-argument and projection null-forgiving, the [MapTo] registry's
# collection helper annotations, [Obsolete] enum members: ~1.7 KB of generator IL in all):
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  288,884 B -> 282 KB      Testing 48,772 B -> 47 KB
#   ubuntu   DwarfMapper.1.0.2-rc.1.nupkg  288,676 B -> 281 KB      Testing 48,732 B -> 47 KB   (the gate's own)
# The two platforms now STRADDLE the KB boundary (the 208-byte CRLF/LF difference in the XML doc and nuspec).
# The ceiling is the larger measurement, 282: it is a measurement (Windows), and the same tree must be green
# wherever the gate is run - 281 would leave 92 bytes on ubuntu and be red on every Windows pack. Headroom to
# the first red byte (289,792): 908 B Windows, 1,116 B ubuntu. Same five entries. If the doc XML were written
# with LF on every platform the two numbers would coincide; that is a build change, not a gate change.
# Same five entries, no dependency, no resource - none of the class this gate exists to catch - so this is a
# raise with its reason, not a finding against the package. Headroom to the first red byte (281 KB = 287,744 B) is 555 bytes. Note that this
# gate runs ONLY in the nightly CI `package-size` job: scripts/housekeeping.ps1 never packs, so no local run,
# `-Nightly` included, can see this red. A local pack + Assert-PackageSizeWithinCeiling under -Nightly would
# close that hole; it is the one gate the script does not mirror.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
#
# RE-MEASURED 2026-09-06 (round-29 Phase 2 gate, task 2.10), at commit f216f33, SDK 10.0.101, Release,
# -p:EnablePackageValidation=false, CI=true, both environments packing the SAME tree:
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  316,155 B -> floor(316155/1024) = 308 KB   (was 282)
#            DwarfMapper.Testing...nupkg    51,032 B -> floor( 51032/1024) =  49 KB   (was  47)
#   ubuntu   DwarfMapper.1.0.2-rc.1.nupkg  315,638 B -> 308 KB   (the gate's own environment: a
#            DwarfMapper.Testing...nupkg    50,840 B ->  49 KB    mcr.microsoft.com/dotnet/sdk:10.0.101
#                                                                 container, locked restore, from a clean
#                                                                 `git worktree add` of the same commit)
# The two platforms agree on the KB this time — they straddled the boundary at the round-28 measurement —
# so the "larger measurement" rule and the "same tree green wherever the gate runs" rule pick the same
# number: 308 and 49. Headroom to the first red byte (309 KB = 316,416 B): 261 B on Windows, 778 B on
# ubuntu. For DwarfMapper.Testing (50 KB = 51,200 B): 168 B and 360 B.
#
# WHERE THE ~27 KB WENT, entry by entry (deflated bytes, the rc7 pack of 2026-09-02 -> this tree; raw sizes
# in the same order). Measured by diffing the two .nupkg central directories, not estimated:
#   analyzers/.../DwarfMapper.Generator.dll   192,441 -> 211,433   (raw 583,680 -> 639,488)  +18,992
#   analyzers/.../DwarfMapper.CodeFixes.dll    11,192 ->  17,230   (raw  24,576 ->  36,864)   +6,038
#   README.md                                  25,328 ->  27,595   (raw  74,568 ->  80,105)   +2,267
#   lib/net10.0/DwarfMapper.dll                18,079 ->  18,063   (raw unchanged at 42,496)     -16
#   DwarfMapper.nuspec                            761 ->     773                                  +12
#   (total 287,591 -> 314,885 deflated = +27,294; the .psmdcp part is NuGet's random name, not payload)
# The Generator growth is round 29's pipeline work — TransferModelShape/LayoutHygiene, DWARF101, DWARF103,
# DWARF106, DWARF107, the DWARF070 extension and roughly sixteen fixed emission sites. The CodeFixes growth
# is one new provider, ConvertToRecordStructCodeFixProvider. The README growth is the transfer-model-struct
# sections and the regenerated quality badges. SAME FIVE ENTRIES AS BEFORE: no new dependency, no new
# resource, nothing newly shipping that should not — none of the class this gate exists to catch — so this
# is a raise with its reason, not a finding against the package. DwarfMapper.xml is byte-identical to rc7's
# and no longer appears in the delta at all.
# ─────────────────────────────────────────────────────────────────────────────────────────────────
#
# RE-MEASURED 2026-09-07, in the commit that WITHDREW the [GenerateView] endpoint (round 29 Phase 1;
# Issues/round29/WITHDRAWN-generated-views.md). SDK 10.0.101, Release, -p:EnablePackageValidation=false,
# CI=true, packed from this worktree:
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  316,224 B -> floor(316224/1024) = 308 KB   (was 318)
#            DwarfMapper.Testing...nupkg    51,039 B -> floor( 51039/1024) =  49 KB   (UNCHANGED)
# 316,224 is the LARGER of two packs taken in this session (316,223 and 316,224), so the number below does
# not rest on a single sample. ONLY WINDOWS WAS MEASURED and the ceiling is stated with that limit: no
# ubuntu container was available here. It is the safe direction — Windows measured the LARGER of the two at
# every round-28/29 pairing (287,189 vs 286,985; 316,155 vs 315,638, a ~200-500 B CRLF-vs-LF difference in
# DwarfMapper.xml and the nuspec) — and applying that offset puts ubuntu near 315,700 B, the same 308 KB.
# The nightly `package-size` job is the only place this gate runs at all.
#
# THE CEILING CAME DOWN, 318 -> 308, and it did NOT come down to where it started. Entry by entry against
# the LAST PRE-VIEW LOCAL PACK (the 8e11b52 tree, 316,131 B, whose table the Phase 1 note recorded), raw /
# deflated bytes:
#   analyzers/.../DwarfMapper.Generator.dll   639,488 / 211,436 -> 639,488 / 211,491   +55
#   DwarfMapper.nuspec                          1,370 /     746 ->   1,417 /     776   +30
#   analyzers/.../DwarfMapper.CodeFixes.dll    36,864 /  17,227 ->  36,864 /  17,236    +9
#   lib/net10.0/DwarfMapper.dll                42,496 /  18,066 ->  42,496 /  18,066     0
#   lib/net10.0/DwarfMapper.xml               168,571 /  38,638 -> 168,571 /  38,638     0
#   README.md                                  80,105 /  27,595 ->  80,105 /  27,595     0
#   _rels/.rels                                   505 /     287 ->     505 /     285    -2
#   (total deflated +92, and 316,131 + 92 = 316,223 exactly; the .psmdcp part is NuGet's random name,
#    not payload, and is excluded)
#
# THE READING THAT SURVIVES THE NUMBERS, and the reason this paragraph is longer than the table. Two rows
# moved with their RAW SIZE UNCHANGED. That is NOT "compression noise": deflate is deterministic, so a
# different compressed size means different bytes, and it is worth naming which:
#   * Generator.dll (+55). The generator is not back to its pre-view self: it KEEPS Identifiers.EscapeTypeName
#     and the doc comment carrying that helper's measurement, which the pre-view tree never had. Measured,
#     not assumed — `git diff f216f33 -- src/DwarfMapper.Generator/` is exactly that one added method. A
#     method's worth of IL and metadata fits inside the PE's existing file-alignment padding, so the RAW
#     size cannot show it and only the compressed size can. NOT ALL 55 BYTES ARE THAT METHOD, though: the
#     next row shows an assembly with byte-identical SOURCE moving 9 bytes across the same two packs, so
#     whatever did that is in this row too. The split is not pinned here and is not worth pinning.
#   * nuspec (+47 raw). NuGet embeds `<repository branch=... commit=... />`. So the nuspec's SIZE tracks the
#     BRANCH NAME and its BYTES track the COMMIT SHA: this package can never be byte-identical across two
#     commits however unchanged the code, and it changes size if the branch is renamed. The baseline's
#     shorter element is the same cause the Phase 1 note named for its own odd rows — THE BASELINE CAME FROM
#     A DIFFERENT CHECKOUT (there, a `C:/dmbase` worktree whose path leaks into the PE; here, visible in
#     plain text instead of inside a PE).
#   * CodeFixes.dll (+9) with `git diff f216f33 -- src/DwarfMapper.CodeFixes/` EMPTY — byte-identical source,
#     different bytes out. Same reading: THE BASELINE WAS BUILT SOMEWHERE ELSE. Which PE bytes carry that
#     difference is deliberately NOT claimed: `CI=true` sets ContinuousIntegrationBuild, so paths are mapped
#     out, and the control below shows one assembly reproducing byte-for-byte across the two packs — so a
#     bare "the path leaks into the PE" would be contradicted by this session's own evidence. The nuspec
#     row above is where the differing checkout is visible in plain text, and it is the strongest thing in
#     this block; the honest statement here is that the baseline's build environment differed and the
#     channel is unpinned.
#   * .rels (-2). Its only variable content is the random .psmdcp part name NuGet generates per pack, so a
#     byte here is not attributable to the tree at all.
# THE CONTROL that makes those four legible: DwarfMapper.dll, DwarfMapper.xml and README.md came out
# BYTE-IDENTICAL to the baseline. The runtime source IS identical to the pre-view tree
# (`git diff f216f33 -- src/DwarfMapper/` is empty) and the deterministic build reproduced it exactly. A
# comparison in which nothing matched would prove nothing about the rows that moved.
#
# THE OPERATIONAL CONCLUSION, now with a demonstration in each direction: ASSUME ANY CHANGE MOVES THIS
# NUMBER AND RE-MEASURE; do not reason about which way it went. Phase 1 DELETED two predicates and a
# paragraph and the package GREW 145 B. This commit deleted ~1,100 lines of generator and the package
# landed 92 B ABOVE the tree that never had them. Same entries as before either way: no new dependency, no
# new resource, nothing newly shipping — none of the class this gate exists to catch.
#
# Headroom to the first red byte (309 KB = 316,416 B): 192 B on Windows. That is TIGHT and deliberately so
# — R1 forbids a cushion — and it is 192 B against a pack-to-pack drift measured at 1 byte in this session
# (the Phase 1 note measured a 3-byte spread over four packs). For DwarfMapper.Testing (50 KB = 51,200 B):
# 161 B, and its 49 KB is re-measured in this commit, not merely carried.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
#
# RE-MEASURED 2026-09-07, in the commit that made the generator structurally incapable of emitting a broken
# identifier (round 29, task 1.y). SDK 10.0.101, Release, -p:EnablePackageValidation=false, packed twice
# from this worktree:
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  317,532 B -> floor(317532/1024) = 310 KB   (was 308)
#            DwarfMapper.Testing...nupkg    51,060 B -> floor( 51060/1024) =  49 KB   (UNCHANGED)
# 317,532 is the LARGER of two packs in this session (317,531 and 317,532), so the number does not rest on
# a single sample. ONLY WINDOWS WAS MEASURED, stated with that limit exactly as the previous block states
# it: no ubuntu container was available here. Windows has measured the LARGER of the pair at every
# round-28/29 pairing (287,189 vs 286,985; 316,155 vs 315,638 — a ~200-500 B CRLF-vs-LF difference in
# DwarfMapper.xml and the nuspec), so applying that offset puts ubuntu near 317,000-317,300 B, which is
# 309 KB. The ceiling takes the LARGER of the two, 310, per the rule the round-28 block states: the same
# tree must be green wherever the gate is run, and 309 would be red on every Windows pack.
#
# WHERE THE ~1.2 KB WENT, entry by entry, against a pack of cce2977 (the [GenerateView]-withdrawal commit,
# this task's parent) taken from a `git worktree add` in the SAME session so the toolchain is held fixed.
# Deflated bytes, measured by diffing the two .nupkg central directories:
#   analyzers/.../DwarfMapper.Generator.dll   211,522 -> 212,763   +1,241
#   DwarfMapper.nuspec                            745 ->     776      +31
#   lib/net10.0/DwarfMapper.dll                18,106 ->  18,084      -22
#   analyzers/.../DwarfMapper.CodeFixes.dll    17,275 ->  17,254      -21
#   (total +1,229; 316,303 + 1,229 = 317,532 exactly. The .psmdcp part is NuGet's random name, not payload.)
#
# THE READING. One row carries the change and it is the expected one: Generator.dll +1,241 B for the Emit*
# computed properties on six model records, Identifiers.Unescaped, and the ~40 emission sites rewritten to
# go through them — plus their doc comments, which are the measurement this task exists to stop being
# rediscovered. SAME FOUR ENTRIES AS BEFORE: no new dependency, no new resource, nothing newly shipping
# that should not, which is the class this gate exists to catch. The two NEGATIVE rows are the previous
# block's own finding restated: the baseline was built in a DIFFERENT CHECKOUT (a temporary worktree, whose
# detached HEAD also explains the nuspec's `<repository commit=.../>` moving 31 B), and DwarfMapper.dll's
# source is untouched by this task — `git diff cce2977 -- src/DwarfMapper/` is empty — so a 22-byte move
# there is the same unpinned build-environment channel, not payload.
#
# Headroom to the first red byte (311 KB = 318,464 B): 932 B on Windows. For DwarfMapper.Testing
# (50 KB = 51,200 B): 140 B, and its 49 KB is re-measured in this commit rather than merely carried.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
#
# RE-MEASURED 2026-09-07, in the commit that added [MapShare] and the immutability proof (round 29, task
# 3.1). SDK 10.0.101, Release, -p:EnablePackageValidation=false, packed twice from this worktree:
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  322,630 B -> floor(322630/1024) = 315 KB   (was 310)
#            DwarfMapper.Testing...nupkg    51,060 B -> floor( 51060/1024) =  49 KB   (UNCHANGED)
# 322,630 is the LARGER of two packs in this session (322,629 and 322,630). ONLY WINDOWS WAS MEASURED, and
# that limit is stated exactly as the two blocks above state it: no ubuntu container was available here.
# Windows has measured the LARGER of the pair at every round-28/29 pairing (a ~200-500 B CRLF-vs-LF
# difference in DwarfMapper.xml and the nuspec), so ubuntu lands near 322,100-322,400 B, which is 314 KB.
# The ceiling takes the LARGER of the two, 315, per the round-28 rule: the same tree must be green wherever
# the gate is run, and 314 would be red on every Windows pack.
#
# WHERE THE 5,200 B WENT, entry by entry, against a pack of 3c2c1ef (this task's parent) taken from a
# `git worktree add` in the SAME session so the toolchain is held fixed. Deflated bytes, from the two
# .nupkg central directories:
#   analyzers/.../DwarfMapper.Generator.dll   212,737 -> 216,730   +3,993
#   lib/net10.0/DwarfMapper.xml                38,638 ->  39,649   +1,011
#   lib/net10.0/DwarfMapper.dll                18,064 ->  18,201     +137
#   DwarfMapper.nuspec                            744 ->     776      +32
#   analyzers/.../DwarfMapper.CodeFixes.dll    17,231 ->  17,257      +26
#   _rels/.rels                                   285 ->     286       +1
#   (README.md, [Content_Types].xml and the .psmdcp are byte-identical; total +5,200, and
#    317,430 + 5,200 = 322,630 exactly.)
#
# THE READING. Two rows carry the change and both are the expected ones. Generator.dll +3,993 B is
# ImmutabilityProof.cs (the deep-immutability proof), MapperExtractor.Share.cs (the per-member decision),
# the DWARF104 descriptor with its message and help text — descriptor strings are payload, not comments —
# and the emitter's guard branch. DwarfMapper.xml +1,011 B is MapShareAttribute's own XML documentation,
# whose <remarks> spell out the three tiers and what the caller is asserting; that page is what a consumer
# sees in IntelliSense, so it is shipped weight on purpose. DwarfMapper.dll +137 B is the attribute type
# itself: a sealed class, one constructor, one property. SAME NINE ENTRIES AS BEFORE — no new dependency,
# no new resource, nothing newly shipping, which is the class this gate exists to catch.
#
# The three small rows are the previous block's own finding restated rather than re-derived: the baseline
# was packed in a DIFFERENT CHECKOUT (a detached-HEAD worktree, which is also why the nuspec's
# `<repository commit=.../>` moves 32 B), and `git diff 3c2c1ef HEAD -- src/DwarfMapper.CodeFixes/` is
# EMPTY — so CodeFixes.dll's +26 B is that unpinned build-environment channel, not payload.
#
# Headroom to the first red byte (316 KB = 323,584 B): 954 B on Windows. For DwarfMapper.Testing
# (50 KB = 51,200 B): 140 B, and its 49 KB is re-measured in this commit rather than merely carried.
# ──────────────────────────────────────────────────────────────────────────────────────────────────────────
#
# RE-MEASURED AGAIN 2026-09-07, in the commit that fixed the dropped [MapProperty] modifier and the two
# refusals that were not true (round 29, task 3.1, review round). The block above was measured one commit
# earlier, and this file's own rule is to ASSUME ANY CHANGE MOVES THIS NUMBER AND RE-MEASURE rather than
# reason about which way it went - so it was packed again, twice, on the same SDK 10.0.101 / Release /
# -p:EnablePackageValidation=false:
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  322,694 B -> floor(322694/1024) = 315 KB   (UNCHANGED)
#            DwarfMapper.Testing...nupkg    51,057 B -> floor( 51057/1024) =  49 KB   (UNCHANGED)
# 322,694 is the LARGER of the two packs in this session (322,693 and 322,694). ONLY WINDOWS, with the same
# stated limit and the same CRLF-vs-LF reasoning as the block above.
#
# THE +64 B, entry by entry against that block's own 322,630 measurement. Deflated bytes, from the two
# .nupkg central directories:
#   analyzers/.../DwarfMapper.Generator.dll   216,730 -> 216,807     +77
#   _rels/.rels                                   286 ->     287      +1
#   lib/net10.0/DwarfMapper.xml                39,649 ->  39,649       0
#   analyzers/.../DwarfMapper.CodeFixes.dll    17,257 ->  17,253      -4
#   lib/net10.0/DwarfMapper.dll                18,201 ->  18,194      -7
#   DwarfMapper.nuspec                            776 ->     773      -3
#   (README.md, [Content_Types].xml and the .psmdcp are byte-identical; total +64, and
#    322,630 + 64 = 322,694 exactly.)
#
# THE READING. One row carries the change and it is the only one that could: Generator.dll +77 B is the
# widened modifier gate in ResolveExplicitMaps (the `hasExtras` lookup and the `which` ternary), the
# collection/dictionary check moved ahead of the proof, the cycle guard keyed on the constructed type, and
# the four new message literals - descriptor and message strings are payload, not comments. SAME NINE
# ENTRIES AS BEFORE. The four small rows are noise on channels this file has already characterised: no
# source under src/DwarfMapper/ or src/DwarfMapper.CodeFixes/ changed in that commit
# (`git diff 7e112c7 HEAD -- src/DwarfMapper/ src/DwarfMapper.CodeFixes/` is empty), and the nuspec moves
# with the `<repository commit=.../>` sha, which is a different sha by construction.
#
# Headroom to the first red byte (316 KB = 323,584 B): 890 B on Windows. For DwarfMapper.Testing
# (50 KB = 51,200 B): 143 B. Both ceilings are re-measured in this commit rather than merely carried.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# RE-MEASURED 2026-09-07, in the commit that added [MapDenseEnumKeys] and the dense-enum range proof
# (round 29, task 3.2). SDK 10.0.101, Release, -p:EnablePackageValidation=false, packed twice from this
# worktree:
#   Windows  DwarfMapper.1.0.2-rc.1.nupkg  329,154 B -> floor(329154/1024) = 321 KB   (was 315)
#            DwarfMapper.Testing...nupkg    51,056 B -> floor( 51056/1024) =  49 KB   (UNCHANGED)
# 329,154 is the LARGER of the two packs in this session (329,153 and 329,154). ONLY WINDOWS WAS MEASURED,
# and that limit is stated exactly as the three blocks above state it: no ubuntu container was available
# here. Windows has measured the LARGER of the pair at every round-28/29 pairing (a ~200-500 B CRLF-vs-LF
# difference in DwarfMapper.xml and the nuspec), so ubuntu lands near 328,650-328,950 B, which is still
# 321 KB. The ceiling takes the LARGER of the two per the round-28 rule: the same tree must be green
# wherever the gate is run.
#
# WHERE THE 6,534 B WENT, entry by entry, against a pack of aa6eb7a (this task's parent) taken from a
# `git worktree add` in the SAME session so the toolchain is held fixed. Deflated bytes, from the two
# .nupkg central directories:
#   analyzers/.../DwarfMapper.Generator.dll   216,800 -> 221,835   +5,035
#   lib/net10.0/DwarfMapper.xml                39,649 ->  40,930   +1,281
#   lib/net10.0/DwarfMapper.dll                18,177 ->  18,348     +171
#   DwarfMapper.nuspec                            746 ->     775      +29
#   analyzers/.../DwarfMapper.CodeFixes.dll    17,230 ->  17,249      +19
#   _rels/.rels                                   287 ->     286       -1
#   (README.md and [Content_Types].xml are byte-identical; the .psmdcp part is NuGet's random name, not
#    payload, and its 642 B moves from one name to the other. Total +6,534, and
#    322,620 + 6,534 = 329,154 exactly.)
#
# THE READING. Two rows carry the change and both are the expected ones. Generator.dll +5,035 B is
# DenseEnumProof.cs (the range proof over every declared enum member and the helper it authorises),
# MapperExtractor.DenseEnum.cs (the per-member decision plus the directive's own name/duplicate/conflict
# validation), the two resolution sites, the new DWARF092 arm for this directive, and the DWARF105
# descriptor with its message and help text - descriptor and message strings are payload, not comments,
# and this feature's refusals carry a sentence each by design. DwarfMapper.xml +1,281 B is
# MapDenseEnumKeysAttribute's own XML documentation, whose <remarks> state what is proven, what is
# caller-visible at run time and why [Flags] is refused; that page is what a consumer sees in IntelliSense,
# so it is shipped weight on purpose. DwarfMapper.dll +171 B is the attribute type itself: a sealed class,
# one constructor, one property, one settable int. SAME NINE ENTRIES AS BEFORE - no new dependency, no new
# resource, nothing newly shipping, which is the class this gate exists to catch.
#
# The two small rows are the previous blocks' own finding restated rather than re-derived: the baseline was
# packed in a DIFFERENT CHECKOUT (a detached-HEAD worktree, which is also why the nuspec's
# `<repository commit=.../>` moves 29 B), and `git diff aa6eb7a -- src/DwarfMapper.CodeFixes/` is EMPTY -
# so CodeFixes.dll's +19 B is that unpinned build-environment channel, not payload.
#
# THIS IS AN OBSERVABILITY RATCHET, NOT A BUDGET. 315 -> 321 KB is what a real feature costs and the number
# is re-measured rather than defended; nothing about the design was shaped to fit under the old ceiling.
#
# Headroom to the first red byte (322 KB = 329,728 B): 574 B on Windows. For DwarfMapper.Testing
# (50 KB = 51,200 B): 144 B. Both ceilings are re-measured in this commit rather than merely carried.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$script:PackageSizeCeilingsKb = [ordered]@{
    'DwarfMapper'         = 321
    'DwarfMapper.Testing' = 49
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
