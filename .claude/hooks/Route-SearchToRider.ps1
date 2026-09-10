# SPDX-License-Identifier: GPL-2.0-only
#
# SWAP a text search over C# SOURCE for the equivalent Rider MCP call: deny the command and hand back the
# exact replacement, parameters already filled in from what was intercepted. This is a ROUTER, not a wall --
# the refusal always carries the command to run instead.
#
# WHY ROUTE AT ALL. grep answers "which lines contain these characters". The question is usually "where is
# this defined", "who calls this", "what implements this". Rider answers those from the IDE's index and
# symbol model, and even its plain text search returns file + line + COLUMN SPANS, which is what a windowed
# Read wants next.
#
# ── THE BOUNDARY, MEASURED 2026-09-10, AND IT IS THE WHOLE REASON THIS HOOK IS CONDITIONAL ────────────
# Rider's MCP search sees ONLY files that belong to a project in the solution. In this repository that is
# .cs under src/, tests/, samples/, benchmarks/. It is BLIND to scripts/*.ps1, Issues/**, docs/**,
# .github/**, *.json, *.md, *.yml -- which is where the gate scripts, the ledgers, the CI matrix and most
# recorded rulings live. Probed, not assumed:
#
#   search_regex --q "GoldenFeatureCoverageTests"  -> 3 hits in tests/**/*.cs
#   search_regex --q "NullableProjectRefForgiving" -> 11 hits in src/**/*.cs
#   search_regex --q "HOUSEKEEPING PASSED"         -> {"items":[]}   (exists: scripts/housekeeping.ps1:711)
#   search_regex --q "PackageSizeCeilingsKb"       -> {"items":[]}   (exists: scripts/gate-checks.ps1)
#
# An empty result is INDISTINGUISHABLE from "no such text". So routing a search of scripts/ or Issues/ to
# Rider would not merely fail -- it would answer "not found" confidently and wrongly. That is a silent
# false negative, the failure class this repository spends its rounds removing. Hence: route ONLY when
# every target is a project file; otherwise stay out of the way.
#
# ALSO NOT ROUTED, measured the same day: a WINDOWED read. Rider's read_file ignored --startLine/--endLine
# and returned all 143 lines of a file, so routing `sed -n '40,60p'` there would INCREASE context -- the
# opposite of what Deny-UnwindowedRead.ps1 is for. Windowed reads stay on the built-in Read tool.
#
# AND NOT ROUTED: a search binary reading STDIN in a pipeline. `dotnet test | grep -E "Failed"` filters
# command OUTPUT, not files; no MCP tool can serve it, and blocking it would make build output unreadable
# without dumping all of it into context.
#
# Contract: stdin is the hook payload JSON; stdout is either empty (no opinion) or a PreToolUse decision.
# Any parse failure exits 0 -- a hook that cannot read its input must not block work.

$ErrorActionPreference = 'Stop'

$root = 'C:/Users/Jouda/RiderProjects/DwarfMapper.NET'
$searchBinaries = @('grep', 'egrep', 'fgrep', 'zgrep', 'rg', 'ripgrep', 'ack', 'ag', 'sift', 'ugrep', 'findstr')

# Directories whose contents Rider indexes as project files. Anything else is invisible to its search.
$projectRoots = @('src', 'tests', 'samples', 'benchmarks')

