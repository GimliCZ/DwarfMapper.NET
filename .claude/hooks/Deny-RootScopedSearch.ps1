# SPDX-License-Identifier: GPL-2.0-only
#
# PreToolUse(Bash) hook. One job: turn a root-scoped text search into an immediate, self-explaining
# rejection instead of a permission prompt the owner has to answer.
#
# WHY. A global deny rule protects credential-bearing configuration files (.env, appsettings*.json,
# secrets). A recursive `grep`/`rg` whose path is the repository root — or is omitted, which means the
# current directory — could read those files, and the harness cannot prove otherwise, so it escalates to the
# owner. Every such command therefore costs a human interruption, and the owner has twice asked not to be
# interrupted for tool approvals. Twice in one round a subagent spent a turn on exactly this.
#
# The repository already forbids it (`global-constraints.md`: use the dedicated Grep tool with an explicit
# repo-relative path). Writing the rule down did not stop it. This makes it hard to get wrong: the call is
# denied here, before the prompt, with a message naming the tool to use instead, so the agent reads the
# reason and retries correctly within the same turn.
#
# DENY, NEVER APPROVE. This hook can only refuse. An earlier hook in this repository tried to auto-APPROVE
# commands and a review found it unsound — a leading-word allowlist says nothing about what the rest of a
# command does, and a hook that launders a denied read is worse than no hook at all. Refusing is safe in a
# way approving is not: the worst case is a command the owner could have allowed, which they can still run.
#
# NARROW BY CONSTRUCTION. Only a RECURSIVE search (-r/-R/--recursive, or `rg`, which recurses by default)
# whose every path operand is the repo root, `.`, `./`, or absent. A search naming `src`, `tests`, a single
# file, or any other path passes untouched — including a recursive one.
#
# PowerShell rather than Python deliberately, matching Strip-RedundantCd.ps1: pwsh is already required by
# this repository, so it adds no new runtime to the hot path.
#
# Contract: stdin is the hook payload JSON; stdout is either empty (no opinion) or a PreToolUse decision.
# The exit code is always 0 — a hook that fails must never block a tool call.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The per-segment test is a FUNCTION, and the loop that calls it sits outside any `try`, because of a
# PowerShell trap this hook was measured falling into: `continue` inside a `try` block does not continue the
# enclosing loop, it abandons it. With the loop inside the try, the first skipped segment silently ended the
# scan — and `cd …` and `&&` are both skipped, so EVERY compound command sailed through while single-segment
# ones were caught. That is precisely the shape of the command this hook exists to stop. Loop control never
# crosses a try boundary here; `return` from a function does.
function Test-RootScopedSearch {
    param([string] $Text)

    $trimmed = $Text.Trim()
    if ($trimmed -eq '') { return $false }

    # Tokenise on whitespace, ignoring quoting: this only ever decides to REFUSE, so a mis-split can cost a
    # false refusal (recoverable, and the message says how) but never a false approval.
    # -split returns a SCALAR for a single token, whose [0] would be its first CHARACTER — hence @(...).
    $tokens = @($trimmed -split '\s+' | Where-Object { $_ -ne '' })
    if ($tokens.Count -eq 0) { return $false }

    # Leaf of the command word WITHOUT Split-Path, which throws on tokens holding shell metacharacters
    # (a bare `&&` separator, say) and would land in the caller's catch.
    $tool = ($tokens[0] -split '[/\\]')[-1]
    $isGrep = $tool -in @('grep', 'egrep', 'fgrep')
    $isRg = $tool -in @('rg', 'ripgrep')
    if (-not ($isGrep -or $isRg)) { return $false }

    # `1..0` counts DOWNWARD in PowerShell and would index the array backwards, so guard the 1-token case.
    $rest = if ($tokens.Count -gt 1) { $tokens[1..($tokens.Count - 1)] } else { @() }

    # `rg` recurses by default; grep only with an explicit flag, including inside a bundle such as -rn.
    $recursive = $isRg
    foreach ($t in $rest) {
        if ($t -eq '--recursive') { $recursive = $true; continue }
        if ($t -like '--*') { continue }
        if ($t -cmatch '^-[A-Za-z]*[rR]') { $recursive = $true }
    }
    if (-not $recursive) { return $false }

    # Path operands: drop flags, drop a flag's value, and drop the first bare token (the pattern).
    $takesValue = @('-e', '-f', '--include', '--exclude', '--exclude-dir', '--glob', '-g', '-m',
        '--max-count', '-A', '-B', '-C', '--type', '-t', '--regexp', '--file')
    $paths = @()
    $seenPattern = $false
    $skipNext = $false
    foreach ($t in $rest) {
        if ($skipNext) { $skipNext = $false; continue }
        if ($t -like '-*') {
            if ($t -in $takesValue) { $skipNext = $true }
            continue
        }
        # Redirections are not path operands. `grep -r x . 2>/dev/null` would otherwise contribute
        # "2>/dev/null" as a second path, which resolves to nothing, which reads as "not root-scoped" and
        # lets the exact command this hook exists to stop through. Measured, not imagined.
        if ($t -match '[<>]') { continue }
        if (-not $seenPattern) { $seenPattern = $true; continue }
        $paths += $t
    }

    # No path operand at all means "search the current directory" — the repo root — which is the case.
    if ($paths.Count -eq 0) { return $true }

    foreach ($p in $paths) {
        $clean = $p.Trim('"', "'").TrimEnd('/', '\')
        if ($clean -eq '.' -or $clean -eq '') { continue }

        # The shell expands these to the working directory; this hook cannot, so name them. Without this,
        # `grep -r x "$PWD"` reads as an unresolvable path and therefore as not root-scoped.
        if ($clean -in @('$PWD', '${PWD}', '$(pwd)', '`pwd`', '$CWD')) { continue }

        $full = $null
        try { $full = (Resolve-Path -LiteralPath $clean -ErrorAction Stop).Path.TrimEnd('/', '\') } catch { }
        if ($null -ne $full -and $full -eq (Get-Location).Path.TrimEnd('/', '\')) { continue }

        return $false
    }

    return $true
}

$raw = ''
try { $raw = [Console]::In.ReadToEnd() } catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

$command = ''
try {
    $payload = $raw | ConvertFrom-Json
    if ($payload.tool_name -ne 'Bash') { exit 0 }
    $command = [string]$payload.tool_input.command
}
catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

# Split on the separators that start a new command, so `cd x && grep -r . ` is inspected too — but ONLY
# outside quotes. A naive `-split` on `|` tears a quoted regex in half: `grep -rc "\[Fact\]\|\[Theory\]" path`
# became the fragment `grep -rc "\[Fact\]\`, which has a recursive flag and no path operand left, and so read
# as a pathless root search. That refused a correct command — measured, on this hook's own author, within
# minutes of installing it. A false refusal is the cheap failure here, but it is still a failure.
function Split-Segments {
    param([string] $Command)

    $segments = New-Object System.Collections.Generic.List[string]
    $current = ''
    $quote = [char]0

    for ($i = 0; $i -lt $Command.Length; $i++) {
        $ch = $Command[$i]

        if ($quote -ne [char]0) {
            $current += $ch
            if ($ch -eq $quote) { $quote = [char]0 }
            continue
        }
        if ($ch -eq '"' -or $ch -eq "'") {
            $quote = $ch
            $current += $ch
            continue
        }
        if ($ch -eq ';' -or $ch -eq '|' -or $ch -eq '&') {
            $segments.Add($current) | Out-Null
            $current = ''
            continue
        }
        $current += $ch
    }

    $segments.Add($current) | Out-Null
    return $segments
}

$offending = $false
foreach ($segment in (Split-Segments -Command $command)) {
    $hit = $false
    try { $hit = Test-RootScopedSearch -Text $segment } catch { $hit = $false }
    if ($hit) { $offending = $true; break }
}

if (-not $offending) { exit 0 }

$reason = @"
Root-scoped recursive search refused by .claude/hooks/Deny-RootScopedSearch.ps1 (not by the permission system).

Searching the repository root would read credential-bearing configuration files that a global deny rule
protects, so this cannot be approved without interrupting the owner — who has asked not to be interrupted
for tool approvals.

Use a SCOPED search instead. For C# source, Route-SearchToRider.ps1 will send you to the Rider MCP
(`search_regex --q "..."`), which is solution-scoped and returns line AND column spans. For scripts/,
Issues/, docs/ and .github/ -- which Rider's search cannot see -- the Grep tool is the right instrument and
takes an explicit path:

    Grep(pattern: "<your pattern>", path: "src/DwarfMapper.Generator", output_mode: "content")

Everything in this repository lives under: src, tests, docs, scripts, samples, benchmarks. Search the one
that holds your answer. For semantic questions -- who calls this, what implements this, where is this
defined -- prefer the roslyn-lens MCP tools over any text search.

If a scoped search genuinely cannot answer your question, report it as unverifiable rather than widening
the search.
"@

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 6 -Compress

exit 0
