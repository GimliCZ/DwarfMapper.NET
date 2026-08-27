#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Reproducible-build check, run LOCALLY the way CI runs it.
#
# WHY THIS EXISTS. scripts/repro-pack-check.py only COMPARES two directories of packages; producing them was
# left to the reader. So reproducing the `reproducible-build` job locally meant hand-copying its command
# line, and the two drifted apart in the ways that matter most:
#
#   * CI=true. The .props files switch Deterministic and ContinuousIntegrationBuild on `'$(CI)' == 'true'`,
#     and GitHub sets CI on every runner. A local pack without it builds with DIFFERENT settings from the
#     ones being certified, so a local green would say nothing about CI and a local red might be an artefact
#     of the missing switch. The check script's own header records its measurement as taken with CI=true.
#   * --locked-mode on restore, and -p:EnablePackageValidation=false on pack, exactly as the job does.
#   * A wipe between the two rounds. Without it round B is an INCREMENTAL build, and comparing an
#     incremental build against a clean one proves nothing about either.
#
# WHY A WORKTREE. The job's wipe is `git clean -xdf`, which deletes every ignored and untracked file. Running
# that in a working tree someone is using would take their bin/, obj/, TestResults/, editor settings and any
# scratch file with it -- a destructive step buried inside a verification script is exactly the kind of thing
# that only has to surprise someone once. So this checks out a throwaway worktree at HEAD and does the whole
# job in there; the wipe cannot reach anything you are working on, and the worktree is removed at the end.
#
# The packages themselves are written OUTSIDE the worktree, or the wipe would delete round A along with the
# build output -- the same reason the CI job puts them under RUNNER_TEMP.
#
# Usage:  pwsh scripts/repro-pack.ps1
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $stamp = git -C $repo rev-parse --short HEAD
    $work = Join-Path ([IO.Path]::GetTempPath()) "dwarf-repro-$stamp-$PID"
    $packA = Join-Path ([IO.Path]::GetTempPath()) "dwarf-pack-a-$PID"
    $packB = Join-Path ([IO.Path]::GetTempPath()) "dwarf-pack-b-$PID"

    $projects = @(
        'src/DwarfMapper/DwarfMapper.csproj',
        'src/DwarfMapper.Testing/DwarfMapper.Testing.csproj'
    )

    Write-Host "== Throwaway worktree at $stamp ==" -ForegroundColor Cyan
    Write-Host "   $work"
    & git -C $repo worktree add --detach $work HEAD
    if ($LASTEXITCODE) { throw 'repro-pack: could not create the worktree' }

    try {
        # Matches the job: GitHub sets CI on every runner, and the .props read it.
        $env:CI = 'true'

        foreach ($round in @(@{ Name = 'A'; Out = $packA }, @{ Name = 'B'; Out = $packB })) {
            if ($round.Name -eq 'B') {
                # -x removes ignored files (bin/, obj/, the lot); -d recurses into untracked directories.
                # Scoped to the throwaway worktree, never to the repo you are sitting in.
                Write-Host '== Wiping the worktree so round B is a CLEAN build ==' -ForegroundColor Cyan
                & git -C $work clean -xdf | Out-Null
                & git -C $work status --porcelain
            }

            Write-Host "== Round $($round.Name): locked restore + pack ==" -ForegroundColor Cyan
            if (Test-Path $round.Out) { Remove-Item -Recurse -Force $round.Out }

            & dotnet restore (Join-Path $work 'DwarfMapper.NET.sln') --locked-mode
            if ($LASTEXITCODE) { throw "repro-pack: restore failed in round $($round.Name)" }

            foreach ($proj in $projects) {
                & dotnet pack (Join-Path $work $proj) -c Release -o $round.Out -p:EnablePackageValidation=false
                if ($LASTEXITCODE) { throw "repro-pack: pack of $proj failed in round $($round.Name)" }
            }
        }

        Write-Host '== Comparing the two packs entry by entry ==' -ForegroundColor Cyan
        python (Join-Path $PSScriptRoot 'repro-pack-check.py') $packA $packB
        if ($LASTEXITCODE) { throw 'repro-pack: the two packs differ in build-produced bytes' }
        Write-Host 'Reproducible.' -ForegroundColor Green
    }
    finally {
        # The worktree goes even on failure; the PACKAGES stay, because a red here is a claim about specific
        # bytes and the reader needs the bytes. Same reason the CI job uploads them on failure only.
        & git -C $repo worktree remove --force $work 2>$null
        Write-Host "Packages left at:`n   $packA`n   $packB" -ForegroundColor DarkGray
    }
}
finally {
    Pop-Location
}
