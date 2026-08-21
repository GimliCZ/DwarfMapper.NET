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
