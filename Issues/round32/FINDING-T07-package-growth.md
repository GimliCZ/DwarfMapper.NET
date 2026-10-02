<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding (round 32 T07): what grew the shipped packages past their ceilings

**Question** (round 32 finding, note B). Nightly run 213 packed `DwarfMapper` at 361,965 B (353 KB), against a
322 KB ceiling. It packed `DwarfMapper.Testing` at 54,616 B (53 KB), against 52 KB. The ceilings were last moved
in `1972246a` (2026-09-09). Which changes grew the packages, and was any of the growth unintended? Labels follow
protocol §7.

**Answer.** Every changed entry traces to intended work in rounds 30 and 31. The one entry class this gate
exists to catch, a newly shipping file, appears once: `build/DwarfMapper.props` and its `buildTransitive/` twin.
Round 31 T17 added them on purpose and says so. No growth is unattributed [MEASURED]. The ceiling move itself is
the owner's (D3), and one of its inputs cannot be measured here (§4).

## 1. Method [MEASURED]

- **Recipe:** CI's `package-size` job, step for step:
  - `CI=true`, which switches on `ContinuousIntegrationBuild` and `RestoreLockedMode` in
    `Directory.Build.props`;
  - `dotnet restore DwarfMapper.NET.sln --locked-mode`;
  - then, for `src/DwarfMapper/DwarfMapper.csproj` and `src/DwarfMapper.Testing/DwarfMapper.Testing.csproj`,
    `dotnet pack "$proj" -c Release -o <dir> -p:EnablePackageValidation=false`.
- **Toolchain:** SDK 10.0.101, Microsoft's build from `mcr.microsoft.com/dotnet/sdk:10.0.101-noble-amd64`, on
  Ubuntu 24.04 x64.
- **Where:** a scratch clone outside the repository, with `origin` set to the GitHub URL as CI's checkout has it.
  Each pack started from `git clean -xdf`.
- **Reproducibility.** Each commit was packed twice. `scripts/repro-pack-check.py` passed every pair: 26 entries,
  or 28 at `24a31def`, byte-for-byte. The lengths moved by at most 1 B between runs, which the script's header
  attributes to NuGet's random `.psmdcp` part name. The task's STOP condition did not fire.
- **Calibration against CI.** The first packs of `24a31def`, the commit run 213 packed, came out 19 B and 20 B
  under CI's. The scratch clone had a detached HEAD, so the nuspec lacked CI's `branch="refs/heads/master"`.
  Repacked on a `master` branch:

  | Package | This session | Run 213 (CI) |
  |---|---:|---:|
  | `DwarfMapper` | 361,963 B | 361,965 B |
  | `DwarfMapper.Testing` | 54,613 B | 54,616 B |

  Every whole-KB figure below agrees with CI's. The interval tables use the detached packs, the same way at all
  three commits, so the branch attribute cancels out.

## 2. Sizes

| Commit | What it is | `DwarfMapper` (runs 1 / 2) | KB | `DwarfMapper.Testing` (runs 1 / 2) | KB |
|---|---|---:|---:|---:|---:|
| `1972246a` | the last ceiling move | 329,526 / 329,525 | 321 | 53,678 / 53,679 | 52 |
| `b688bb90` | round 30's merge | 342,397 / 342,397 | 334 | 54,060 / 54,059 | 52 |
| `24a31def` | round 31's merge, run 213 | 361,946 / 361,947 | 353 | 54,596 / 54,595 | 53 |

The ceilings are 322 and 52. `DwarfMapper` went red during round 30. `DwarfMapper.Testing` went red during
round 31.

## 3. Entry by entry

Deflated bytes, run 1 of each commit. The `(zip headers)` row is the archive's own structure. Each table's rows
sum exactly to the change in file length.

### `DwarfMapper`, `1972246a` → `b688bb90` (round 30): +12,871 B

| Entry | Raw | Deflated | Δ | Source |
|---|---|---|---:|---|
| `analyzers/…/DwarfMapper.Generator.dll` | 674,304 → 702,464 | 221,899 → 232,540 | +10,641 | 125 commits to `src/DwarfMapper.Generator`: 53 files, +2,466/−1,175 lines |
| `lib/net10.0/DwarfMapper.dll` | 43,520 → 46,080 | 18,320 → 19,732 | +1,412 | 4 commits to `src/DwarfMapper`, among them `e01ff233` (`Key` becomes a readonly record struct, an owner ruling), `77dc3452` and `53cd56e9` |
| `lib/net10.0/DwarfMapper.xml` | 175,445 → 177,540 | 40,799 → 41,418 | +619 | the same commits' doc comments |
| `analyzers/…/DwarfMapper.CodeFixes.dll` | 36,864 → 36,864 | 17,224 → 17,352 | +128 | 4 commits to `src/DwarfMapper.CodeFixes`, +55/−32 lines; the change fits inside the PE's alignment padding, so only the deflated size shows it |
| `README.md` | 81,577 → 81,003 | 28,133 → 28,205 | +72 | 15 commits, mostly re-pinned mutation and coverage badges |
| `DwarfMapper.nuspec`, `_rels/.rels` | unchanged | | +1, −2 | the commit hash in `<repository>`; NuGet's random part name |

