#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Seam reach — does the golden corpus actually EXERCISE every phase of ExtractCore?
#
# WHY THIS EXISTS. Round 27 decomposes ExtractCore one phase per commit, each proven byte-identical against
# the 973-case golden manifest. That proof is only as good as the corpus's REACH: a phase no case executes
# is not locked at all, and moving it could change bytes for inputs outside the corpus while the manifest
# stayed green. "The manifest is unchanged" would then mean "nothing I tested noticed", which is exactly the
# kind of vacuous green this repository keeps finding in its own instruments.
#
# So reach is measured rather than assumed, and measured the honest way: run the golden corpus under coverage
# and ask which lines of ExtractCore were executed, then map those lines onto the seam comments the method
# already carries.
#
# MEASURED 2026-08-26, before any extraction: 26 of 26 phases reached, 1,225 covered lines inside the method.
# The lock is real. (A first pass reported 27/30 — the three "misses" were the two-line `End X` pair-markers,
# which close a phase rather than opening one. They are not phases and are skipped.)
#
# Usage:  pwsh scripts/seam-reach.ps1
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $results = Join-Path $repo 'TestResults/seamcov'
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }

    Write-Host '== Running the golden corpus under coverage ==' -ForegroundColor Cyan
    $out = & dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj `
        -c Release --nologo --filter 'FullyQualifiedName~Golden' `
        --collect:"XPlat Code Coverage" --results-directory $results 2>&1
    if ($LASTEXITCODE) {
        $out | Select-Object -Last 15 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
        throw 'seam-reach: the golden corpus did not pass, so its coverage describes nothing'
    }

    $report = Get-ChildItem $results -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    if (-not $report) { throw 'seam-reach: no coverage report produced' }

    # The mapping itself is a few dozen lines of text handling; keeping it in Python avoids reimplementing
    # cobertura parsing in PowerShell, and this script exists to be run by a human rather than by CI.
    python (Join-Path $PSScriptRoot 'seam-reach.py') $report.FullName
    if ($LASTEXITCODE) { throw 'seam-reach: one or more phases are not exercised by the golden corpus' }
}
finally {
    Pop-Location
}
