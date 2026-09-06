# Round 30 backlog — de-silencing inventory (taken 2026-09-03, read-only scan of the tree at feat/round29-hardware-mode)

The user's standing mandate (2026-09-03): "remove silences of errors and warnings and exclusions as much as possible"
and "let the user know at the exact location what is wrong". This is the inventory that mandate starts from. Nothing
here is judged yet; the judgment is the round-30 work. Counts are of SITES, verified against line numbers on the day.

## Summary

| Kind | Count | Verdict class |
|---|---|---|
| `#pragma warning disable` (hand-written + emitted) | 26 sites / 21 files | 4 sites carry NO reason (below) |
| `[SuppressMessage]` attributes | 4 (tests only) | all reasoned |
| `// ReSharper disable` | 33 (src 6, tests 25, samples 2) | naming-convention headers; Rider-only, not build-visible |
| `<NoWarn>` elements | 19 across 13 project files | **13 files give NO reason** — incl. shipped `src/DwarfMapper.Testing` |
| `AnalysisMode` downgraded to `Recommended` | 2 samples (Conformance, Gallery) | no reason adjacent |
| `.editorconfig` severity → none | 4 ids, scoped to ONE file (`SurfaceFixtures.cs`) | reasoned (reflection-read fields) |
| `[ExcludeFromCodeCoverage]` | 9 sites, one category (compile-time-only attributes) | reasoned; sanctioned round 22 |
| Coverage tool ignore lists | `codecov.yml` ignore of samples/benchmarks/tests | reasoned |
| Stryker exclusions | 0 negations, 0 ignore-methods; 1 `// Stryker disable all` (DocSnippetInjector.cs:36) | pinned at exactly 1 by a self-test |
| Skipped tests / assertion-free tests | 0 / 0 | — |
| Env-gated early return | 1 (`DWARF_FUZZ_FULL`) | reasoned (routine run stays fast; pairwise theory is always on) |
| `try/catch` in the generator | 0 | — |
| Deliberately unreported generator condition | 1: `BlittableProof.TryExplainNearMiss` stays silent when field counts or field TYPES differ | design decision (round 25) — revisit under the mandate |
| `DeclaredDivergences` | 13 findings / 76 cells, shrink-only ratchet | round-19 ruling: declared, not ratified |
| Surface-matrix `NotApplicable` | 28 + 8 cells, every one with a non-empty reason (enforced) | — |

## Priority 1 — shipped code

- `src/DwarfMapper.Testing/DwarfMapper.Testing.csproj:10` — `<NoWarn>CA5394;CA1510;CA1032</NoWarn>` with no reason. This assembly SHIPS. CA5394 (insecure randomness) in a testing helper needs a stated non-security use or a `Random` replacement; CA1510 (use `ArgumentNullException.ThrowIfNull`) and CA1032 (standard exception constructors) are fixable, not silenceable.
- `src/DwarfMapper.Generator/Pipeline/BlittableProof.cs:137-146` — the near-miss explanation is withheld when field counts or types differ. The mandate wants the exact reason at the exact location; a "same field NAMES, different TYPES" pair is the consumer's most likely honest mistake (e.g. `int` vs `long` Zip). Candidate: widen `TryExplainNearMiss` to name the first differing field when the names line up (keep silence only when nothing lines up).
- `src/DwarfMapper.Generator/Pipeline/EnumConverter.cs:434` — the emitted `#pragma warning disable CS0612, CS0618` is the one silence the product writes into consumer code. Justified (F26/F27) and scoped to one switch; keep, but the docs page must say it exists (check `docs/diagnostics.md` / howto).

## Priority 2 — pragmas and NoWarn with no reason (tests and samples)

