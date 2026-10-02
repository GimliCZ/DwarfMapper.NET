<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 32 — task log

There is one entry per executed task. Each entry records what was done and the evidence for it. It also records
every place where the task list's instructions were **not** followed literally, with the reason. Escalations go
under their own heading at the end (protocol §6).

## T00 — baseline · `5561ada5`, 2026-10-02

- **Session.** An attended cloud session on `feat/round32`, run start `5561ada5`.
- **Toolchain.** `builds.dotnet.microsoft.com` is blocked by this environment's network policy, and so are
  `dotnetcli.azureedge.net` and `ci.dot.net`. The pinned SDK was installed from another official Microsoft
  channel, which the policy allows:
  1. Fetched the `mcr.microsoft.com/dotnet/sdk:10.0.101-noble-amd64` image manifest.
  2. Downloaded the three layers that carry `/usr/share/dotnet`, each verified against its manifest SHA-256.
  3. Copied `/usr/share/dotnet` into `$HOME/dotnet`.

  The result is Microsoft's own 10.0.101 build, not a rebuild. Ubuntu's archive offered only 10.0.105 and
  10.0.112, which the pin forbids. `dotnet --version` prints `10.0.101` in the repository.

  The other tools:
  - `dotnet-ilverify` 10.0.11 from nuget.org, the version CI pins;
  - PowerShell 7.5.11 from packages.microsoft.com;
  - python3, and ICU 74 already present.

  `DOTNET_CLI_UI_LANGUAGE=en`; the clone was unshallowed earlier in the session.
- **Restore and build.** `dotnet restore DwarfMapper.NET.sln --locked-mode` exit 0, including the NuGet audit.
  `dotnet build DwarfMapper.NET.sln -c Release --no-restore` reports **0 Warning(s), 0 Error(s)**.
- **Default lane.** Run as CI's `build-test` job runs it:
  `--filter "Category!=SurfaceMatrix&Category!=Perf" --blame-hang --blame-hang-timeout 5m`. All nine assemblies
  passed with 0 failed:

  | Assembly | Passed |
  |---|---|
  | CorpusTests | 20 |
  | Testing.Tests | 139 |
  | ConsumerTests.Host | 23 |
  | DifferentialTests | 70 |
  | CleanCorpus | 53 |
  | NegativeCases | 192 |
  | IntegrationTests | 987 |
  | CompilerTests | 52 |
  | Generator.Tests | 7,831 |
- **Surface matrix.** Run as CI's `surface-matrix` job runs it: 901 passed, 0 failed.
- **Golden.** `GoldenCorpusTests` 2/2 passed with `DWARF_GOLDEN_UPDATE` unset; the manifest has 1,014 cases.
- **Audit `baseline`:**

  | Figure | Value |
  |---|---|
  | `head` | 5561ada5 |
  | `sdk` | 10.0.101 |
  | `bom_configs` | 1 |
  | `pragma_src` | 7 |
  | `probes` | 0 |
  | `r1_rows` | 3 |
- **Audit `static`:** every task reports TODO (16 TODO, exit 1), which proves the audit can see work.

## T01 — the runtime config's BOM, test first · `3543f6ae`

Done as specified.

- **RED, three ways:**
  - `StrykerConfigEncodingTests` failed naming exactly `stryker-config.runtime.json: starts with a UTF-8
    byte-order mark`.
  - CI's own config-check Python, lifted unchanged from `ci.yml`, raised `JSONDecodeError: Unexpected UTF-8 BOM`.
  - So did the publish step's `project-info` read.
- **Fix:** the first three bytes removed and nothing else. `cmp` shows old-minus-three-bytes against new:
  16,096 → 16,093 bytes.
- **GREEN, all three:**
  - the test passes;
  - the config check prints `break 97 <= low 97 - runnable`;
  - the publish read prints `module = runtime`.
- **Gate:** Generator.Tests 7,832, CompilerTests 52 and IntegrationTests 987, all green.
- **Owed:** a nightly or dispatch. The runtime leg will run in CI for the first time since `ed69922`, and its
  round-31 red (90.45 % against `break` 97, with a re-measure owed) may now show honestly. That is a known
  owner action, not a regression.

## T08 — R1 covers all six mutation configs · `17874059`

Done as specified, and the prediction made while planning held: two configs went RED, one passed.

- **RED:** with the three `InlineData` rows added and nothing else changed:
  - `codefixes`: "carries no 'MEASURED YYYY-MM-DD' / 'RE-MEASURED YYYY-MM-DD' provenance";
  - `testing`: "quotes no 'score NN.NN %' measurement at all";
  - `pipeline`: passed.
- **The measurements existed in both comments;** only the wording missed R1's patterns. These are one-word
  fixes, and each `stryker-config` section was verified identical before and after:
  - "RE-VERIFIED 2026-09-26" became "RE-MEASURED 2026-09-26";
  - "detected = 100.00 %" became "detected, score 100.00 %".

  Both figures agree with the ledger's rows: codefixes 87.64 % (2026-09-26), testing 100.00 % (2026-09-21).
- **GREEN:** `RatchetInvariantScanTests` 13/13. CI's config check still prints "runnable" for both files.
- **Gate:** Generator.Tests 7,835, CompilerTests 52 and IntegrationTests 987, all green.

`Ruling: change the comments' wording rather than widen R1's regex to accept "RE-VERIFIED" or "= NN.NN %" —
costs if wrong: one revert; widening the instrument was the option that loosens a gate (I-3).`

## T09 — the probe rule is a test · `3deb34b9`

Done as specified. `ProbeFileScanTests` is a guard (green on the tree), so it was shown failing once:

| State | Result |
|---|---|
| clean tree | 1/1 passed |
| `tests/DwarfMapper.Generator.Tests/Round32/ZZPlantedProbe.cs` planted | failed, naming the file |
| plant removed | 1/1 passed |

The plant was never staged. **Gate:** Generator.Tests 7,836, CompilerTests 52 and IntegrationTests 987, all
green.

## T10 — agent worktrees are ignored · `b6d99dce`

Done as specified.

- **RED:** `git check-ignore -q .claude/worktrees/agent/x` → exit 1.
- **GREEN:** exit 0.
- **Control:** `.claude/skills/round/SKILL.md` and `.claude/hooks/*.ps1` are still tracked (exit 1).

## T11 — the NegativeCases README states the rule the ratchet enforces · `48f46a15`

Done as specified. The paragraph quotes the test ("a counted, bounded population"). It names the guard
(`The_exemption_list_is_an_exactly_pinned_bounded_population`) and the constants (`PredatesThisProjectPin`,
`ExemptionIdHorizon`) rather than their values, so it cannot drift from them the way the old one did.
NegativeCases builds with 0 warnings and passes 192/192.

## T12 — the ledgers README names its capture commit · `d8f24aa7`

**Deviation.** The task list said to state that "later ledgers name their own capture point in their headers".
Checked before writing: rounds 21, 22, 23 and 25 do not. The README instead gives `656042c2` (parent `069987f7`)
for the rounds 19 and 20 copies, and the `git log --diff-filter=A` command for every later file. It says only
"some" later headers state their capture.
