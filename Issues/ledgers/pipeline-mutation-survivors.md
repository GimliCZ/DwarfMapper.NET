<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The pipeline leg — every undetected mutant, and why

Round 30. The leg (`stryker-config.pipeline.json`) mutates the four member-resolution phases in
`MapperExtractor.Members.Phases.cs` plus the two context records they are parameterised by. It was pinned
at `break` 89 on 2026-09-09 (89.80 %, 273 of 304) and measured **81.25 % (247 of 304 — 56 survived,
1 uncovered, 0 timeouts) on 2026-09-11**, reproduced identically on two independent runs, with **zero
commits** to any of the three mutated files since the pin. After the kill program it measured **93.42 %
(284 of 304 — 19 survived, 1 uncovered, 0 timeouts)** the same day, and `break` moved 89 → 93.

This file carries the case analysis behind every mutant that remains after the kill program. The
machine-readable rows are in [`equivalent-mutants.md`](equivalent-mutants.md); this is the prose they
anchor to, in the shape of [`codefixes-mutation-survivors.md`](codefixes-mutation-survivors.md).

## The score, and what moved it

| run | scoreable | killed | survived | uncovered | score |
|---|---:|---:|---:|---:|---:|
| 2026-09-09 pin (`StrykerOutput/2026-09-09.06-09-56`) | 304 | 273 | 29 | 2 | 89.80 % |
| 2026-09-11, before (`StrykerOutput/2026-09-11.09-25-59`) | 304 | 247 | 56 | 1 | 81.25 % |
| 2026-09-11, after the kill program (`StrykerOutput/2026-09-11.13-02-15`) | 304 | **284** | 19 | 1 | **93.42 %** |

The mutant-by-mutant diff of the two 2026-09-11 reports is one-way: **37 Survived → Killed**, no other
status change, and the 37 are exactly the mutants the tests in commit `0b94ad0` were written against. The
20 that remain are the 16 adjudicated below plus the four in *Left open, on purpose*. `rawCeiling` is
**94.73 %** — `(304 − 16) / 304` — so the measured score sits four undetected mutants under the ceiling,
every one of the four named.

## Why the score fell with no source change

Every survivor sits on a diagnostic-reporting site or a resolution branch that the suite reaches but does not
*observe*. The traced example: mutant `11256` blanks the DWARF041 message at line 166 to `$""`; the report's
`coveredBy` attributes it to `MapValueGeneratorTests.Invalid_use_provider_reports_DWARF041`, whose only
assertion was `Assert.NotNull(Find(diags, "DWARF041"))`. A blanked message still carries the id.

That gap pre-existed the 2026-09-09 pin. What changed is Stryker's *attribution*: 26 of the 56 survivors are
mutants Stryker flags `static` (coverage it could not attribute to any test, so it runs the whole suite
against them), and the other 30 are attributed to specific tests that assert an id and nothing else. The
2026-09-09 run happened to kill many of these through whole-suite runs; as the suite grew, per-test
attribution got precise enough to stop killing them by accident. The score describes the tests, not the
code, and the tests had the hole all along.

## The kill program (2026-09-11)

Tests only; `src/` untouched. Three themes, each RED against its mutant before GREEN (every logic and
statement mutant was planted transiently in `src/` and its targeted test run against it — the driver log is
the evidence, the source was restored byte-for-byte before any commit).

**Message content, not id.** Fourteen diagnostic-message mutants (`$""`, `""`, a blanked `", "` join) on
descriptors whose format is a bare `"{0}"` — so the resolver's text IS the whole diagnostic. Each covering
test now asserts the distinguishing fragment: the member it names plus the core phrase. A non-empty
fragment cannot be found in a blanked message, so these are RED by construction; the GREEN run proves the
fragment is in the live message.

**Refused before resolved, so no cascade.** Seven removed `continue`s after an error report. Each lets the
erroring member fall into ordinary resolution, and the tests prove the drop with a source type that cannot
convert: the refusal is the *only* error id, never a DWARF005 about a conversion the mapper was never going
to perform. A caller reads one refusal per directive.

