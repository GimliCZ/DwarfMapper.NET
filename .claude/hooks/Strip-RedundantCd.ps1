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

# Commands that read and cannot modify. An ALLOWLIST, not a denylist of dangerous things: a tool nobody has
# vetted should ask, rather than slip through for not being on a blocklist.
$ReadOnlyTools = @(
    'grep', 'egrep', 'fgrep', 'awk', 'sed', 'cat', 'head', 'tail', 'wc', 'ls', 'sort', 'uniq',
    'cut', 'tr', 'diff', 'find', 'stat', 'basename', 'dirname', 'echo', 'printf'
)

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

function Test-UnderScratchRoot {
    # Session directories live beneath the scratchpad ROOT, so the test is on the root: the session id
    # changes every run and a rule naming one session's path would rot immediately.
    param([string] $CanonicalPath)

    $root = ConvertTo-CanonicalPath (Join-Path $env:LOCALAPPDATA 'Temp/claude')
    if (-not $root) { return $false }
    return $CanonicalPath -eq $root -or $CanonicalPath.StartsWith("$root/")
}

function Test-ReadOnlyCommand {
    # True only when every stage is a read-only tool and nothing writes, deletes or executes. Any
    # redirection, backgrounding or command substitution disqualifies the whole command, as does a stage
    # whose leading word is not on the allowlist.
    param([string] $Command)

    foreach ($token in '>', '<', '$(', '`', '&', ';;') {
        if ($Command.Contains($token)) { return $false }
    }
    foreach ($stage in $Command.Split('|')) {
        $words = $stage.Trim() -split '\s+' | Where-Object { $_ }
        if (-not $words) { return $false }
        $tool = [System.IO.Path]::GetFileName($words[0]).ToLowerInvariant()
        if ($ReadOnlyTools -notcontains $tool) { return $false }
        $rest = @($words | Select-Object -Skip 1)
        # `find -exec/-ok/-delete` executes or deletes; `sed -i` edits in place. None of those are reads.
        if ($tool -eq 'find' -and ($rest | Where-Object { $_ -like '-exec*' -or $_ -eq '-delete' -or $_ -eq '-ok' })) { return $false }
        if ($tool -eq 'sed' -and ($rest | Where-Object { $_ -eq '-i' -or $_ -like '-i.*' })) { return $false }
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

    if ($target -and (Test-UnderScratchRoot $target) -and (Test-ReadOnlyCommand $rest)) {
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
