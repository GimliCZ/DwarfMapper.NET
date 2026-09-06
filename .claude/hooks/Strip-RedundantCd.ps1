# SPDX-License-Identifier: GPL-2.0-only
#
# PreToolUse(Bash) hook. Two jobs, both narrow:
#
#   1. Strip a redundant leading `cd <repo root>` from a command. The session's working directory IS the
#      repo root, so the prefix changes nothing — but the harness cannot resolve the working directory of a
#      compound command, and with a deny rule configured for credential-bearing files it refuses rather than
#      guess, costing a permission prompt every time.
#
#   2. Approve a READ-ONLY command inside the harness's own scratchpad. That directory is created per
#      session under the OS temp path and holds nothing but this session's intermediate files; it cannot
#      contain the project's configuration or credentials. The same working-directory ambiguity applies
#      there and no permission rule can settle it, so the answer is given here — and only for commands that
#      cannot change anything.
#
# A `cd` to any OTHER directory is left completely alone: relative paths would then resolve against the
# repo and drop scratch files into the project tree.
#
# PowerShell rather than Python deliberately. This runs on every Bash call, and pwsh is already required by
# this repository (scripts/*.ps1, `shell: pwsh` in CI), so it adds no runtime to the hot path.
#
# Contract: stdin is the hook payload JSON; stdout is either empty (no opinion) or a PreToolUse decision.
# The exit code is always 0 — a hook that fails must never block a tool call.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The repo root, resolved from this script's own location (.claude/hooks/ -> root) so the hook survives the
# checkout being moved or cloned elsewhere.
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Tools with NO write or execute capability in ANY flag form. Deliberately shorter than "tools that usually
# read", because a leading-word allowlist is not a proof: several obvious candidates were removed after a
# review found each of them could write or execute --
#   awk   `BEGIN{system("…")}` runs a shell, and `print > file` writes;
#   sed   `-i`, `-Ei`, `--in-place` edit the file in place;
#   find  `-exec`, `-ok`, `-delete`, `-fprint` execute or write;
#   sort  `-o FILE` writes;
#   uniq  takes an output file as its second positional argument;
#   python (and any interpreter) runs a script file, which is arbitrary code.
# Nothing goes back on this list without naming the flag set that makes it safe.
$ReadOnlyTools = @(
    'grep', 'egrep', 'fgrep', 'cat', 'head', 'tail', 'wc', 'ls',
    'stat', 'basename', 'dirname', 'echo', 'printf', 'cut', 'tr', 'diff'
)

# Anything that chains, backgrounds, substitutes or redirects ends the analysis: past one of these, the
# leading word of the command says nothing about what else runs. `;` matters most -- without it,
# `cat a.txt; rm -rf x` reads as a `cat`. `|` is NOT here: it is the one separator this hook splits on and
# validates stage by stage.
$ForbiddenTokens = @(';', '&', '>', '<', '$(', '`', "`n", "`r")

