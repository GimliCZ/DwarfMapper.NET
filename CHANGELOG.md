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

- **A constructor-only NESTED type reached the compiler as `CS1729` out of a generated `[MapTo]` file.**
  `DWARFR09` guarded the `[MapTo]` target and nothing else, but the registry constructs a second kind of
  type with `new T { … }`: every nested object — and, through the collection path, every element type. A
  positional record (or any other ctor-only type) in either position emitted an object initializer that
  does not compile, with no diagnostic at all: exactly the failure `DWARFR09` exists to replace, one level
  down. Both construction sites now ask the question, under the same id — its title changed from
  *"[MapTo] target has no accessible parameterless constructor"* to **"A type the `[MapTo]` registry
  constructs has no accessible parameterless constructor"**, and the message now names the type at fault
  and, for a nested one, the member it was reached through. A nested type that *can* be built with an
  object initializer still is. Alongside it, a loud nested refusal no longer also reports `DWARFR05`
  *"the source and destination member types are incompatible"* — they are not incompatible, and the
  recursive-nesting refusal (`DWARFR06`) drew one such false companion per nesting level. (round 22, W3/B30)
- **`DWARF044` warned about a `[Flatten]` that pulled nothing up.** The nullable-reference-root warning —
  *"a null value throws at runtime when its flattened members are read"* — was reported the moment the root
  resolved, before anything asked whether a single leaf landed on a destination member. A flatten that maps
  nothing at all still warned, about members nobody reads. It now fires only for a root some destination
  member really pulled a leaf up from, which is the unguarded `src.Root.Leaf` the warning is about; where
  two nullable roots are declared and one lands a leaf, exactly that one is reported. No other trigger
  changed: a consumed nullable root still warns, and a non-nullable root still does not. (round 22, W2/B26)
- **A `[MapIgnore]` that named nothing was silently inert at every endpoint — and the `[MapTo]` registry
  silently discarded its argument.** Two halves of one silence (B20 and B15), decided one way. Class model:
  an unscoped `[MapIgnore("Name")]` whose name matches no destination member anywhere it is read — a typo,
  or `[MapIgnore("id")]` against a property `Id`, even under `CaseInsensitive = true` — excluded nothing and
  said nothing; the caller believed a member was excluded while the completeness gate went on demanding it.
  It now reports the new **`DWARF095`**, a Warning like its pair-scoped sibling `DWARF056`: a method-site
  name is judged against that method's own destination, a class-site name against *every* pair the class
  maps (a class-wide ignore that is about one of two pairs stays legitimately silent on the other), and a
  name matching only a span/async *element* pair remains `DWARF090`'s report. The comparer question the
  silence had left open (B21) is settled with it: **directive names bind ordinally under every option** —
  `CaseInsensitive` fuzzes auto-matching, never the binding of a name the caller wrote — and the mismatch is
  now loud instead of silently inert. Registry: `[MapIgnore("x")]` on a `[MapTo]` source member has always
  meant "ignore the annotated member", with the argument accepted and *discarded* — while the identical text
  on a co-located host member is refused as `DWARF089`. The discard now reports the new **`DWARFR12`**, a
  Warning that keeps the behaviour: the member is still ignored, the argument the caller wrote is just no
  longer dropped without a word. (round 22, W1/B15+B20+B21)
- **The span map and the async-stream map emitted code that did not compile when the element converter
  carried the reference-tracking tail.** Under `ReferenceHandling = Preserve` every auto-nested element
  mapper takes a `(DwarfRefContext, int)` tail, and the element-wise emissions called it without one —
  **`CS7036`** in the generated file, at both endpoints, with no DwarfMapper word (the surface matrix's two
  `EmittedInvalidCode` cells, filed as B33). The same missing tail was reachable with no `Preserve` in
  sight: a *recursive* element pair under default `None` handling, and under `OnCycle = SetNull` — where a
  second sibling gap compounded it, the SetNull post-pass skipping the span/async models because it read
  their *parameter* type (a span struct) where the *element* is what cycles. Both emitters now allocate
  **one shared `DwarfRefContext` per call** and thread it into every element — the identity-map scope the
  top-level collection path already gives a `List<T>` map, which is what made correct emission the
  non-ambiguous choice over a refusal: under `Preserve`, two span slots or two stream elements holding the
  same source object land the **same** target instance, and a child shared between two elements stays one
  child. (For an async stream that identity map lives as long as the iterator — the collection path's
  retention cost, stretched over a lazy sequence.) Pinned by *executing* tests
  (`ElementWiseReferenceHandlingRuntimeTests`), each mode sabotage-verified red before the fix; the
  `EmittedInvalidCodeCellCeiling` re-measured 10 → **8**. (round 21, T6/B33)
