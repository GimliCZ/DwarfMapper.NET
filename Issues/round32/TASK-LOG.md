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
