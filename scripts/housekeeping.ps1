#!/usr/bin/env pwsh
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# DwarfMapper LOCAL housekeeping — the heavy integrity checks kept OUT of CI on purpose (time/cost).
# Run before a release or a large push. Each stage fails fast.
#
#   pwsh scripts/housekeeping.ps1                 # suite + exhaustion + AOT execute
#   pwsh scripts/housekeeping.ps1 -SkipExhaustion # skip the ~6 min full power-set
#   pwsh scripts/housekeeping.ps1 -Mutation       # also run Stryker mutation testing (very slow)
#   pwsh scripts/housekeeping.ps1 -Heal           # regenerate AnalyzerReleases rows (self-heal) then test
#   pwsh scripts/housekeeping.ps1 -Coverage       # collect coverage during stage 1, enforce per-assembly floors
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipAot,
    [switch]$SkipExhaustion,
    [switch]$Mutation,
    [switch]$Heal,
    [switch]$Coverage
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Coverage floors — per-assembly LINE coverage, percent. MEASURED, not aspired-to: each value is the
# full-suite Release measurement (coverlet XPlat collector merged by ReportGenerator) rounded DOWN to one
# decimal place ([math]::Floor(x*10)/10), so run-to-run float noise cannot flip the gate — that truncation
# is the only slack, no cushion is added on top. The gate is line coverage; branch coverage is printed as
# informational only. Raising a floor to a new measured value is the normal move; lowering one demands a
# written reason in the commit that lowers it.
#
# Measured 2026-08-21 on cedad48 plus this commit's collector refs (7,669 tests) — line / branch:
#   DwarfMapper 77.7/76.1 · Generator 93.8/87.4 · DocTooling 90.7/84.2 · CodeFixes 92.4/68.4 · Testing 83.2/82.2
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$coverageFloors = [ordered]@{
    'DwarfMapper'            = 77.7
    'DwarfMapper.Generator'  = 93.8
    'DwarfMapper.DocTooling' = 90.7
    'DwarfMapper.CodeFixes'  = 92.4
    'DwarfMapper.Testing'    = 83.2
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Stryker EXITS 0 when its `mutate` filter matches nothing. Every mutant comes back "Removed by mutate
# filter", no score is calculated, and `if ($LASTEXITCODE) { throw }` sees success — a leg that tested
# nothing passes the gate. That is not hypothetical: it is exactly what stryker-config.runtime.json did
# on 2026-08-16, because `mutate` globs resolve against the PROJECT directory and the file listed them
# repo-root-relative.
#
# So the exit code is necessary but not sufficient. Each leg must also prove it actually scored some
# mutants. Requires the "json" reporter, which all three configs declare.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-MutantsWereTested {
    param(
        [Parameter(Mandatory)][string]$Leg,
        [Parameter(Mandatory)][datetime]$Since
    )
    $report = Get-ChildItem -Path (Join-Path $root 'StrykerOutput') -Recurse -Filter 'mutation-report.json' `
                            -ErrorAction SilentlyContinue |
              Where-Object { $_.LastWriteTime -ge $Since } |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $report) {
        throw "mutation ($Leg): no mutation-report.json was written for this run — the leg produced no report at all"
    }
    # Counted over the raw text rather than ConvertFrom-Json: the report embeds every mutated file's full
    # source, so it runs to tens of megabytes and parsing it buys nothing this check needs.
    $raw = Get-Content -Raw -LiteralPath $report.FullName
    $scoreable = ([regex]::Matches($raw, '"status"\s*:\s*"(Killed|Survived|Timeout|NoCoverage)"')).Count
    if ($scoreable -eq 0) {
        throw ("mutation ($Leg): 0 scoreable mutants — the run was VACUOUS and Stryker still exited 0. " +
               "Check that the config's 'mutate' globs are PROJECT-relative ('**/Name.cs'), not " +
               "repo-root-relative ('src/Proj/Name.cs'), which silently matches nothing.")
    }
    # ${Leg} braced, not $Leg: — a bare '$Leg:' parses as a SCOPE-qualified variable reference, not the
    # variable followed by a colon, and the script fails to parse at all.
    Write-Host "   ${Leg}: $scoreable scoreable mutants" -ForegroundColor DarkGray
}

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
    $covDir = Join-Path $root 'TestResults/coverage'
    $testArgs = @()
    if ($Coverage) {
        # Collect during the SAME stage-1 run rather than in a second pass: the suite is the cost driver
        # (measured 2026-08-21: 74.5 s plain, 92.9 s collecting, both Release/--no-build), so folding the
        # collector in prices -Coverage at ~+18 s instead of a whole extra suite run. Stale cobertura
        # files from a previous run would merge silently and corrupt the floor comparison — wipe first
        # (the coverage analog of Assert-MutantsWereTested's -Since filter).
        if (Test-Path $covDir) { Remove-Item -Recurse -Force $covDir }
        $testArgs = @('--collect:XPlat Code Coverage', '--results-directory', $covDir)
    }
    dotnet test DwarfMapper.NET.sln -c Release --nologo @testArgs
    if ($LASTEXITCODE) { throw "self-test suite failed" }

    if ($Coverage) {
        Write-Host "== 1b Coverage floors (install: dotnet tool install -g dotnet-reportgenerator-globaltool) ==" -ForegroundColor Cyan
        # TestResults/ is git-ignored, so both the raw cobertura files and the rendered report stay out of
        # the repo tree. The HTML report is for humans; the gate reads Summary.json.
        $reportDir = Join-Path $root 'TestResults/coverage-report'
        reportgenerator "-reports:$covDir/**/coverage.cobertura.xml" "-targetdir:$reportDir" `
            '-reporttypes:Html;JsonSummary;TextSummary' `
            ('-assemblyfilters:+' + ($coverageFloors.Keys -join ';+'))
        if ($LASTEXITCODE) { throw "coverage: ReportGenerator failed" }
        $summary = Get-Content -Raw -LiteralPath (Join-Path $reportDir 'Summary.json') | ConvertFrom-Json
        $failures = @()
        foreach ($name in $coverageFloors.Keys) {
            $asm = @($summary.coverage.assemblies | Where-Object { $_.name -eq $name })
            if (-not $asm) {
                # The vacuity guard — the same reason Assert-MutantsWereTested exists: a renamed or
                # dropped assembly must FAIL the gate, not silently fall out of it.
                $failures += "coverage: assembly '$name' is missing from the merged report - the gate cannot see it"
                continue
            }
            # Same rounding rule as the floors: truncate to one decimal, computed from the raw line
            # counts rather than trusting the report's own culture-formatted percentage strings.
            $measured = [math]::Floor($asm[0].coveredlines / $asm[0].coverablelines * 1000) / 10
            Write-Host ("   {0}: line {1}% (floor {2}%), branch {3}% (informational)" -f `
                $name, $measured, $coverageFloors[$name], $asm[0].branchcoverage) -ForegroundColor DarkGray
            if ($measured -lt $coverageFloors[$name]) {
                $failures += "coverage: $name line coverage $measured% fell below the measured floor $($coverageFloors[$name])%"
            }
        }
        if ($failures) { throw ($failures -join [Environment]::NewLine) }
    }

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
        $legStart = Get-Date
        dotnet stryker
        if ($LASTEXITCODE) { throw "mutation score below break threshold (generator)" }
        Assert-MutantsWereTested -Leg 'generator' -Since $legStart

        # Stryker mutates ONE project per run, so the documentation pipeline needs its own config. Without
        # this leg the doc tests are trusted on the strength of being green — the evidence a vacuous test
        # also provides.
        Write-Host "== 4/4b Mutation testing (DocTooling) ==" -ForegroundColor Cyan
        $legStart = Get-Date
        dotnet stryker --config-file stryker-config.doctooling.json
        if ($LASTEXITCODE) { throw "mutation score below break threshold (doc tooling)" }
        Assert-MutantsWereTested -Leg 'doc tooling' -Since $legStart

        # The SHIPPED runtime assembly. Unlike the attribute surface, registry members, the IDwarfMapper
        # facade and the exception types have no derivable case-space — no AttributeUsage to decompose, no
        # endpoint matrix to cross them against — so a surviving mutant is the only non-textual proof that a
        # case is untested.
        Write-Host "== 4/4c Mutation testing (runtime assembly) ==" -ForegroundColor Cyan
        $legStart = Get-Date
        dotnet stryker --config-file stryker-config.runtime.json
        if ($LASTEXITCODE) { throw "mutation score below break threshold (runtime)" }
        Assert-MutantsWereTested -Leg 'runtime' -Since $legStart
    }

    Write-Host "HOUSEKEEPING PASSED" -ForegroundColor Green
}
finally {
    Pop-Location
}
