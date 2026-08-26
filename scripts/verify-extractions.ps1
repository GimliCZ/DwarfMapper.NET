#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Round 27 moved 2,565 lines of generator logic into fourteen new methods. This proves the moves.
#
# WHY THE EXISTING PROOFS ARE NOT ENOUGH. Every extraction commit was gated on the golden manifest being
# unchanged, which certifies GENERATED OUTPUT for the inputs the corpus exercises. Measured afterwards,
# ~138 lines across those methods are executed by no test at all. A rename applied to the wrong identifier
# there, or a jump rewritten one statement off, would leave the manifest green, the suite green, and the
# defect in place. Those proofs are about behaviour; this one is about the text.
#
# TWO HALVES, because each is blind to the other's failure:
#
#   bodies — each extracted method is the original span, line for line. Verified by INVERTING the renames
#            and jump rewrites on what is committed and diffing against the pre-extraction source. The
#            inverse direction is deliberate: re-running the forward transformation would only prove the
#            lift scripts are deterministic, which is not in doubt.
#   sites  — the hole the span came out of. A call placed a few lines off, or an unrelated edit smuggled
#            into the same commit, passes the body check completely. So each commit's own diff of the
#            source file must remove exactly the span and add exactly the call.
#
# Usage:  pwsh scripts/verify-extractions.ps1
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    Write-Host '== Bodies: each extracted method against the span it came from ==' -ForegroundColor Cyan
    python (Join-Path $PSScriptRoot 'verify-extraction-bodies.py')
    if ($LASTEXITCODE) { throw 'verify-extractions: an extracted body does not re-derive from its original' }

    Write-Host ''
    Write-Host '== Sites: what each extraction left behind ==' -ForegroundColor Cyan
    python (Join-Path $PSScriptRoot 'verify-extraction-sites.py')
    if ($LASTEXITCODE) { throw 'verify-extractions: a call site carries changes the extraction does not explain' }
}
finally {
    Pop-Location
}
