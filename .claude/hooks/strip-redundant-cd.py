#!/usr/bin/env python3
"""PreToolUse(Bash) hook: strip a redundant leading `cd <project root>` from a command.

Why this exists
---------------
Claude Code cannot resolve the working directory of a compound command like

    cd /c/Users/.../DwarfMapper.NET; grep -n "foo" tests/Some.cs

so, with a deny rule configured for credential-bearing files, it refuses rather than guess —
and every such call costs a permission prompt. The `cd` is pure habit: the session's working
directory is ALREADY the project root, so the prefix changes nothing.

This hook removes exactly that redundancy and nothing else. It rewrites the command only when
the `cd` target resolves to this repository's root; every other command passes through
untouched, and the rewritten command still goes through all normal permission checks — the
deny rules and the secrets guard are unaffected. It never grants permission for anything.

Contract: stdin is the hook payload JSON; stdout is either empty (no opinion) or a
PreToolUse `updatedInput` object. Exit code is always 0 — a hook that fails must not block
the tool call.
"""
import json
import os
import re
import sys

# Resolve the repository root from this file's location (.claude/hooks/ -> repo root), so the
# hook keeps working if the checkout moves.
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def _canonical(path):
    """Normalise a shell path to a comparable form.

    Accepts the three spellings that reach this hook on Windows: `C:\\Users\\...`,
    `C:/Users/...` and Git Bash's `/c/Users/...`. Returns a lowercase, forward-slash,
    trailing-slash-free string, or None when the argument is not an absolute path.
    """
    p = path.strip().strip('"').strip("'")
    if not p:
        return None
    p = p.replace("\\", "/")
    # Git Bash drive form: /c/Users/... -> c:/Users/...
    m = re.match(r"^/([a-zA-Z])/(.*)$", p)
    if m:
        p = f"{m.group(1)}:/{m.group(2)}"
    if not re.match(r"^[a-zA-Z]:/", p):
        return None
    return p.rstrip("/").lower()


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return  # Malformed payload is not this hook's problem; stay silent.

    if payload.get("tool_name") != "Bash":
        return
    command = (payload.get("tool_input") or {}).get("command")
    if not isinstance(command, str) or not command.strip():
        return

    # Match a leading `cd <path>` followed by `;` or `&&`. The path may be quoted or bare;
    # a bare path stops at the first whitespace, which is why quoted forms are matched first.
    match = re.match(
        r"""^\s*cd\s+(?:"([^"]+)"|'([^']+)'|(\S+))\s*(?:;|&&)\s*(?P<rest>.+)$""",
        command,
        re.DOTALL,
    )
    if not match:
        return

    target = match.group(1) or match.group(2) or match.group(3)
    if _canonical(target) != _canonical(REPO):
        return  # A cd somewhere else is meaningful — leave it alone.

    rest = match.group("rest").strip()
    if not rest:
        return

    updated = dict(payload.get("tool_input") or {})
    updated["command"] = rest
    json.dump(
        {
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "updatedInput": updated,
            }
        },
        sys.stdout,
    )


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass  # Never block a tool call because this hook misbehaved.
    sys.exit(0)