Pragmas without a reason line (the repo's own rule is "reason line + bare directive"):
- `tests/DwarfMapper.IntegrationTests/UserConversionRuntimeTests.cs:3` — `CA1815, CA1062, CA2225, CA1305, IDE0079` — no comment anywhere in the file.
- `tests/DwarfMapper.Generator.Tests/FeatureCombinationInvalidTests.cs:9` — `CA1062` — file comment discusses intent, not the id.
- `tests/DwarfMapper.IntegrationTests/SetNullCollectionCycleRuntimeTests.cs:6` — `CA5394` — sibling files carry the reason, this one does not.
- `tests/DwarfMapper.Generator.Tests/RegistryDiagnosticsGenTests.cs:458,476` — `CS8625` inside test-source strings (exercising a null-literal argument) — needs the reason in the surrounding test, not in the fixture.

`<NoWarn>` without an adjacent reason (13 files): `samples/DwarfMapper.AotBench` (:16), `samples/DwarfMapper.AotSample` (:24 — includes DWARF038/DWARF044, i.e. the sample silences the product's own diagnostics), `samples/DwarfMapper.Conformance` (:10-11, plus `AnalysisMode=Recommended`), `samples/DwarfMapper.Gallery` (:10,15, plus `AnalysisMode=Recommended`), `tests/DwarfMapper.CompilerTests` (:9), `tests/DwarfMapper.ConsumerTests/{Contracts,ProviderA,ProviderB}` (:10 each), `tests/DwarfMapper.ConsumerTests/Host` (:17), `tests/DwarfMapper.CorpusTests` (:8 — includes CA1062), `tests/DwarfMapper.Generator.Tests` (:13 — includes CA1508), `tests/DwarfMapper.NegativeCases` (:8), `tests/DwarfMapper.Testing.Tests` (:7). The template to copy is `tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj:6-24` (one comment per id).

Rule to adopt (round 30): a `RatchetInvariantScanTests` check that every `<NoWarn>` id in every project file has a comment naming it within the preceding lines, and that every `#pragma warning disable` in hand-written code has a reason line — the same shape as the existing `// Stryker disable` count pin. Sample projects silencing DWARF0xx ids get a per-id justification or the sample is fixed to be clean (a sample that silences the product's diagnostics teaches the consumer to do the same).

## Priority 3 — design-level

- `DeclaredDivergences` (13/76): each is a known behavioural inconsistency between endpoints. The round-20 plan left A4–A11 fixing them; whatever remains after round 29 is round-30 material — a divergence is a place where the user gets different behaviour for the same directive with no diagnostic saying so.
- ReSharper naming headers (25 test files): consolidate into `.editorconfig` `dotnet_naming_rule` exemptions for test methods, or a single `[assembly:]`-level Rider suppression, so 25 identical headers become one reasoned place.

## Priority 1b — an undeclared second language runtime in the build (raised by the owner, 2026-09-06)

This is not a silenced warning, but it is the same class of finding: something the build depends on that
nothing declares. A consumer auditing this toolchain — the CRA framing the round works under — would not
expect a C# compiler plugin's gates to require Python, and nothing tells them.

Measured, not asserted:
- **Five committed `scripts/*.py`**: `extracted-reach.py`, `seam-reach.py`, `repro-pack-check.py`,
  `verify-extraction-bodies.py`, `verify-extraction-sites.py`.
- **Three direct `python3` invocations in `.github/workflows/ci.yml`** (lines ~228, ~612, ~890), one of them
  running `repro-pack-check.py` — so the reproducible-build gate itself does not pass without Python.
- **Five `scripts/*.ps1` call into a `.py`**, and two of them (`extracted-reach`, `seam-reach`) exist as a
  `.ps1`/`.py` PAIR where the shell script is a thin wrapper. Two implementations of one job are free to
  drift, which is the DRY half of the mandate.

Nothing here is broken today. The questions for the round are: (1) is the dependency declared anywhere a
consumer or a CI maintainer would look — README, CONTRIBUTING, a tool-versions manifest — and if not, declare
it; (2) can the five scripts move to PowerShell (already required: nine `.ps1`, `shell: pwsh` in CI) or to C#
(.NET 10 file-based apps run a single `.cs` with no project, and the SDK is required regardless), removing the
runtime rather than documenting it; (3) collapse the two `.ps1`/`.py` pairs to one implementation either way.

Precedent already set: `.claude/hooks/Strip-RedundantCd.ps1` was ported from Python to PowerShell in `a3f309d`
for exactly this reason. That one was easy — it is Claude Code tooling on a latency-sensitive path with no
project code depending on it. The `scripts/` five are load-bearing for CI and need their gates re-run after any
port, so this is a round-30 task, not a drive-by.

## Not defects (recorded so nobody re-inventories them)

- The 9 `[ExcludeFromCodeCoverage]` on compile-time-only attributes (round-22 §2.2), the `codecov.yml` ignore of non-shipping trees, the `SurfaceFixtures.cs`-scoped editorconfig (reflection-read fields), the `SurfaceMatrix` category (run in its own CI step, not dropped), the one `// Stryker disable all` (pinned), the `DWARF_FUZZ_FULL` gate.
