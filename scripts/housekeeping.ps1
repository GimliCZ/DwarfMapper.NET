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
#   pwsh scripts/housekeeping.ps1 -ILVerify       # ILVerify the shipped runtime + a generated-consumer assembly
#   pwsh scripts/housekeeping.ps1 -Deep           # DWARF_DEEP=1 for stage 1: multiplied fuzz/property/torture counts
#   pwsh scripts/housekeeping.ps1 -Nightly        # EXACTLY what CI's nightly deep-test job runs: -Deep -Coverage
#                                                 # -ILVerify, exhaustion and AOT skipped. Mutation stays behind
#                                                 # -Mutation (its own cost class; CI runs those legs as own jobs).
#                                                 # Needs: dotnet-reportgenerator-globaltool + dotnet-ilverify.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
param(
    [switch]$SkipAot,
    [switch]$SkipExhaustion,
    [switch]$Mutation,
    [switch]$Heal,
    [switch]$Coverage,
    [switch]$ILVerify,
    [switch]$Deep,
    [switch]$Nightly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# -Nightly is an AGGREGATE, not a new stage: it pins the exact switch set the CI deep-test job runs
# (.github/workflows/ci.yml), so a maintainer reproduces the nightly locally with one switch and the two
# cannot drift apart. It skips exhaustion and AOT (exhaustion is a default local stage, AOT has its own
# aot-trim-gate CI job) and deliberately does NOT imply -Mutation — the mutation legs are their own cost
# class and their own nightly CI jobs (the 'mutation' matrix).
if ($Nightly) {
    $Deep = $true; $Coverage = $true; $ILVerify = $true
    $SkipExhaustion = $true; $SkipAot = $true
}

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# Coverage floors — per-assembly LINE coverage, percent. MEASURED, not aspired-to: each value is the
# full-suite Release measurement (coverlet XPlat collector merged by ReportGenerator) rounded DOWN to one
# decimal place ([math]::Floor(x*10)/10), so run-to-run float noise cannot flip the gate — that truncation
# is the only slack, no cushion is added on top. The gate is line coverage; branch coverage is printed as
# informational only. Raising a floor to a new measured value is the normal move; lowering one demands a
# written reason in the commit that lowers it.
#
# Re-measured 2026-08-21 (round-21 T5, fast tier, Release) at the commit that wires the nightly — line / branch:
#   DwarfMapper 78.3/76.1 · Generator 93.7/87.4 · DocTooling 90.7/84.2 · CodeFixes 92.4/68.4 · Testing 83.2/81.9
# DwarfMapper rose 77.7 -> 78.3: raised, the normal move. Generator moved 93.8 -> 93.7 (raw 93.781,
# 8,384/8,940) — NOT a lost test: T6 (B27's DWARF094 refusal, B33's span/async-stream context threading)
# grew the coverable-line DENOMINATOR after the floor's measuring commit (cedad48), and a measured floor
# tracks the measurement at HEAD; the written reason this lowering demands is this comment plus the commit
# that carries it. The deep tier (-Nightly, same day) measures the same five line values to this decimal,
# so the floors hold for both tiers — deep coverage is a superset of fast on the same tree.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
$coverageFloors = [ordered]@{
    'DwarfMapper'            = 78.3
    'DwarfMapper.Generator'  = 93.7
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

# ─────────────────────────────────────────────────────────────────────────────────────────────────────
# The OTHER way a leg dies silently (H4): Stryker 4.16 refuses to run a config whose thresholds.break >
# thresholds.low ("Threshold low must be more than or equal to threshold break") — AND EXITS 0. That is
# exactly how the doctooling leg was unrunnable from 2026-08-19 until H1 noticed: T3 raised break to 83
# and left low at 80. Checked BEFORE the run so the mistake fails in milliseconds with its cause named,
# instead of surfacing as Assert-MutantsWereTested's "no report was written". Mirrored inline in the CI
# 'mutation' matrix job (.github/workflows/ci.yml); the two must stay in step.
# ─────────────────────────────────────────────────────────────────────────────────────────────────────
function Assert-StrykerConfigSane {
    param([Parameter(Mandatory)][string]$ConfigFile)
    $thresholds = (Get-Content -Raw -LiteralPath (Join-Path $root $ConfigFile) |
                   ConvertFrom-Json).'stryker-config'.thresholds
    if ($null -eq $thresholds.break -or $null -eq $thresholds.low) {
        throw "mutation ($ConfigFile): thresholds.break/low missing - the config cannot prove it is runnable"
    }
    if ($thresholds.break -gt $thresholds.low) {
        throw ("mutation ($ConfigFile): thresholds.break ($($thresholds.break)) > thresholds.low " +
               "($($thresholds.low)) - Stryker 4.16 refuses to run this config AND EXITS 0, so the leg " +
               "would be silently dead. Whenever break moves, move low with it (NOTE 9 in " +
               "stryker-config.doctooling.json).")
    }
    Write-Host "   ${ConfigFile}: break $($thresholds.break) <= low $($thresholds.low) - runnable" -ForegroundColor DarkGray
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
    if ($Deep) {
        # One knob, one reader: tests/Shared/DeepTier.cs. Multiplies every registered fuzz/property/torture
        # population per its catalog entry; the fast tier (unset) is byte-for-byte the routine counts.
        $env:DWARF_DEEP = '1'
        Write-Host "   (deep tier: DWARF_DEEP=1)" -ForegroundColor DarkGray
    }
    $covDir = Join-Path $root 'TestResults/coverage'
    # --blame-hang is the H7-f runner-level backstop (Issues/ledgers/H7-timeout-dissection.md §4): a hung
    # testhost is dumped and killed with the offending test NAMED, instead of idling until someone kills
    # the run (or, in CI, until the job-level timeout eats the evidence). VSTest infrastructure, not test
    # semantics — nothing about pass/fail changes for a test that terminates. 5m is ~3x the longest
    # observed single-test runtime in this stage; the exhaustion stage (a single multi-minute test) and
    # the Stryker legs (per-mutant ceiling plays this role) deliberately do NOT carry it.
    $testArgs = @('--blame-hang', '--blame-hang-timeout', '5m')
    if ($Coverage) {
        # Collect during the SAME stage-1 run rather than in a second pass: the suite is the cost driver
        # (measured 2026-08-21: 74.5 s plain, 92.9 s collecting, both Release/--no-build), so folding the
        # collector in prices -Coverage at ~+18 s instead of a whole extra suite run. Stale cobertura
        # files from a previous run would merge silently and corrupt the floor comparison — wipe first
        # (the coverage analog of Assert-MutantsWereTested's -Since filter).
        if (Test-Path $covDir) { Remove-Item -Recurse -Force $covDir }
        $testArgs += @('--collect:XPlat Code Coverage', '--results-directory', $covDir)
    }
    dotnet test DwarfMapper.NET.sln -c Release --nologo @testArgs
    if ($Deep) { Remove-Item Env:DWARF_DEEP }
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

    if ($ILVerify) {
        Write-Host "== ILVerify (install: dotnet tool install -g dotnet-ilverify) ==" -ForegroundColor Cyan
        # Resolve against the newest installed net10 REF PACK — the contract surface the compiler resolved
        # against — rather than shared/Microsoft.NETCore.App. Verified empirically 2026-08-21: the ref pack
        # resolves everything both assemblies need (ilverify 10.0.11).
        # Resolution is cross-platform (T5: the nightly deep-test CI job runs this stage on a Linux
        # runner, where $env:ProgramFiles is empty): try the packs dir of every dotnet root we can name
        # (DOTNET_ROOT, wherever `dotnet` on PATH lives, Program Files on Windows), then the NuGet cache's
        # microsoft.netcore.app.ref — both layouts share the '<version>/ref/net10.0' shape. Newest by
        # [version], not by string sort (which ranks 10.0.9 above 10.0.11); prerelease suffixes stripped
        # before the cast so a rc/preview pack dir cannot crash the stage.
        $dotnetRoots = @()
        if ($env:DOTNET_ROOT) { $dotnetRoots += $env:DOTNET_ROOT }
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnetCmd) { $dotnetRoots += (Split-Path -Parent $dotnetCmd.Source) }
        if ($env:ProgramFiles) { $dotnetRoots += (Join-Path $env:ProgramFiles 'dotnet') }
        $packRoots = @($dotnetRoots | ForEach-Object { Join-Path $_ 'packs/Microsoft.NETCore.App.Ref' }) +
                     @(Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages/microsoft.netcore.app.ref')
        $refPack = $packRoots | Where-Object { Test-Path $_ } |
                   ForEach-Object { Get-ChildItem $_ -Directory } |
                   Where-Object { $_.Name -like '10.*' } |
                   Sort-Object { [version]($_.Name -replace '-.*$', '') } -Descending |
                   Select-Object -First 1
        if (-not $refPack) { throw "ilverify: no net10 ref pack found (searched: $($packRoots -join '; '))" }
        $refPackGlob = Join-Path $refPack.FullName 'ref/net10.0/*.dll'

        # Known-unverifiable methods. NEVER a blanket suppression: each entry is ONE exact method whose
        # unverifiable IL is a deliberate source construct, named here. Anything else fails the stage.
        $knownUnverifiable = @{
            # samples/DwarfMapper.Gallery/18_SpanMap.cs lines 25-26: two `stackalloc` buffers in the
            # sample's HAND-WRITTEN Run() — localloc at IL_0003/IL_002F plus the cpblk initializer copy at
            # IL_001E — demonstrating the zero-alloc span overload. stackalloc is unverifiable IL by design.
            # The generator-emitted Mapper::Map(ReadOnlySpan<int>, Span<long>) itself verifies clean.
            'DwarfMapper.Gallery.Ex18.Example::Run()' = 'stackalloc (localloc + cpblk) in hand-written sample code'
        }
        # '(?!)' never matches: with an EMPTY known map, an empty pattern would match every line and turn
        # the filter into a blanket suppression — exactly the failure mode this stage exists to prevent.
        $knownPattern = if ($knownUnverifiable.Count -gt 0) {
            ($knownUnverifiable.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'
        } else { '(?!)' }

        # Targets: the SHIPPED runtime, and the Gallery — a generated-consumer assembly whose IL contains
        # generator-emitted mapping code including the blit/SIMD paths (21_BlittableSimd, 22_Reinterpret).
        # The Gallery's bin dir doubles as its own dependency root (DwarfMapper.dll etc. are copied there).
        $targets = @(
            @{ Dll = 'src/DwarfMapper/bin/Release/net10.0/DwarfMapper.dll'; ExtraRefs = @() }
            @{ Dll = 'samples/DwarfMapper.Gallery/bin/Release/net10.0/DwarfMapper.Gallery.dll'
               ExtraRefs = @('samples/DwarfMapper.Gallery/bin/Release/net10.0/*.dll') }
        )
        foreach ($target in $targets) {
            $dll = Join-Path $root $target.Dll
            if (-not (Test-Path $dll)) { throw "ilverify: $($target.Dll) not built - build the solution Release first" }
            $refArgs = @('-r', $refPackGlob)
            foreach ($extra in $target.ExtraRefs) { $refArgs += @('-r', (Join-Path $root $extra)) }
            $out = @(& ilverify $dll @refArgs | ForEach-Object { $_.ToString() })
            $exit = $LASTEXITCODE
            $out | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
            $errors = @($out | Where-Object { $_ -match '\[IL\]:\s*Error' })
            $unexpected = @($errors | Where-Object { $_ -notmatch $knownPattern })
            if ($unexpected) {
                throw ("ilverify: unexpected IL errors in $($target.Dll):`n" + ($unexpected -join "`n"))
            }
            if ($exit -ne 0 -and $errors.Count -eq 0) {
                # Nonzero exit with no [IL] error lines means the tool itself failed (bad -r resolution,
                # missing file...) — never treat that as a pass.
                throw "ilverify: exited $exit for $($target.Dll) without reporting IL errors - tool failure"
            }
            $known = $errors.Count - $unexpected.Count
            if ($known -gt 0) {
                Write-Host "   $($target.Dll): $known known-unverifiable finding(s), all matched to declared source constructs" -ForegroundColor DarkGray
            }
        }
    }

    if ($Mutation) {
        Write-Host "== 4/4 Mutation testing (Stryker — install: dotnet tool install -g dotnet-stryker) ==" -ForegroundColor Cyan
        # All three configs sanity-checked up front: a break > low mistake in leg 3 should fail here, not
        # after legs 1 and 2 have spent half an hour proving what was already known.
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.doctooling.json'
        Assert-StrykerConfigSane -ConfigFile 'stryker-config.runtime.json'
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
