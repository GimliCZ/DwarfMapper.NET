#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Instruction-set matrix — run the RUNTIME suites once per ISA configuration.
#
# WHY THIS EXISTS. Every test run, on every developer machine and both CI runners, has had AVX2
# available: ci.yml's cross-platform leg varies the OS (windows/macos) but never the instruction set, and
# nothing in .github/, scripts/ or tests/ has ever set DOTNET_Enable* or PreferredVectorBitWidth. So the
# generator emits `Vector.Widen` behind a `Vector.IsHardwareAccelerated` guard WITH A SCALAR TAIL, and that
# fallback branch may never once have executed under test. That is a corpus hole in the exact shape this
# repository keeps finding — the defect hides in what the corpus cannot generate, not in the code.
#
# WHAT IT IS NOT. This is not a substitute for real hardware. It restricts what the JIT may EMIT on this
# CPU; it cannot reproduce a different memory model, a different calling convention, or ARM's actual NEON
# codegen. What it does cover is the axis that decides SIMD correctness — how wide the vectors are, and
# whether there are any — which is the axis the emitted fast paths branch on.
#
# Usage:  pwsh scripts/isa-matrix.ps1
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    # The runtime suites only. Generator tests assert over emitted SOURCE, which no instruction set can
    # change, so running them four times would cost minutes to prove nothing.
    $projects = @(
        'tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj',
        'tests/DwarfMapper.DifferentialTests/DwarfMapper.DifferentialTests.csproj'
    )

    # Each row carries what the runtime must REPORT under it. InstructionSetMatrixTests asserts that, so a
    # configuration that silently failed to apply fails the run instead of passing as a duplicate of native.
    # Width is left unset where it is not knowable up front (it depends on the host's widest ISA).
    $configs = @(
        @{ Name = 'native (host ISA)'       ; Env = @{}                                        ; Accel = 'true'  ; Width = $null }
        @{ Name = '256-bit (AVX2 pinned)'   ; Env = @{ DOTNET_PreferredVectorBitWidth = '256' } ; Accel = 'true'  ; Width = '8'  }
        @{ Name = '128-bit (NEON/Graviton)' ; Env = @{ DOTNET_PreferredVectorBitWidth = '128' } ; Accel = 'true'  ; Width = '4'  }
        @{ Name = 'no AVX2 (SSE only)'      ; Env = @{ DOTNET_EnableAVX2 = '0' }                ; Accel = 'true'  ; Width = '4'  }
        @{ Name = 'NO hardware intrinsics'  ; Env = @{ DOTNET_EnableHWIntrinsic = '0' }         ; Accel = 'false' ; Width = $null }
    )

    Write-Host "== Building once (Release); each configuration then runs --no-build ==" -ForegroundColor Cyan
    dotnet build DwarfMapper.NET.sln -c Release --nologo | Out-Null
    if ($LASTEXITCODE) { throw "isa-matrix: the Release build failed before any configuration ran" }

    $failures = @()
    foreach ($cfg in $configs) {
        Write-Host ""
        Write-Host "== ISA: $($cfg.Name) ==" -ForegroundColor Cyan

        # Set for THIS process so `dotnet test` and the test host it spawns both inherit them.
        foreach ($k in $cfg.Env.Keys) { Set-Item -Path "Env:$k" -Value $cfg.Env[$k] }
        $env:DWARF_ISA_EXPECT_HWACCEL = $cfg.Accel
        if ($cfg.Width) { $env:DWARF_ISA_EXPECT_WIDTH = $cfg.Width } else { Remove-Item Env:DWARF_ISA_EXPECT_WIDTH -ErrorAction SilentlyContinue }

        try {
            foreach ($p in $projects) {
                # Capture first, filter after. Piping `dotnet test` straight into Out-String/Select-String
                # swallowed BOTH the output and the exit code, so the matrix reported PASS while running
                # nothing — the exact vacuity these tests exist to prevent, in the harness meant to prevent it.
                $out = & dotnet test $p -c Release --no-build --nologo 2>&1
                $code = $LASTEXITCODE
                # ASCII-only pattern on purpose: the summary line is localised (Czech on this host) and
                # matching its accented words depends on the console encoding surviving the capture, which
                # it does not. The "<dll> (net10.0)" suffix is invariant.
                $summary = $out | Where-Object { $_ -match '\(net10\.0\)\s*$' }

                if (-not $summary) {
                    $failures += "$($cfg.Name) :: $(Split-Path -Leaf $p) :: NO TEST SUMMARY (ran nothing)"
                    Write-Host "   NO TEST SUMMARY from $(Split-Path -Leaf $p)" -ForegroundColor Red
                    $out | Select-Object -Last 5 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
                }
                else {
                    $summary | ForEach-Object { Write-Host "   $_" }
                }

                if ($code) { $failures += "$($cfg.Name) :: $(Split-Path -Leaf $p) :: exit $code" }
            }
        }
        finally {
            foreach ($k in $cfg.Env.Keys) { Remove-Item "Env:$k" -ErrorAction SilentlyContinue }
            Remove-Item Env:DWARF_ISA_EXPECT_HWACCEL -ErrorAction SilentlyContinue
            Remove-Item Env:DWARF_ISA_EXPECT_WIDTH -ErrorAction SilentlyContinue
        }
    }

    Write-Host ""
    if ($failures.Count) {
        Write-Host "ISA MATRIX FAILED:" -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
        throw "isa-matrix: $($failures.Count) configuration/project combination(s) failed"
    }

    Write-Host "ISA MATRIX PASSED — identical results at every width, and with intrinsics off" -ForegroundColor Green
}
finally {
    Pop-Location
}
