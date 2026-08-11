# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Diagnostic ids (`DWARF###`, `DWARFR##`) are part of the public surface: consumers suppress them, document
them, and gate builds on them. Adding, retiring, or changing the severity of one is a user-visible change and
belongs in this file as well as in `src/DwarfMapper.Generator/AnalyzerReleases.*.md`.

The release workflow reads the section matching the tag being built and uses it as the GitHub Release notes,
so a version with no section here ships with no notes.

## [Unreleased]

### Fixed

- **Member visibility was dropped on twenty code paths.** `ReadableMembers`/`WritableMembers` defaulted their
  `compilation` and `allowNonPublic` arguments, so paths that omitted them answered "which members can I read
  from this type?" as if the mapper had never set `[DwarfMapper(AllowNonPublic = true)]`. Legal code was
  rejected with `DWARF043` ("source path has no member 'X'"), `DWARF045`, or `DWARF001` naming a member that
  was plainly present. Both parameters are required now, at the wrappers and at `MemberFacts` itself, so
  omitting them is a compile error rather than a silent wrong answer. `ConstructorSelector` had the same
  defect as a direct caller: a constructor whose parameter was fed by an internal source member scored as
  unsatisfiable, so overload selection preferred a narrower constructor. (ISSUE-044)
- **`AutoNest = false` was ignored on one projection path.** `ResolveProjectionCtorExpr`'s `autoNest`
  argument was omitted at the nested-object call site and defaulted back to `true`, so that one path
  auto-nested while every sibling path honoured the setting. (ISSUE-043)

### Added

- **`DWARFR01`–`DWARFR09` are release-tracked.** The registry (`[MapTo]`) diagnostics suppressed
  `RS2000`/`RS2001` and appeared in no `AnalyzerReleases` file, despite shipping in the same package and
  surfacing in the same IDE error list as the `DWARF0xx` rules. They now have rows, the suppressions are
  gone, and `AssemblyScanTests` enforces the descriptor ↔ release-notes sync for the `DWARFR` family the same
  way it does for `DWARF###`. (ISSUE-047)
- **This file**, and a release-workflow step that publishes the matching section as the release notes.

### Changed

- **Roslyn floor raised to `Microsoft.CodeAnalysis` 5.0.0** (from 4.14.0; `Microsoft.CodeAnalysis.Analyzers`
  3.11.0 → 5.3.0). The declared toolchain requirement is now **SDK 10.0.100+ / Roslyn 5.0+** — an older SDK
  cannot load the generator at all, since Roslyn refuses an analyzer compiled against a newer compiler than
  the host (`CS9057`). One consequence surfaced immediately: Roslyn 5.0 annotates
  `SymbolDisplay.FormatPrimitive` as returning `string?`, which turned into a build error under
  warnings-as-errors and is now handled explicitly at both call sites rather than assumed away.
  A non-blocking CI leg builds on a newer 10.0.x SDK to prove forward compatibility; it is marked
  `continue-on-error` until its first green run, and that marker is meant to be removed.
- **The SDK pin is now real.** `global.json` used `rollForward: latestPatch` while CI installed `10.0.x`, so
  contributor and CI could compile a source generator — whose output the suite compares byte-for-byte — on
  different compilers. The pin is `10.0.101` with `rollForward: disable`, CI installs exactly that, and a CI
  step fails the build if the two ever disagree. Contributors on another patch must install `10.0.101`
  side by side. (ISSUE-038)
- **Line endings are LF, enforced by `.gitattributes`.** Without it the working tree adopted whatever
  `core.autocrlf` the contributor had, and C# raw-string literals carried the checkout's terminators — two
  `DocSnippetInjectorTests` failed on a `core.autocrlf=true` checkout while passing on an LF one. The
  `*.txt` rule matters most: those files are Verify snapshots compared byte-for-byte against LF generator
  output. (ISSUE-048)

### Security

- No security-relevant change. See `SECURITY.md` for the reporting process and the supported-version
  statement.

<!--
When cutting a release, replace the `## [Unreleased]` heading with `## [X.Y.Z] - YYYY-MM-DD` and open a
fresh Unreleased section above it. The release workflow matches on the bare version (`X.Y.Z`, including any
pre-release suffix), so the heading must contain the tag's version without the leading `v`.
-->
