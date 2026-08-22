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