### `DwarfMapper`, `b688bb90` → `24a31def` (round 31): +19,549 B

| Entry | Raw | Deflated | Δ | Source |
|---|---|---|---:|---|
| `analyzers/…/DwarfMapper.Generator.dll` | 702,464 → 736,256 | 232,540 → 244,824 | +12,284 | 19 commits to `src/DwarfMapper.Generator`: 27 files, +1,459/−590 lines (round 31's tasks, T26's `Dwarf.Map` binding among them) |
| `lib/net10.0/DwarfMapper.xml` | 177,540 → 191,762 | 41,418 → 45,407 | +3,989 | doc comments on round 31's runtime surface |
| `lib/net10.0/DwarfMapper.dll` | 46,080 → 49,152 | 19,732 → 21,119 | +1,387 | 10 commits to `src/DwarfMapper`: T09, T11, T12, T14, T17, T19 and T26 |
| `build/DwarfMapper.props` | new | 540 | +540 | **a new file:** `3c342d6a` (T17), extended by T26 |
| `buildTransitive/DwarfMapper.props` | new | 540 | +540 | **a new file:** the same file, packed twice by design |
| `(zip headers)` | | 1,270 → 1,534 | +264 | the two new entries' headers |
| `README.md` | 81,003 → 82,516 | 28,205 → 28,746 | +541 | 3 commits: T17, T26 and `53530452` |
| `[Content_Types].xml` | 582 → 646 | 221 → 227 | +6 | the new `.props` extension registered |
| `CodeFixes.dll`, `nuspec`, `.rels` | unchanged | | −2, 0, 0 | no source change; the commit hash in assembly metadata |

### `DwarfMapper.Testing`, round 30: +382 B; round 31: +536 B

| Entry | Round 30 Δ | Round 31 Δ | Source |
|---|---:|---:|---|
| `lib/net10.0/DwarfMapper.Testing.dll` | +40 (raw 35,840 → 36,352) | −5 (raw unchanged) | 12 commits to `src/DwarfMapper.Testing` in round 30 (the `ObjectFactoryV2` and oracle fixes); none in round 31 |
| `lib/net10.0/DwarfMapper.Testing.xml` | +268 | 0 | the same round-30 commits |
| `README.md` | +72 | +541 | the same `README.md` as the main package |
| `nuspec`, `.rels` | +2 | 0 | commit hash; random part name |

`DwarfMapper.Testing` crossed its ceiling in round 31 with **no change to its own code**. All of its round-31
growth is `README.md`. That is the problem `Issues/round30/CI-NIGHTLY-REVIEW.md` already named: a code-size gate
that a prose edit can turn red.

## 4. What it means for D3

- **The decisions table** decides D3 "by T07's measurement: if every byte is attributed to an intended change,
  A". Every entry is attributed.
- **The one new entry is intended,** in its author's words (`3c342d6a`): "PublishAot reaches the generator through
  a NEW package file, build/DwarfMapper.props (+ buildTransitive/)". Round 31 T26 then made the same file enable
  the interceptors behind `Dwarf.Map`. The file is 1,064 B, sets one property and exposes one other, and its own
  header says what it touches.
- **One input cannot be measured here.** The repository's rule (`scripts/gate-checks.ps1`, round 28) takes the
  ceiling from the larger of the Windows and ubuntu packs, so the same tree is green wherever the gate runs.
  Windows has packed 200–603 B larger at every recorded pairing (CRLF in `README.md` and the XML doc).
  - `DwarfMapper`: 361,965 B on ubuntu is 353 KB. Adding 603 B gives 362,568 B, which is 354 KB. So the Windows
    figure decides between 353 and 354 [INFERRED: the offset is from earlier pairings, not measured on this
    tree].
  - `DwarfMapper.Testing`: 54,616 B is 53 KB on ubuntu, and stays 53 at any recorded offset.
- **The raise is the owner's,** because loosening a gate is (protocol §4). It needs a Windows pack of the same
  tree. That pack is the owner's machine's to take, and the same commit carries the entry-by-entry account
  above, in the manner of `1972246a`.
