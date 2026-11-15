# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Diagnostic ids (`DWARF###`, `DWARFR##`) are part of the public surface: consumers suppress them, document
them, and gate builds on them. Adding, retiring, or changing the severity of one is a user-visible change and
belongs in this file as well as in `src/DwarfMapper.Generator/AnalyzerReleases.*.md`.

The release workflow publishes the section whose heading starts with `## [<version>]` - the tag without its
leading `v` - as the GitHub Release notes. A tag with no such section, or an empty one, fails the release, and a
section must stay under 125,000 characters.

## [Unreleased]

## [1.1.0-rc.1] - Unreleased

The first release since 1.0.2-rc.1, and the first with this changelog: every entry below describes a change made
after 1.0.2-rc.1 was packed. A detailed per-change record is kept in the repository's maintainers' history.

### Highlights

- **C# 15 union mapping.** With the .NET 11 SDK, one union maps into another case by case - as a declared map, a
  member, a collection or dictionary element, in update-into, span and async-stream maps, under every
  reference-handling mode. A case is paired only by an explicit arm, the same type or a declared method, never by
  its members or its name.
- **One package, two generator builds.** The package carries a generator built against Roslyn 5.0 (loaded by every
  .NET 10 SDK) and one built against Roslyn 5.12 (loaded by the .NET 11 SDK, which adds union mapping). The SDK
  loads exactly one; on code without unions both emit identical output. `net11.0` projects are supported.
- **DwarfMapper.Testing is published for the first time**: seeded fixture builders, a property-based fuzzer, a
  round-trip verifier, structural and graph comparers, and lens-law checks for update-into maps.
- **`Dwarf.Map`**, a static ambient call that the generator binds at compile time to the generated mapper when the
  calling project registers the pair, and falls back to the registry otherwise.
- **New mapping capabilities:** automatic sharing of provably immutable members and `[MapShare]`, enum-keyed
  dictionaries written into inline arrays (`[MapDenseEnumKeys]`), `[MapNullSkip]`, `[ProvidesMap]`,
  `[RestatesBase]`, a static `{Method}Expression` on every projection, and block copies and vectorised widening
  for lists, `ImmutableArray<T>`, enum arrays and span maps.
- **Statements of intent instead of `<NoWarn>`:** `[MapClone]`, `[MapProperty(AllowNull = true)]` and
  `[MapProperty(KeepDefault = true)]` accept one deliberate case per member or method.
- **Security hardening:** a converter, hook or factory that calls the mapper again, a `[FlattenGraph]` cycle through
  a node member, and a `[MapDerivedType]` cycle through a collection no longer end the process with a
  `StackOverflowException`. `[MapIgnore]` now reaches nested members and elements under reference handling, and the
  assembly-wide explicit-only mode now covers the `[MapTo]` registry.
- **Fifty new diagnostics**, most of them replacing a silent drop or a compiler error inside a generated file with
  a report against the code you wrote.

### Breaking changes

- **The generator needs Roslyn 5.0 (SDK 10.0.100 or later).** 1.0.2-rc.1 was built against Roslyn 4.14. Every
  released .NET 10 SDK carries Roslyn 5.0, so this affects a .NET 10 preview SDK or another older compiler, which
  refuses to load the generator (`CS9057`). Fix: build with a .NET 10 SDK, 10.0.100 or later.
- **`IDwarfMapper` gained `void Map<TSource, TDestination>(TSource source, TDestination destination)`**, the
  update-into call, with no default implementation. A class of yours that implements `IDwarfMapper` (a decorator,
  a test double) stops compiling with `CS0535`. Fix: implement the member, for example by forwarding to the
  wrapped mapper.
- **The `[MapTo]` extension class is `internal` unless the assembly opts in.** It used to be `public` whenever
  the source and targets were, ignoring the documented default of `DwarfMapperOptions.PublicExtensions`. Fix: if
  another assembly calls your `[MapTo]` extensions, add `[assembly: DwarfMapperOptions(PublicExtensions = true)]`.
- **A declared mapping method whose destination is an enum or a built-in scalar now converts its value.**
  `partial StatusDto Map(Status s)` (and `string` to enum, enum to `int`, `long` to `int`, `string` to `int`)
  returned the default for every input. It now returns the conversion `[GenerateMap]` of the same pair always
  produced, with `DWARF038` where it is lossy. Fix: review callers that worked around the default.
- **Under `ReferenceHandling = Preserve` or `OnCycle = SetNull`, a mapping method's own directives now apply
  when its pair is mapped as a nested member or collection element.** A member the method `[MapIgnore]`s was
  mapped anyway in those positions; renames, `[MapValue]`, guards and formats on the method were not applied
  there either. Fix: check what those mappers produce; nothing changes without reference handling.
- **The array block copy no longer bypasses a declared element converter or a pair-scoped directive.** A
  layout-identical `SrcV[]` member skipped the mapper's own `Convert(SrcV)`, an `implicit operator`, and a
  `[MapIgnore<T>]`/`[MapProperty<S,T>]`/`[MapValue<T>]` or hook for the element pair; they now run, so the
  elements can differ. Two shapes that built now fail: two methods converting the same element pair report
  `DWARF013` (fix: `[MapProperty(Use = nameof(...))]` on the member), and `[AutoNest(false)]` with a pair-scoped
  directive on a blittable element pair reports `DWARF005` (fix: drop the directive or re-enable auto-nesting).
