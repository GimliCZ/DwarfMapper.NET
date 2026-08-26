#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Extracted-method reach — does the golden corpus EXECUTE the methods the seam stage created?
#
# WHY THIS EXISTS, separately from seam-reach.ps1. That script proves reach for ExtractCore's phases and
# nothing else; it is hardcoded to that method. Every cut in this stage was proven byte-identical against
# the 973-case manifest, and that proof is worth exactly as much as the corpus's reach into the code moved.
# A method no case executes could have had its jumps rewritten wrongly with the manifest still green and the
# suite still passing -- both would be measuring the absence of the code.
#
# The risk is concentrated in the jump rewrites: nine continue->return in the flatten body, the six
# conversion arms' return->resolved pairs, and the heterogeneous branch. Those are where an unreached path
# would hide a real defect behind a green result.
#
# Usage:  pwsh scripts/extracted-reach.ps1
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $results = Join-Path $repo 'TestResults/extractedcov'
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }

    Write-Host '== Running the golden corpus under coverage ==' -ForegroundColor Cyan
    $out = & dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj `
        -c Release --nologo --filter 'FullyQualifiedName~Golden' `
        --collect:"XPlat Code Coverage" --results-directory $results 2>&1
    if ($LASTEXITCODE) {
        $out | Select-Object -Last 15 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
        throw 'extracted-reach: the golden corpus did not pass, so its coverage describes nothing'
    }

    $report = Get-ChildItem $results -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    if (-not $report) { throw 'extracted-reach: no coverage report produced' }

    python (Join-Path $PSScriptRoot 'extracted-reach.py') $report.FullName
    if ($LASTEXITCODE) { throw 'extracted-reach: one or more extracted methods are not exercised by the golden corpus' }
}
finally {
    Pop-Location
}
