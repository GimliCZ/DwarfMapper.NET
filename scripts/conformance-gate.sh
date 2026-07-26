#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
#
# Conformance gate — Phase 5 of the test-hardening programme (spec 2026-07-26).
#
# samples/DwarfMapper.Conformance exercises every feature and returns a real exit code from its assertions.
# It was never executed by CI, so it only ever ran when a human remembered — which makes it a ritual, not a
# gate. Same for the AOT sample: `aot-trim-gate` proves the binary COMPILES trim/AOT-clean and never runs it.
#
# This runs them and, crucially, FAILS CLOSED. A gate that only fails when the run fails still passes when the
# run never happened, and "skipped" and "passed" look identical in a green check. So absence is failure here:
# an unmeasurable run, a missing/short artifact, or an assertion count that went DOWN all exit non-zero.
# (What a script cannot check is whether it was invoked at all — see the note further down.)
#
# Usage:  scripts/conformance-gate.sh [rid]
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESULTS_DIR="$ROOT/conformance/results"
DATE="$(date -u +%Y-%m-%d)"
ARTIFACT="$RESULTS_DIR/$DATE-$RID.md"
COMMIT="${GITHUB_SHA:-$(git -C "$ROOT" rev-parse HEAD)}"

mkdir -p "$RESULTS_DIR"

echo "== conformance gate: $RID @ ${COMMIT:0:12} =="

# ── Run the proof ────────────────────────────────────────────────────────────────────────────────────────
# `set -e` would abort before the exit code can be inspected and reported, so capture it deliberately.
set +e
OUTPUT="$(dotnet run --project "$ROOT/samples/DwarfMapper.Conformance" -c Release 2>&1)"
EXIT=$?
set -e

echo "$OUTPUT"

# The sample's final line is "<pass> passed, <fail> failed  (of <total>)".
SUMMARY="$(printf '%s\n' "$OUTPUT" | grep -E '[0-9]+ passed, [0-9]+ failed' | tail -1 || true)"
PASSED="$(printf '%s' "$SUMMARY" | sed -nE 's/.*?([0-9]+) passed.*/\1/p')"
FAILED="$(printf '%s' "$SUMMARY" | sed -nE 's/.*, ([0-9]+) failed.*/\1/p')"
TOTAL="$(printf '%s' "$SUMMARY" | sed -nE 's/.*\(of ([0-9]+)\).*/\1/p')"

# A parse failure must not be mistaken for success: without a count there is nothing to ratchet against, so
# the gate refuses rather than writing an artifact claiming zero assertions.
if [[ -z "${TOTAL:-}" ]]; then
  echo "FAIL: could not parse an assertion summary from the conformance run." >&2
  echo "      The gate cannot certify a run it cannot measure." >&2
  exit 1
fi

# ── Ratchet: the assertion count may only grow ───────────────────────────────────────────────────────────
# Deleting assertions to make a red build green is the failure mode this catches; it is silent otherwise,
# because a smaller suite still reports "all passed".
PREVIOUS="$(ls -1 "$RESULTS_DIR"/*.md 2>/dev/null | grep -v "$(basename "$ARTIFACT")" | tail -1 || true)"
if [[ -n "$PREVIOUS" ]]; then
  PREV_TOTAL="$(sed -nE 's/^- \*\*Assertions:\*\* ([0-9]+).*/\1/p' "$PREVIOUS" | head -1)"
  if [[ -n "${PREV_TOTAL:-}" ]] && (( TOTAL < PREV_TOTAL )); then
    echo "FAIL: assertion count decreased: $PREV_TOTAL -> $TOTAL (previous: $(basename "$PREVIOUS"))." >&2
    echo "      Conformance coverage must only grow. If an assertion was legitimately removed, say so in" >&2
    echo "      the commit and update the previous artifact deliberately." >&2
    exit 1
  fi
fi

# ── Emit the dated artifact ──────────────────────────────────────────────────────────────────────────────
# Evidence with a date and a commit on it. A green check proves a job ran; it does not say what it proved.
RESULT="PASS"; [[ "$EXIT" -ne 0 ]] && RESULT="FAIL"

cat > "$ARTIFACT" <<EOF
<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Conformance run — $DATE ($RID)

- **Commit:** \`$COMMIT\`
- **RID:** \`$RID\`
- **Assertions:** $TOTAL
- **Passed:** ${PASSED:-0}
- **Failed:** ${FAILED:-0}
- **Result:** **$RESULT**

Produced by \`scripts/conformance-gate.sh\`. This file is the gate's evidence: CI fails if it cannot be
produced, or if the assertion count dropped below the previous run.

\`\`\`
$SUMMARY
\`\`\`
EOF

echo "artifact: ${ARTIFACT#"$ROOT"/}"

# ── Fail closed ──────────────────────────────────────────────────────────────────────────────────────────
if [[ ! -s "$ARTIFACT" ]]; then
  echo "FAIL: no artifact was produced — the gate cannot certify a run that left no evidence." >&2
  exit 1
fi

# NOTE ON WHAT THIS CANNOT DO. An earlier version "verified" that the artifact's commit matched the build
# commit — but this same script had just written that commit, so the check could never fail. It was a
# self-fulfilling assertion dressed as a safeguard, which is worse than no check at all because it reads like
# one. Removed deliberately.
#
# "Did the gate run at all?" is genuinely unanswerable from inside the gate. It is a workflow-level property:
# the conformance-gate job must exist in ci.yml (asserted by CiGateScanTests) and be a required check on the
# branch. What this script CAN do is refuse to certify a run it could not measure, and refuse a run whose
# coverage shrank — both above, both able to fail.

# Re-read the artifact and confirm it describes the run we just performed. Catches a truncated or unwritable
# artifact, which would otherwise leave a green gate with unusable evidence.
RECORDED="$(sed -nE 's/^- \*\*Assertions:\*\* ([0-9]+).*/\1/p' "$ARTIFACT" | head -1)"
if [[ "$RECORDED" != "$TOTAL" ]]; then
  echo "FAIL: artifact records '$RECORDED' assertions but the run reported '$TOTAL'." >&2
  exit 1
fi

if [[ "$EXIT" -ne 0 ]]; then
  echo "FAIL: conformance run exited $EXIT ($FAILED assertion(s) failed)." >&2
  exit "$EXIT"
fi

echo "PASS: $TOTAL assertions, commit ${COMMIT:0:12}"
