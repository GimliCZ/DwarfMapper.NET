# SPDX-License-Identifier: GPL-2.0-only
<#
.SYNOPSIS
    Records a stats snapshot of the RUNNING FusedChat.Api and FusedChat.Web processes, so two branches
    (DwarfMapper vs develop/AutoMapper) can be compared by the same instrument.

.DESCRIPTION
    Per app it records: identity (repo, branch, commit, mapper package, build configuration), the build output on
    disk, a process snapshot, 30 s of runtime counters (dotnet-counters), a GC heap census (dotnet-gcdump, which
    forces one full GC in the target), and warm HTTP latency of one anonymous endpoint.

    Protocol for a fair comparison - do the SAME on both branches:
      1. Build in the same configuration (Rider, Debug) and start Api + Web fresh.
      2. Do the same things in the app (or nothing), wait the same time (default: until both have run >= 120 s).
      3. Run:  pwsh -File Capture-FusedChatStats.ps1 -Label dwarfmapper   (or -Label develop)
      4. Compare:  pwsh -File Compare-FusedChatStats.ps1 -Before develop -After dwarfmapper

.PARAMETER Label
    Folder name for this snapshot, e.g. 'dwarfmapper' or 'develop'.
#>
param(
    [Parameter(Mandatory)] [string] $Label,
    [string] $Repo = 'C:\Users\Jouda\RiderProjects\MedbotOmega',
    [int] $CounterSeconds = 30,
    [int] $HttpSamples = 50,
    [int] $MinUptimeSeconds = 120
)

$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot $Label
New-Item -ItemType Directory -Force -Path $out | Out-Null

$apps = @(
    @{ Name = 'FusedChat.Api'; Url = 'https://localhost:7195/health' },
    @{ Name = 'FusedChat.Web'; Url = 'https://localhost:5001/' }
)

function Get-Git([string[]] $gitArgs) { (& git -C $Repo @gitArgs 2>$null) -join "`n" }

$mapperPackages = Get-ChildItem -Path (Join-Path $Repo 'src') -Recurse -Filter *.csproj |
    Select-String -Pattern 'Include="(DwarfMapper[^"]*|AutoMapper[^"]*)" Version="([^"]+)"' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { "$($_.Groups[1].Value) $($_.Groups[2].Value)" } |
    Sort-Object -Unique

$summary = [ordered]@{
    label          = $Label
    capturedAt     = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    repo           = $Repo
    branch         = Get-Git @('branch', '--show-current')
    commit         = Get-Git @('log', '-1', '--format=%h %s')
    dirtyFiles     = @((Get-Git @('status', '--short')) -split "`n" | Where-Object { $_ })
    mapperPackages = @($mapperPackages)
    machine        = [ordered]@{
        os       = [System.Environment]::OSVersion.VersionString
        cpu      = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
        cores    = [System.Environment]::ProcessorCount
        ramGB    = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
        freeRamGB = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    }
    apps           = [ordered]@{}
}

