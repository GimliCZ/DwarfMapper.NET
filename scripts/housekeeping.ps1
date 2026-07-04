#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# DwarfMapper LOCAL housekeeping — the heavy integrity checks kept OUT of CI on purpose (time/cost).
# Run before a release or a large push. Each stage fails fast.
#
#   pwsh scripts/housekeeping.ps1                 # suite + exhaustion + AOT execute
#   pwsh scripts/housekeeping.ps1 -SkipExhaustion # skip the ~6 min full power-set
#   pwsh scripts/housekeeping.ps1 -Mutation       # also run Stryker mutation testing (very slow)
#   pwsh scripts/housekeeping.ps1 -Heal           # regenerate AnalyzerReleases rows (self-heal) then test
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipAot,
    [switch]$SkipExhaustion,
    [switch]$Mutation,
    [switch]$Heal
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if ($Heal) {
        Write-Host "== self-heal: regenerate AnalyzerReleases rows ==" -ForegroundColor Cyan
        $env:DWARF_SELF_HEAL = '1'
        dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release --nologo `
            --filter "FullyQualifiedName~SelfHeal_AnalyzerReleases"
        Remove-Item Env:DWARF_SELF_HEAL
    }

    Write-Host "== 1/4 Full self-test suite ==" -ForegroundColor Cyan
    dotnet test DwarfMapper.NET.sln -c Release --nologo
    if ($LASTEXITCODE) { throw "self-test suite failed" }

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
        dotnet publish samples/DwarfMapper.AotBench/DwarfMapper.AotBench.csproj -c Release -r $rid -p:PublishAot=true --nologo
        if ($LASTEXITCODE) { throw "AOT publish failed" }
        $bin = Get-ChildItem -Recurse -Path samples/DwarfMapper.AotBench/bin/Release -Filter "DwarfMapper.AotBench*" |
               Where-Object { $_.FullName -match 'publish' -and ($_.Extension -eq '' -or $_.Extension -eq '.exe') } |
               Select-Object -First 1
        if (-not $bin) { throw "AotBench native binary not found under publish/" }
        Write-Host "Running native AOT binary: $($bin.FullName)"
        & $bin.FullName
        if ($LASTEXITCODE) { throw "AotBench reported AOT instability (exit $LASTEXITCODE)" }
    }

    if ($Mutation) {
        Write-Host "== 4/4 Mutation testing (Stryker — install: dotnet tool install -g dotnet-stryker) ==" -ForegroundColor Cyan
        dotnet stryker
        if ($LASTEXITCODE) { throw "mutation score below break threshold" }
    }

    Write-Host "HOUSEKEEPING PASSED" -ForegroundColor Green
}
finally {
    Pop-Location
}
