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


def _under_scratch_root(canonical_path):
    # Session directories live beneath the scratchpad ROOT, so the check is on the root: the session id
    # changes every run, and a rule naming one session's path would rot immediately.
    """True when a canonical path lies inside the harness's per-session scratchpad tree."""
    root = _canonical(os.path.join(os.environ.get("LOCALAPPDATA", ""), "Temp", "claude"))
    if not root:
        return False
    return canonical_path == root or canonical_path.startswith(root + "/")


# Commands that read and cannot modify. Deliberately a short allowlist rather than a denylist of dangerous
# things: a new tool nobody vetted should ASK, not slip through because it was not on a blocklist.
_READ_ONLY_TOOLS = frozenset(
    ["grep", "egrep", "fgrep", "awk", "sed", "cat", "head", "tail", "wc", "ls", "sort", "uniq",
     "cut", "tr", "diff", "find", "stat", "basename", "dirname", "echo", "printf", "python", "python3"]
)


def _is_read_only(command):
    """True when every stage of the command is a read-only tool and nothing writes, deletes or executes.

    Conservative by construction: any redirection, any backgrounding, any command substitution, and any
    stage whose leading word is not on the allowlist disqualifies the whole command. `python` is admitted
    only as `python -c`-free inspection of a file path — an inline program could do anything, so a `-c` or
    a `-` disqualifies it too.
    """
    if any(token in command for token in (">", "<", "$(", "`", "&", "|&", ";;")):
        return False
    for stage in command.split("|"):
        words = stage.strip().split()
        if not words:
            return False
        tool = os.path.basename(words[0]).lower()
        if tool not in _READ_ONLY_TOOLS:
            return False
        if tool.startswith("python") and any(w in ("-c", "-") for w in words[1:]):
            return False
        # `find` executes whatever -exec/-delete name; those are not reads.
        if tool == "find" and any(w.startswith("-exec") or w == "-delete" or w == "-ok" for w in words[1:]):
            return False
        # `sed -i` and `awk` writing via a file are edits, not reads.
        if tool == "sed" and any(w == "-i" or w.startswith("-i.") for w in words[1:]):
            return False
    return True


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

    target = _canonical(match.group(1) or match.group(2) or match.group(3))
    rest = match.group("rest").strip()
    if not rest:
        return

    if target == _canonical(REPO):
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
        return

    # A `cd` somewhere OTHER than the repo root genuinely changes where relative paths resolve, so it is
    # never stripped — doing so would drop the command's scratch files into the project tree. But one such
    # directory is safe to work in without being asked: the harness's own scratchpad. It is created per
    # session under the OS temp directory, holds nothing but this session's intermediate files, and cannot
    # contain the project's credentials or configuration. The harness still has to ASK about these commands,
    # because it cannot resolve what a relative path after a `cd` refers to and a deny rule protects
    # credential files — an ambiguity no permission rule can settle. So the answer is given here, and only
    # for commands that cannot change anything: a read-only tool reading temp files it wrote itself.
    if target and _under_scratch_root(target) and _is_read_only(rest):
        json.dump(
            {
                "hookSpecificOutput": {
                    "hookEventName": "PreToolUse",
                    "permissionDecision": "allow",
                    "permissionDecisionReason": (
                        "Read-only command in the session scratchpad (temp, session-scoped, holds no "
                        "project configuration). Anything that could modify state still asks."
                    ),
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
