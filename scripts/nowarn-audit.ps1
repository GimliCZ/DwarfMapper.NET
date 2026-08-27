<#
.SYNOPSIS
    Finds suppressed warning codes that suppress NOTHING.

.DESCRIPTION
    `<NoWarn>` lists accrete. A code is added to get a build green, the code that provoked it is deleted or
    rewritten six months later, and the suppression stays — indistinguishable from one that is still load
    bearing. The repository had 158 suppressed codes across its projects when this was written, one project
    listing 33 of them and naming CA1000 twice, which is what accretion-without-review looks like from the
    outside.

    A stale suppression is not merely untidy. Every entry is a standing instruction to ignore a class of
    defect, and nobody can tell which entries are still earning that by reading the list — only by removing
    one and rebuilding. This script does that removal wholesale and reports the answer.

    METHOD. One build per project with NoWarn cleared as a GLOBAL MSBuild property (`-p:NoWarn=`). Global
    properties cannot be reassigned from inside the project, so the csproj's own
    `<NoWarn>$(NoWarn);CA1050;...</NoWarn>` is inert for that run and every suppressed warning is free to
    surface. TreatWarningsAsErrors is turned off at the same time, because the point is to enumerate ALL of
    them rather than stop at the first.

    A declared code that does not appear in that output is suppressing nothing on this SDK, for this
    configuration, today. That is a strong enough basis to delete it: if it was really needed, deleting it
    turns the build red immediately and loudly, which is the cheapest possible way to be wrong.

    WHAT IT DOES NOT PROVE. A code can be dormant for a configuration this script does not build (a different
    TargetFramework, a NativeAOT publish, a different SDK). Codes whose analyzer only runs under `dotnet
    publish` are the realistic case. So this reports; the deletion is still a human decision, and the
    reasoning belongs in the commit that makes it.

.PARAMETER FailOnStale
    Exit non-zero when any declared code never fires. Off by default so the script can be read as a report;
    housekeeping turns it on once the lists have been trimmed, which is what stops them growing back.

.EXAMPLE
    pwsh scripts/nowarn-audit.ps1
    pwsh scripts/nowarn-audit.ps1 -FailOnStale
#>
[CmdletBinding()]
param(
    [switch]$FailOnStale
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $projects = Get-ChildItem -Path $root -Recurse -Filter '*.csproj' -File |
                Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
                Sort-Object FullName

    $totalDeclared = 0
    $totalStale = 0
    $rows = @()

    foreach ($project in $projects) {
        $xml = [xml](Get-Content -Raw -LiteralPath $project.FullName)

        # Every <NoWarn> in the file, split on ';' and with the $(NoWarn) inheritance token dropped.
        $declared = @()
        foreach ($node in $xml.SelectNodes('//*[local-name()="NoWarn"]')) {
            $declared += ($node.InnerText -split ';' |
                          ForEach-Object { $_.Trim() } |
                          Where-Object { $_ -and $_ -notmatch '^\$\(' })
        }

        # @(...) forced, not decorative: Select-Object -Unique returns a SCALAR when the list has one
        # element, and under Set-StrictMode a scalar has no .Count. src/DwarfMapper.CodeFixes declares
        # exactly one code, so the single-item case is real and not hypothetical.
        $declared = @($declared | Select-Object -Unique)
        if ($declared.Count -eq 0) { continue }

        $relative = $project.FullName.Substring($root.Length + 1).Replace('\', '/')
        Write-Host "== $relative" -ForegroundColor Cyan
        Write-Host "   declared: $($declared -join ' ')" -ForegroundColor DarkGray

        # --no-incremental is LOAD BEARING, and its absence produced a spectacular false result the first
        # time this ran: `dotnet build` on an up-to-date project skips compilation, emits no warnings at all,
        # and every declared code therefore looks stale. That run reported 101 of 157 suppressions (64.3 %)
        # as suppressing nothing. A single forced rebuild of one of them -- Gallery, reported 14 of 14 stale
        # -- emitted 420 CA1515, 68 CA5394 and 26 CA1002. Acting on the first number would have deleted a
        # hundred live suppressions and turned the build red across the repository.
        $output = & dotnet build $project.FullName -c Release --nologo -v n --no-incremental `
                                 -p:NoWarn= -p:TreatWarningsAsErrors=false 2>&1 | Out-String

        $fired = @([regex]::Matches($output, 'warning\s+([A-Z]+[0-9]+)') |
                   ForEach-Object { $_.Groups[1].Value } |
                   Select-Object -Unique)

        $stale = @($declared | Where-Object { $fired -notcontains $_ })

        $totalDeclared += $declared.Count
        $totalStale += $stale.Count

        if ($stale.Count -eq 0) {
            Write-Host "   every declared code still fires" -ForegroundColor Green
        } else {
            Write-Host "   SUPPRESSING NOTHING: $($stale -join ' ')" -ForegroundColor Yellow
        }

        $rows += [pscustomobject]@{
            Project  = $relative
            Declared = $declared.Count
            Stale    = $stale.Count
            Codes    = ($stale -join ' ')
        }
    }

    Write-Host ''
    Write-Host '== summary ==' -ForegroundColor Cyan
    $rows | Where-Object { $_.Stale -gt 0 } |
        Format-Table -AutoSize Project, Declared, Stale, Codes | Out-String | Write-Host

    $pct = if ($totalDeclared) { [math]::Round(100.0 * $totalStale / $totalDeclared, 1) } else { 0 }
    Write-Host "$totalStale of $totalDeclared declared suppressions ($pct%) suppress nothing." -ForegroundColor Yellow

    if ($FailOnStale -and $totalStale -gt 0) {
        throw ("nowarn-audit: $totalStale declared warning suppression(s) suppress nothing. Delete them, or " +
               'if one guards a configuration this script does not build (a publish-only analyzer, another ' +
               'TargetFramework), say which in the comment beside it so the next reader need not re-derive it.')
    }
}
finally {
    Pop-Location
}
