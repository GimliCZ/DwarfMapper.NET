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

- **`[AfterMap]` on an update-into mapping method generated infinite recursion.** Hook collection accepted
  the partial mapping method itself as a hook whenever its signature happened to fit, and
  `void Update(Src, Dst)` fits the two-parameter after-hook shape exactly — so the generated body of `Update`
  ended in a call to `Update(s, d)`, which never terminates. It compiled, and nothing in the build said so.
  The same shape registered `void MapSpan(ReadOnlySpan<S>, Span<D>)` as a hook that was then never invoked,
  and made `DWARF018` complain about the signature of `Dst Map(Src)` when the signature was never the
  problem. A partial method with no implementing part has no body at all, so all three are now refused as
  `DWARF091`. Found by the surface matrix — which had scored the recursion as the directive being *honoured*,
  because a cell turns green when the output **changes**, not when it is **right** (`D16`). (round 20)
- **A co-located `[GenerateMap<S, T>]` host read no member-level `[MapProperty]` / `[MapIgnore]` at all.**
  At that endpoint the mapping is declared **by** the annotated type, so a member of the host is part of the
  declaration and carries the member form — `[MapProperty("Full")]` on a `Name` member means *`Name` comes
  from the source's `Full`*, and a bare `[MapIgnore]` means *never assign this member*. Neither was read:
  `[GenerateMap]` is extracted by `MapperExtractor`, which took these attributes off the class or the method
  symbol only, and the `[MapTo]` registry was the sole reader of the member forms. Every case compiled,
  changed nothing, and said nothing — including the `Use` converter, the `When` predicate, the
  `NullSubstitute` and the `StringFormat` that ride on the same one-argument constructor. Both placements now
  go through **one** parser, so they cannot drift apart. Found by the surface matrix, which measured all
  twenty cells — every case, on both the property and the field site — as silent (`D20`). (round 20)
- **The wrong *overload* of `[MapProperty]` or `[MapIgnore]` was accepted and discarded in silence.** Both
  attributes cover two placements behind one name, each with its own constructor: the member form
  (`[MapProperty("Dest")]`, bare `[MapIgnore]`) belongs on a member of a type that declares its own mapping,
  and the method form (`[MapProperty(source, target)]`, `[MapIgnore(destination)]`) on a mapper class or a
  mapping method. Written at the other placement, each was skipped without a word — `ReadExplicitMaps` accepts
  only the two-argument application and `ReadIgnores` only the one-argument one. Three consequences a caller
  could not see: a bare `[MapIgnore]` on a method or class excluded **nothing** while its author believed a
  member was excluded; `[MapProperty("Name")]` on a method bound `Name` to itself, which is what auto-matching
  already does; and — sharpest — the named arguments ride on that same one-argument constructor, so
  `[MapProperty("Name", Use = nameof(F))]` dropped the **converter** along with the binding, and likewise
  `When`, `NullSubstitute` and `StringFormat`. All are refused now as `DWARF088` (see Added). The mirror
  misuse at the `[MapTo]` registry — the two-name method form on a source member — now reports `DWARFR04`,
  which existed for a *different* arity (how many `[MapProperty]` attributes were stacked, not how many values
  one of them carries) and so never fired for this. Found by the surface matrix, which measured all four as
  silent divergences (`D3`, `D4`, `D5`, `D21`, forty-nine cells). (round 20)
- **Two `[FlattenGraph]` directives filling one collection emitted code that does not compile.** Each
  directive contributes its own initializer for the destination collection it names, and nothing checked that
  two of them had not named the same one — so the generator accepted the shape and emitted
  `new RootDto { Nodes = …, Nodes = … }`. The consumer's build failed with
  `CS1912: Duplicate initialization of member 'Nodes'`, located in `Demo.M.g.cs`: a generated file they never
  wrote and cannot edit. This is refused up front now, as `DWARF087` (see Added). Refusal is keyed on the
  **destination collection**, not on the two directives being character-identical —
  `[FlattenGraph("Entry", "Nodes")]` beside `[FlattenGraph("Other", "Nodes")]` emitted the very same CS1912
  from two directives that are not duplicates of each other. `[FlattenGraph]` remains `AllowMultiple`:
  several directives naming *different* collections are unaffected and are still the supported way to flatten
  more than one graph into one DTO. Found by the surface matrix's derived `AllowMultiple ×2` axis. (round 20,
  N4)
