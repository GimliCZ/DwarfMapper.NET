# DwarfMapper.NET — round-28 audit report (2026-09-01/02)

Working tree: 146 changed/new files vs HEAD b1f2e29, nothing committed, nothing pushed.
Full ledger with evidence per finding: `AUDIT-LEDGER.md` (same directory). Patches: `rider-cleanup.patch`
(100 files), `consumer-fixes.patch` (11 files: the three consumer defects plus the same two shapes swept through projection, dictionary values and the `[MapTo]` registry) — all applied to the tree.

## Verification state of the tree (all on the final tree unless noted)
| Check | Result |
|---|---|
| `dotnet build DwarfMapper.NET.sln` Debug + Release (samples included) | clean, 0 warnings |
| Fast tier, all 9 test projects | green on the final tree's sources (copy run 07:47; Generator.Tests 7209 incl. 17 consumer-shape tests, IntegrationTests 868, CompilerTests 52, NegativeCases 120, Testing.Tests 80, DifferentialTests 70, corpus/consumer 67) |
| Coverage band (closing run, deep tier, final tree) | DwarfMapper 91.5/91.2 · Generator 94.8/94.5 · DocTooling 96.3/96.0 · CodeFixes 96.2/96.2 · Testing 96.4/96.4 — all inside the band |
| Deep tier (`DWARF_DEEP=1`) | green on the FINAL tree (closing run 08:17; Generator.Tests 15281 in 1 m 49 s); also green at 00:39 before the patches |
| 20×-scaled deep tier (temporary catalog patch, reverted) | green: Generator.Tests 130,784 / CompilerTests 52 (16 min) / IntegrationTests 1763 / Testing.Tests 80 |
| Exhaustion (`DWARF_FUZZ_FULL=1`) | green on the FINAL tree (closing run; 766 tests in 10 m); also green at 00:28 before the patches |
| AOT publish + EXECUTE, ILVerify | passed on the final tree (closing run) |
| Benchmarks | full 55-benchmark suite 21:52–22:17 (timing table below, pre-fix tree); the FINAL tree's `-BenchSmoke` pin gate in the closing run: 55/55 executed, all 20 allocation pins exact, blit ratio array 2.69× / list 1.87× (floor 1.50×) — the blit path is still taken after the proof fixes |
| Package-size gate | 282 KB main (Windows 288,884 B; ubuntu 288,676 B = 281, the platforms straddle the boundary by 208 B of CRLF; the ceiling is the larger measurement) / 47 KB Testing |
| Generator mutation leg | PASSED 07:23–08:14 (51 min): 330 killed / 48 survived / 13 uncovered / 0 timeouts of 391 = 84.40 %, break 84 holds (population 258 → 391 from this audit's BlittableProof/LocationInfo fixes; 43 BlittableProof survivors are the next kill program; a first attempt was stopped by hand on a misread of Stryker's buffered progress output — the cap was fine; `housekeeping -Mutation`'s 60-minute cap is now tight against 51 measured) |

## Findings (26), by severity
**HIGH, fixed:** F11 (DWARF064 fix applied to 1 of N sites — projection endpoint), F12 (blit proof re-sorted
fields by file path; accepted reversed layouts), F13 (proof ignored `StructLayout.Size`, `[InlineArray]`,
fixed-buffer length), F19 (Testing coverage floor 87.1 never met — every `-Coverage`/`-Nightly` run since
08-26 stopped at the gate), F20 (graph oracle's violation paths executed by no test — 27 negative controls
added), F24 (nullable-element collections → CS8604 per element in the consumer's build; swept through dictionary values and the `[MapTo]` registry, where the collection helper also mis-declared its parameter and return annotations), F25 (constructor
arguments never null-forgiven / no DWARF070, unlike members; swept through the projection endpoint, which now forgives and reports DWARF070 too).
**HIGH, reported:** F1 (nightly CI red 10 nights, three distinct causes), F2 (mutation jobs fail at the
decontamination step, not the score).
**MEDIUM, fixed:** F4 (package ceiling 247 KB never re-measured since round 24; the gate exists only in the
nightly job), F8, F10, F17 (nested-pair diagnostics had no location), F26 ([Obsolete] enum members → CS0618).
**MEDIUM, reported:** F3 (mutation(pipeline) wall-time 67 → 98 min → 350-min timeout).
**LOW:** F6, F7, F9, F15, F16 (doc fixed; `Validate()` uniqueness gap reported), F18 (fixed), F14, F21, F22
(analyzer PDBs ship nowhere), F23 (attestation excludes SBOM/SHA256SUMS; SBOM is whole-solution) — reported
with the fix in each case. F5 NOTE (preview-SDK canary firing, unread).

## Things a reader should decide (not done, on purpose)
- F21 NaN/∞ comparer fix, F22 `DebugType=embedded` for the analyzers (+ ceiling re-measure), F23 SBOM scope
  (`--exclude-test-projects --exclude-dev`) and attestation subjects — one-line changes each, policy calls.
- Adding a local pack + `Assert-PackageSizeWithinCeiling` to `housekeeping -Nightly`, so the one gate the
  script does not mirror becomes visible before CI.
- The generator's object-helper signature (`S → T` with `return null!`) is the honest-annotation gap behind
  F24; the precise fix (`S? → T?` + `[return: NotNullIfNotNull]`) rewrites every golden snapshot.
- Rider: ~330 nullability guesses in the netstandard2.0 projects are an IDE-severity setting, not code.

## Benchmarks (BenchmarkDotNet, N=1000 unless noted; from `bench-table.md`)
| Category | Dwarf | Mapperly | Mapster | AutoMapper |
|---|---:|---:|---:|---:|
| Flat (1 obj) | 5.52 ns / 40 B | 5.43 / 40 | 14.65 / 40 | 53.47 / 40 |
| Nested | 11.09 / 112 | 11.80 / 112 | 20.41 / 112 | 58.90 / 112 |
| Array | 4,917 / 48,048 | 4,925 / 48,048 | 6,530 / 48,048 | 5,696 / 48,048 |
| List | 6,032 / 48,112 | 5,904 / 48,112 | 5,572 / 48,112 | 8,669 / 56,656 |
| Dict | 8,202 / 31,120 | 19,333 / 31,176 | 26,023 / 102,376 | 18,950 / 102,320 |
| Blit (struct reinterpret) | 417 / 12,048 | 960 / 12,048 | 988 / 12,048 | 1,055 / 12,048 |
| Widen | 344 / 8,048 | 436 / 8,048 | 714 / 8,048 | 730 / 8,048 |
| NumList | 669 / 8,112 | 961 / 8,112 | 938 / 8,112 | 2,359 / 16,656 |
| NestedFill | 916 / 8,112 | 1,764 / 9,456 | 1,659 / 8,112 | 2,095 / 10,776 |
| Enum by name | 12.6 / 24 | 12.4 / 24 | — | 74.3 / 48 |
BlitRatio fast/scalar: array 434 vs 1,063 ns (2.45×), list 453 vs 1,077 ns (2.38×). Figures are Windows,
12-core; the Dict lead is a Mapperly property, not ours (see memory).