- **A `[MapDerivedType]` dispatch over a base that can be instantiated needs an arm for that base (`DWARF119`).**
  Such a dispatch used to build and throw `ArgumentException` at run time for an instance of exactly the base type.
  Fix: make the base `abstract`, or add `[MapDerivedType<Base, BaseDto>]`. Nested under `Preserve` or `SetNull`, a
  concrete-base dispatch also now runs: a `Dog` held as an `Animal` becomes a `DogDto`, not an `AnimalDto`.
- **A `[FlattenGraph]` node member that leads back to the flatten method no longer builds (`DWARF118`).** Data
  that closed that loop overflowed the stack. Fix: keep that member out of the flattened node - ignore it for the
  node's destination type, or give it a type that does not lead back (an identifier in place of the object).
- **`[FlattenGraph]` under `Preserve` leaves a widened or block-copied collection member at its default and
  reports `DWARF075`.** It used to copy it once per node, losing the sharing `Preserve` promises. Fix: map the
  member explicitly, or use `ReferenceHandling = None` for that mapper.
- **Under `NullCollections = AsNull`, a null source array on the block-copy and widen paths stays null.** A nullable
  `int[]?` to `long[]?`, a block-copied struct array or a `ByValue` enum array came back as an empty array, unlike
  every other collection member of the same mapper. Fix: if a caller relied on the empty array, make that
  destination member non-nullable (it then gets an empty array) or use `NullCollections = AsEmpty`.
- **A `Dictionary<K, string?>` mapped into a `Dictionary<K, string>` throws `InvalidOperationException` on a null
  value** ("Dictionary entry was null"), as a null list or array element already did, instead of storing the null.
  Fix: make the destination value type nullable if nulls are expected.
- **A null nested value is lifted to null wherever the destination can hold it**, across every pairing of struct,
  class and record on the two sides, per collection element and dictionary value. Several of those pairings threw
  `InvalidOperationException`; code that caught it now receives a null. `NullStrategy` still governs a nullable
  value going into a non-nullable target. Fix: handle the null where it arrives, or declare that destination
  non-nullable to keep the `NullStrategy` behaviour.
- **Projections honour `NullCollections`.** A null source collection came back `null` through `Project` and empty
  through `Map`; under the default `AsEmpty`, `Project` now yields an empty collection too. Fix: to keep the null,
  set `NullCollections = AsNull` and make the destination member nullable.
- **`ImplicitConversions = false` is enforced in projections and on `Nullable<T>` members.** A lossy conversion
  through `Project`, or wrapped in `Nullable<T>` (`long?` to `double?`, `long?` to `int?`, `string` to `int?`),
  reported nothing or only a Warning; it is now the `DWARF038` Error the option promises. Under the permissive
  default `Project` now reports the same `DWARF038` Warning as `Map`. Fix: `[MapProperty(Use = nameof(...))]`.
- **Directives that were silently ignored now take effect.** Member-level `[MapProperty]`/`[MapIgnore]` on a
  co-located `[GenerateMap]` host type; `[DwarfMapperConstructor]`, `[Flatten]` and constant `[MapValue]` in
  projections; `ImplicitConversions = false` in span and async-stream maps and `[FlattenGraph]` leaves;
  `AutoNest = false` on one projection path; `AllowNonPublic` on twenty member-lookup paths, which can change the
  constructor chosen. A projection of a `struct` with an explicit constructor now calls it, as `Map` does. Fix:
  check the output of mappers that carry these directives.
- **Directives written where they cannot work are now Errors.** The member form of `[MapProperty]`/`[MapIgnore]` on
  a mapper class, method or mapper member (`DWARF088`; its arguments, `Use` included, were dropped); the two-name
  form on a `[MapTo]` source member (`DWARFR04`); a `[MapTo]` `[MapProperty]` naming no destination member
  (`DWARFR17`); a `[MapTo]` member auto-matched while the assembly sets `AutoMatchMembers = false` (`DWARFR10`); a
  hand-written `[assembly: DwarfProvidesMap]` or `DwarfRequiresMap` (`DWARF086`); a scalar map with no conversion
  to return - the same type, an implicit conversion, or a nullable value type - which generated nothing or a silent
  default (`DWARF117`). Fix: the remedy each message names.
- **New Warnings fail builds that use `TreatWarningsAsErrors`.** `DWARF089`-`DWARF093`, `DWARF095`, `DWARF098`,
  `DWARFR11`, `DWARFR12` and `DWARFR16` report directives that did nothing; `DWARF115` reports a projection method
  EF Core cannot precompile in a NativeAOT project; `DWARF070` now also covers collection elements, dictionary keys
  and values, constructor parameters, mapping parameters, `[FlattenGraph]` leaves and `[MaybeNull]`/`[DisallowNull]`
  members. Fix: the remedy in each message, or a severity in a `.globalconfig`.
- **A convenience extension for a map declared to return a nullable reference now returns it nullable.**
  `src.ToDst()` is `Dst?` when the method is `partial Dst? Map(Src s)`, so `src.ToDst().Member` can raise `CS8602`
  in your own code (where it can be suppressed) instead of `CS8603` inside a generated file. Fix: handle the null,
  or declare the map's return non-nullable if it never returns null.

### Security