function Test-RiderCanSee {
    param([string] $Target)

    $t = $Target.Trim().Trim('"').Trim("'") -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($t)) { return $false }
    $t = $t -replace '^\./', '' -replace [regex]::Escape($root + '/'), ''

    $head = ($t -split '/')[0]
    if ($projectRoots -notcontains $head) { return $false }

    # Directory or file? ASK THE FILESYSTEM. Guessing from "does it end in a dot-something" is what an
    # earlier version did, and it silently allowed every search of `src/DwarfMapper.Generator` -- a
    # DIRECTORY whose name ends in `.Generator`, read as a file with an unknown extension. Caught by this
    # hook's own pipe-test before it was wired, which is the only reason it is not still there.
    $abs = if ([System.IO.Path]::IsPathRooted($t)) { $t } else { Join-Path $root $t }
    if (Test-Path -LiteralPath $abs -PathType Container) { return $true }
    if (Test-Path -LiteralPath $abs -PathType Leaf) { return $t.ToLowerInvariant().EndsWith('.cs') }

    # Not on disk (a glob, a typo, a path from another tree): fall back to the extension heuristic, but
    # only for a trailing extension that looks like a real one -- and treat anything else as invisible,
    # because a WRONG route is worse than a missed one.
    if ($t -match '\.(cs)$') { return $true }
    if ($t -match '\.[A-Za-z0-9]{1,5}$') { return $false }
    return $true
}

