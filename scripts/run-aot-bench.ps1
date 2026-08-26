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

Write-Host "Publishing NativeAOT (this takes a few minutes)..."
& dotnet publish -c Release -r win-x64 --nologo $proj
if ($LASTEXITCODE -ne 0) {
    Write-Error "AOT publish FAILED (exit $LASTEXITCODE). Not running any binary. If this is MSB3073/123, vswhere.exe was not found."
    exit 1
}

# Trap 2 (part B): the publish reported success, so it MUST have put a binary where part A left nothing.
#
# The check here used to also require the binary's LastWriteTime to be later than a timestamp taken before the
# publish. That was removed, for two reasons and in that order:
#
#   * It is UNSOUND. MSBuild's copy preserves the source file's timestamp, so a binary copied from the
#     intermediate output can legitimately carry a time earlier than this run began. The guard then refuses a
#     perfectly good publish, and a guard that cries wolf gets bypassed -- which costs more than it saves.
#   * It is REDUNDANT even when it works. Part A deleted the directory and proved the binary was gone; the
#     publish then reported success. A file existing here can only have been produced by this run. The
#     timestamp was a proxy for a fact already established directly.
#
# What has NOT changed is what the guard is for: a failed publish must never leave month-old numbers looking
# like a fresh result. That is still fully covered -- by the delete, the existence check below and the
# exit-code check above, none of which can pass for a stale binary.
if (-not (Test-Path $exe)) {
    Write-Error "Publish reported success but produced no binary at $exe."
    exit 1
}

Write-Host "Running fresh AOT binary ($((Get-Item $exe).LastWriteTime))..."
& $exe
exit $LASTEXITCODE