- **A converter, hook or factory that calls the mapper again can no longer overflow the stack.** The depth guard
  counts only the calls the generator emits, so a `[MapProperty(Use = ...)]` converter, an adopted method, a
  `[MapConstructor]` factory or a `[BeforeMap]`/`[AfterMap]` hook that calls a mapping method was an unbounded loop
  on deep input: a `StackOverflowException`, which cannot be caught and ends the process, in every
  reference-handling mode and through the ambient facade. Every declared mapping method of a mapper with a method
  body of its own now starts with `RuntimeHelpers.EnsureSufficientExecutionStack()`, so the same input ends in a
  catchable `InsufficientExecutionStackException`; a mapper with no method of its own is emitted as before.
  `MaxDepth` still does not count that loop, and code outside the mapper that reaches one by itself (a property
  getter or conversion operator on a mapped type) is not covered. This is the failure class of AutoMapper's
  CVE-2026-128081.
- **A `[FlattenGraph]` node member that leads back to the flatten method is refused at build time (`DWARF118`).**
  Data that closed the loop overflowed the stack in every reference-handling mode.
- **A `[MapDerivedType]` method reached as a collection element or dictionary value keeps the shared context.**
  With an abstract or interface base, a cycle through the collection overflowed the stack in every mode; it is now
  `DwarfMappingDepthException`, the shared instance under `Preserve`, or `null` on the back edge under `SetNull`.
- **Span and async-stream maps over recursive or reference-tracked element pairs are bounded by `MaxDepth`**: such
  a map allocates one reference context per call and threads it into every element.
- **`[AfterMap]` on an update-into method no longer recurses forever.** The mapping method itself was accepted as
  its own hook, so `Update` ended by calling `Update`; such a hook is now dropped and reported (`DWARF091`).
- **`[MapIgnore]` keeps a member out of nested members and elements.** Under `Preserve` or `SetNull`, a member a
  method ignores was copied when its pair was mapped as a nested member or element, and an array block copy skipped
  a pair-scoped `[MapIgnore<T>]` (see Breaking changes).
- **The explicit-only trust boundary covers the `[MapTo]` registry.**
  `[assembly: DwarfMapperDefaults(AutoMatchMembers = false)]` was ignored there, so registry maps kept
  auto-matching same-named members; that is now `DWARFR10`.
- **The default `MaxDepth` (64) ends AutoMapper CVE-2026-32933's uncontrolled-recursion shapes in a catchable
  `DwarfMappingDepthException`**, now verified at the advisory's own depth for self-referential, list- and
  dictionary-routed recursion in both reference modes and recorded in `SECURITY.md`.
- **A threat model ships in the repository** (`.oss-scanner/threat_model.md`): where untrusted values enter, what is
  out of scope, and how a finding is rated.

### New diagnostics

Severities are the defaults; each id's trigger and remedy are in `docs/diagnostics.md`.

- `DWARF078` (Warning) - no code was generated for a mapper that has a DwarfMapper error; signposts the `CS8795`s
  that follow.
- `DWARF079` (Error) - `[MapIgnore]` on a `required` destination member, which the initializer cannot omit.
- `DWARF080` (Info) - a `[MapConstructor]` factory owns construction, so a mapped value for an `init`-only or
  `required` member is discarded.
- `DWARF081` (Info) - two mappers synthesize the same nested pair two different ways (each helper takes the policy
  of the mapper that reached it).
- `DWARF082` (Error) - a `[ProvidesMap]` method cannot be registered (it must be public, take one parameter and
  return a value, with publicly nameable types).
- `DWARF083` (Info) - a declared enum-to-string map writes `[EnumMember]`/`[Description]` values, not member
  identifiers.
- `DWARF084` (Error) - `[RestatesBase]` cannot identify the base pair.
- `DWARF085` (Warning) - a pair declared with `[RestatesBase]` no longer maps a member the way its base pair does.
- `DWARF086` (Error) - a hand-written `[assembly: DwarfProvidesMap]`/`[assembly: DwarfRequiresMap]`; those are
  generator output.
- `DWARF087` (Error) - two `[FlattenGraph]` directives fill the same destination collection.
- `DWARF088` (Error) - the member form of `[MapProperty]`/`[MapIgnore]` written on a mapper class, mapping method
  or member of the mapper.
- `DWARF089` (Warning) - a directive on a co-located `[GenerateMap]` host member cannot be applied; it is dropped.
- `DWARF090` (Warning) - an unscoped member directive on a span or async-stream map, which only pair-scoped
  directives configure; the message names the pair-scoped form.
- `DWARF091` (Warning) - `[BeforeMap]`/`[AfterMap]` on a partial method with no body, the mapping method itself
  included; the hook is dropped.
- `DWARF092` (Warning) - a directive is not read at this mapping endpoint (`[FlattenGraph]`, `[MapDerivedType]`,
  `[ReverseMap]`, `[MapCollectionKey]`, `[MapDenseEnumKeys]`); the message names the endpoint that reads it.
- `DWARF093` (Warning) - `[GenerateWrapperMap]` on a mapper with no `[GenerateMap]` pair to expand.
- `DWARF094` (Error) - a `[GenerateMap]` pair duplicates another one or a partial method over the same pair.
- `DWARF095` (Warning) - a `[MapIgnore]` names no destination member, unscoped or pair-scoped.
- `DWARF096` (Warning) - one `Project` method was not generated because a member cannot be translated; the rest of
  the mapper was.
- `DWARF097` (Warning) - one mapping method was not generated because a destination member has no source; the
  rest of the mapper was.
- `DWARF098` (Warning) - `[DwarfMapperConstructor]` names a constructor the mapper cannot use (inaccessible,
  obsolete, copy constructor, `ref`/`out` parameter, static); the message names the reason.