- **`[GenerateMap<A, B>]` colliding with an existing map over the same pair was a bare `CS0111` out of
  generated code.** Every `[GenerateMap]`-synthesized method is named `Map` and overloaded by the *source*
  type, so the same pair declared twice — or declared beside a `partial B Map(A)` over the same pair — would
  emit two members with an identical signature *and* return type: `CS0111` plus a `CS0121` ambiguity cascade
  at every call site, about a collision the generator created (B27; eight surface-matrix
  `EmittedInvalidCode` cells). It is now refused as the new **`DWARF094`**, an Error, reported *before*
  anything is emitted — the gap between **`DWARF060`** (same signature, *different* return types) and
  **`DWARF057`** (the generated mapper *type* collides) is closed. The message names which of the two shapes
  it met and the way out of each: remove the duplicate attribute, or declare the pair once — keep the
  partial method, or keep the `[GenerateMap]` (the `[GenerateWrapperMap]` family needs the latter, and
  `DWARF093`'s sharp-edge sentence now points here instead of narrating the raw `CS0111`). Refused rather
  than deduplicated for `DWARF087`'s reason — keeping one of two identical directives silently hides the
  mistake — plus one sharper: a co-located host's member directives bind to declared pairs *positionally*,
  so a duplicated pair shifts which pair a directive configures. Wrapper-expanded pairs flow through the
  same detection, and the legal neighbours are pinned: a *differently-named* partial over the same pair and
  two pairs sharing only a source or only a target stay accepted. The `EmittedInvalidCodeCellCeiling`
  re-measured 8 → **0** — the population this repository says must not exist, actually emptied. (round 21,
  T6/B27)
- **`[DwarfMapperConstructor]` was accepted and ignored by the `[MapTo]` registry.** The directive names the
  constructor DwarfMapper must build a target with; the registry front door selects no constructor *at all*
  — there is no `ConstructorSelector` call anywhere under `Registry/` — and builds every type it constructs
  with `new T { … }`, so the annotation changed nothing and nobody said so. It is now refused as the new
  **`DWARFR11`**, a Warning: the object-initializer mapping the caller gets is correct and complete, so the
  mapper still ships; what is wrong is that the construction the caller asked for is not the one the emitted
  code performs. **Refused rather than honoured, argued from the front door's own design**: `DWARFR09`
  already refuses a constructor-only target with *"use the `[DwarfMapper]` class model (which supports
  constructor mapping)"*, and object-initializer-only construction is what that message describes, not an
  oversight — teaching the registry constructor binding, parameter satisfaction and a per-parameter
  completeness gate is a feature, not the fix for this silence. The prescribed remedy is **measured**, not
  asserted: the same annotation on the same target type, mapped through a `[DwarfMapper]` class model map
  over that pair, selects the annotated constructor (`RegistryDiagnosticsGenTests`). It fires at **both** of
  this generator's construction sites — the `[MapTo]` target and the nested-object helper, which is also the
  path a collection's element type reaches — from one function, because reporting only at the target would
  have left the identical silence one level down. The predicate is `ConstructorSelector`'s own, hoisted:
  "carries `[DwarfMapperConstructor]`" was written inline twice inside that selector and would have become a
  third copy here. Found by the surface matrix as `A11-F2`; its registry cell closes. (round 20, A14)