- **A duplicate update-into registration was recorded against the CREATE table.** `DwarfMapperRegistry`
  keeps two key spaces on purpose — a pair can legitimately have both a `TDest Map(TSource)` and a
  `void Update(TSource, TDest)` — but only the create table had an ambiguity set, and `RegisterUpdate`
  marked its duplicates there. Two update registrations for a pair with no create map therefore left
  `IsAmbiguous(S, T)` returning `true` while `IsProvided(S, T)` returned `false`: a contested map that had
  never been registered. `RegisterUpdate` now marks its own set, surfaced by `IsUpdateAmbiguous` (new API,
  see Added below). **Behaviour change:** `IsAmbiguous` no longer reports duplicates that belong to the
  update table. If you worked around the false positive by treating "ambiguous but not provided" as an
  update-table duplicate, ask `IsUpdateAmbiguous` instead. Nothing inside the package read that set — the
  generator-emitted `DwarfMap.Validate()` calls `IsProvided` only, and compile-time ambiguity (`DWARF063`)
  is computed from the manifests — so no diagnostic or validation result changes. The duplicate delegate
  itself was already first-wins and still is. (round 19, REG-06)
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

- **`DWARF090` — a member directive that the element-wise endpoints cannot apply.** The generalization of
  `DWARF077`, and the same root cause: a span map or an async-stream map resolves no members of its own. It
  maps the *element* pair through a mapper synthesized per `(source, target)` and shared by every route that
  reaches that pair, so only a directive that **names the pair** can configure it. An unscoped
  `[MapIgnore("Id")]` or `[MapProperty("Id", "Name")]` written on the mapping method — or, for `[MapIgnore]`,
  on the mapper class — belongs to the declaration rather than to the pair, and was dropped in silence: one
  mapper excluded a member on its create, update and projection overloads and copied it on the other two.
  Propagating it instead was rejected for the reason `DWARF077` already records — one method's unscoped
  directive would silently re-configure a nested mapping another method owns — so the message names the
  **pair-scoped** replacement, which is measured working at both endpoints:
  `[MapIgnore<TTarget>("Id")]`, `[MapProperty<TSource, TTarget>("Id", "Name")]`. A **Warning**, for the reason
  `DWARF088` is one: a blocking error suppresses the whole class's emission, so every partial mapping method
  on it loses its implementing part and the refusal reaches the consumer as a wall of `CS8795`. Escalate with
  `dotnet_diagnostic.DWARF090.severity = error` where the stricter reading is wanted. (round 20, D1 and D2)
- **`DWARF091` — `[BeforeMap]` / `[AfterMap]` on a partial method with no body.** Hook collection scanned
  every method on the mapper class and accepted anything whose signature fitted, which includes the partial
  **mapping method declarations themselves** — methods whose bodies this generator writes, so the caller has
  no code there for a hook to run. The signature filter then produced three different wrong answers for one
  mistake: `DWARF018` complained about the signature of `Dst Map(Src)`, which was never the problem;
  `void MapSpan(ReadOnlySpan<S>, Span<D>)` fitted the two-parameter after-hook shape exactly, was registered,
  and was never called; and `void Update(S, D)` fitted **and was called**, so the emitted body ended in
  `Update(s, d);` — unconditional infinite recursion. The rule is not specific to mapping methods and does not
  need to be: C# erases a partial method with no implementing part along with every call to it, so as a hook
  it can only ever be a no-op, or — where the generator supplies the missing part — a call back into the
  method being generated. Refused now before the signature is looked at, so one diagnostic covers all five
  endpoints. A **Warning**, for the reason `DWARF088` is one; the hook is dropped and the mapper is emitted.
  Escalate with `dotnet_diagnostic.DWARF091.severity = error`. (round 20, D16)
- **`DWARF089` — a directive on a co-located `[GenerateMap]` host member that cannot be applied.** The exact
  inverse of `DWARF088`: that one refuses the *member* form where there is no member, this one refuses the
  *method* form on a member — `[MapProperty(source, target)]` and `[MapIgnore(destination)]` both name a
  destination the placement has already named. It also covers two shapes the placement alone cannot decide:
  stacked directives whose count does not match the `[GenerateMap]` pairs the host is the destination of
  (they bind positionally, one per pair, as they do to `[MapTo]` targets), and a member directive on a host
  that is the destination of no pair it declares. A **Warning**, for the reason `DWARF088` is one: a blocking
  error would suppress the host's emission, so the generated `<Host>Mapper`, its convenience extension and
  its DI registration would all vanish and every call site would meet `CS1061` instead of the refusal. The
  offending directive is dropped and the rest of the host's mapping is emitted; escalate with
  `dotnet_diagnostic.DWARF089.severity = error` where the stricter reading is wanted. (round 20, D20)
- **`DWARF088` — the member-placement overload of `[MapProperty]` / `[MapIgnore]` written on a mapper.** A
  **Warning**, and the remedy is in the message: supply the argument the method form takes
  (`[MapProperty("Name", "FullName")]`, `[MapIgnore("Extra")]`). Refusal is right whichever way the directive
  is read — honouring `[MapProperty("Name")]` at a method would bind `Name` to itself, a no-op nobody writes
  on purpose, and discarding it evaporates a binding the caller stated explicitly. A Warning rather than an
  Error deliberately: a blocking DwarfMapper error suppresses the whole class's emission, so the refusal would
  reach the consumer as a wall of `CS8795` from the unimplemented partial methods; escalate with
  `dotnet_diagnostic.DWARF088.severity = error` where the stricter reading is wanted. Its registry mirror
  `DWARFR04` stays an Error, having no partial declaration to strand. See the entry under Fixed for what it
  was replacing. (round 20)