- `DWARF099` (Error) - one pair carries two contradicting `[MapNullSkip<TSource, TTarget>]` declarations.
- `DWARF100` (Info) - an array, list or span pair narrowly missed the block copy; the message names the blocker.
- `DWARF101` (Info) - a transfer-model struct spends a quarter or more of its size on padding; the message prints
  the field order that packs it.
- `DWARF103` (Info) - a mapped collection's class element could be a `readonly record struct`; a code fix performs
  the rewrite.
- `DWARF104` (Error) - `[MapShare]` names a member that cannot be shared.
- `DWARF105` (Error) - `[MapDenseEnumKeys]` names a member whose dense index cannot be proven.
- `DWARF106` (Info) - `[Reinterpret]` takes the block copy over a declared conversion or a pair-scoped directive.
- `DWARF107` (Warning) - a declared converter returns a nullable reference that is stored where null is forbidden.
- `DWARF108` (Warning) - `OnCycle = SetNull` reaches a value-type destination, which cannot hold the null; the pair
  falls back to the depth-guarded body.
- `DWARF109` (Error) - an `[AfterMap]` hook taking its target `by ref` matched a pair whose destination is a
  different type; the hook is skipped for that pair.
- `DWARF110` (Info) - a mapper nested as `private`/`protected` is left out of the extensions, `AddDwarfMappers()`
  and the ambient registry.
- `DWARF111` (Warning) - a `[ProvidesMap]` method is not registered because its pair is already provided.
- `DWARF112` (Info) - a projection's `{Method}Expression` property was not generated because the name is taken.
- `DWARF113` (Error) - a union type cannot be mapped here; the message names the missing condition (compiler,
  language version, endpoint, pair shape).
