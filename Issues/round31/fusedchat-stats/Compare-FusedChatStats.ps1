# SPDX-License-Identifier: GPL-2.0-only
<#
.SYNOPSIS
    Side-by-side comparison of two Capture-FusedChatStats.ps1 snapshots, written to comparison-<Before>-vs-<After>.md.

.EXAMPLE
    pwsh -File Compare-FusedChatStats.ps1 -Before develop -After dwarfmapper
#>
param(
    [Parameter(Mandatory)] [string] $Before,
    [Parameter(Mandatory)] [string] $After
)

$ErrorActionPreference = 'Stop'
$b = Get-Content (Join-Path $PSScriptRoot "$Before/summary.json") -Raw | ConvertFrom-Json -AsHashtable
$a = Get-Content (Join-Path $PSScriptRoot "$After/summary.json") -Raw | ConvertFrom-Json -AsHashtable

function Row($md, [string] $name, $x, $y) {
    $delta = ''
    if ($x -is [ValueType] -and $y -is [ValueType] -and [double]$x -ne 0) {
        $d = [double]$y - [double]$x
        $delta = '{0:+0.##;-0.##;0} ({1:+0.#;-0.#;0} %)' -f $d, (100 * $d / [double]$x)
    }
    [void]$md.AppendLine("| $name | $x | $y | $delta |")
}

$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("# FusedChat: $Before vs $After")
[void]$md.AppendLine()
[void]$md.AppendLine("| | $Before | $After |")
[void]$md.AppendLine('|---|---|---|')
[void]$md.AppendLine("| captured | $($b.capturedAt) | $($a.capturedAt) |")
[void]$md.AppendLine("| branch / commit | $($b.branch) / $($b.commit) | $($a.branch) / $($a.commit) |")
[void]$md.AppendLine("| mapper packages | $($b.mapperPackages -join ', ') | $($a.mapperPackages -join ', ') |")
[void]$md.AppendLine("| free RAM at capture (GB) | $($b.machine.freeRamGB) | $($a.machine.freeRamGB) |")
[void]$md.AppendLine()
[void]$md.AppendLine('Delta is After minus Before; negative is smaller/faster. Single snapshots, not repeated runs: treat')
[void]$md.AppendLine('differences under ~5 % in memory and ~10 % in latency as noise unless they repeat.')

foreach ($app in @('FusedChat.Api', 'FusedChat.Web')) {
    if (-not $b.apps.ContainsKey($app) -or -not $a.apps.ContainsKey($app)) { continue }
    $x = $b.apps[$app]; $y = $a.apps[$app]
    [void]$md.AppendLine()
    [void]$md.AppendLine("## $app")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| metric | $Before | $After | delta |")
    [void]$md.AppendLine('|---|---:|---:|---:|')
    Row $md 'configuration' $x.build.configuration $y.build.configuration
    Row $md 'uptime at capture (s)' $x.process.uptimeSec $y.process.uptimeSec
    Row $md 'working set (MB)' $x.process.workingSetMB $y.process.workingSetMB
    Row $md 'peak working set (MB)' $x.process.peakWorkingSetMB $y.process.peakWorkingSetMB
    Row $md 'private bytes (MB)' $x.process.privateMB $y.process.privateMB
    Row $md 'threads' $x.process.threads $y.process.threads
    Row $md 'handles' $x.process.handles $y.process.handles
    Row $md 'CPU time since start (s)' $x.process.cpuSec $y.process.cpuSec
    Row $md 'loaded modules' $x.process.modules $y.process.modules
    Row $md 'live heap after full GC (MB)' $x.heap.liveBytesMB $y.heap.liveBytesMB
    Row $md 'live objects' $x.heap.liveObjects $y.heap.liveObjects
    Row $md 'distinct heap types' $x.heap.typeCount $y.heap.typeCount
    Row $md 'mapper-library objects' $x.heap.mapperTypeCount $y.heap.mapperTypeCount
    Row $md 'mapper-library bytes' $x.heap.mapperTypeBytes $y.heap.mapperTypeBytes
    Row $md 'bin folder (MB)' $x.build.totalMB $y.build.totalMB
    Row $md 'bin files' $x.build.fileCount $y.build.fileCount
    Row $md 'FusedChat.*.dll total (KB)' $x.build.ownDllKB $y.build.ownDllKB
    Row $md 'HTTP mean (ms)' $x.http.meanMs $y.http.meanMs
    Row $md 'HTTP p50 (ms)' $x.http.p50Ms $y.http.p50Ms
    Row $md 'HTTP p95 (ms)' $x.http.p95Ms $y.http.p95Ms

    $names = @($x.counters.Keys) + @($y.counters.Keys) | Sort-Object -Unique
    [void]$md.AppendLine()
    [void]$md.AppendLine('Runtime counters (mean over the capture window):')
    [void]$md.AppendLine()
    [void]$md.AppendLine("| counter | $Before | $After | delta |")
    [void]$md.AppendLine('|---|---:|---:|---:|')
    foreach ($n in $names) {
        $u = if ($x.counters.ContainsKey($n)) { $x.counters[$n].mean } else { '-' }
        $v = if ($y.counters.ContainsKey($n)) { $y.counters[$n].mean } else { '-' }
        Row $md $n $u $v
    }

    $dlls = @($x.build.ownDlls.Keys) + @($y.build.ownDlls.Keys) | Sort-Object -Unique
    [void]$md.AppendLine()
    [void]$md.AppendLine('Own assemblies (KB) - generated mapping code lands here:')
    [void]$md.AppendLine()
    [void]$md.AppendLine("| assembly | $Before | $After | delta |")
    [void]$md.AppendLine('|---|---:|---:|---:|')
    foreach ($d in $dlls) {
        $u = if ($x.build.ownDlls.ContainsKey($d)) { $x.build.ownDlls[$d] } else { '-' }
        $v = if ($y.build.ownDlls.ContainsKey($d)) { $y.build.ownDlls[$d] } else { '-' }
        Row $md $d $u $v
    }

    [void]$md.AppendLine()
    [void]$md.AppendLine("Mapper DLLs in the bin folder: $Before = $((@($x.build.mapperDlls.Keys) | ForEach-Object { $_ + ' ' + $x.build.mapperDlls[$_] + ' KB' }) -join ', '); $After = $((@($y.build.mapperDlls.Keys) | ForEach-Object { $_ + ' ' + $y.build.mapperDlls[$_] + ' KB' }) -join ', ').")
}

$file = Join-Path $PSScriptRoot "comparison-$Before-vs-$After.md"
$md.ToString() | Out-File -Encoding utf8 $file
Write-Host "Written: $file"