- **`[DwarfMapperConstructor]` was accepted and ignored at the projection endpoint.** The directive names the
  constructor DwarfMapper must use when it builds the destination, and a projection builds one — but the
  projection resolver decided that on its own, by a local widest-arity `FirstOrDefault` over the target's
  public constructors, and its one call to `ConstructorSelector` *discarded the return value*. So on a target
  with a parameterless constructor and writable members the annotated overload was never reached: the same
  mapper called `new Dst(id, name)` through `.Map` and `new Dst { Id = …, Name = … }` through `.Project`, with
  no diagnostic either way. The decision is now made **once**, by `ConstructorSelector` — the same policy the
  create map, span map, async stream and co-located host already run — and the *nested* projection path,
  which carried a second copy of the widest-arity pick under a comment instructing the reader to keep the two
  in step, is the second call site of that one function rather than a third copy. Three consequences were
  measured, not predicted: `[DwarfMapperConstructor]` on a nested projection target is honoured too;
  **`DWARF025`** (two annotated constructors) now reaches the projection endpoint, where the malformed
  declaration the create map refuses used to be accepted in silence, because the check sat on a branch a
  target with a parameterless constructor never enters; and an unbindable constructor parameter is now
  `DWARF024` here as it is at the create map, rather than a `DWARF001` about the member the parameter feeds.
  Two defects the fix uncovered are fixed with it: annotating the **parameterless** constructor projected as
  the *widest* one (selection reports that pick with `useObjectInitializerOnly = false`, and reading the flag
  rather than the chosen constructor's arity sent it down the constructor branch), and the nested
  constructor-projection path emitted `new LeafDto(x, y) { X = …, Y = … }` — assigning both members twice —
  because its leftover filter matched parameter to member under the configured comparer alone while the
  top-level one also matched case-insensitively. That filter is now one predicate with two call sites. No new
  diagnostic id. **One knock-on, measured both ways rather than predicted, and a Map/Project divergence
  closing rather than a new one opening:** a `struct` destination with an explicit non-parameterless
  constructor used to project as `new Dst { X = …, Y = … }`, because the local decision saw the struct's
  *implicit* parameterless constructor and preferred member-init. `ConstructorSelector` deliberately skips
  that constructor for a struct that declares an explicit one — it is a zero-init no-op — so the projection
  now calls the explicit constructor, which is what `.Map` over the same pair has always done. No test in
  the repository covered that shape, so the suite passing meant *uncovered*, not unchanged; it is pinned
  now. Found by the surface matrix as `A11-F2`; its projection cell closes. (round 20, A14)

- **`[MapTo]` on a `struct` generated code that did not compile.** The attribute's own `AttributeUsage` admits
  `AttributeTargets.Struct` and the registry's target check admits `TypeKind.Struct`, so a value-type source is a
  placement the product advertises — but `MapToGenerator` wrote `if (source is null) throw …` into every extension
  method it emitted, without ever asking whether the source could *be* null. Against a non-nullable value type that
  pattern is **`CS0037`**, so the generated file was rejected by the compiler at all seven endpoints and the caller's
  only clue was an error inside a file they never wrote. No diagnostic is added: the placement is legal and now works.
  The guard is gated on the new `TypeFacts.CanBeNull`, which answers "can a value of this type be null" rather than
  the narrower "is this a reference type" — a `Nullable<T>` source is a value type that *can* be null and keeps its
  guard, and `where T : struct` loses it. **All four of the registry's emission sites read it**, which is the part
  that took two passes: the nested-object helper already had the discrimination inline as `IsReferenceType`, and the
  synthesized *collection* helper — sixty lines further up — had none either, writing `if (s is null) return …` into
  every helper it produced. That third site needs no value-type *source* at all to bite:
  `CollectionConverter.TryGetEnumerableElement` admits any `IEnumerable<T>` including a struct one, so an ordinary
  **class** with a value-type collection member emitted the same `CS0037`. Nothing in `samples/` or `tests/` had ever
  put `[MapTo]` on a value type, which is why this shipped; conformance feature **F49** and
  `RegistryMapToValueTypeSourceRuntimeTests` now exercise the shape and assert the mapped *values*, not merely that it
  builds. Found by the surface matrix as `A11-F1`; its fourteen cells leave the `NotCompilable` population and read
  `Honoured` at every endpoint. (round 20, A11-F1)
- **`[GenerateWrapperMap]` on a mapper class that declares no `[GenerateMap]` pair did nothing and said
  nothing.** The attribute is an *expansion* of the `[GenerateMap<A, B>]` pair list — it appends the closed
  wrapper instantiation `W<A> -> W<B>` per declared pair — and the expansion routine returned early on an
  empty list, before any validation at all. So on a `[DwarfMapper]` class whose maps are declared as partial
  mapping *methods*, the opt-in was read by nobody at every one of the five mapper endpoints: no wrapper map,
  no refusal. It is now refused as the new **`DWARF093`** (a Warning, so the mapper is still emitted), naming
  the working form — and naming its one sharp edge, which was measured: `[GenerateMap<A, B>]` emits its own
  `B Map(A)`, so adding it beside a `partial B Map(A)` over the same pair collides (originally a raw
  `CS0111`; refused as `DWARF094` since round 21), and there the pair must
  be declared with `[GenerateMap]` *instead of* the partial method. Refused rather than widened, argued from
  what the attribute means: it is defined relative to `[GenerateMap]`, a partial mapping method is a different
  declaration mechanism, and four of the five endpoints — update-into, projection, span map, async stream —
  have no `W<A> -> W<B>` create-map shape to synthesize at all. The check runs *before* the wrapper's shape is
  validated, because with no pairs to expand even a perfectly-shaped wrapper expands nothing. Found by the
  surface matrix as `D15`; all ten of its cells close. **The finding's stated mechanism was wrong**: it read
  the `DWARF067` at the co-located host as the generator having an opinion about *where* the attribute is
  valid, and `DWARF067` is an opinion about the *wrapper type* — it fired there only because that template
  declares a `[GenerateMap]` pair and the probe's sampled `typeof(Dst)` is not a single-parameter generic.
  (round 20, D15)
- **`[Reinterpret]` was dropped by the two endpoints whose whole purpose is bulk element throughput.** A
  forced blit reinterprets one array's memory as another in a single block copy, and it is exactly what a
  caller reaches for when moving elements in bulk — yet `[Reinterpret("Data")]` on a span map or an
  async-stream map was read by nobody and reported by nobody, and the member was copied element by element
  through an ordinary conversion helper instead. Honoured on the same mapper's create map and update-into,
  silent on the next two. Both endpoints now refuse it as **`DWARF090`**, the element-wise gate it belongs to.
  It is the one arm of that gate with **no pair-scoped twin**, so its remedy is a *declared* create map rather
  than a re-scoped attribute — measured before it was printed: with the directive on a
  `partial Dst Map(Src s)` beside the span method, the emitted loop is `d[__i] = Map(s[__i]);` and `Data` is
  assigned through `MemoryMarshal.Cast`, because an element-wise map resolves its element pair through a
  declared mapping method where the class has one rather than synthesizing a fresh one. Found by the surface
  matrix as `D12`; all four of its cells close (2 renderings × 2 endpoints). **The finding's evidence was wrong about one endpoint**:
  it claimed the directive acts at *projection* as well, and it does not — the projection branch never reads
  it, and the reading that looked like an effect was a `DWARF028` the pair earns with or without it. (round
  20, D12)
- **`[MapCollectionKey]` was read on an update-into and discarded on the mapper's other four overloads.** A
  key-based upsert **merges** the source elements into the `List<T>` the destination already holds — matching
  on the named key, replacing what matches and appending what does not, so untouched elements survive. Only
  the update-into branch ever read it. Written on a create map, a projection, a span map or an async-stream
  map it was discarded without a word and the collection was rebuilt by whole-collection replacement, which
  is what it would have been without the directive: one mapper, one declaration, a merge on one overload and
  a wholesale rebuild on the next four. All four endpoints now refuse it as **`DWARF092`**, naming the
  update-into as the place to declare it — measured before it was printed, and carrying **no** transfer claim
  at any endpoint, element-wise included, because what a span or stream loop adopts is a declared *create*
  map for its element pair and an update-into is not one. `DWARF092`'s title changes from *"Directive is read
  only at the create-map endpoint"* to **"Directive is not read at this mapping endpoint"**: the id now
  carries both directions of one shape, and the gate behind it (`ReportDirectivesNotReadHere`) is called from
  all five branches with each arm naming its own home endpoint. `DWARF074`'s documentation had listed "not an
  update-into method" as a case it covered since it was written, and no call site ever implemented it; that
  comment is corrected where it stood. Found by the surface matrix as `D14`; all eight of its cells close.
  **The finding's own evidence was false** and the correction is worth reading: it claimed the directive
  "acts at UpdateInto", and against the fixture it was measured on it could not — that fixture declared
  `List<Item>` on the source and `List<ItemDto>` on the destination, and the v1 upsert requires the same
  element type, so the one endpoint the directive exists for was a `DWARF074` error behind `CS8795`.
  (round 20, D14)
- **`[ReverseMap]` was read on a create map and discarded on the mapper's other four overloads.** It makes a
  **separately-declared** inverse method inherit the forward method's simple renames with their ends swapped,
  matched by signature — a forward `TDto Map(TSource s)` against an inverse `TSource Back(TDto d)`, both
  create maps. No other endpoint's signature is that shape, so a `[ReverseMap]` on an update-into, a
  projection, a span map or an async-stream map made nothing look for an inverse and nothing inherit a
  rename — and raised no `DWARF052` either, because that check lives on the same create-map path. The caller
  was left with an inverse that silently maps nothing across the renamed member. All four endpoints now
  refuse it as **`DWARF092`**. Its message is the one that carries **no** transfer claim, even at the
  element-wise endpoints where the other two directives do: `[ReverseMap]` changes nothing about the method
  it sits on, so "the element-wise loop calls that create map" says nothing about it. Found by the surface
  matrix as `D13`; all four of its cells close. The entry's mechanism is corrected where it stood —
  `[ReverseMap]` does not *generate* an inverse, and a missing one is `DWARF052` rather than a silent
  absence. (round 20, D13)
- **`[MapDerivedType]` was read on a create map and discarded on the mapper's other four overloads, in both
  of its forms.** A dispatch arm decides which destination **type** to construct from the source's runtime
  type, and only the create-map branch resolved one. Written on an update-into, a projection, a span map or an
  async-stream map, a derived instance was mapped as its **base** and every member the derived DTO declares
  beyond the base one was dropped — with no diagnostic. Nor was anything validated there: the create map
  refuses a type that is not assignable, a duplicate source type, or a pair that is not mappable
  (`DWARF035`), and elsewhere the same nonsense passed without a word. All four endpoints now refuse it as
  **`DWARF092`**, quoting the directive back in the form it was written — generic stays generic, `typeof`
  stays `typeof` — and naming the create map. At the two element-wise endpoints that create map is what the
  emitted loop calls, so the dispatch really does reach them through it. Found by the surface matrix as `D8`;
  all sixteen of its cells close. **The finding's own evidence was partly wrong**, and the correction is worth
  reading: it claimed the directive "acts at CreateMap in BOTH forms", and the open form acted nowhere — the
  probe's flat DTO pair declares no hierarchy, so the sampled arguments named a type not assignable to the
  method's source parameter and the create map was simply refusing nonsense. (round 20, D8)
- **`[FlattenGraph]` was read on a create map and discarded on the mapper's other four overloads.** A graph
  flatten replaces the source of a destination collection with a breadth-first walk of a source navigation.
  Only the create-map branch resolved one, so `[FlattenGraph("Root", "Flat")]` on an update-into, a
  projection, a span map or an async-stream map was read by nobody and reported by nobody: the destination
  collection was filled by ordinary direct mapping instead. One declaration, one mapper, a walked graph on one
  overload and a shallow copy on the next four — in silence. All four endpoints now refuse it as the new
  **`DWARF092`** (a Warning, so the mapper is still emitted), naming the create map as the place to declare
  it. At the two element-wise endpoints that remedy is more than advice and was measured before it was
  printed: a span or stream map adopts a **declared** mapping method for its element pair, so the emitted loop
  is `d[__i] = Map(s[__i]);` and the walk runs per element through the create map that carries the directive.
  At update-into and projection nothing carries it, and the message says only that the create map honours it.
  Found by the surface matrix as `D11`; all eight of its cells close. (round 20, D11)
- **A projection did not read `[Flatten]`, and neither element-wise endpoint read `[Flatten]` or
  `[MapValue]`.** Projection is emitted by a **separate translator** from the runtime map, and it had no copy
  of the flatten walk at all: `[Flatten("Child")]` pulled `Child`'s members up through `.Map` and left the
  same destination members at their defaults through `.Project`, saying nothing. It now resolves through
  `ResolveFlattenInfos`, the one walk both resolvers call — so a root that names nothing, or names a scalar,
  is refused by the same `DWARF016` at both endpoints, and a pulled-up leaf becomes `__s.Child.X`, the
  navigation access a query provider translates. (A nullable root still warns `DWARF044` on the runtime path
  and deliberately does not in a projection, where the provider yields null rather than dereferencing —
  matching the dotted `[MapProperty]` source path, which already made that call.) At the **span** and
  **async-stream** endpoints both directives are now refused as `DWARF090` with a remedy that was measured
  working before it was prescribed: `[MapValue<Dst>("Name", "api-v2")]` for the constant, and
  `[MapProperty<Src, Dst>("Child.<leaf>", "<leaf>")]` for the flatten, which has no pair-scoped twin of its
  own. **Projection reads `[MapValue]` too**, in the position the create map reads it and through the create
  map's own validation rather than a copy of it: a constant becomes a literal in the `SELECT`, and only the
  `Use =` value provider is refused there, as `DWARF028`, because a query provider cannot call back into
  managed code from inside an expression tree — the treatment `[MapProperty(Use =)]` already gets at that
  endpoint. Found by the surface matrix as `D9` and `D10`; both close outright. (round 20, D9 and D10)
- **`[MapNullSkip]` had three readers, and each one saw a different part of the option.** The two documented
  scopes of `SkipNullSourceMembers` — `[MapNullSkip]` on a mapping method and `[MapNullSkip<S, T>]` on the
  mapper class — reached almost exactly complementary halves of the surface. The method endpoints read the
  method form and never consulted the pair-scoped one, so `[MapNullSkip<Src, Dst>]` was **discarded at the
  create-map and update-into overloads of the pair it named** — the endpoint patch-merge exists for. The
  `[GenerateMap]` and auto-synthesized pairs consulted the pair-scoped form and had no method to read. And the
  projection resolver was handed the bare class value, so it saw neither. Between them a caller reached every
  endpoint; with either one alone, about half, silently. All the mapping-shape readers are now one
  (`ResolveNullSkip`), resolved **most-specific-wins**: the method form, then the pair-scoped form, then the
  mapper's `SkipNullSourceMembers`, then the assembly default — which is what both attributes' documentation
  already implied, since carving one method out of a class only works if the carve-out outranks the class.
  Where the method form structurally *cannot* reach — a span or async-stream map takes its configuration only
  from directives that name the pair — it is now refused as `DWARF090` with the pair-scoped remedy, instead of
  being dropped. **Projection is the fourth and last reader**, and it is now the same one: an untranslatable
  null-skip is refused there as `DWARF028`, per affected member — the refusal `[DwarfMapper(SkipNullSourceMembers
  = true)]` and its assembly-level twin have always got at that endpoint, and which the two narrower scopes of
  the same option simply never reached. All four scopes of one option now get the same answer everywhere.
  Found by the surface matrix as `D6` and `D7`, one finding inverted; all nine of their cells close — four
  `Honoured`, two `Refused` element-wise, three `Refused` at projection — and both entries are deleted.
  (round 20, D6 and D7)
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
  endpoints. A **Warning**, for the reason `DWARF089` and `DWARF090` are; the hook is dropped and the mapper
  is emitted.
  Escalate with `dotnet_diagnostic.DWARF091.severity = error`. (round 20, D16)
- **`DWARF089` — a directive on a co-located `[GenerateMap]` host member that cannot be applied.** The exact
  inverse of `DWARF088`: that one refuses the *member* form where there is no member, this one refuses the
  *method* form on a member — `[MapProperty(source, target)]` and `[MapIgnore(destination)]` both name a
  destination the placement has already named. It also covers two shapes the placement alone cannot decide:
  stacked directives whose count does not match the `[GenerateMap]` pairs the host is the destination of
  (they bind positionally, one per pair, as they do to `[MapTo]` targets), and a member directive on a host
  that is the destination of no pair it declares. A **Warning** — unlike `DWARF088`, which is an Error — for
  a reason of its own: a blocking
  error would suppress the host's emission, so the generated `<Host>Mapper`, its convenience extension and
  its DI registration would all vanish and every call site would meet `CS1061` instead of the refusal. The
  offending directive is dropped and the rest of the host's mapping is emitted; escalate with
  `dotnet_diagnostic.DWARF089.severity = error` where the stricter reading is wanted. (round 20, D20)
- **`DWARF088` — the member-placement overload of `[MapProperty]` / `[MapIgnore]` written on a mapper.** An
  **Error**, matching its registry mirror `DWARFR04`, which refuses the identical misuse at the other front
  door. The remedy is in the message: supply the argument the method form takes
  (`[MapProperty("Name", "FullName")]`, `[MapIgnore("Extra")]`). Refusal is right whichever way the directive
  is read — honouring `[MapProperty("Name")]` at a method would bind `Name` to itself, a no-op nobody writes
  on purpose, and discarding it evaporates a binding the caller stated explicitly. **It was drafted as a
  Warning and escalated before release**, and the reason for the escalation is that the original reason was
  never a product reason: a blocking DwarfMapper error suppresses the whole class's emission, so the refusal
  arrives alongside `CS8795` from the unimplemented partial methods — a cost paid by *every* blocking id on
  this class, `DWARF011` and `DWARF087` included, and both of those are Errors. What this id refuses is silent
  data loss: `[MapProperty("Name", Use = nameof(F))]` is `ctor(1)` plus property initializers, so the
  converter, the `When` predicate, the null substitute and the format string are discarded together with the
  binding and the caller gets auto-matching. Downgrade with
  `dotnet_diagnostic.DWARF088.severity = warning` if you need the build to proceed while you fix the call
  sites. See the entry under Fixed for what it was replacing. (round 20, A2 + A12)
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
- **`DWARFR01`–`DWARFR10` are release-tracked** (and `DWARFR11`, added later in this same Unreleased
  section, with them). The registry (`[MapTo]`) diagnostics suppressed
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
- **The initial diagnostic surface — the 76 ids that predate this file, announced retroactively.** DwarfMapper
  has never shipped, so nothing was ever mis-announced: this Unreleased section becomes the first release's
  notes, and with this block those notes carry every diagnostic the first release ships. They are announced as
  one block, in id order per severity, rather than attributed to a per-version history that never existed.
  Severities are the descriptors' defaults; each id's full trigger-and-remedy documentation is its section in
  `docs/diagnostics.md` (the IDE "learn more" link).

  **Errors** — the mapping is incomplete or the configuration is invalid; the build fails:
  - `DWARF002` — a class annotated `[DwarfMapper]` is not `partial`, so the generator cannot add the method bodies.
  - `DWARF003` — a mapping method's signature matches no supported shape (create, update-into, projection, span, async-stream).
  - `DWARF005` — a paired source and destination member have no implicit conversion and no converter applies.
  - `DWARF007` — a mapping targets a read-only destination member, so the value would be lost.
  - `DWARF008` — `[MapProperty]`'s destination member does not exist or is not writable.
  - `DWARF009` — `[MapProperty]`'s source member does not exist or is not readable.
  - `DWARF010` — under `CaseInsensitive = true`, a destination member matches more than one source member.
  - `DWARF011` — a destination member has more than one `[MapProperty]`.
  - `DWARF012` — a member is both `[MapIgnore]`d and `[MapProperty]`-mapped.
  - `DWARF013` — more than one mapping method can convert the member's types.
  - `DWARF015` — by-name enum mapping found a source enum member with no same-named destination member.
  - `DWARF016` — `[Flatten]`'s root does not exist, is not readable, or exposes no readable sub-members.
  - `DWARF017` — a destination member is flattened from more than one source root.
  - `DWARF018` — a `[BeforeMap]`/`[AfterMap]` hook method has the wrong shape.
  - `DWARF020` — a `[RoundTrip]` method has no inverse mapping method.
  - `DWARF021` — a `[RoundTrip]` method has more than one candidate inverse.
  - `DWARF022` — `[Reinterpret]` must map an array to an array of an unmanaged (blittable) element type.
  - `DWARF023` — an `[AfterMap]` on a value-type target takes it by value, so changes are lost; take it by `ref`.
  - `DWARF024` — a required constructor parameter has no mappable source member.
  - `DWARF025` — several constructors tie for the most parameters, or more than one carries `[DwarfMapperConstructor]`.
  - `DWARF026` — the destination type has no accessible, non-obsolete instance constructor to map into.
  - `DWARF027` — the collection/dictionary target type is not supported.
  - `DWARF028` — a projection member has no database-query translation (converter, value provider, non-translatable collection kind, reference handling, …).
  - `DWARF030` — a member set through a constructor parameter or `init`-only property takes part in a reference cycle under `ReferenceHandling = Preserve`, so the cycle cannot be reconstructed.
  - `DWARF031` — the generator reached its limit of 512 synthesized nested mappers.
  - `DWARF032` — a `[MapProperty(Use=)]` converter is opaque, so `ReferenceHandling = Preserve` cannot track the converted value's identity.
  - `DWARF033` — auto-nesting an abstract/interface source would silently drop members that exist only on derived runtime types.
  - `DWARF034` — a `[FlattenGraph]` is misconfigured; the message states the specific problem.
  - `DWARF035` — a `[MapDerivedType]` arm is invalid; the message states the specific problem.
  - `DWARF036` — two `[MapDerivedType]` arms overlap, so dispatch is ambiguous.
  - `DWARF040` — a constant `[MapValue]` literal is not assignable to the destination type.
  - `DWARF041` — a `[MapValue(Use=)]` provider must be parameterless and return a value assignable to the destination.
  - `DWARF042` — `[MapValue]` conflicts with `[MapProperty]`/`[MapIgnore]` on the same target, names an unknown member, or targets a constructor parameter.
  - `DWARF046` — a `[MapProperty]` unflatten target conflicts with a direct mapping of the same root.
  - `DWARF048` — under `NameConvention.Flexible`, two source members normalize to the same destination member.
  - `DWARF049` — a `[MapProperty(NullSubstitute=)]` value is not assignable to the destination member.
  - `DWARF050` — a `[MapProperty(When=)]` predicate must be a `bool` method taking the source.
  - `DWARF052` — a `[ReverseMap]` has no inverse mapping method (types swapped) to attach to.
  - `DWARF053` — a mapping method declares type parameters; a generator cannot emit a body for an unbound type.
  - `DWARF054` — the class carrying the mapping is generic, which is not supported.
  - `DWARF057` — the generated `<Host>Mapper` type name collides with an existing type.
  - `DWARF059` — a `[MapConstructor]` factory method does not exist or its signature is incompatible.
  - `DWARF060` — two maps from the same source type to different targets would overload `Map` by return type alone (`CS0111`); give one a distinct method name.
  - `DWARF061` — a required ambient map consumed through `IDwarfMapper` is provided by no assembly in the graph (reported at the `[DwarfMapperValidationRoot]`, pulling a runtime `DwarfMapMissingException` forward to build time).
  - `DWARF067` — `[GenerateWrapperMap]`'s wrapper is not a generic type with exactly one type parameter and one payload member of that type.
  - `DWARF068` — a `MapConfig<S,T>` fluent call uses a selector that is not a member-access chain, or an argument that is not a method group.
  - `DWARF069` — the same destination member is configured more than once, by attribute and/or `MapConfig<S,T>` call.
  - `DWARF072` — under `[DwarfMapper(AutoMatchMembers = false)]` (the trust-boundary / anti-over-posting guard), a destination member has a same-named source match that the explicit-only mode refuses to auto-wire silently.
  - `DWARF073` — `[MapProperty(StringFormat=)]` requires a `string` destination and an `IFormattable` source, and cannot combine with `Use=`.
  - `DWARF074` — `[MapCollectionKey]` is outside the v1 upsert's scope (update-into method, same-element-type `List<T>` on both sides, readable key member).
  - `DWARF077` — explicit-only mapping cannot be enforced element-wise, so a span/async-stream method on an `AutoMatchMembers = false` mapper is refused rather than left half-guarded.
  - `DWARF079` — `[MapIgnore]` cannot ignore a `required` destination member; omitting it from the generated initializer would be `CS9035` in generated code.
  - `DWARF082` — a `[ProvidesMap]` method cannot be registered into the ambient registry (it must be public, take one parameter, return a value, and both types must be publicly nameable).
  - `DWARF084` — `[RestatesBase]` cannot identify the base pair to compare against.

  **Warnings** — the configuration compiles but something was skipped or will not behave as expected:
  - `DWARF037` — `OnCycle` is set together with `ReferenceHandling = Preserve`, where it has no effect.
  - `DWARF044` — a dotted `[MapProperty]` source path traverses a nullable member and can throw `NullReferenceException` at runtime.
  - `DWARF051` — a forward `[MapProperty]` cannot be auto-inverted by `[ReverseMap]` (it uses `Use=`, a dotted path, `NullSubstitute`, or `When`); declare the reverse rename explicitly.
  - `DWARF056` — a class-level pair-scoped attribute matches no mapped pair, so it silently does nothing (usually a typo'd type argument or a missing `[GenerateMap]`).
  - `DWARF070` — a nullable reference source member is assigned to a non-nullable target member; reported against your DTO instead of the compiler's `CS8601` inside generated code, which is suppressed.
  - `DWARF075` — a `[FlattenGraph]` data-bearing complex leaf could not be flattened under `ReferenceHandling = Preserve` and the destination member is left at its default.
  - `DWARF076` — a declared create-map maps a type to itself, a shallow copy that is usually a mistyped type argument.
  - `DWARF078` — no code was generated for a mapper that had at least one DwarfMapper error; the signpost for the wall of `CS8795` that follows.
  - `DWARF085` — a pair declared with `[RestatesBase]` no longer maps a member the way its base pair does.

  **Info** — visible in the IDE, never build-breaking; surfaces a footgun without forcing a change:
  - `DWARF038` — an implicit type conversion is applied (lossy sub-cases report as Warning; `[DwarfMapper(ImplicitConversions = false)]` turns them into errors).
  - `DWARF039` — under `RequiredMapping = Both`, a source member is read by no destination member.
  - `DWARF047` — an additional mapping parameter matched no destination member by name.
  - `DWARF055` — a single mapper resolves a very large number of members, which can add IDE/compile latency.
  - `DWARF058` — two mappers would produce the same `source.ToTarget()` convenience extension, so it was generated for neither; the instance methods still work.
  - `DWARF062` — a mapper with constructor dependencies cannot be constructed by the ambient registry's module initializer and is left out of ambient `IDwarfMapper` resolution.
  - `DWARF064` — a `[MapValue]` shadows a same-named readable source member, so the real source value is never read.
  - `DWARF065` — an update-into maps a nested object member by replacing the destination's instance, not by deep-merging into it.
  - `DWARF066` — a `[MapProperty(When=)]` guards a non-nullable member, which keeps its default when the predicate is false.
  - `DWARF071` — the source member's declared type has derived types in the compilation whose extra members would be silently dropped at run time.
  - `DWARF080` — a `[MapConstructor]` factory owns construction, so a mapped source value for an `init`-only or `required` member is discarded.
  - `DWARF081` — two mappers auto-synthesize the same nested pair two different ways, because a synthesized helper inherits the policy of the mapper that reached it.
  - `DWARF083` — a declared enum↔string mapping writes `[EnumMember]`/`[Description]` values rather than the member identifiers.

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

Every live diagnostic id is announced in this file. The seventy-six ids that predated it are the "initial
diagnostic surface" block under `### Added` (task `D-e` in Issues/round20/TASKS.md, resolved 2026-08-21),
and the `Scan9` gate in tests/DwarfMapper.Generator.Tests/SelfValidation/AssemblyScanTests.cs fails the
build for any id with no entry — with no exemption set, so this holds for future ids too.
-->