- `DWARF114` (Error) - a closed set of types (a C# 15 `closed` hierarchy or a union) has a member with no
  `[MapDerivedType]` arm. A `closed` hierarchy is checked when it comes from a referenced assembly; one declared in
  the mapper's own project is not checked yet.
- `DWARF115` (Warning) - a projection method in a NativeAOT project that references EF Core; the message names the
  precompilable shape.
- `DWARF116` (Warning) - a statement of intent (`AllowNull`, `KeepDefault`, `[MapClone]`) matches nothing.
- `DWARF117` (Error) - a scalar map cannot be generated as declared (no conversion to return, or a nullable value
  type on either side).
- `DWARF118` (Error) - a `[FlattenGraph]` node member leads back to the flatten method.
- `DWARF119` (Error) - a `[MapDerivedType]` dispatch has no arm for its own instantiable base type.
- `DWARF120` (Error) - `[DwarfMapper]` and `[GenerateMap]` are not read on a record.
- `DWARFR10` (Error) - a `[MapTo]` member is auto-matched while the assembly sets `AutoMatchMembers = false`.
- `DWARFR11` (Warning) - `[DwarfMapperConstructor]` is not read by the `[MapTo]` registry.
- `DWARFR12` (Warning) - the argument of `[MapIgnore("x")]` on a `[MapTo]` source member is not read (the member
  is still ignored).
- `DWARFR13` (Error) - a `[MapTo]` source type cannot be generic.
- `DWARFR14` (Error) - a `[MapTo]` target type cannot be an open generic.
- `DWARFR15` (Warning) - the `[MapTo]` registry assigns a nullable source where the destination forbids null.
- `DWARFR16` (Warning) - a `[MapProperty]` argument (`Use`, `When`, `NullSubstitute`, `StringFormat`,
  `KeepDefault`) is not read by the `[MapTo]` registry.
- `DWARFR17` (Error) - a `[MapTo]` `[MapProperty]` names a destination member that does not exist or is not
  writable.

### Added

- **Union mapping (C# 15, .NET 11 SDK).** A map between two unions is a `switch` over the source union's case types
  whose arms produce the target union's case types. A case is paired by an explicit `[MapDerivedType]` arm, by the
  same type, or by the one declared method that maps it; anything else is `DWARF114`. An empty union maps to an
  empty union, a nullable union keeps null apart from an empty one, and a case type converts into its union by the
  language's union conversion. See `docs/howto/map-unions.md` and `samples/DwarfMapper.Unions`.
- **`Dwarf.Map<TSource, TDestination>(source)` and `Dwarf.Map(source, destination)`.** They resolve like
  `IDwarfMapper.Map`, but when the calling project registers the pair the generator binds the call to the generated
  mapper with a C# interceptor: no registry lookup. A pair from another assembly, a create pair a referenced
  assembly also provides, and a call over type parameters use the registry; for an update pair that two assemblies
  register, the bound call uses the local map (`IsUpdateAmbiguous` reports it). `IDwarfMapper` calls are never bound,
  so decorators and test doubles keep working. The package enables interceptors for the `DwarfMapper.Generated`
  namespace through `build/DwarfMapper.props`; the consuming project sets nothing. Works under NativeAOT.
- **Update-into through the ambient registry.** `IDwarfMapper.Map(source, destination)`,
  `DwarfMapperRegistry.RegisterUpdate`, `Update`, `IsUpdateProvided` and `IsUpdateAmbiguous`; update maps have a
  key space of their own, so a pair can have both a create and an update map.
- **Ambient registry:** lookups resolve through interfaces; each declared map also registers six collection shapes
  keyed on `IEnumerable<TSource>` (`RegisterCollectionShapes`, on by default); `DwarfMapperRegistry.RegisterMany`
  registers a batch, and generated module initializers use it, so startup allocation no longer grows with the
  square of the map count.
- **`[ProvidesMap]`** registers a hand-written method into the ambient registry, for shapes the generator cannot
  express. A generated map of the same pair takes precedence (`DWARF111`).
- **`[RestatesBase<TSource, TTarget>]`** marks a pair that restates its base pair's configuration; the generator
  compares the two member by member and reports drift (`DWARF085`).
- **`[MapNullSkip]` and `[MapNullSkip<TSource, TTarget>]`** scope `SkipNullSourceMembers` to one method or one pair,
  most specific first: method, pair, mapper, assembly. Span and async-stream maps take the pair form; a projection
  refuses an untranslatable skip with `DWARF028`.
- **`EnumStringSource = Identifier`** maps enum members to and from strings by their C# names instead of
  `[EnumMember]`/`[Description]` values.
- **Provably immutable members are shared, not copied.** A same-typed member of an immutable collection type,
  `string`, or a `sealed` type whose members are all get-only or `init`-only gets the source's reference. Null
  handling is unchanged, and a `default(ImmutableArray<T>)` source now gives an empty array instead of throwing.
  Interfaces such as `IReadOnlyList<T>` are still copied.
- **`[MapShare("Member")]`** shares a reference the automatic proof cannot see through; a member the generator
  can see is mutable is refused (`DWARF104`).
- **`[MapDenseEnumKeys("Member")]`** writes an enum-keyed dictionary into an `[InlineArray(n)]` member with no
  allocation, after proving at build time that every declared enum member lands in range (`Offset` moves the
  window). A shape that fails the proof is `DWARF105`; `[Flags]` enums are refused; a key the enum does not
  declare throws `ArgumentOutOfRangeException` at run time. The emitted code is verifiable IL.
- **Statements of intent:** `[MapClone]` accepts a same-type map (`DWARF076`) for one method;
  `[MapProperty(AllowNull = true)]` accepts a deliberate null (`DWARF070`, `DWARFR15`) for one member;
  `[MapProperty(When = ..., KeepDefault = true)]` accepts a guarded member's unset default (`DWARF066`). Each
  changes nothing that is generated, and one with nothing to accept is `DWARF116`.
- **`{Method}Expression` on every projection**: the static `Expression<Func<S, D>>` the method applies, for
  composing EF queries as `db.Orders.Where(...).Select(OrderMapper.ProjectExpression)`.
- **Block copies for more shapes.** `array`/`List<T>` in every combination (and the interface types that
  materialise to `List<T>`), `ImmutableArray<T>`, enum arrays where the conversion is a reinterpret (`ByValue` over
  the same underlying type, or an enum against its underlying primitive), `Nullable<T>` pairs of layout-identical
  `T`, and span maps (`void Map(ReadOnlySpan<S>, Span<D>)`) all take one block copy when the build-time proof holds.
  `ByName` enum arrays are excluded, since their switch rejects undeclared values.
- **Vectorised widening for more shapes.** A lossless primitive widen (`int` to `long`, `float` to `double` and
  the five other `Vector.Widen` pairs) into or out of a `List<T>` or `ImmutableArray<T>`, or between spans, takes
  the vectorised loop that array-to-array widening uses. Results are identical element for element.
- **Transfer models as structs.** `DWARF103` suggests a `readonly record struct` for a mapped collection's element
  and its code fix rewrites the type and the transfer models it inlines; `DWARF101` names a padding-free field
  order. `docs/diagnostics.md` lists the hazards of the conversion, and `docs/PERFORMANCE.md` the measurements.
- **DwarfMapper.Testing, first published.** `ObjectFactoryV2` (seeded fixtures for every type, abstract and
  interface substitution, `[Flags]` combinations), `Fuzzer`, `RoundTrip.Verify`, `StructuralComparer`,
  `GraphOracleComparer`, and `LensLaws.VerifyIdempotent`/`VerifyLastWriteWins`, which check an update-into map
  against a destination that already holds data and report one replay seed (`LensLawException`). Reflection-based
  and test-only.
- **.NET 11:** a `net11.0` project references the `net10.0` assembly as it is. CI builds the repository with SDK
  11.0.100, builds and tests the samples as `net11.0`, and publishes the NativeAOT sample with the .NET 11 compiler.

### Changed

- **Package layout:** the generator ships as `analyzers/dotnet/roslyn5.0/cs` and `analyzers/dotnet/roslyn5.12/cs`
  (1.0.2-rc.1 had one build in `analyzers/dotnet/cs`). The package also carries `build/DwarfMapper.props` and
  `buildTransitive/`, which enable interceptors for `DwarfMapper.Generated` and make `PublishAot` visible to the
  generator.
- **One incomplete method no longer takes the whole mapper down.** An unmapped destination member (`DWARF001`) or an
  untranslatable projection member (`DWARF028`) withholds only that method, signposted by `DWARF097` or `DWARF096`;
  the build still fails on the error, but the other methods are generated. Class-level errors still stop the class.
- **Diagnostics point at your code.** A diagnostic from a nested pair is placed at the declared method that reached
  it instead of having no location; `DWARF058`, `DWARF061`, `DWARF062`, `DWARF063` and `DWARF081`, which are
  reported without a location, now start their message with the id; no message names a generated helper as yours.
- **Existing diagnostics:** `DWARF012` names the directive actually written and is titled "Conflicting [MapIgnore]
  and a mapping directive"; `DWARF070` says "source member" or "mapping parameter", names elements and dictionary
  values, carries the destination in its `Member` property and is titled "A nullable source is assigned to a
  non-nullable target member"; `DWARF063` counts and names distinct providing assemblies; `DWARF044` fires only for
  a nullable root that a destination member reads; `DWARF066` no longer fires on an update-into, on a member with a
  non-null initializer, or on a parameter of a positional record; `DWARF070` no longer reports a converter argument
  that `[MapNullSkip]` or `SkipNullSourceMembers` guards; `DWARFR09` covers every type the `[MapTo]` registry
  constructs, nested and element types included; `DWARF035` also reports an invalid union arm; `DWARF098` also
  reports a static constructor; `DWARF068` and `DWARF069` have readable titles.
- **`[MapTo]` refuses union pairs with `DWARF113`** instead of mapping the union as an ordinary struct; the same union
  is still copied.
- **Suppression guidance.** DWARF diagnostics are reported where neither `#pragma` nor a per-file `.editorconfig`
  severity reaches them; a `.globalconfig` (`is_global = true`) does, in both directions, and the docs and messages
  say so. The statements of intent above replace every `<NoWarn>` recommendation.
- **Generated code:** null guards call `ArgumentNullException.ThrowIfNull`; `DwarfRefContext` flags are passed by
  name; a switch that names an `[Obsolete]` enum member is wrapped in a scoped `#pragma warning disable CS0612, CS0618`
  (an `[Obsolete(error: true)]` member is skipped); `DwarfRefContext` is `[EditorBrowsable(Never)]`.
- **Faster:** the generator semantically binds only invocations shaped like `Dwarf.Map`, not every `.Map(...)`
  call; ambient collection shapes pre-size from the source count; collection lookups through the ambient registry
  stay flat as an application adds maps; a projection builds its expression tree once and maps an in-memory
  `IQueryable` (`list.AsQueryable()`) through a compiled delegate; value-element lists are filled through a span.
- **The public API is tracked:** a rename or removal is a declared break.

### Fixed

- **Generated code that did not compile, with no DwarfMapper diagnostic:** identifiers named after keywords
  (`@class`, `@event`) in mapper, method, parameter, hook, converter, enum-member and registry positions (and
  `[MapCollectionKey]` now finds a keyword-named member instead of reporting `DWARF074`); a mapper
  nested as `private` or `protected` (`CS0122` in the assembly-wide extensions); `[FlattenGraph]` into a `struct`
  node; `[MapTo]` on a `struct` source or with a value-type collection member; a collection whose `Count` is an
  explicit interface implementation; a nullable element over a value-type source; a projection with a nullable
  object into a value-type member (now `DWARF028`); span
  and async-stream maps under `Preserve`, over recursive pairs or with self-referencing elements; a span map over
  nullable elements; a `SetNull` dispatch whose arm leads back into the graph; on a Roslyn 5.9 or newer compiler, a
  C# 14 project with a union-typed converter, hook or member (`CS9327`).
- **Projections:** a nested member's null guard is written `(object)x == null`, so it no longer calls a user
  `operator ==` (which also gave `CS0034` for a type with two of them); a nullable nested member whose source and
  destination types are declared as different kinds (struct, class, record) now projects with the same null lift as
  `Map` instead of failing with `DWARF028`.
- **A `[FlattenGraph]` method's pair can be mapped inside another map under `Preserve` and `OnCycle = SetNull`.** It
  stopped with `DWARF001` on the flattened collection; the nested use now goes through the flatten method, and under
  `Preserve` one source graph is one target wherever it is met.
- **Collisions reported where they were a raw compiler error:** a `[GenerateMap]` duplicating a map (`DWARF094`),
  two `[FlattenGraph]` directives on one collection (`DWARF087`), a constructor-only nested `[MapTo]` type
  (`DWARFR09`), `SetNull` into a value-type destination (`DWARF108`), a `by ref` hook on a derived pair
  (`DWARF109`), a generic `[MapTo]` source (`DWARFR13`), an open generic target (`DWARFR14`, previously misreported
  as `DWARFR09`), a record carrying `[DwarfMapper]` or `[GenerateMap]` (`DWARF120`, previously a bare `CS8795` or
  nothing).
- **Nullable warnings inside generated files, which no consumer setting can suppress:** nullable source, return and
  destination parameters (`CS8611`, `CS8819`, `CS8603` in the extensions and the ambient registration); nullable
  extra mapping parameters (`CS8611`, `CS8604`, `CS8601`, and `CS0266` for `int?` into `int`); collection elements,
  dictionary keys and values, constructor parameters, async-stream elements and nested members routed through a
  converter you declared (`CS8600`, `CS8604`); nullable-value array elements (`CS8629`); nullable source
  collections on the block-copy and widen paths (`CS8604`); immutable targets with nullable elements (`CS8619`);
  `[MaybeNull]`/`[DisallowNull]` members; the `[MapTo]` registry (`CS8601`, `CS8604`, `CS8619`, `CS8620`, now
  `DWARFR15`). Where a null could be stored, the code now reports `DWARF070` or `DWARF107`.
- **A nested member mapped through your own map method follows the null policy:** null in, null out where the
  destination can hold it, instead of `CS8604` or an `ArgumentNullException` depending on which converter the
  resolver picked.
- **Directives that were dropped without a word are reported:** `[MapIgnore]` naming nothing (`DWARF095`), with an
  ordinal name match under every option; the wrong overload or placement of `[MapProperty]`/`[MapIgnore]`
  (`DWARF088`, `DWARF089`); unscoped directives, `[Reinterpret]`, `[Flatten]` and `[MapValue]` on span and
  async-stream maps (`DWARF090`); `[FlattenGraph]`, `[MapDerivedType]`, `[ReverseMap]` and `[MapCollectionKey]` on
  endpoints that do not read them (`DWARF092`); `[GenerateWrapperMap]` with nothing to expand (`DWARF093`);
  `[DwarfMapperConstructor]` on a static constructor (`DWARF098`) or under `[MapTo]` (`DWARFR11`); `[MapTo]`
  arguments the registry does not apply (`DWARFR12`, `DWARFR16`); a null directive name, now treated as the empty
  name.
- **`DWARF064`'s remedy works:** `[MapIgnoreSource]` now silences it at the create map and the projection, and the
  message names the shadowed member as the source spells it.
- **A `[FlattenGraph]` with `[MapDerivedType]` arms no longer drops a derived node's nested-object or collection
  member** (it is flattened, and reported as `DWARF075` under `Preserve`).
- **Block-copy soundness:** the proof could accept two structs whose fields were split across partial files in
  opposite orders, and the copy swapped their values; a struct whose instance fields span partial declarations is
  now never block-copied, by proof or by `[Reinterpret]`. `[StructLayout(Size = ...)]`, `[InlineArray(n)]` and
  fixed-buffer lengths are now compared too; a mismatch threw or left the destination partly zeroed.
- **Block copies under `Preserve`** are no longer taken where they would give two members sharing one source
  collection two target collections. `DWARF100` is no longer reported where no block copy could be taken, and is
  reported on a span map whose names are reconciled by `[MapProperty<S,T>]`.
- **The generator could crash in an IDE** on a stale source location and silently contribute no output (reported only
  as `CS8785`); it now drops the location and carries on.
- **`[MapProperty(StringFormat = ...)]`** no longer emits an unused private helper.
- **Documentation:** the blit is guarded by its build-time proof alone (no run-time size guard, as earlier text
  claimed); `DwarfMap.Validate()` checks that a map is provided, not that it is provided once (`DWARF063` and
  `DwarfMapperRegistry.IsAmbiguous` answer that).

## [1.0.2-rc.1] - 2026-08-04

Published to nuget.org by hand, before this changelog was kept and before releases were tagged: there is no
per-change record, and no SBOM or provenance attestation for this version. The package carried one generator build
(in `analyzers/dotnet/cs`) and the following 82 diagnostic ids, listed here as its diagnostic surface. Severities are
the defaults.

**Errors:**
- `DWARF001` - a destination member is not mapped.
- `DWARF002` - a class annotated `[DwarfMapper]` is not `partial`.
- `DWARF003` - a mapping method's signature matches no supported shape.
- `DWARF005` - a paired source and destination member have no implicit conversion and no converter applies.
- `DWARF007` - a mapping targets a read-only destination member.
- `DWARF008` - `[MapProperty]`'s destination member does not exist or is not writable.
- `DWARF009` - `[MapProperty]`'s source member does not exist or is not readable.
- `DWARF010` - under `CaseInsensitive = true`, a destination member matches more than one source member.
- `DWARF011` - a destination member has more than one `[MapProperty]`.
- `DWARF012` - a member is both `[MapIgnore]`d and `[MapProperty]`-mapped.
- `DWARF013` - more than one mapping method can convert the member's types.
- `DWARF014` - a named conversion method was not found.
- `DWARF015` - by-name enum mapping found a source member with no same-named destination member.
- `DWARF016` - `[Flatten]`'s root does not exist, is not readable, or exposes no readable members.
- `DWARF017` - a destination member is flattened from more than one source root.
- `DWARF018` - a `[BeforeMap]`/`[AfterMap]` hook has the wrong shape.
- `DWARF020` - a `[RoundTrip]` method has no inverse mapping method.
- `DWARF021` - a `[RoundTrip]` method has more than one candidate inverse.
- `DWARF022` - `[Reinterpret]` must map an array to an array of an unmanaged element type.
- `DWARF023` - an `[AfterMap]` on a value-type target takes it by value; take it by `ref`.
- `DWARF024` - a required constructor parameter has no mappable source member.
- `DWARF025` - several constructors tie, or more than one carries `[DwarfMapperConstructor]`.
- `DWARF026` - the destination type has no accessible, non-obsolete instance constructor.
- `DWARF027` - the collection or dictionary target type is not supported.
- `DWARF028` - a projection member has no database-query translation.
- `DWARF030` - a constructor-bound or `init`-only member takes part in a reference cycle under `Preserve`.
- `DWARF031` - the generator reached its limit of 512 synthesized nested mappers.
- `DWARF032` - a `[MapProperty(Use=)]` converter is opaque, so `Preserve` cannot track the value's identity.
- `DWARF033` - auto-nesting an abstract or interface source would drop members of derived runtime types.
- `DWARF034` - a `[FlattenGraph]` is misconfigured.
- `DWARF035` - a `[MapDerivedType]` arm is invalid.
- `DWARF036` - two `[MapDerivedType]` arms overlap.
- `DWARF040` - a constant `[MapValue]` is not assignable to the destination type.
- `DWARF041` - a `[MapValue(Use=)]` provider must be parameterless and return an assignable value.
- `DWARF042` - a `[MapValue]` conflicts with another directive, names an unknown member, or targets a constructor
  parameter.
- `DWARF043` - a dotted `[MapProperty]` source path names a segment that does not exist.
- `DWARF045` - a `[MapProperty]` unflatten target path is invalid.
- `DWARF046` - a `[MapProperty]` unflatten target conflicts with a direct mapping of the same root.
- `DWARF048` - under `NameConvention.Flexible`, two source members normalize to the same destination member.
- `DWARF049` - a `[MapProperty(NullSubstitute=)]` value is not assignable to the destination member.
- `DWARF050` - a `[MapProperty(When=)]` predicate must be a `bool` method taking the source.
- `DWARF052` - a `[ReverseMap]` has no inverse mapping method to attach to.
- `DWARF053` - a mapping method declares type parameters.
- `DWARF054` - the class carrying the mapping is generic.
- `DWARF057` - the generated `<Host>Mapper` type name collides with an existing type.
- `DWARF059` - a `[MapConstructor]` factory method does not exist or is incompatible.
- `DWARF060` - two maps from the same source type would overload `Map` by return type alone.
- `DWARF061` - a required ambient map is provided by no assembly in the graph.
- `DWARF067` - a `[GenerateWrapperMap]` wrapper is not a single-payload generic type.
- `DWARF068` - a `MapConfig<S,T>` call uses an unsupported selector or argument.
- `DWARF069` - the same destination member is configured more than once.
- `DWARF072` - under `AutoMatchMembers = false`, a destination member has a same-named source match.
- `DWARF073` - `[MapProperty(StringFormat=)]` is not applicable to the member.
- `DWARF074` - `[MapCollectionKey]` is outside the key-based upsert's scope.
- `DWARF077` - explicit-only mapping cannot be enforced on a span or async-stream method.
- `DWARFR01` - invalid `[MapTo]` target.
- `DWARFR02` - a `[MapTo]` destination member is not mapped.
- `DWARFR03` - conflicting sources for one `[MapTo]` destination member.
- `DWARFR04` - a `[MapProperty]` value count does not match the `[MapTo]` targets.
- `DWARFR05` - no conversion between `[MapTo]` mapped members.
- `DWARFR06` - recursive nested mapping is not supported by the `[MapTo]` registry.
- `DWARFR08` - two `[MapTo]` targets generate the same method name.
- `DWARFR09` - a `[MapTo]` target has no accessible parameterless constructor.

**Warnings:**
- `DWARF037` - `OnCycle` is set together with `ReferenceHandling = Preserve`, where it has no effect.
- `DWARF044` - a dotted source path traverses a nullable member and can throw at run time.
- `DWARF051` - a forward `[MapProperty]` cannot be auto-inverted by `[ReverseMap]`.
- `DWARF056` - a pair-scoped attribute matches no mapped pair.
- `DWARF063` - an ambient map is provided by more than one assembly.
- `DWARF070` - a nullable source member is assigned to a non-nullable target member.
- `DWARF075` - a `[FlattenGraph]` leaf could not be flattened under `Preserve` and is left at its default.
- `DWARF076` - a declared create map maps a type to itself.

**Info:**
- `DWARF038` - an implicit type conversion is applied (Warning for lossy kinds; Error under
  `ImplicitConversions = false`).
- `DWARF039` - under `RequiredMapping = Both`, a source member is read by no destination member.
- `DWARF047` - an additional mapping parameter matched no destination member.
- `DWARF055` - a single mapper resolves a very large number of members.
- `DWARF058` - two mappers would produce the same convenience extension, so neither got it.
- `DWARF062` - a mapper with constructor dependencies is left out of the ambient registry.
- `DWARF064` - a `[MapValue]` shadows a same-named readable source member.
- `DWARF065` - an update-into replaces a nested object instead of merging into it.
- `DWARF066` - a `[MapProperty(When=)]` guards a non-nullable member, which keeps its default when false.
- `DWARF071` - the source member's type has derived types whose extra members would be dropped.
- `DWARFR07` - a `[MapTo]` member uses a lossy implicit numeric conversion.

## [1.0.1-rc.1] - 2026-07-05

Published to nuget.org by hand, before this changelog was kept and before releases were tagged: there is no
per-change record, and no SBOM or provenance attestation for this version.

## [1.0.0-rc.1] - 2026-07-04

The first published version. Published to nuget.org by hand, before this changelog was kept and before releases
were tagged: there is no per-change record, and no SBOM or provenance attestation for this version.

<!--
Cutting a release: rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (pre-release suffix included, e.g.
`## [1.1.0-rc.1] - 2026-11-20`) and open a fresh, empty `## [Unreleased]` above it. The release workflow
(.github/workflows/release.yml) matches a heading that starts with `## [` + the tag without its leading `v`, takes
everything up to the next `## [`, and fails if that section is missing, empty, or longer than 125,000 characters.
The 1.1.0-rc.1 heading reads "Unreleased" until the tag is cut; replace that word with the tag's date then.

Every live diagnostic id must appear somewhere in this file. Scan9 in
tests/DwarfMapper.Generator.Tests/SelfValidation/AssemblyScanTests.cs fails the build for a DWARF### id it cannot
find, exempting only the reserved ids listed there; the DWARFR## registry ids are not scanned, but each is named
here as well. Write each id in full - a substring search does not read a range such as "DWARFR10-DWARFR17". A new
id belongs under `### New diagnostics` of the section being prepared.
-->
