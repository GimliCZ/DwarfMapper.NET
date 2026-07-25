# [ISSUE-038] `global.json` `latestPatch` can float within the feature band (ISSUE-035 follow-up)

| | |
|---|---|
| **Severity** | Low–Medium |
| **Type** | Build reproducibility |
| **Component** | Repo configuration |
| **Finding ID** | R6-1 |
| **Affects** | `global.json`, CI |
| **Status** | **OPEN — machine-conditional; did NOT reproduce on the maintainer's machine** |

## Claim (external audit round 6)
`global.json` pins `10.0.101` with `rollForward: latestPatch`, but the auditor's machine had `10.0.110`
installed and `dotnet --version` resolved to `10.0.110` — i.e. the pin floated within the `10.0.1xx` feature
band, the same band CA1875 arrived in. The build being green is because the code was fixed (`487932a`), not
because the pin froze the analyzer set.

## Verification on the maintainer's machine
`dotnet --version` reports **`10.0.101`** — an exact match to the pin — because only `10.0.101` is installed
here. So the concrete "floats to 10.0.110" observation is **machine-specific**: `latestPatch` resolves to the
exact pinned version when no higher patch of the same feature band is installed.

The **abstract** point stands: `latestPatch` *permits* floating to any higher `10.0.1xx` patch that happens to
be installed, which is where new analyzer rules can land — so the guarantee is weaker than "frozen". It is not a
live defect on this machine, but it is a real reproducibility gap on any machine/CI with a newer patch.

## Options
- **Strict:** `{ "sdk": { "version": "<installed>", "rollForward": "disable" } }` — exact match; an SDK bump
  becomes a deliberate `global.json` commit where newly-enabled rules are fixed and attributed. Downside: fails
  on a machine that lacks that exact patch.
- **Keep `latestPatch`** (current) but document that intra-band analyzer float remains possible, and add a CI
  drift check that fails if `jq -r .sdk.version global.json` != `dotnet --version`.

Deferred to the maintainer — it is a build-environment policy choice, not a code fix, and does not reproduce on
the current machine.
