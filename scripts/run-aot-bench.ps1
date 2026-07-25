# SPDX-License-Identifier: GPL-2.0-only
#
# Publishes and runs the NativeAOT benchmark sample, guarding the two traps that produced a stale/false result
# during the 2026-07-24 sweep:
#
#   1. vswhere.exe not on PATH -> ILCompiler cannot find the MSVC linker -> `dotnet publish` fails with
#      MSB3073/exit-123. The VS installer directory is prepended to PATH for this process only.
#   2. A FAILED publish leaves the PREVIOUS publish/ output in place, so naively running the exe afterwards
#      reports MONTH-OLD numbers with no error. This script deletes publish/ first, checks the publish exit
#      code, and refuses to run a binary older than the publish it just attempted.
#
# Usage:  pwsh scripts/run-aot-bench.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'samples\DwarfMapper.AotBench'
$publishDir = Join-Path $proj 'bin\Release\net10.0\win-x64\publish'
$exe = Join-Path $publishDir 'DwarfMapper.AotBench.exe'

# Trap 1: make vswhere.exe discoverable without permanently editing PATH.
$vsInstaller = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
if ((Test-Path (Join-Path $vsInstaller 'vswhere.exe')) -and ($env:PATH -notlike "*$vsInstaller*")) {
    $env:PATH = "$vsInstaller;$env:PATH"
    Write-Host "Prepended VS Installer dir to PATH for vswhere.exe."
}

# Trap 2 (part A): remove stale output so a failed publish cannot masquerade as success.
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
    Write-Host "Cleared previous publish output."
}

$publishStart = Get-Date
Write-Host "Publishing NativeAOT (this takes a few minutes)..."
& dotnet publish -c Release -r win-x64 --nologo $proj
if ($LASTEXITCODE -ne 0) {
    Write-Error "AOT publish FAILED (exit $LASTEXITCODE). Not running any binary. If this is MSB3073/123, vswhere.exe was not found."
    exit 1
}

# Trap 2 (part B): a fresh publish MUST have produced a binary newer than when we started.
if (-not (Test-Path $exe)) {
    Write-Error "Publish reported success but produced no binary at $exe."
    exit 1
}
$exeTime = (Get-Item $exe).LastWriteTime
if ($exeTime -lt $publishStart) {
    Write-Error "Binary at $exe is dated $exeTime, older than this publish ($publishStart) — it is STALE. Refusing to run it."
    exit 1
}

Write-Host "Running fresh AOT binary ($exeTime)..."
& $exe
exit $LASTEXITCODE