function ConvertTo-CanonicalPath {
    # Normalises the three spellings that reach this hook on Windows -- C:\Users\..., C:/Users/... and Git
    # Bash's /c/Users/... -- to one comparable form. Returns $null for anything that is not an absolute path.
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $p = $Path.Trim().Trim('"').Trim("'").Replace('\', '/')
    if ($p -match '^/([a-zA-Z])/(.*)$') { $p = '{0}:/{1}' -f $Matches[1], $Matches[2] }
    if ($p -notmatch '^[a-zA-Z]:/') { return $null }
    return $p.TrimEnd('/').ToLowerInvariant()
}

function Get-ScratchRoot {
    # Session directories live beneath the scratchpad ROOT, so every test is against the root: the session
    # id changes each run and a rule naming one session's path would rot immediately.
    return ConvertTo-CanonicalPath (Join-Path $env:LOCALAPPDATA 'Temp/claude')
}

function Test-ReadOnlyCommand {
    <#
        True only when the command provably cannot modify anything AND cannot read outside the scratchpad.

        Four independent conditions, each closing a hole a review found in the first version of this hook:
          1. No chaining, backgrounding, substitution or redirection (`$ForbiddenTokens`). Without this,
             `cat a.txt; rm -rf x` was auto-approved on the strength of its leading word.
          2. Every pipeline stage's leading word is on `$ReadOnlyTools`, which now contains only tools with
             no write or execute flag in any form.
          3. No argument escapes the scratchpad. The working directory is the scratchpad, so a RELATIVE path
             is fine -- but `..` climbs out, and an ABSOLUTE path can name anything. Without this the hook
             would have auto-approved `cd <scratch> && cat C:/proj/appsettings.json`, overriding the very
             deny rule the user configured. A hook that launders a denied read is worse than no hook.
          4. No `--` style long option that takes a file (`--output`, `--file`), for the same reason as (2):
             a flag can turn a reader into a writer.
    #>
    param([string] $Command, [string] $ScratchRoot)

    foreach ($token in $ForbiddenTokens) {
        if ($Command.Contains($token)) { return $false }
    }

    foreach ($stage in $Command.Split('|')) {
        $words = $stage.Trim() -split '\s+' | Where-Object { $_ }
        if (-not $words) { return $false }   # an empty stage means `||` or a trailing pipe

        $tool = [System.IO.Path]::GetFileName($words[0]).ToLowerInvariant()
        if ($ReadOnlyTools -notcontains $tool) { return $false }

        foreach ($word in @($words | Select-Object -Skip 1)) {
            $arg = $word.Trim('"').Trim("'")
            if (-not $arg) { continue }

            if ($arg.StartsWith('-')) {
                # A long option that names a file turns a reader into a writer.
                if ($arg -match '^--(output|out-file|file|write)') { return $false }
                continue
            }

            if ($arg.Contains('..')) { return $false }   # climbs out of the scratchpad

            $canonical = ConvertTo-CanonicalPath $arg
            if ($canonical) {
                # An absolute path: allowed only if it stays inside the scratchpad tree.
                if (-not $canonical.StartsWith("$ScratchRoot/") -and $canonical -ne $ScratchRoot) { return $false }
            }
            elseif ($arg.StartsWith('/')) {
                return $false   # a POSIX-absolute path this hook cannot canonicalise
            }
        }
    }
    return $true
}

function Write-HookDecision {
    param([hashtable] $Output)
    @{ hookSpecificOutput = $Output } | ConvertTo-Json -Depth 6 -Compress | Write-Output
}

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json
    if ($payload.tool_name -ne 'Bash') { exit 0 }

    $command = $payload.tool_input.command
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

    # A leading `cd <path>` followed by `;` or `&&`. Quoted forms are matched first, because a bare path
    # stops at the first whitespace.
    $pattern = '^\s*cd\s+(?:"([^"]+)"|''([^'']+)''|(\S+))\s*(?:;|&&)\s*(?<rest>.+)$'
    $match = [regex]::Match($command, $pattern, 'Singleline')
    if (-not $match.Success) { exit 0 }

    $target = ConvertTo-CanonicalPath ($match.Groups[1].Value + $match.Groups[2].Value + $match.Groups[3].Value)
    $rest = $match.Groups['rest'].Value.Trim()
    if (-not $rest) { exit 0 }

    if ($target -and $target -eq (ConvertTo-CanonicalPath $RepoRoot)) {
        $updated = @{}
        foreach ($property in $payload.tool_input.PSObject.Properties) { $updated[$property.Name] = $property.Value }
        $updated['command'] = $rest
        Write-HookDecision @{ hookEventName = 'PreToolUse'; updatedInput = $updated }
        exit 0
    }

    $scratchRoot = Get-ScratchRoot
    $inScratch = $scratchRoot -and $target -and ($target -eq $scratchRoot -or $target.StartsWith("$scratchRoot/"))
    if ($inScratch -and (Test-ReadOnlyCommand -Command $rest -ScratchRoot $scratchRoot)) {
        Write-HookDecision @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = 'allow'
            permissionDecisionReason = 'Read-only command in the session scratchpad (temp, session-scoped, holds no project configuration). Anything that could modify state still asks.'
        }
    }
}
catch {
    # Never block a tool call because this hook misbehaved.
}

exit 0