- **`DWARF087` — two `[FlattenGraph]` directives may not fill one destination collection.** An **Error**, and
  the remedy is in the message: keep one directive per collection, or name a different collection member to
  flatten a second graph. It replaces a `CS1912` against generated source with a diagnostic against the
  attribute that caused it — see the entry under Fixed for what it was replacing. Refused rather than
  collapsed to one directive, matching `DWARF011` on `[MapProperty]`: a repeated directive is a copy-paste
  mistake, and quietly keeping one of the two hides it from the only person who can fix it. (round 20, N4)
- **`DWARF086` — a manifest attribute the generator emits may not be hand-written.**
  `[assembly: DwarfProvidesMap(...)]` and `[assembly: DwarfRequiresMap(...)]` are the generator's *output*:
  the cross-assembly manifest that a `[DwarfMapperValidationRoot]` compilation reads back from referenced
  metadata to decide `DWARF061`. A hand-written entry claims a map the generator never produced, so the
  root validates against a manifest that no longer describes the assembly, and the failure reappears at the
  first call site at run time (`DwarfMapMissingException`) instead of at compile time — the exact failure
  `DWARF061` exists to pull forward. Refused as an **Error**; only hand-written occurrences are refused, the
  generator's own `.g.cs` emission is unaffected. (round 19)
- **`DwarfMapperRegistry.IsUpdateAmbiguous(Type, Type)`.** Mirrors `IsAmbiguous`, but reads the update-into
  table's own duplicate set rather than the create table's — see the `RegisterUpdate` entry under Fixed,
  above, for why the two tables needed separate sets. (round 19, REG-06)
- **`DWARFR10` — a `[MapTo]` member auto-matched across a trust boundary the assembly had closed.** The
  registry counterpart of `DWARF072`. `[assembly: DwarfMapperDefaults(AutoMatchMembers = false)]` says nothing
  is mapped unless the caller said so; the mapper-level `[DwarfMapper(AutoMatchMembers = false)]` acts at all
  five method endpoints, and the assembly-level form was honoured everywhere **except** the `[MapTo]` front
  door — which read no assembly-level configuration at all. So an assembly that had switched auto-matching off
  still had every registry map auto-matching, silently, and the completeness gate could not notice because the
  member *was* mapped. Half a trust boundary is worse than none, because the developer believes they have one.
  A destination reached by a name the caller wrote (`[MapProperty("Dest")]`) still maps; one reached only
  because the names line up is refused, and does **not** also draw `DWARFR02`. Found by the surface matrix
  (`D19`). (round 20)
- **`DWARFR01`–`DWARFR10` are release-tracked.** The registry (`[MapTo]`) diagnostics suppressed
  `RS2000`/`RS2001` and appeared in no `AnalyzerReleases` file, despite shipping in the same package and
  surfacing in the same IDE error list as the `DWARF0xx` rules. They now have rows, the suppressions are
  gone, and `AssemblyScanTests` enforces the descriptor ↔ release-notes sync for the `DWARFR` family the same
  way it does for `DWARF###`. (ISSUE-047)
- **Coverage for C# 14 consumer shapes.** The generator's contract is over consumer code, and the corpus had
  never seen a `field`-backed property, a `partial` constructor, an extension block, or a user-defined
  compound assignment operator. All four are now pinned as working, along with the fact that an extension
  block in scope does not disturb converter discovery. One gap is pinned as *observed* rather than fixed:
  `[MapProperty(Use = …)]` naming an extension member is refused with `DWARF014` ("conversion method not
  found") — safe, but the reason is wrong, since the method is plainly there. See
  `Issues/round17/roslyn-5-upgrade-opportunities.md`.
- **This file**, and a release-workflow step that publishes the matching section as the release notes.

### Changed

- **BREAKING: the `[MapTo]` extension class is now `internal` unless the assembly opts in.**
  `[assembly: DwarfMapperOptions(PublicExtensions = true)]` decides the accessibility of the generated
  convenience extensions, and its documented default has always been *"all generated extensions are
  assembly-internal"*. The registry front door read no assembly-level configuration, so it emitted
  `public static class __DwarfRegistry_<Source>` whenever the source and every target were public — a caller
  got the assembly default honoured for their `[DwarfMapper]` classes and quietly overridden for their
  `[MapTo]` types. Both emitters now read the same option. **If you ship `[MapTo]` types for another assembly
  to consume, add `[assembly: DwarfMapperOptions(PublicExtensions = true)]`**; in-assembly use is unaffected.
  As before, the opt-in is a ceiling and not a decision: a pair involving a non-public type stays internal,
  since a public member over an internal type does not compile. Found by the surface matrix (`D18`).
  (round 20)
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