**Branches nothing observed.** The field arm of the `SkipNullSourceMembers` deferrable-target selection
(four mutants, one fixture with a public mutable field); the seven-term skip chain (a `When`-guarded member
must keep its `NullRefIntoNonNullable`, i.e. its `!` and its DWARF070; an init-only target must not be
deferred); the Flexible-normalised lookup in DWARF064; the explicit-map arm of the required
constructor parameter (CS9035), of `ConverterReturnIsNullableRef` (DWARF107), and of `MapOr`'s
pre-rendered `NullSubLiteral`; the first-match tie-break for two extra parameters differing only by case;
the flatten leaf's `Root.Leaf` spelling in DWARF070; and the `[Reinterpret]` member's *assignment* (the blit
helper is synthesized before the member is added, so a presence check on the helper never saw the member
vanish).

## The sixteen that remain, adjudicated

All in `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Members.Phases.cs`. Line numbers are as of this
writing. Three lemmas are shared and stated once:

- **L1 — `srcTypeByName` lookups of `""` or of any dotted string fail.** The dictionary
  (`ApplySkipNullSourceMembers`, line 39) is keyed by `ReadableMembers` names under `lookups.Comparer`, which
  is `StringComparer.Ordinal` or `OrdinalIgnoreCase` (`ResolveMembers`, line 186) — never the Flexible
  normaliser. A C# member name is non-empty and contains no `.`, so neither comparer equates one to `""` or
  to `Root.Leaf`.
- **L2 — no `MemberMap` carries both a non-empty `SourceName` and a `ValueExpression`.** The only two
  constructions passing `ValueExpression:` are lines 156 and 174, both with `SourceName` `""` (Stryker's own
  report lists those two `""` literals as separate mutants, so the invariant is measured, not assumed).
- **L3 — an unflatten member's `TargetName` is never in `deferrableTargets`.** `ResolveUnflattenTarget`
  admits a target only when `tgtName.Split('.')` has exactly two segments, and `deferrableTargets` holds
  simple property/field names; so `!deferrableTargets.Contains(m.TargetName)` is true for every member
  with `UnflattenIntermediateFqn` set.

### `ApplySkipNullSourceMembers`

**`acc.Result.Count > 0` → `>= 0`** (line 37, Equality) — *proven*. With `>= 0` the block also runs on an
empty member list: it builds `srcTypeByName` and `deferrableTargets` and iterates zero members. Nothing is
added, changed or reported; the two forms differ only in an allocation.

**`m.SourceName.IndexOf('.') >= 0` → `> 0`** (line 59, Equality) — *proven*. Differs only when `SourceName`
begins with `.` (`IndexOf == 0`). Every `SourceName` in `acc.Result` is one of: `""` (lines 156, 174, 731,
and the top-level-collection sentinel); a symbol name from `ReadableMembers`/`SourceGroups` (the auto,
share, dense, blit and simple explicit arms); `fm.Root + "." + fm.Leaf` with `Root` a symbol name (line 807);
or a user-written dotted path that `TryResolvePath` accepted (line 290), whose first segment had to equal a
readable member's name and so is non-empty. `ResolveUnflattenTarget` validates its `srcName` identically.
None begins with `.`, so the two comparisons agree everywhere.

**The seven-term skip chain, three of its `||` → `&&`** (line 58 onward, Logical ×3) — *proven*. Write the
chain as `a || b || c || d || e || f || g` (`a` empty source, `b` dotted source, `c` `ValueExpression`, `d`
unflatten, `e` `When`, `f` already `SkipIfSourceNull`, `g` target not deferrable); it parses
left-associatively. When the `if` does not `continue`, the only effect is line 69's
`srcTypeByName.TryGetValue(m.SourceName, …)` and the assignment it guards.

- `a || b` → `a && b`: differs iff exactly one of `a`, `b` holds and `c`…`g` are all false. The mutant then
  falls through with `SourceName` `""` or dotted; the lookup fails by L1; nothing happens. Equal.
- `(a || b) || c` → `(a || b) && c`: differs iff `(a || b)` xor `c`, with `d`…`g` false. `(a || b)` true and
  `c` false: fall-through fails by L1. `c` true and `(a || b)` false: a `ValueExpression` member with a
  non-empty, undotted `SourceName` — none exists by L2. Equal.
- `P₂ || d` → `P₂ && d` (`P₂ = a || b || c`): differs iff `P₂` xor `d`, with `e`, `f`, `g` false. `d` true
  and `P₂` false: an unflatten member, for which `g` is true by L3 — contradiction. `P₂` true and `d` false:
  fall-through with `SourceName` `""`/dotted (fails by L1) or a `ValueExpression` member with a simple
  `SourceName` (none by L2). Equal.