foreach ($app in $apps) {
    $p = Get-Process -Name $app.Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { Write-Warning "$($app.Name) is not running - skipped"; continue }
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)"
    $exe = $cim.ExecutablePath
    $binDir = Split-Path $exe
    $uptime = (Get-Date) - $p.StartTime
    if ($uptime.TotalSeconds -lt $MinUptimeSeconds) {
        $wait = [int]($MinUptimeSeconds - $uptime.TotalSeconds)
        Write-Host "$($app.Name): up $([int]$uptime.TotalSeconds) s, waiting $wait s to reach $MinUptimeSeconds s"
        Start-Sleep -Seconds $wait
        $p.Refresh(); $uptime = (Get-Date) - $p.StartTime
    }
    $appDir = Join-Path $out $app.Name
    New-Item -ItemType Directory -Force -Path $appDir | Out-Null
    Write-Host "== $($app.Name) (pid $($p.Id)) =="

    # ── Build output on disk ─────────────────────────────────────────────────────
    $files = Get-ChildItem -Path $binDir -Recurse -File
    $own = $files | Where-Object { $_.Name -like 'FusedChat.*.dll' -and $_.DirectoryName -eq $binDir }
    $mapperDlls = $files | Where-Object { $_.Name -match '^(AutoMapper|DwarfMapper).*\.dll$' }
    $build = [ordered]@{
        configuration  = if ($binDir -match '\\bin\\(Debug|Release)\\') { $Matches[1] } else { 'unknown' }
        binDir         = $binDir
        totalMB        = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
        fileCount      = $files.Count
        dllCount       = @($files | Where-Object Extension -eq '.dll').Count
        ownDllKB       = [math]::Round(($own | Measure-Object Length -Sum).Sum / 1KB, 1)
        ownDlls        = [ordered]@{}
        mapperDlls     = [ordered]@{}
    }
    foreach ($f in $own | Sort-Object Name) { $build.ownDlls[$f.Name] = [math]::Round($f.Length / 1KB, 1) }
    foreach ($f in $mapperDlls | Sort-Object Name) { $build.mapperDlls[$f.Name] = [math]::Round($f.Length / 1KB, 1) }

    # ── Process snapshot ─────────────────────────────────────────────────────────
    $p.Refresh()
    $proc = [ordered]@{
        pid             = $p.Id
        startTime       = $p.StartTime.ToString('yyyy-MM-dd HH:mm:ss')
        uptimeSec       = [int]$uptime.TotalSeconds
        workingSetMB    = [math]::Round($p.WorkingSet64 / 1MB, 1)
        peakWorkingSetMB = [math]::Round($p.PeakWorkingSet64 / 1MB, 1)
        privateMB       = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        pagedMB         = [math]::Round($p.PagedMemorySize64 / 1MB, 1)
        threads         = $p.Threads.Count
        handles         = $p.HandleCount
        cpuSec          = [math]::Round($p.TotalProcessorTime.TotalSeconds, 2)
        modules         = @($p.Modules).Count
    }

    # ── Runtime counters ─────────────────────────────────────────────────────────
    $csv = Join-Path $appDir 'counters.csv'
    if (Test-Path $csv) { Remove-Item $csv }
    & dotnet-counters collect -p $p.Id --format csv -o $csv `
        --duration ("00:00:{0:D2}" -f $CounterSeconds) `
        --counters 'System.Runtime,Microsoft.AspNetCore.Hosting,Microsoft-AspNetCore-Server-Kestrel' | Out-Null
    $counters = [ordered]@{}
    if (Test-Path $csv) {
        $rows = Import-Csv $csv
        foreach ($g in $rows | Group-Object { "$($_.Provider) | $($_.'Counter Name')" } | Sort-Object Name) {
            $vals = $g.Group | ForEach-Object { [double]($_.'Mean/Increment') }
            $counters[$g.Name] = [ordered]@{
                type = $g.Group[0].'Counter Type'
                last = [math]::Round($vals[-1], 3)
                mean = [math]::Round(($vals | Measure-Object -Average).Average, 3)
                max  = [math]::Round(($vals | Measure-Object -Maximum).Maximum, 3)
            }
        }
    }

    # ── GC heap census (forces one full GC in the target) ────────────────────────
    $gcdump = Join-Path $appDir 'heap.gcdump'
    if (Test-Path $gcdump) { Remove-Item $gcdump }
    & dotnet-gcdump collect -p $p.Id -o $gcdump | Out-Null
    $reportFile = Join-Path $appDir 'heap-report.txt'
    & dotnet-gcdump report $gcdump | Out-File -Encoding utf8 $reportFile
    # The report's header carries the authoritative totals; each row is "<bytes of ONE instance> <count> <type>",
    # so a type's footprint is bytes x count (rows for one type can repeat, split by size bucket).
    $types = @()
    $heapBytes = 0; $heapObjects = 0
    foreach ($line in Get-Content $reportFile) {
        if ($line -match '^\s*([\d,]+)\s+GC Heap bytes') { $heapBytes = [long]($Matches[1] -replace ',', ''); continue }
        if ($line -match '^\s*([\d,]+)\s+GC Heap objects') { $heapObjects = [long]($Matches[1] -replace ',', ''); continue }
        if ($line -match '^\s*([\d,]+)\s+([\d,]+)\s+(\S.*)$') {
            $size = [long]($Matches[1] -replace ',', '')
            $n = [long]($Matches[2] -replace ',', '')
            $types += [pscustomobject]@{ bytes = $size * $n; count = $n; type = ($Matches[3] -replace '\s+\[[^\]]+\]$', '').Trim() }
        }
    }
    $types = @($types | Group-Object type | ForEach-Object {
            [pscustomobject]@{ type = $_.Name; bytes = ($_.Group | Measure-Object bytes -Sum).Sum; count = ($_.Group | Measure-Object count -Sum).Sum }
        })
    $heap = [ordered]@{
        liveBytesMB     = [math]::Round($heapBytes / 1MB, 2)
        liveObjects     = $heapObjects
        typeCount       = $types.Count
        mapperTypeBytes = ($types | Where-Object { $_.type -match 'AutoMapper|DwarfMapper' } | Measure-Object bytes -Sum).Sum
        mapperTypeCount = ($types | Where-Object { $_.type -match 'AutoMapper|DwarfMapper' } | Measure-Object count -Sum).Sum
        top25           = @($types | Sort-Object bytes -Descending | Select-Object -First 25 |
                ForEach-Object { "{0,12:N0}  {1,9:N0}  {2}" -f $_.bytes, $_.count, $_.type })
        mapperTypes     = @($types | Where-Object { $_.type -match 'AutoMapper|DwarfMapper' } |
                Sort-Object bytes -Descending |
                ForEach-Object { "{0,12:N0}  {1,9:N0}  {2}" -f $_.bytes, $_.count, $_.type })
    }

    # ── Warm HTTP latency ────────────────────────────────────────────────────────
    $handler = [System.Net.Http.HttpClientHandler]::new()
    # The localhost dev certificate. A scriptblock cannot serve as this callback - it runs on a thread with no
    # PowerShell runspace, so every request failed - hence the framework's own accept-any delegate.
    $handler.ServerCertificateCustomValidationCallback = [System.Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    $status = $null
    for ($i = 0; $i -lt 5; $i++) { try { $status = [int]$client.GetAsync($app.Url).GetAwaiter().GetResult().StatusCode } catch { } }
    $ms = @()
    for ($i = 0; $i -lt $HttpSamples; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try { $r = $client.GetAsync($app.Url).GetAwaiter().GetResult(); $null = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); $status = [int]$r.StatusCode } catch { $status = -1 }
        $sw.Stop(); $ms += $sw.Elapsed.TotalMilliseconds
    }
    $sorted = $ms | Sort-Object
    $http = [ordered]@{
        url    = $app.Url
        status = $status
        n      = $ms.Count
        meanMs = [math]::Round(($ms | Measure-Object -Average).Average, 2)
        p50Ms  = [math]::Round($sorted[[int]($sorted.Count * 0.50)], 2)
        p95Ms  = [math]::Round($sorted[[math]::Min($sorted.Count - 1, [int]($sorted.Count * 0.95))], 2)
        maxMs  = [math]::Round(($ms | Measure-Object -Maximum).Maximum, 2)
    }
    $client.Dispose()

    $summary.apps[$app.Name] = [ordered]@{ build = $build; process = $proc; counters = $counters; heap = $heap; http = $http }
}

