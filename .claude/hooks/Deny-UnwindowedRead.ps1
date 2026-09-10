# SPDX-License-Identifier: GPL-2.0-only
#
# Deny an UNWINDOWED read of a large file, and say what to do instead.
#
# WHY. Adapted from "Portal by Spotify cut my Claude Code token usage by 90%"
# (engineering.atspotify.com, 2026-09). That article's `shunt` plugin blocks reads over a threshold
# (SHUNT_MIN_LINES, default 350) so bulk reading is delegated to a cheaper model running on Spotify's
# Portal infrastructure. Portal is Spotify-internal — `portal@portal`, `shunt@portal` and `/portal:setup`
# all require a Portal instance — so the DELEGATION half does not transfer. The GATE half does, and it is
# the half that does the work: the saving comes from not pulling a 1,300-line file into context to look at
# forty lines of it, not from which model does the pulling.
#
# The threshold is a budget, not a rule about correctness. A read that names its window is always allowed
# however large the file; a read of a small file is always allowed. What this refuses is the specific
# wasteful shape — "open the whole thing and see".
#
# MEASURED MOTIVATION, from this repository rather than the article: on 2026-09-09 a wrong finding was
# filed because one side of an emitted pair was read and the other was not. The failure was retrieval
# precision, not reasoning. Windowed reads do not fix that by themselves, but they make the cost of
# "read it all and skim" visible at the moment it is paid.
#
# Contract: stdin is the hook payload JSON; stdout is either empty (no opinion) or a PreToolUse decision.
# Any parse failure exits 0 — a hook that cannot read its input must not block work.

$ErrorActionPreference = 'Stop'

# Same knob the article exposes, same default, this repo's own prefix.
$limit = 350
if ($env:DWARF_MAX_UNWINDOWED_READ_LINES) {
    $parsed = 0
    if ([int]::TryParse($env:DWARF_MAX_UNWINDOWED_READ_LINES, [ref]$parsed) -and $parsed -gt 0) { $limit = $parsed }
}

# Extensions with no line semantics, where a "window" is meaningless and the Read tool has its own paging
# (PDF pages, notebook cells, images rendered visually). Never refuse these.
$binaryish = @(
    '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.webp', '.svg', '.ico',
    '.pdf', '.ipynb', '.dll', '.exe', '.pdb', '.zip', '.nupkg', '.snupkg'
)

$raw = ''
try { $raw = [Console]::In.ReadToEnd() } catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try {
    $payload = $raw | ConvertFrom-Json
    if ($payload.tool_name -ne 'Read') { exit 0 }
    $path = [string]$payload.tool_input.file_path
}
catch { exit 0 }
if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

# A read that already names a window is the shape this hook exists to encourage. Either bound counts:
# `limit` caps what comes back, and `offset` means the caller knows where they are going.
$hasWindow = $false
try {
    if ($null -ne $payload.tool_input.limit -or $null -ne $payload.tool_input.offset) { $hasWindow = $true }
}
catch { $hasWindow = $false }
if ($hasWindow) { exit 0 }

if ($binaryish -contains ([System.IO.Path]::GetExtension($path).ToLowerInvariant())) { exit 0 }
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { exit 0 }

# Count lines without materialising the file: a hook that reads the whole file to complain about reading
# the whole file has spent the cost it is trying to save.
$lines = 0
try {
    $reader = [System.IO.StreamReader]::new($path)
    try { while ($null -ne $reader.ReadLine()) { $lines++ } } finally { $reader.Dispose() }
}
catch { exit 0 }

if ($lines -le $limit) { exit 0 }

$name = Split-Path -Leaf $path
$reason = @"
$name is $lines lines and this Read names no window, so it would pull the whole file into context.

Threshold: $limit lines (DWARF_MAX_UNWINDOWED_READ_LINES). A read that names a window is never refused,
however large the file.

Do one of these instead:

  * Read(file_path: "...", offset: <line>, limit: 80) once you know roughly where to look.
  * Grep(pattern: "...", path: "$name", output_mode: "content", -n: true, -C: 5) to find the place first,
    then read that window. Grep IS ripgrep and reports line numbers, which is what offset wants.
  * For a semantic question -- who calls this, what implements it, where is a symbol defined, what does
    this type look like -- prefer the roslyn-lens MCP tools. They answer from the compilation and return
    the symbol, not the file it happens to live in.

If you genuinely need the whole file (a full rewrite, or a review of every line), pass an explicit
`limit` large enough to cover it. Naming the cost is the point; paying it deliberately is allowed.
"@

@{
    hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 6 -Compress

exit 0
