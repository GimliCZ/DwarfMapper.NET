<#
.SYNOPSIS
    Counts, per DwarfMapper diagnostic id, how many distinct sites fire across a consumer corpus.

.DESCRIPTION
    The question this answers is "how loud is this diagnostic on code that looks like the field?", and it is
    the only honest way to judge an Info: an argument about whether DWARF103 is too noisy is settled by
    counting it on representative code, not by counting it inside a mapper's own test suite (which is dense
    with synthetic DTO pairs BECAUSE it tests a mapper).

    Two things make the number trustworthy and both are load-bearing:

      * `-v:detailed`. Info-severity diagnostics are not printed at normal verbosity, so a normal-verbosity
        build of a corpus full of them reports zero.
      * `--no-incremental`. A generator does not re-run for an up-to-date project, so a cached build reports
        zero too — and a zero from a skipped compile is indistinguishable from a clean corpus.

    Three counts are reported per id, because they answer different questions:

      * Reports    — every diagnostic the compiler emitted. What a consumer's build log shows them.
      * Sites      — distinct file/line/column. What a consumer would have to visit to act on them.
      * Messages   — distinct message text. DWARF103 dedupes once per element PAIR per mapper class, so this
                     is the unit comparable with a "distinct pairs" figure quoted elsewhere.

.PARAMETER Project
    Project (or directory) to build. Defaults to the CleanCorpus consumer corpus.

.PARAMETER Configuration
    Build configuration. Release by default, matching the verification tier.

.PARAMETER Detail
    Also list every distinct site under each id.

.EXAMPLE
    pwsh scripts/corpus-diagnostic-density.ps1

.EXAMPLE
    pwsh scripts/corpus-diagnostic-density.ps1 -Project tests/DwarfMapper.ConsumerTests -Detail
#>
[CmdletBinding()]
param(
    [string] $Project = 'tests/DwarfMapper.ConsumerTests/CleanCorpus/DwarfMapper.ConsumerTests.CleanCorpus.csproj',
    [string] $Configuration = 'Release',
    [switch] $Detail
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot $Project

if (-not (Test-Path $target)) {
    throw "No such project or directory: $target"
}

# PROVENANCE, printed before the counts and never separable from them. A density figure is a statement about
# a GENERATOR, not about a corpus: the same corpus reported 4 and then 7 within one afternoon because another
# task was editing the classifier's layout table. A number quoted without the revision it was measured against
# is not evidence, so the script emits the revision itself rather than trusting whoever pastes the table.
$head = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
$dirty = @(& git -C $repoRoot status --porcelain -- src 2>$null | Where-Object { $_ })

Write-Output ("Generator revision: {0}{1}" -f $head, $(if ($dirty) { ' + UNCOMMITTED src/ changes' } else { ' (src/ clean)' }))
foreach ($change in $dirty) {
    Write-Output "  $change"
}
if ($dirty) {
    Write-Output "  Counts below describe THIS working tree, not $head. Re-measure on a clean tree before quoting them."
}

Write-Output "Building $Project ($Configuration, detailed, no-incremental) …"

$log = & dotnet build $target -c $Configuration --no-incremental -v:detailed 2>&1 | Out-String
$exitCode = $LASTEXITCODE

# `  1>C:\path\File.cs(12,34): info DWARF103: message [C:\path\Project.csproj]` — the MSBuild node prefix and
# the trailing project tag are noise; everything between them is the diagnostic.
$pattern = '^\s*(?:\d+>)?(?<file>[^(\r\n]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>info|warning|error)\s+(?<id>DWARF\d+):\s*(?<msg>.*?)(?:\s*\[[^\[\]]*\.csproj\])?$'

# A diagnostic reported with no syntax location — one about the assembly rather than a line, such as
# DWARF058's "the convenience extension was not generated". It has no site to visit, so it is counted at the
# pseudo-site "(no location)" and stays visible rather than being dropped for not matching the shape above.
$unlocated = '^\s*(?:\d+>)?(?<sev>info|warning|error)\s+(?<id>DWARF\d+):\s*(?<msg>.*?)(?:\s*\[[^\[\]]*\.csproj\])?$'

$rows = @()
foreach ($line in ($log -split "`r?`n")) {
    $m = [regex]::Match($line, $pattern)
    $located = $m.Success

    if (-not $located) {
        $m = [regex]::Match($line, $unlocated)
        if (-not $m.Success) { continue }
    }

    $rows += [pscustomobject]@{
        Id       = $m.Groups['id'].Value
        Severity = $m.Groups['sev'].Value
        File     = if ($located) { $m.Groups['file'].Value.Trim() } else { '(no location)' }
        Line     = if ($located) { [int] $m.Groups['line'].Value } else { 0 }
        Column   = if ($located) { [int] $m.Groups['col'].Value } else { 0 }
        # The help link is the same for every report of an id and would not distinguish two messages.
        Message  = ($m.Groups['msg'].Value -replace '\s*\(https://\S+\)\s*$', '').Trim()
    }
}

# One build compiles a project once but MSBuild echoes each diagnostic per node, so identical (id, file, line,
# column, message) tuples are the SAME report seen twice, not two reports.
$reports = $rows | Sort-Object Id, File, Line, Column, Message -Unique

$byId = $reports | Group-Object Id | Sort-Object Name

Write-Output ''
Write-Output "| Id | Severity | Reports | Sites | Messages |"
Write-Output "|---|---|---:|---:|---:|"

foreach ($group in $byId) {
    # An unlocated report has no line to be distinct at, so its message stands in for its position — two of
    # them saying different things are two sites, not one.
    $sites = ($group.Group | ForEach-Object {
            if ($_.File -eq '(no location)') { "(no location)|$($_.Message)" }
            else { "$($_.File)|$($_.Line)|$($_.Column)" }
        } | Sort-Object -Unique).Count
    $messages = ($group.Group.Message | Sort-Object -Unique).Count
    $severity = ($group.Group.Severity | Sort-Object -Unique) -join '/'
    Write-Output ("| {0} | {1} | {2} | {3} | {4} |" -f $group.Name, $severity, $group.Count, $sites, $messages)
}

if ($byId.Count -eq 0) {
    Write-Output "| (none) | | 0 | 0 | 0 |"
}

if ($Detail) {
    foreach ($group in $byId) {
        Write-Output ''
        Write-Output "### $($group.Name)"
        foreach ($report in ($group.Group | Sort-Object File, Line, Column)) {
            $relative = $report.File.Replace($repoRoot, '').TrimStart('\', '/')
            Write-Output ("  {0}({1},{2})" -f $relative, $report.Line, $report.Column)
            Write-Output ("    {0}" -f $report.Message)
        }
    }
}

Write-Output ''
Write-Output ("Build exit code: {0}" -f $exitCode)

if ($exitCode -ne 0) {
    throw "The build failed; the counts above cover only what it reached before stopping."
}