function New-Guidance {
    param([string] $Pattern, [string[]] $Targets)

    $q = if ([string]::IsNullOrWhiteSpace($Pattern)) { '<pattern>' } else { $Pattern }
    $where = if ($Targets.Count -gt 0) { ' (was searching: ' + ($Targets -join ', ') + ')' } else { '' }

    return @"
Text search over C# source is routed to the Rider MCP$where.

RUN THIS INSTEAD -- parameters already filled in:

  mcp__rider__execute_tool(
      command:    'search_regex --q "$q"',
      rootFolder: "$root")

It returns filePath + startLine + startColumn + endLine + endColumn per hit; feed startLine straight into a
windowed Read(file_path, offset, limit).

PREFER A SEMANTIC TOOL WHEN THE QUESTION IS SEMANTIC -- reach for these BEFORE search_regex:

  Analysis      analyze_calls (--symbolFqn --analysisKind), get_file_problems, get_project_problems,
                lint_files, get_project_dependencies, get_solution_projects,
                build_solution_start / build_solution_state
  Code insight  get_symbol_info (--filePath --line --column), get_class_hierarchy,
                find_default_value_overrides, findTests
  By name       search_symbol (any code identifier), search_file (--q "<file name>")
  Text, last    search_text (literal), search_regex (pattern)

"Who calls this" is analyze_calls, not a regex. "What implements this" is get_class_hierarchy. "Where is
this symbol" is search_symbol. A character match only approximates all three.

VERIFIED-WORKING param names: search_regex/search_text/search_file take --q; get_symbol_info takes
--filePath --line --column; read_file takes --file_path. analyze_calls needs --symbolFqn AND --analysisKind,
whose accepted values are NOT yet known (incoming / CALLS_TO / Incoming / usages / callers all rejected) --
discover it and record it here. build_project, get_project_modules and validate_inspection_kts appear in the
Rider docs but this instance does not expose them.

NOT ROUTED, deliberately: scripts/**, Issues/**, docs/**, .github/**, *.ps1, *.md, *.json, *.yml. Rider's
search is blind to non-project files and returns {"items":[]}, which reads as "not found". Search those with
the ordinary tools.
"@
}

$raw = ''
try { $raw = [Console]::In.ReadToEnd() } catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

function Deny {
    param([string] $Reason)
    @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'deny'
            permissionDecisionReason = $Reason
        }
    } | ConvertTo-Json -Depth 6 -Compress
    exit 0
}

# ── The Grep TOOL ────────────────────────────────────────────────────────────────────────────────────
if ($payload.tool_name -eq 'Grep') {
    $p = ''
    $target = ''
    try { $p = [string]$payload.tool_input.pattern } catch { $p = '' }
    try { $target = [string]$payload.tool_input.path } catch { $target = '' }

    # No path means the whole repo, which includes everything Rider cannot see. Leave it alone.
    if ([string]::IsNullOrWhiteSpace($target)) { exit 0 }
    if (-not (Test-RiderCanSee -Target $target)) { exit 0 }

    Deny -Reason (New-Guidance -Pattern $p -Targets @($target))
}

if ($payload.tool_name -ne 'Bash') { exit 0 }

$command = ''
try { $command = [string]$payload.tool_input.command } catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

function Split-WithPipeFlag {
    param([string] $Command)

    $segments = New-Object System.Collections.Generic.List[object]
    $current = ''
    $quote = [char]0
    $prevWasPipe = $false

    for ($i = 0; $i -lt $Command.Length; $i++) {
        $ch = $Command[$i]

        if ($quote -ne [char]0) {
            $current += $ch
            if ($ch -eq $quote) { $quote = [char]0 }
            continue
        }
        if ($ch -eq '"' -or $ch -eq "'") { $quote = $ch; $current += $ch; continue }

        if ($ch -eq ';' -or $ch -eq '|' -or $ch -eq '&' -or $ch -eq "`n") {
            $segments.Add([pscustomobject]@{ Text = $current; PrecededByPipe = $prevWasPipe }) | Out-Null
            # '||' and '&&' are control flow, not pipes. BOTH characters of '||' must clear the flag:
            # testing only the NEXT character cleared the first '|' and then let the second one set it,
            # which allowed `build.sh || grep foo file.cs` through. Caught by pipe-test case 8.
            $nextIsPipe = ($i + 1 -lt $Command.Length -and $Command[$i + 1] -eq '|')
            $prevIsPipe = ($i -gt 0 -and $Command[$i - 1] -eq '|')
            $prevWasPipe = ($ch -eq '|') -and -not $nextIsPipe -and -not $prevIsPipe
            $current = ''
            continue
        }
        $current += $ch
    }

    $segments.Add([pscustomobject]@{ Text = $current; PrecededByPipe = $prevWasPipe }) | Out-Null
    return $segments
}

foreach ($seg in (Split-WithPipeFlag -Command $command)) {
    $text = ([string]$seg.Text).Trim()
    if ([string]::IsNullOrWhiteSpace($text) -or $seg.PrecededByPipe) { continue }

    $tokens = @($text -split '\s+' | Where-Object { $_ -ne '' })
    if ($tokens.Count -eq 0) { continue }

    $first = $tokens[0]
    while ($first -match '^[A-Za-z_][A-Za-z0-9_]*=' -and $tokens.Count -gt 1) {
        $tokens = $tokens[1..($tokens.Count - 1)]
        $first = $tokens[0]
    }
    $bin = ((Split-Path -Leaf $first) -replace '\.exe$', '').ToLowerInvariant()
    if ($searchBinaries -notcontains $bin) { continue }

    # Operands after the binary: the first non-flag is the pattern, the rest are targets. Flags that TAKE a
    # value (-e, --include, -m ...) would otherwise be read as the pattern; only the ones actually used here
    # are handled, and an unrecognised shape simply falls through to "leave it alone".
    $pattern = ''
    $targets = New-Object System.Collections.Generic.List[string]
    $skipNext = $false
    for ($k = 1; $k -lt $tokens.Count; $k++) {
        $tok = $tokens[$k]
        if ($skipNext) { $skipNext = $false; continue }
        if ($tok -match '^--?(e|m|A|B|C|include|exclude|glob|type|max-count)$') { $skipNext = $true; continue }
        if ($tok.StartsWith('-')) { continue }
        if ([string]::IsNullOrEmpty($pattern)) { $pattern = $tok.Trim('"').Trim("'"); continue }
        $targets.Add($tok) | Out-Null
    }

    # No explicit target: the search is repo-wide, so it spans files Rider cannot see. Not ours to redirect
    # (Deny-RootScopedSearch.ps1 already refuses the pathless root case for its own reasons).
    if ($targets.Count -eq 0) { continue }

    $allVisible = $true
    foreach ($t in $targets) { if (-not (Test-RiderCanSee -Target $t)) { $allVisible = $false; break } }
    if (-not $allVisible) { continue }

    Deny -Reason (New-Guidance -Pattern $pattern -Targets $targets.ToArray())
}

exit 0