The other three `||` in the chain (`… || e`, `… || f`, `… && g`) are NOT equivalent and are killed by the
`When`-guarded and init-only fixtures in `MapNullSkipScopeTests`.

### `ResolveMapValues`

**`continue;` after the DWARF040 report** (line 153, Statement) and **`continue;` after the DWARF041 report**
(line 167, Statement) — *proven, WEAKER THAN THE OTHERS.* Without the `continue` the failing `[MapValue]`
still adds a `MemberMap` (`TryFormatConstant` leaves `literal = ""`; the Use= arm adds
`Escape(mv.Use) + "()"`). Both diagnostics are Errors, and an Error withholds the mapper's entire emission —
the mechanism has its own diagnostic, DWARF078 ("No code was generated for mapper 'M' because it has
unresolved DwarfMapper errors"), so the member is never emitted. Every pass that reads `acc.Result`
afterwards produces nothing from it: `ApplySkipNullSourceMembers` skips it (`ValueExpression` non-null), the
DWARF070 report needs `NullRefIntoNonNullable`, source coverage's `AddConsumed` ignores `""`, and the dense
post-pass reads only members a validated directive names — and `ValidateDenseEnumDirectives` refuses a
directive beside a `[MapValue]`. Weaker because it rests on the error-suppresses-emission invariant rather
than on the language; it becomes killable the day a refused mapper emits anything.

**`""` → `"Stryker was here!"` as the constant member's `SourceName`** (line 156, String) and **the same on
the Use= member** (line 174, String) — *proven.* Consumers of `SourceName` for a member whose
`ValueExpression` is set: `ApplySkipNullSourceMembers` — `c` holds, `continue`; the DWARF070 report (line
430 of `Members.cs`) — gated on `NullRefIntoNonNullable`, false here; `AddConsumed` (line 1820 of
`MapperExtractor.cs`) — adds the string to the consumed set, and `ReportUnconsumed` iterates SOURCE members,
which "Stryker was here!" is not an identifier of; `AddPlanLine` (`MapEmitter.cs` line 126) and
`AppendValueExpression` (line 1448) — both short-circuit on `ValueExpression`; `EmitCollectionKeyUpsert` —
only for `UpsertKeyMember` members, and `ApplyCollectionKeyUpserts` (`Flatten.cs` line 398) resolves the
name through `MemberTypeByName`, which answers null for `""` and for the mutant alike, refusing with the same
DWARF074. Equal on every reachable input.

### `ResolveExplicitMaps`

**`TryGetValue(tgtName, out var uex) && (…)` → `||`** (line 222, Logical) and **`TryGetValue(tgtName, out
var shareExtras) &&` → `||`** (line 347, Logical) — *proven.* When the lookup misses, the `out` tuple is
`default` — `HasNullSub` false, `When` null — so the right operand is false and both forms give false. When
it hits, the right operand is true, because every entry in `ExtrasByTarget` was admitted by one of exactly
four producers, each gated on `HasNullSub || When is not null`: `ReadMapPropertyExtras` (`Flatten.cs` line
354), `MatchPairProps` (`Pairs.cs` line 215), the co-located host reader (`CoLocatedHost.cs` line 96), and
`MapConfig`'s `MapOr` (`MapConfig.cs` line 280, `HasNullSub = true`). Both forms give true. Equal.

**The `synthBeforeConversion` snapshot taken unconditionally** (line 330, Conditional-true) — *proven.* The
snapshot is read at exactly one site, line 490, inside `if (req.StringFormats is not null &&
req.StringFormats.TryGetValue(tgtName, out var fmt))`. For a member with no format the forced snapshot is a
`HashSet` nothing reads; for one with a format the original took it too. Emitted text identical.

**`break;` in the When-predicate search** (line 536, Statement) — *proven.* The loop body's only effect is
`ok = true`; nothing resets `ok`, and a later iteration can only set it again. Removing the early exit
changes the iteration count, not the outcome.

### `ResolveAutoMatchedMembers`

**`acc.HandledTargets.Add(target.Name);` in the extra-parameter arm** (line 758, Statement) — *proven.*
`HandledTargets` is read after this point by two sites. Line 654, for LATER targets: `WritableMembers`
yields each name once (its `seen` set), so the current name never recurs in the loop. `ResolveMembers` line
322, the read-only silent-loss guard: its names come from `ReadOnlyMembers`, disjoint from the writable
targets an extra parameter can bind. Every other reader (`TryValidateMapValueTarget`, `ResolveExplicitMaps`,
`ResolveUnflattenTarget`) ran before this pass. Equal.

**DWARF080's `lookups.Flexible ? NormalizeName(target.Name) : target.Name` → `target.Name`** (line 642,
Conditional-false) — *proven, WEAKER THAN THE OTHERS.* The branch is entered only when
`req.FactoryExcludedMembers` is non-null, and exactly two `ResolveMembers` callers pass it: the
`[GenerateMap]` pair (`Phases.cs` line 3471) and the synthesized nested pair (line 3761). Both construct
their `MapperOptions` with `NameConvention: 0` (lines 3445 and 3724), so `lookups.Flexible` is false on
every input that reaches the ternary and both forms evaluate `target.Name`. This was found the empirical
way: a `[DwarfMapper(NameConvention = NameConvention.Flexible)]` `[GenerateMap]` factory fixture reported
DWARF080 identically with the mutant planted, and the wiring above is why. Weaker because it rests on that
wiring — the day a `[GenerateMap]` pair honours `Flexible`, this row is a killable mutant and a real hole
(the lookup would miss even a same-spelled member).