$summary | ConvertTo-Json -Depth 8 | Out-File -Encoding utf8 (Join-Path $out 'summary.json')

# ── Human-readable record ────────────────────────────────────────────────────
$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("# FusedChat stats - $Label")
[void]$md.AppendLine()
$tick = [char]96
$packages = (@($summary.mapperPackages) | ForEach-Object { $tick + $_ + $tick }) -join ', '
[void]$md.AppendLine('Captured ' + $summary.capturedAt + ' from ' + $tick + $summary.repo + $tick + ', branch ' + $tick +
        $summary.branch + $tick + ', ' + $tick + $summary.commit + $tick + '.')
[void]$md.AppendLine('Mapper packages: ' + $packages)
if ($summary.dirtyFiles.Count) { [void]$md.AppendLine("Uncommitted: $($summary.dirtyFiles.Count) file(s).") }
[void]$md.AppendLine("Machine: $($summary.machine.cpu), $($summary.machine.cores) logical cores, $($summary.machine.ramGB) GB RAM ($($summary.machine.freeRamGB) GB free at capture), $($summary.machine.os).")
foreach ($name in $summary.apps.Keys) {
    $a = $summary.apps[$name]
    [void]$md.AppendLine()
    [void]$md.AppendLine("## $name")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| metric | value |")
    [void]$md.AppendLine("|---|---:|")
    [void]$md.AppendLine("| configuration | $($a.build.configuration) |")
    [void]$md.AppendLine("| uptime at capture (s) | $($a.process.uptimeSec) |")
    [void]$md.AppendLine("| working set (MB) | $($a.process.workingSetMB) |")
    [void]$md.AppendLine("| peak working set (MB) | $($a.process.peakWorkingSetMB) |")
    [void]$md.AppendLine("| private bytes (MB) | $($a.process.privateMB) |")
    [void]$md.AppendLine("| threads / handles | $($a.process.threads) / $($a.process.handles) |")
    [void]$md.AppendLine("| CPU time since start (s) | $($a.process.cpuSec) |")
    [void]$md.AppendLine("| loaded modules | $($a.process.modules) |")
    [void]$md.AppendLine("| live managed heap after full GC (MB) | $($a.heap.liveBytesMB) |")
    [void]$md.AppendLine("| live objects / distinct types | $($a.heap.liveObjects) / $($a.heap.typeCount) |")
    [void]$md.AppendLine("| mapper-library objects (count / bytes) | $($a.heap.mapperTypeCount) / $($a.heap.mapperTypeBytes) |")
    [void]$md.AppendLine("| bin folder (MB / files / dlls) | $($a.build.totalMB) / $($a.build.fileCount) / $($a.build.dllCount) |")
    [void]$md.AppendLine("| FusedChat.*.dll total (KB) | $($a.build.ownDllKB) |")
    $mapperList = (@($a.build.mapperDlls.Keys) | ForEach-Object { $_ + ' ' + $a.build.mapperDlls[$_] }) -join '; '
    [void]$md.AppendLine('| mapper DLLs (KB) | ' + $mapperList + ' |')
    [void]$md.AppendLine("| HTTP $($a.http.url) status | $($a.http.status) |")
    [void]$md.AppendLine("| HTTP mean / p50 / p95 / max (ms, n=$($a.http.n)) | $($a.http.meanMs) / $($a.http.p50Ms) / $($a.http.p95Ms) / $($a.http.maxMs) |")
    [void]$md.AppendLine()
    [void]$md.AppendLine("Runtime counters over $CounterSeconds s (last / mean / max):")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| counter | last | mean | max |")
    [void]$md.AppendLine("|---|---:|---:|---:|")
    foreach ($c in $a.counters.Keys) { $v = $a.counters[$c]; [void]$md.AppendLine("| $c | $($v.last) | $($v.mean) | $($v.max) |") }
    [void]$md.AppendLine()
    [void]$md.AppendLine("Top 25 heap types after a full GC (bytes, count, type):")
    [void]$md.AppendLine()
    [void]$md.AppendLine('```text')
    foreach ($l in $a.heap.top25) { [void]$md.AppendLine($l) }
    [void]$md.AppendLine('```')
    if ($a.heap.mapperTypes.Count) {
        [void]$md.AppendLine()
        [void]$md.AppendLine("Mapper-library types on the heap:")
        [void]$md.AppendLine()
        [void]$md.AppendLine('```text')
        foreach ($l in $a.heap.mapperTypes) { [void]$md.AppendLine($l) }
        [void]$md.AppendLine('```')
    }
    [void]$md.AppendLine()
    $ownList = (@($a.build.ownDlls.Keys) | ForEach-Object { $_ + ' ' + $a.build.ownDlls[$_] }) -join '; '
    [void]$md.AppendLine('Own assemblies (KB): ' + $ownList)
}
$md.ToString() | Out-File -Encoding utf8 (Join-Path $out 'summary.md')
Write-Host "Written: $out\summary.md and summary.json"