**`""` → `"Stryker was here!"` as the extra-parameter member's `SourceName`** (line 731, String) — *proven.*
The member carries `SourceAccessExpression`, and every consumer prefers it: the DWARF070 report (`Members.cs`
line 430, `SourceAccessExpression ?? SourceName`), `AddPlanLine`, `AppendValueExpression` (line 1459), and
the ThrowIfNull message (line 1536). `ApplySkipNullSourceMembers` no longer `continue`s on `a`, but falls
through to a lookup that fails by L1. `AddConsumed`/`ReportUnconsumed`: as for the MapValue rows. Equal.

## Left open, on purpose

- **Line 217, `tgtName.IndexOf('.') >= 0` → `> 0`, and line 284, `srcName.IndexOf('.') >= 0` → `> 0`.**
  Distinguishable by a leading-dot name (`[MapProperty("Full", ".Name")]`, `[MapProperty(".Full",
  "Name")]`): the original refuses through the path arm — DWARF045 `unflatten intermediate '' is not a
  writable destination member`, DWARF043 `source path '.Full' has no member ''` — and the mutant through the
  flat arm (DWARF008/DWARF009 naming `'.Name'`/`'.Full'`), which is the *more* legible refusal. Not pinned:
  a test here would lock the awkward message in place. Worklist item, not an adjudication.
- **Line 670, `req.ConsumedCtorParams is null` → `is not null`** — the mutant is *better* than the original.
  Probed: `[MapIgnore("X")]` on a `required` member `X` that the constructor also takes (`C(int X)`, no
  `[SetsRequiredMembers]`) emits `new C(X: s.X) { … }` with `X` omitted from the initializer — **CS9035 in
  the consumer's .g.cs with no DWARF079**, the exact silent shape DWARF079 exists to prevent. The mutant
  reports DWARF079 there. This was a source defect, recorded here and not fixed in the test-only program.
  **Fixed 2026-09-13**: the ctor-consumed exemption was removed from the guard (it had shipped with DWARF079
  in `684f403` without a stated reason — that commit documents only the `[SetsRequiredMembers]` and
  update-into exemptions), pinned by
  `IgnoredRequiredMemberTests.Reports_when_the_ignored_required_member_is_also_a_constructor_argument` and its
  non-ignored twin. The mutant's clause no longer exists, so the leg's scoreable population shrinks at the
  next measurement; the floor moves then, not in the fix commit (the leg takes ~45 minutes).
- **Line 105, `?? false` → `?? true`** (NoCoverage) — a dead-branch question, not an equivalence. Both
  producers of `IgnoredSourceMembers` (`IgnoredSourcesFor`, `ClassIgnoredSources`) return a `HashSet`, and
  every `ResolveMembers` caller passes one, so the null-coalesce is never reached. A denominator question
  for the maintainer, deliberately absent from the ledger (see "What this ledger is NOT").
