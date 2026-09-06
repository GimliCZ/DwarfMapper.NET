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

### Changed

- **Two sources that used to build now report an error, both because the array/list block copy stopped
  overriding the resolver (see *Fixed*).** Neither is a silent behaviour change — each replaces a silent WRONG
  mapping with a diagnostic that names the fix:
  - **Two declared methods converting the same element pair** (e.g. two `DstV Convert(SrcV)` overloads on the
    mapper) beside a `SrcV[]`/`List<SrcV>` member used to take the block copy, quietly resolving the ambiguity
    by calling neither. The element pair is now resolved, so the existing **`DWARF013`** ("more than one mapping
    method converts these types") reports it. **Remedy:** disambiguate with
    `[MapProperty(Use = nameof(TheOneYouMeant))]` on the member, which resolves ahead of auto-adoption; or
    delete/rename the converter you did not intend to offer.
  - **`[AutoNest(false)]` plus a pair-scoped directive on a blittable element pair** used to blit and emit a
    `DWARF056` warning. The directive now keeps the element loop, and with auto-nesting disabled there is no
    synthesized mapper for the pair to route through, so it fails with **`DWARF005`**. **Remedy:** drop the
    pair-scoped directive, or re-enable auto-nesting for that method. On an **array** member you may instead
    apply `[Reinterpret]`, which names that member explicitly and keeps the block copy; note it is
    array-to-array only, so on a `List<T>` or `ImmutableArray<T>` member it is refused with **`DWARF022`** and
    one of the first two remedies is the answer.

  A third consequence goes the other way and needs no action: a pair-scoped `[MapIgnore<T>]`/`[MapProperty<S,T>]`/
  `[MapValue<T>]` on a blittable element pair **stops reporting `DWARF056`**. It used to say "matched no pair"
  because the block copy meant the pair never got a helper to apply it to; the directive is now genuinely
  applied, so the diagnostic correctly goes quiet.

- **The public API surface is now frozen.** All 315 entries moved from `PublicAPI.Unshipped.txt` to
  `PublicAPI.Shipped.txt` — 277 for `DwarfMapper`, 38 for `DwarfMapper.Testing`. From here a rename or a
  removal is a declared break the analyzer refuses, rather than a free edit. Done ahead of the first tag
  deliberately: the architecture rounds that follow move a great deal of code, and that is exactly when an
  accidental surface change is most likely to slip through unnoticed.
- **`DwarfRefContext` is marked `[EditorBrowsable(Never)]`.** It is public because generated code in other
  assemblies must construct it, not because consumers should call it. No signature changed.
- **Generated code passes `DwarfRefContext` flags by name.** Emitted call sites now read
  `new DwarfRefContext(64, preserve: true)` rather than `(64, true)`. Cosmetic in the output, but it removes a
  transposition hazard from the shipped API's own call sites.
- **`DwarfMapper.Testing.ObjectFactory` is removed**, merged into `ObjectFactoryV2`. The survivor gained
  abstract/interface substitution, `[Flags]` combined values and deterministic constructor selection from the
  factory it replaced, so no fixture capability was lost.

### Added

- **`DWARF103` (Info) — a mapped collection builds one class element per item, and that element type could be
  a `readonly record struct`.** Reported at the MAPPING SITE, never on the type: the allocation is paid once
  per element, and a type-level rule would fire on every DTO in a solution. The message names the pair, what
  the rewrite buys ("the collection becomes one allocation instead of one per element") and the size of the
  would-be struct. **Remedy:** declare the element type as a `readonly record struct` — or take the code fix
  below, which does it for you.

  **Read the hazards before taking it.** Unlike the other performance hints this one asks for a change of
  *meaning*: a struct has no reference identity, cannot be `null`, and cannot be mutated through an indexer
  (`list[i].X = v` is CS1612). Every one of those surfaces as a **compile error** rather than a silent
  behaviour change, which is the whole reason the suggestion is safe to make — and why it is informational.
  Ignoring it is a legitimate answer. Measured on a four-class DTO tree decomposed into nested structs
  (`Issues/round29/RESEARCH-hardware-mode.md`, section 9): 0.30× the time at 1,000 elements (3.3× faster)
  and 0.09× at 100,000 (11× faster). A single flat DTO is 2.2× faster at 1,000 and 9–12× faster at 100,000
  — smaller at the low end, comparable at scale — with 43 % less memory.

  **The block copy is only mentioned where it is possible and earned.** The clause naming it requires the
  SOURCE element to be transfer-model shaped in its own right — only the target is classified otherwise, so
  without this the message advised that an ORM-tracked entity become a struct — and requires neither type to
  hold a reference, since `CanReinterpret` needs both sides unmanaged and a DTO with a `string` can never
  blit however it is declared. Even then it says "could", naming the layout and field-name identity the
  proof still requires, and it holds the source to the target's own standards: a source declared in a `.g.cs`
  is never named (you cannot rewrite it), and a `public` unsealed source is carried into the same
  assembly-scope caveat the target gets, in one sentence covering both. An unshaped source is still reported:
  collapsing N object headers into one array is the measured win and does not depend on the source.

  **The size is honest about what it is.** It is the WOULD-BE struct's size, counting any transfer model the
  type holds as a struct too (that is the rewrite being advised), and it is printed as a bound — "at most N
  bytes" — whenever a member is a reference, since a reference is 8 bytes on x64 and 4 on x86 and the number
  can only be an over-estimate. Over **64** bytes — and only there — the message adds the `in` advice: between
  32 and 64 it prints the size and stops, because a struct that size still beat its class by value in the
  measurement (26.4 ns against 29.9 ns), so an indirection is not what the numbers ask for. For a `public`
  unsealed type
  it states the SCOPE of the derived-type sweep rather than its conclusion: it covered this assembly, and a
  referencing project can still derive from a type this one never sees.

  **It is deliberately hard to trigger.** The shape rules are `TransferModelShape`'s and refuse far more than
  they accept — anything derived from, abstract, generic, `static`, disposable, event-declaring,
  interface-implementing (bar `IEquatable<T>` of itself), ORM-tracked, validated in its constructor, or
  declared in a referenced assembly. On top of those the report itself refuses four more: the elements must be
  built by code this generator emits (a hand-written converter or a conversion operator owns its own
  construction; an identity copy allocates nothing per element), the pair must carry no directive or hook a
  value-type target would silently drop, the mapper must not be in `Preserve` or `SetNull` mode where
  reference identity is the point, and a DTO another source generator emitted is never named — you cannot
  rewrite a declaration inside a `.g.cs`, nor suppress a diagnostic raised in one — and neither is a transfer
  model the element HOLDS, because the size in the message counts that model inline and the fix cannot rewrite
  it either. The shape rules also refuse a class declaring `Equals(object)`, `operator ==` or `operator !=`: a
  record struct synthesises those and does not stand aside for a hand-written one, so following the advice
  would have been CS0111. (`Equals(T)`, `GetHashCode()` and `ToString()` it does stand aside for, and those are
  still fine.) Reported once per element pair per mapper. Over the 938-case combinatorial corpus it fires on 5,
  every one a `List<record>` of a positional data record — which is the shape it exists for.

- **Code fix for `DWARF103`: *Convert 'X' (and N nested transfer models) to readonly record struct; call
  sites are not updated and may stop compiling*.** The second half of that title is not decoration: the
  rewrite deliberately leaves your usages alone, the consequence lands as CS1612/CS0037 in OTHER files, and
  Roslyn's preview shows one document — so the title is the only place you can be told before you click. One
  action, one solution change: it rewrites the element type's declaration — every `partial` half of it, in
  whichever file it lives — and the declarations of every transfer model that type INLINES, transitively.
  **The transitivity is not a convenience.** The size `DWARF103` prints counts a nested transfer model at the
  nested struct's own size rather than at pointer width, so converting the element alone would leave you a
  type smaller than the number you were shown; if any part of the set cannot be rewritten, the fix changes
  nothing at all. The rewrite makes the declaration compile on its own — `set` becomes `init` (CS8341), an
  instance field gains `readonly` (CS8340), a type that keeps member initialisers gains a constructor
  (CS8983), and a nested member the nullable context left *oblivious* is emitted as `Inner?`, which preserves
  both the null it could legally hold and the size the diagnostic measured for it. It deliberately leaves
  your USAGES alone: a `null` check, an aliasing assignment and `list[i].X = v` are meant to become compile
  errors, loud over silent, which is the bargain the hazards above describe. Its targets travel in the
  diagnostic's property bag as `DocumentationCommentId`s rather than being read out of the message text, so
  rewording the message cannot silently stop the lightbulb appearing.

- **`DWARF101` (Info) — a transfer-model struct spends a quarter or more of its bytes on padding, and the
  message names the field order that packs it.** Reported for the element types of a mapped collection, where
  the waste is paid once per item: `{bool; long; byte; double; short}` is 40 bytes, 20 of them padding, and
  the same five fields declared `long, double, short, bool, byte` are 24. Measured on that exact shape
  (`Issues/round29/RESEARCH-hardware-mode.md`, row *D. layout hygiene*), the packed pair blits in
  0.57×–0.62× the time and allocates 0.60× the memory. **Remedy:** declare the fields in the order the
  message prints — on BOTH sides of the pair, since packing only one of a layout-identical pair costs it the
  block copy. Ignoring it is also a legitimate answer, which is why it is informational rather than a
  warning.

  **It is deliberately hard to trigger.** Both thresholds must hold — at least a quarter of the size AND at
  least 8 bytes — so the ubiquitous `{byte; long}` (7 bytes wasted of 16) stays silent, as does anything
  whose layout the generator cannot compute: metadata structs, `LayoutKind.Auto`/`Explicit`, an explicit
  `Pack` or `Size`, `[InlineArray]`, fixed buffers, fields split across `partial` declarations, structs another
  source generator emitted (their field order is not yours to change, and a `.g.cs` is not yours to suppress
  in either), and any
  struct containing a platform-sized `nint`/`nuint`/`IntPtr`/`UIntPtr`, whose width belongs to the machine
  the consumer runs on rather than the one that built the mapper. An informational diagnostic is only worth
  having while it is rare.

- **`DWARF106` (Info) — `[Reinterpret]` takes the block copy instead of a declared conversion *or a pair-scoped
  directive*.** Reports the one conflict the new blit rule (below) deliberately resolves in favour of the
  attribute: the member carries `[Reinterpret]`, and its element pair also has something you wrote that a block
  copy will not run. **One id, two message shapes.** The first is a conversion — a declared method on the
  mapper, or a user-defined conversion operator. The second is a pair-scoped `[MapIgnore<T>]` /
  `[MapProperty<S,T>]` / `[MapValue<T>]` / `[MapConstructor<S,T>]`, or a `[BeforeMap]`/`[AfterMap]` hook
  matching the element pair: those are applied by the ONE synthesized helper the pair gets, and the block copy
  never calls that helper, so the directive silently does nothing for those elements while continuing to work
  for every other member that maps the pair. `[Reinterpret]` still wins in both cases, because it names one
  member explicitly while an auto-adopted converter or a pair-scoped directive is ambient; but it no longer
  wins silently. The message names the member, and either the conversion that is not being called or the
  directive's spelling and the element pair it was declared for; both say that removing `[Reinterpret]` from
  that member uses it. When both apply the conversion is named, being the more specific fact. The
  customization question is the gate's own (`ElementPairHasCustomization`, now deriving from the rule that
  yields the directive's name), asked through the same non-mutating lookups, so the diagnostic and the gate can
  never disagree. Informational on purpose: an error would be a false positive on a mapper that uses the same
  helper for another member, and a warning becomes a build failure under `TreatWarningsAsErrors`. A
  `[Reinterpret]` member with neither in sight says nothing — this reports the conflict, not the attribute.
  (the directive shape: round 29, T0.2d)

- **A corpus that tests the diagnostic *pathway*, not just the diagnostic.** Every DWARF id is reported by an
  `IIncrementalGenerator` through `SourceProductionContext.ReportDiagnostic`, and Roslyn does not route
  generator diagnostics through the analyzer pipeline — so `#pragma warning disable` and an editorconfig
  `dotnet_diagnostic.X.severity` have **no effect** on them, and only compilation-level `NoWarn` works. All
  three were measured against a real consumer solution before anything was written. `SuppressionPathwayTests`
  now pins that: the fixture reports DWARF076, `NoWarn` silences it, a correctly-placed `#pragma` does not, and
  suppressing one id leaves the others reporting. It carries its own non-vacuity control, and it reads the
  generator's parked exception so a crash cannot make every assertion pass by reporting nothing.
  If Roslyn ever starts honouring pragmas here, the pragma test fails — and that failure is the signal to
  reword every message that currently routes around the limitation.

### Fixed

- **`DWARF070` no longer calls a mapping parameter a "member", and no longer offers it two remedies it cannot
  use.** The diagnostic now fires for an extra mapping parameter as well as a source member, and its message
  said `Source member '{0}'` for both while pointing at `[MapProperty(NullSubstitute = …)]` and
  `[DwarfMapper(SkipNullSourceMembers = true)]` — source-member instruments that cannot reach a parameter. It
  now names what it found (`Source member 'X'` or `Mapping parameter 'x'`) and labels each remedy with the
  shape it applies to; `docs/diagnostics.md` carries a fix table for each. Title changed from *"Nullable source
  member is assigned to a non-nullable target member"* to *"A nullable source is assigned to a non-nullable
  target member"*. The id, severity and trigger are unchanged, so no suppression is affected.

  Read together with the entry above: for a mapping parameter this warning is what a consumer gets **instead
  of** the unsuppressible `CS8611`/`CS8604`/`CS8601`/`CS0266` that shape used to put inside their generated
  file. It is the suppressible form of a signal they previously could not silence at all.

- **A map method whose SOURCE parameter is declared nullable emitted a signature that contradicted it
  (`CS8611`).** `partial Dst Map(Src? s)` was implemented as `Map(global::T.Src s)` — the same dropped `?` as
  the extra parameter above, on the ordinary source parameter, and on update-into, async-stream and projection
  methods alike. Unsuppressible, inside the generated file.

  The annotation now travels on its own, separate from the pair's canonical type name. That distinction is the
  fix rather than an implementation detail: the canonical name is also the operand of `typeof(…)` in the
  generated ambient registration and the target of the `new T` the mapper writes, and a nullable annotation is
  illegal in both — annotating it in place trades `CS8611` for `CS8639` and `CS8628`.

  Two shapes are improved but not yet clean, and both were already broken before this release: a map over
  `IAsyncEnumerable<S?>` now reports the real problem (its element conversion does not handle the null) instead
  of a signature mismatch, and a top-level collection conversion declared to RETURN a nullable element still
  reports `CS8819` on the return type. Both are tracked separately.

- **An extra mapping parameter named after a C# keyword produced a generated file that did not parse.** Both
  the emitted signature and the value expression were built from the raw symbol name, which Roslyn hands over
  without the `@` — the escape is syntax, not part of the name — so `partial Dst Map(Src s, int @class)` was
  implemented as `Map(… s, int class)` with `Class = class`. The consumer's `.g.cs` then stopped being C# at
  all: a parse cascade ending in `CS0111`/`CS0756` against their own partial declaration. The parameter
  identifier is now escaped wherever it is written as code, exactly as a destination member called `@class`
  already was.

- **An extra mapping parameter declared nullable broke the consumer's build twice over: `CS8611` on the
  generated signature, and `CS8604` / `CS8601` / `CS0266` in its body.** A parameter after the source on a map
  method (`partial Dst Map(Src s, Child? inner)`) is matched to a destination member by name. Two things were
  dropped on the way to emission, and the second was hidden behind the first:
  - The implementing half of your partial re-declared that parameter **without its `?`**, so the two halves of
    the partial disagreed — **`CS8611`, inside the generated file**, where no `#pragma`, `NoWarn` or
    editorconfig of yours reaches it and `TreatWarningsAsErrors` makes it a build failure with no remedy on
    your side.
  - The phase that resolves the parameter to a member **discarded the null-handling decision** and handed the
    emitter a finished expression, which bypasses null handling entirely. With the annotation restored, that
    bare emission is `ToDto(inner)` → **`CS8604`** for a nullable parameter into a converter that refuses null,
    `Inner = inner` → **`CS8601`** for a raw assign into a non-nullable member, and — independent of any
    nullable context, so it has been there since extra parameters shipped — `Count = count` for an `int?`
    parameter into an `int` member, which is **`CS0266`**, a hard compile **error** in a file you cannot edit.

  An extra parameter is now answered exactly as the equivalent source member is: the lift
  (`inner is null ? null : ToDto(inner)`) when the destination can hold the null, the forgiven argument
  (`ToDto(inner!)`) with **`DWARF070`** against your own DTO when it cannot, and the mapper's `NullStrategy`
  for a nullable value parameter. It is the same decision the rest of the engine reads, not a second copy of
  it — the parameter is simply where the value is read from.

- **A nested member mapped through one of your OWN map methods ignored the mapper's null policy: `CS8604` in
  the consumer's generated file, or an `ArgumentNullException` at run time.** When a nested edge resolves to a
  *synthesized* helper the engine emits `if (s is null) return null!;` — null in, null out, and it has done so
  since that helper was written. When the SAME edge resolves to a map method you declared yourself, the call
  site got neither that guard nor anything in its place: the null-forgiving `!` is gated on a **non-nullable
  destination** (the `DWARF070` path), and the null-preserving lift only ever fired for a `Nullable<U>` **value**
  destination. A nullable reference into a nullable reference fell between the two and was emitted bare —
  **`CS8604` inside a `.g.cs`**, where no consumer `#pragma`, `NoWarn` or editorconfig reaches it and
  `TreatWarningsAsErrors` turns it into a build failure with no remedy on their side. With both ends declared
  non-nullable, it compiled and threw instead, out of the callee's own `ArgumentNullException.ThrowIfNull`.
  Whether a null survived a nested edge therefore depended on which of the two converters the resolver happened
  to pick, which is not a fact any user can predict from the types.

  The decision is now made once, where the user-declared converter is chosen, so a member, a collection element,
  a dictionary value and a flatten leaf all get the same answer:
  - **destination can hold null** → `x is null ? null : Conv(x)`. No `CS8604`, and the null is preserved.
  - **destination cannot, and the source is not nullable-annotated either** → `x is null ? null! : Conv(x)`. Both
    annotations claim the value cannot be null; the shape that makes one of them false is the ubiquitous
    `Result<T>`/`Outcome<T>` whose `Fail` parks `default!` in the payload, and **mapping a failed result must not
    throw**. The `!` is what keeps `CS8601` out of a file the consumer cannot edit.
  - **destination cannot, and the source IS nullable-annotated** → unchanged, deliberately. That is the shape
    **`DWARF070`** already names against your own DTO, and silently lifting it would swallow the one case the
    mapper is supposed to be loud about.

  A null-tolerant converter (one you declared with a nullable parameter) is untouched and keeps receiving the
  null it was written to accept.

  `[GenerateWrapperMap]` is where this bites hardest — it expands over exactly the pairs already declared as
  map methods, so an envelope's payload edge is almost always a user-declared converter — but it is not a
  wrapper-specific bug and the fix is not a wrapper-specific policy: a plain `Child? Inner` → `ChildDto? Inner`
  beside your own `ChildDto ToDto(Child)` reproduced it with no wrapper anywhere. Found by the representative
  corpus added in round 29 task 2.5. The golden manifest moved **0 existing cases** — no case in it routed a
  nested reference through a declared map, which was the hole rather than the reassurance; two feature cases
  now pin all three arms. (round 29, task 2.6)
- **Every array member with a nullable element carried an unsuppressible `CS8629` into the consumer's build.**
  The array→array fast path indexes with a single length-bounded counter so the JIT can elide both bounds
  checks, and it built that loop by substituting `src[__i]` textually into the shared per-element expression.
  For a LIFTED element — `Nullable<P>` (`P?[] → Q?[]`), or a nullable reference through a synthesized object
  helper — that expression reads its element TWICE, so the substitution produced
  `src[__i].HasValue ? conv(src[__i].Value) : null`: two independent indexer reads, between which C#'s nullable
  flow analysis does not carry a null-state. The compiler flagged the `.Value` with **`CS8629` ("Nullable value
  type may be null")** — inside a `.g.cs`, where neither `#pragma warning disable` nor an editorconfig
  `[*.g.cs]` section reaches it, so under `TreatWarningsAsErrors` (this repo's default, and a very common one)
  a consumer's only lever was a project-wide `NoWarn` that also hides real warnings in hand-written code. The
  element is now bound to a local once before the expression, exactly as the span map's inline element loop
  already did — both emitters ask the same `CollectionConverter.ElementExprReadsItemTwice`, so there is one
  rule in two places rather than two rules. The `for (int __i = 0; __i < src.Length; __i++)` shape the JIT
  proves in-bounds is untouched, and every single-read arm keeps the substitution byte for byte (the golden
  manifest moved 0 existing cases). It went unseen because no schema declared a nullable-VALUE-type element:
  `CombinatorialSchema` now crosses `nullable_struct_array` with every basic type, and
  `ConsumerReportedEmissionWarningsTests` pins the shape. (round 29, T0.2d)
- **The array/list block copy silently bypassed a user-declared element converter or a pair-scoped directive.**
  A `SrcV[] → DstV[]` (or `List<>`/`ImmutableArray<>`) member whose element types are layout-identical took the
  blit on the strength of the proof alone — decided several arms *before* resolution would have adopted the
  mapper's own `public static DstV Conv(SrcV s)`, its `implicit operator`, or applied a pair-scoped
  `[MapIgnore<T>]` / `[MapProperty<S,T>]` / `[MapValue<T>]` / `[BeforeMap]` / `[AfterMap]` matching the element
  pair. The converter was never called, the directive never applied, and nothing in the build said so. The new
  rule is the one the span map already follows: **a proof enables a fast path, it never changes semantics** — so
  the blit is now taken only when the element pair's resolution would land on a synthesized object map (which
  `CanReinterpret` proves the copy reproduces byte for byte), and the element loop is kept otherwise. Asked
  through the resolver's OWN predicates and with NON-mutating directive lookups, so a directive nothing applies
  is still reported by DWARF056. `[Reinterpret]` is unaffected: it names one member explicitly and still forces
  the copy — and, since T0.2d, says so with **`DWARF106`** whichever of the two it is overriding, so the
  override is no longer silent for pair-scoped directives either. Layout-identical pairs with neither a
  converter nor a directive blit exactly as before. Pinned by `BlitSoundnessTests` and
  `ElementConverterBeatsBlitRuntimeTests`. (round 29, T0.2c)
- **The blit near-miss (DWARF100) went quiet on a span map whose element names were reconciled by
  `[MapProperty<S,T>]`** — the one caller the hint is written for. It now follows the proof at both endpoints and
  is silenced only when a user conversion owns the element pair, where "you are one rename away from the block
  copy" would be false. The array arm's behaviour is unchanged. (round 29, T0.2c)
- **A span map over nullable elements (`ReadOnlySpan<P?>`, `ReadOnlySpan<C?>`) emitted code that did not
  compile in the CONSUMER's build.** The struct case handed the source span's `Nullable<P>` element bare to
  the synthesized helper's non-nullable `P` parameter — `CS1503` — and the class case's declared partial
  signature silently dropped the `?` on the span's nullable-annotated reference type argument, mismatching
  the user's own declaration — `CS8611` on the signature itself, before element resolution was even reached.
  Both are fixed the same way the array/list arm already lifts a nullable element
  (`CollectionConverter.ElementExpr`, now shared rather than duplicated): a span map over `Nullable<P>` or
  nullable-reference elements compiles warning-free under nullable enable, preserving null as null and
  mapping every non-null value through the element converter. `ReadOnlySpan<P?> → Span<Q>` (a target that
  cannot hold null) resolves exactly like the array arm resolves `P?[] → Q[]` — a runtime
  `InvalidOperationException`, never an unchecked `.Value` and never a silently unmapped null. That
  BEHAVIOUR mirrors the array arm; the MESSAGE does not — the array arm's own text stays its pre-existing
  `"Collection element was null"` (several of its target shapes have no loop counter to name), while the
  span map always has the index and the destination type in scope, so its own message names both:
  `"Element at index N was null, and the destination element type 'global::T.Q' does not admit null."` Pinned by
  `SpanMapNullableElementTests` and `SpanMapBlitRuntimeTests`. (round 29, T0.2b)
- **The blit proof could accept a struct pair whose real layouts were each other's reverse, and the emitted
  `MemoryMarshal.Cast` then handed every element back with its fields' bytes swapped.** The proof compares the
  two field lists positionally, and it re-sorted each list by (ordinal file path, position) first — added so
  that a struct whose fields are split across partial files would get one verdict whatever order the build fed
  the files in, on the reasoning that for a `Sequential` struct declaration order *is* the emitted layout. That
  reasoning is exactly right about the unsorted list, and exactly why sorting it was unsound: the compiler
  orders split fields by the order it received the files, which is MSBuild's — case-insensitive on Windows,
  where `Point.cs` precedes `Point.Extra.cs` — while the sort was ordinal, where it follows it. The sorted list
  lined up by name with a twin declared in the reverse order, the proof accepted, and `P{X=1,Y=2}` came out as
  `Q{X=2,Y=1}` — silently, because the runtime size check inside the copy was removed in round 26 on the
  strength of this proof. The sort is gone; a struct whose instance fields span more than one partial
  declaration (the shape behind CS0282) is now refused outright — automatic blit and `[Reinterpret]` alike,
  since an opt-in cannot assert bytes the compiler never defined — and `DWARF100` says so. Partial structs
  that keep every instance field in one declaration still blit. Held at the seam in both compile orders and
  both directions, and end-to-end by `BlitSoundnessTests`, which compiles the production shape, maps it and
  reads the values back.
- **The blit proof read the field list as the whole layout; three things change the bytes without changing
  it.** An explicit `[StructLayout(Size = …)]`, an `[InlineArray(n)]` and a fixed-size buffer's length
  (`fixed int Buf[4]` and `fixed int Buf[8]` are both `int*` to the type comparison) were all invisible to the
  proof, which accepted a 4-byte struct against its 32-byte `Size = 32` twin, a plain struct against its
  `[InlineArray(4)]`, and a 16-byte buffer against a 32-byte one. Cast the wide way the copy threw "destination
  is too short"; cast the narrow way it silently filled a fraction of the destination and left the rest
  zeroed. All three are now part of the comparison, each with its own `DWARF100` reason. `Pack` was already
  compared but never documented as a reason; the `dwarf100` entry now lists all of them.
- **The README and comparison still advertised a "JIT-folded runtime size guard" on the blit that round 26
  deleted.** The generation-time proof is the only guard — which is what makes the two fixes above fixes
  rather than hardening — and the prose now says so.
- **A diagnostic raised while resolving a nested pair had no location; now it is anchored at the declared
  method that reached the pair.** A nested pair — `Child` inside `S -> T`, synthesized because a member of the
  outer pair needed it — has no declaration of its own, and everything its resolution reported (`DWARF001`
  for a target-only member, `DWARF005` for an unconvertible one, `DWARF038`, `DWARF070`, the rest) went out
  with `Location.None`: the compiler printed `CSC : error DWARF001: …` against the project, with no file and
  no line, and Rider titled it "Generator 'DwarfGenerator' failed to generate sources". The message named the
  member but not the pair, and its remedy — annotate the method — named a method that does not exist for a
  synthesized pair. The anchor was meant to be the requesting method's location; the code that computed it
  was dead, and the null was later recorded as the contract. The registry now keeps the location each pair was
  first requested at and the pair's own resolution reports under it, so a pair any number of levels down
  inherits the declared method's line through the pairs between. Pinned by `NestedPairDiagnosticLocationTests`.
- **Three documents described a guarantee the code does not give, or withheld one it does.** The
  cross-assembly howto's "one provider per pair" limit read as if `DwarfMap.Validate()` would catch a duplicate
  provider; it checks presence, not uniqueness — `DWARF063` covers duplicates the validation root can see at
  compile time, and a plugin loaded only at runtime stays first-wins in load order, `IsAmbiguous` being the
  runtime question to ask. The howto now says so. In the other direction, `docs/SECURITY.md` and
  `docs/CORRECTNESS.md` still said the `aot-trim-gate` job only compiles the NativeAOT sample; it has
  asserted a native image and executed the sample's behavioural gate on both platforms since the conformance
  gates landed. Both now describe the job that runs.
- **The graph oracle that grades every topology, flatten-graph and cross-type fuzzer had never been seen to
  fail.** Every consumer of `GraphOracleComparer` asserts that the violation list is empty, and no test in
  either tier executed a single line that adds to it: the topology oracle's one violation, both
  `FlattenGraphDiff` violations, every `CrossTypeDiff` mismatch, `ValueDiff`'s null and count arms,
  `TopologyPreserved`, the walk through public fields and the reach through dictionary values. A change that
  silenced any of them — a `violations.Add` turned into a no-op, the shared-instance check turned into
  `true` — would have left the whole suite green, and no mutation leg covers `DwarfMapper.Testing` to say
  otherwise. `GraphOracleSensitivityTests` now pairs each violation with the positive control one edit away:
  a diamond whose shared node was mapped twice passes the value oracle and is named by the topology oracle,
  a cycle the target did not close, an EMPTY collection that is still "not degraded", a back-edge above the
  walker's depth cap, null against a value, a count that does not match, nulls sorted first, a float widened
  to a double, a value past `decimal`'s range, enums of two types, and the render helpers' prefixes and null
  guards. `RoundTrip` now proves that the seed in a `RoundTripException` rebuilds the instance that failed and
  reproduces its diffs, and `StructuralComparer` that a self-referencing graph stops at its depth cap.
- **The `DwarfMapper.Testing` coverage floor was a tenth above anything the tree ever measured, so every
  `-Coverage` and `-Nightly` run stopped at the gate.** The floor was pinned at 87.1 on 2026-08-26; the tree
  at that commit measures 704/809 = 87.02, and so did every later one, with the same covered-line set. The
  gate threw on `measured < floor` and nothing behind it — exhaustion, the AOT publish-and-execute, ILVerify,
  the benchmark smoke, the mutation legs — ran through the script from then on. The floor is now 96.4, the
  truncated measurement (780/809) after the sensitivity tests above, and the script's comment records both
  the never-met value and what the covered lines were.
- **The nightly `package-size` job had been red since round 24, and no local run could have shown it.** The
  `DwarfMapper` package ceiling was measured at 247 KB on 2026-08-22; rounds 24–27 grew the generator by
  ~60 KB of IL and the XML docs by ~15 KB, to 280 KB, and no commit re-measured the number as the gate's own
  comment demands. The gate did exactly its job — the same five entries, no new dependency, no new resource,
  only more of the same — but it runs only in the nightly CI job, `housekeeping.ps1` never packs, and the
  nightly went unread. The ceiling is re-measured — on Windows and in the gate's own ubuntu container — to
  282 KB (the emission fixes below added the last kilobyte and a half; the two platforms straddle a KB boundary
  by 208 bytes of CRLF, and the ceiling is the larger measurement so one tree is green wherever the gate runs),
  with the per-entry growth recorded beside it in `scripts/gate-checks.ps1`; `DwarfMapper.Testing` re-measures
  to the same 47 KB.
- **Three compiler warnings emitted from inside the generated file, reported by a consuming solution on
  1.1.0-rc5, each one a warning the consumer could not suppress** — neither `#pragma` nor an `.editorconfig`
  `[*.g.cs]` section reaches a diagnostic the compiler raises in a generated tree, so the only lever left was a
  project-wide `NoWarn` that also hides real warnings in hand-written code. All three were corpus holes: the
  warning-free oracle existed and had run since it was written, and no schema cell declared the shape.
  - A nullable-ELEMENT collection (`Child?[]`, `List<Child?>`) mapped through a synthesized object helper
    passed each element bare to the helper's non-nullable parameter: CS8604 per element, in a consumer where
    a null element means "empty slot" and is the normal case. The element is now null-forgiven exactly as the
    member path forgives a nullable source into the same helper (the helper null-guards: null in, null out).
  - A nullable source member bound to a non-nullable CONSTRUCTOR parameter was emitted bare, while the same
    member assigned through the object initializer was null-forgiven and reported as DWARF070 — one method,
    one source member, two treatments. Constructor arguments now set the same raw-assign flag, get the same
    `!`, and report the same DWARF070 against the DTO.
  - An enum with an `[Obsolete]` member: every enum↔string and enum↔enum switch names every member, so the
    generated file carried CS0618 the consumer could only silence by un-deprecating a domain value kept for
    backward compatibility. Each switch that has to name such a member is now wrapped in a scoped
    `#pragma warning disable/restore CS0612, CS0618` — only that switch, never the file; an `[Obsolete(…, error: true)]`
    member (CS0619, which no pragma lifts and nobody can reference) is skipped instead of named. CS0612 is
    the id the compiler uses for the bare, message-less `[Obsolete]`; a first cut of the guard named only
    CS0618 and the round-28 patch-coverage test for that form caught it.
  The same two nullability shapes were then swept through every other emitter that binds them, each fixed the
  same way: dictionary VALUES (`Dictionary<string, Child?>`), the projection endpoint (`.Project` now
  null-forgives a nullable source into a non-nullable member or constructor parameter inside the expression
  tree and reports DWARF070 once per source member per method — it used to emit the bare access), and the
  `[MapTo]` registry generator, whose collection helper was declared over `Child[]` for a `Child?[]` member
  (CS8620), returned `ChildDto[]` where the member is `ChildDto?[]` (CS8619), and handed elements bare to its
  object helper (CS8604). `ConsumerReportedEmissionWarningsTests` pins every shape and sibling, with the
  positive controls: the deprecated value is still mapped, a clean enum gets no pragma, the explicit
  `[MapProperty]` constructor binding and the runtime null-slot contract are unchanged.
- **DWARF064's remedy was inert; now it works, at both endpoints, under every name convention.** The
  message tells the reader to write `[MapIgnoreSource("X")]` "if the shadow is intentional", but
  `TryValidateMapValueTarget` consulted only whether a source member existed — the ignore-*source* set never
  reached `ResolveMembers` or `ResolveProjectionMembers` at all, so the attribute changed nothing and anyone
  who followed the message watched the diagnostic survive. The set is now threaded to the shadow check at the
  create map **and** the projection (class-level plus the method's own; synthesized pairs get class-level,
  having no declared method), and disowning is tested by the source member's **real** name — the spelling
  source coverage already reads `[MapIgnoreSource]` under. That last part matters under `CaseInsensitive` and
  `NameConvention.Flexible`, where the shadowed member is not spelled like the target: the message used to put
  the *target's* name in both slots, so the attribute it dictated would have silenced this diagnostic and
  disowned nothing. It now names the shadowed member as the source spells it. Proved by three case **pairs**
  — exact-name create map, `CaseInsensitive` create map, projection — each firing case pinning the real-name
  wording and each `_Remedy` sibling applying exactly the attribute the message names and asserting
  `EXPECT: none`. Every remedy case fails without its half of the fix, which is what makes them evidence
  rather than decoration.
- **The five diagnostics reported with no location now identify themselves.** DWARF058, DWARF061, DWARF062,
  DWARF063 and DWARF081 are reported at the assembly level with `Location.None` — no file, no line — and IDEs
  title generator diagnostics generically: Rider shows every one of them as
  *"Generator 'DwarfGenerator' failed to generate sources"*, which reads as a crash and hides both the id and
  the severity. A consumer saw 128 of those and reasonably concluded the generator was dying. Their message
  text is the only identifying context they have, so it now leads with the id.
- **DWARF063 counted occurrences, not assemblies.** Its text says "provided by more than one **assembly**",
  but `AmbiguousProviders` incremented a per-pair counter for every entry, so a pair listed twice by a single
  assembly — or one component appearing twice in a reference closure — tripped it. The message and the
  implementation disagreed and the message was right: it now counts **distinct** providing assemblies. It also
  **names them** (`… is provided by more than one assembly (Demo.Api, Demo.Plugins)`), which is the
  information the old wording asked you to act on but never supplied. `AmbientValidator.ReadReferenced` keeps
  the providing assembly per pair rather than discarding it.
- **The generator could crash and take every generated map in the consumer's project with it.**
  `LocationInfo.From` called `Location.GetLineSpan()` unguarded. A `Location` is a span into a particular
  `SourceText`, and inside an IDE the generator is handed symbols from a compilation snapshot the user is
  still editing — `ISymbol.Locations` can name a span in another file that has since shrunk, at which point
  `GetLineSpan()` throws `ArgumentOutOfRangeException('character')` inside Roslyn. Roslyn does not let that
  escape: it parks the exception on `GeneratorRunResult.Exception` and downgrades it to a **CS8785 warning**,
  so the build still reports zero errors while `DwarfGenerator` "will not contribute to the output" — one
  stale span silently erased the entire mapping layer. Reported from a consumer as
  `MapperExtractor.ExtractCore`. `From` now bounds-checks the span and returns `null` rather than throwing;
  every consumer already spells the degraded path `Location?.ToLocation() ?? Location.None`, so a diagnostic
  loses its position and nothing else.
- **Nothing in the test suite could tell a crashing generator from a refusing one.** Both produce no sources
  and no errors, and every assertion in the suite reads sources or diagnostics — so the battery would have
  gone green with the generator dying on every input. `GeneratorRunner.Run` now asserts
  `GeneratorRunResult.Exception is null`, which retro-fits crash detection onto all 7150 generator tests, and
  a new adversary suite attacks the engine with unparseable source, unresolved/error symbols, self-referential
  and mutually recursive types, open generics, 60-deep nesting, astral-plane and verbatim-keyword identifiers,
  2000-character names, duplicated and contradictory attributes, `ref struct` and interface targets, and an
  empty compilation. It carries its own non-vacuity control — a deliberately throwing generator that the
  harness must catch — because a resilience suite whose detector is broken passes perfectly.
- **Two emitters disagreed on how the emitted null guard is written.** The registry emitter wrote the guard
  inline as `if (source is null) throw new global::System.ArgumentNullException(nameof(source));`, while every
  other emitter used the BCL throw-helper `global::System.ArgumentNullException.ThrowIfNull(source)`.
  **Nothing observable changes**: the same exception type is thrown, with the same `ParamName` — the helper
  captures the argument expression through `[CallerArgumentExpression]`, and that expression is literally
  `source`. What changes is the emitted IL. The inline form puts a `throw` in the body of a method that runs on
  every map, which inflates its IL size and can stop the JIT inlining it; the helper form keeps the `throw` in
  a `[DoesNotReturn]`, non-inlined callee. Found by compiling the generator's own output with the analyzers
  enabled for the first time, and now guarded by a scan that reads **every** emitter at once — each emitter was
  internally consistent, so the divergence lived between them and no per-emitter test could have seen it.

- **A `[MapProperty]` or `[MapIgnore]` written on a member of the mapper class was swallowed.** `DWARF088` was
  raised off the mapper class and off each mapping method, never off a **member** of the mapper — so the one
  placement left was silent, and a caller who annotated a property or field of their `[DwarfMapper]` type got
  no binding, no exclusion, and no word about it. That member belongs to the mapper, not to either type of any
  pair it maps, so **every** form is inert there: the class/method form too, which then left the completeness
  gate demanding the member the caller believed they had excluded. All forms are now reported as `DWARF088`,
  with the remedy that fits the form — the member-placement overloads were aimed at the wrong *kind of type*
  (they belong on a `[MapTo]` source or a `[GenerateMap]` host), while the class/method overloads were aimed at
  the wrong *symbol* and only need moving. A directive on a member of a real `[GenerateMap]` host is unchanged
  and still read. **`DWARF088` is an Error, so this can break a build that compiled before** — the directive
  never did anything, so no mapping changes; what changes is that the build now says so.

### Added

- **The blittable fast path now covers enum arrays, the `List<T>` shapes, and `ImmutableArray<T>`.** Until
  now the single-block copy required an array on *both* sides, so provably-identical elements still went
  through an element-by-element loop the moment a `List<T>` was involved. Three additions, all behind the
  same generation-time proof and all falling back to the element loop when it cannot be completed:

  - **`array → List<T>`, `List<T> → array`, `List<T> → List<T>`** — and with them the whole interface
    family, since `IList<T>`, `IReadOnlyList<T>`, `ICollection<T>` and `IReadOnlyCollection<T>` all
    materialise to `List<T>`. Measured locally at 2.35x–3.31x against the element loop for a 1,000-element
    collection of 16-byte structs. An *interface* on the source side is refused: reading the storage needs
    `CollectionsMarshal.AsSpan`, which is declared on the concrete `List<T>`.
  - **`ImmutableArray<T>`**, in both directions, via `ImmutableCollectionsMarshal`. The result always wraps a
    freshly allocated array — never the source's own — because sharing one buffer between two immutable
    values would make "immutable" false.
  - **Enum arrays**, but only where the conversion is genuinely a reinterpret: `EnumStrategy.ByValue` over
    the same underlying type, or an enum against its own underlying primitive. **The default `ByName`
    strategy is deliberately excluded** — its emitted switch throws `ArgumentOutOfRangeException` on a value
    matching no member, and an enum may legally hold any value of its underlying type, so a block copy would
    pass such a value through where the mapping rejects it. That is a behaviour change, not an optimisation.

  Nothing about *what* your mappings produce changes; this only affects how the bytes get there. Where the
  proof fails, `DWARF100` below will now often tell you why.

- **`DWARF100` — an array pair narrowly missed the blittable fast path (Info).** A pair of arrays came within
  one identifiable step of the block-copy fast path and took the element-by-element loop instead, silently.
  The three blockers it reports are a layout that is not `Sequential` (`Auto` lets the runtime reorder fields,
  which is why `DateTime` never blits), a struct declared in metadata (where an absent `[StructLayout]` cannot
  be read as `Sequential`), and field names that do not line up — DwarfMapper maps by name, so a positional
  reinterpret is only equivalent when the names agree. **Your mapping was already correct; this only tells you
  what a rename or one attribute would buy.** It is informational and will stay informational: a warning would
  become a build failure under `TreatWarningsAsErrors`, and plenty of callers do not care about the copy
  strategy for an array of four elements. It is also deliberately narrow — a pair whose field counts or field
  *types* differ is silent, because that is an ordinary mapping rather than a missed fast path, and a hint
  that fired on every struct mapping would be suppressed wholesale, hiding the cases worth reading.

- **`DWARF099` — one pair carries two contradicting `[MapNullSkip<TSource, TTarget>]` declarations (Error).**
  The generic form is `AllowMultiple`, so `[MapNullSkip<Dto, Entity>(true)]` beside
  `[MapNullSkip<Dto, Entity>(false)]` compiled clean and the reader returned the first by declaration order —
  **source order decided whether a patch-merge mapper skips nulls**, and the discarded declaration was an
  explicit, opposite statement of the caller's intent. It is an error rather than a warning because the two
  have *identical* scope, so there is nothing to rank; the method-versus-pair contradiction stays
  most-specific-wins, because those forms have different scopes and therefore a defensible ordering. Two
  declarations that **agree** are still accepted in silence, and opposite values over *different* pairs are
  the option working as designed. **This can break a build that compiled before** — the fix is to delete one
  of the two.

- **`DWARF098` — `[DwarfMapperConstructor]` names a constructor the mapper cannot use (Warning).** An
  annotated constructor that is inaccessible, `[Obsolete]`, a copy constructor, or takes a `ref`/`out`
  parameter is filtered out before selection runs, and the destination is then built exactly as it would be
  with no annotation at all. That fallback is deliberate and unchanged — selecting the constructor would emit a
  call the compiler rejects — so this reports rather than refuses. The message names the constructor and the
  **specific** filter that rejected it, because the remedies differ; in particular `AllowNonPublic` rescues an
  `internal` constructor and cannot rescue a `private` one. An **absent** annotation stays silent: nothing was
  written, so nothing was discarded.

### Fixed

- **`Project` did not read `ImplicitConversions` at all, so the strictness gate was silently off at that
  endpoint.** `ImplicitConversions = false` is the trust setting — a consumer turns it on to be told about
  lossy conversions — and `long → double` was an **Error** through `.Map` and produced **no diagnostic at
  all** through `.Project`, which then emitted the assignment. The projection pipeline now reports the same
  `DWARF038`, from the same emitter, at the same severity: a Warning under the permissive default (the member
  still maps, at both endpoints, to the same value) and an Error under `ImplicitConversions = false`. It is
  deliberately **not** a `DWARF028` — that id means "a query provider cannot translate this", and a widening
  cast is the most translatable thing there is; refusing it would make the permissive default reject a member
  `.Map` happily maps. **Not a single character of generated code changes** — a provider that translated your
  projection yesterday translates the identical expression today; what changes is that the build now breaks at
  both endpoints or at neither. Reaches the plain member, the nested object, the collection element and the
  constructor parameter.

- **`ImplicitConversions = false` was silently off for every `Nullable<T>` member.** The strict setting is the
  trust boundary — a consumer turns it on precisely to be told about lossy conversions — and a `Nullable<>`
  wrapper took it off, three different ways. `long? → double?` and `long → double?` reported **nothing at any
  severity**: the lossiness classifier read `Nullable<T>`'s own `SpecialType` and answered "not a numeric
  type", while C# happily *lifted* the lossy conversion and the direct-assign path took it. `long? → double`
  went down an unwrap-then-assign arm that asked no lossiness question at all. And every conversion recursion
  that crossed a `Nullable<>` wrapper dropped the option back to its permissive default, so `long? → int?`
  (narrowing) and `string → int?` (parse) — which *did* report — reported a **Warning** under
  `ImplicitConversions = false` and the mapper was generated anyway. All four now answer exactly as the
  unwrapped pair does, at both severities, in the class engine and in the `[MapTo]` registry (they share the
  classifier). **Same-category widening stays silent, wrapped or not** (`int? → long?` is not a loss).
  **This can break a build that compiled before** — that is the option doing what it promises; the remedy is
  the one `DWARF038` already names, `[MapProperty(Use = nameof(...))]`.

- **`[MapProperty(StringFormat = "…")]` emitted a `private static` helper that nothing called.** A format
  string replaces the converter the member's conversion had already resolved to, and the replaced one stayed
  in the synthesized-helper table, so every formatted member shipped a second, unreferenced
  `__DwarfMap_FmtToStr_*` beside the `__DwarfMap_FmtStrF_*` that is actually used. No behaviour change — the
  method was dead — but it was generated code in a file the consumer cannot edit. A helper another member
  still needs is untouched, whichever side of the formatted member it is declared on.

- **One incomplete mapping method took every other method on the mapper down with it.** A destination member
  with no source is `DWARF001`, an Error, and an error suppressed the whole class — so a mapper declaring a
  complete `MapGood` beside an incomplete `MapBad` generated **nothing**, and the consumer got one real error
  plus a `CS8795` for every *other* partial method on the class, each pointing at code they had not broken.
  Completeness is a promise about ONE method: it is evaluated over that method's `(source, target)` pair and
  honours that method's own `[MapIgnore]` set — which is why `DWARF001`'s own text tells you to annotate *the
  method*. Its unit of evaluation and its unit of remedy are both the method, so the refusal is now confined
  to it: the incomplete method is withheld, everything else on the mapper is generated, and the one `CS8795`
  that follows is signposted by the new `DWARF097`. **The build still fails** — `DWARF001` is unchanged and
  still an Error — so no mapping that compiled before compiles differently now; what changes is that the
  errors point only at what is actually wrong. This reaches the **create map**, the **update-into** map and
  the **projection** (an unmapped member there is scoped exactly as an untranslatable one already was).
  **Three shapes deliberately keep the whole-class kill.** A method that raises `DWARF001` *and* a
  class-level error such as `DWARF010`. An incomplete **synthesized** pair — a nested or element pair is
  mapped through a helper shared by every route that reaches it, so its incompleteness is true of each of
  them and pinning it on one method would be wrong; that is why an incomplete element pair behind a span map
  or an async-stream map still reports `DWARF078`. And an incomplete **`[GenerateMap]` pair**, which is the
  boundary of the whole rule: withholding a method is only safe while its *declaration* survives. A `partial`
  method is declared by you, so another method mapping a nested member through it still binds and the single
  `CS8795` is the whole cost — but a `[GenerateMap]` pair has no declaration, and withholding it left a
  sibling calling a method that does not exist (`CS0103`) in a file you cannot edit. Loud collateral beats
  generated code that does not compile. (round 23, I17)

- **`Project` ignored `NullCollections`, so a null source collection came back EMPTY through `Map` and
  `null` through `Project`.** The option is documented once, for the mapper, with no endpoint qualifier —
  *"Null source collection → `AsEmpty` (never throws)"* — and the projection pipeline never read it: it
  emitted `__s.M == null ? null : …` whatever the mapper had configured, so the same member answered
  differently depending on which method the caller reached for. It reads the option now, and computes the
  effective answer with the **same predicate the runtime endpoint uses** — `AsNull` propagates the null only
  when the destination member can hold it, and degrades to `AsEmpty` when it cannot — so the two endpoints
  agree in a `#nullable`-annotated context and in a nullable-oblivious one alike. The null arm is chosen per
  translatable target kind so both arms of the conditional share one static type and no cast appears:
  `new List<T>()` against `ToList`, `Array.Empty<T>()` against `ToArray`, `Enumerable.Empty<T>()` against
  the lazy `Select`. **This changes behaviour for existing projections**: a consumer reading `null` out of a
  projected collection member under the default now reads an empty collection, which is what the
  documentation has always promised and what `Map` has always done. No ternary is added where none existed —
  the guard is still emitted only for a source member that may be null, so a correctly-annotated query gains
  no construct its provider has to translate. (round 23, I19)

- **A collection member whose `Count` is an explicit interface implementation emitted code that did not
  compile.** The buffer pre-sizing predicate asked whether the source type *implements* `ICollection<T>` or
  `IReadOnlyCollection<T>`, and then wrote `s.Count` as the `List<T>` capacity. Implementing an interface is
  not exposing a member: `ImmutableArray<T>` implements both **explicitly**, and so can any user type —
  reference types included, so this was never confined to structs. The emitted `s.Count` did not bind, in
  two ways. In the generated file as written, with no `using` directives, it is **`CS1061`** — a clean
  break. In a consumer project with implicit usings on, `System.Linq` is in scope, `s.Count` binds to the
  extension **method group**, the capacity overload stops matching and overload selection quietly moves to
  a different `List<T>` constructor — **`CS1503`**. The predicate is now a member lookup: a public instance
  `int Count`, else a public instance `int Length`, following C# hiding rules, so `ImmutableArray<T>` still
  pre-sizes (from `Length`) and a type that exposes neither simply does not. Interfaces and type parameters
  keep the interface reading, because ordinary member lookup on those really does see a base interface's or
  a constraint's `Count` — a source member declared `IReadOnlyCollection<T>` pre-sizes exactly as before.
  The class engine and the dictionary engine ask the same question and were fixed with it: the dictionary's
  `new Dictionary(src.Count)` carried its own copy of the interface test. No generated output changes for
  any type that really exposes `Count`. (round 23, N3/B28)
- **A nullable element over a value-type source emitted code that did not compile, and the generator said
  nothing.** `List<S?> → List<D?>` — and `T[]`, `IReadOnlyList<T>`, `HashSet<T>`, a dictionary value, every
  wrapper measured — where the element pair `S → D` needs a synthesized element map and `S` is a struct or
  record struct: the emitted loop handed the element helper an `S?` where it takes an `S`, which is
  **`CS1503`** in a file the consumer cannot edit, with no DwarfMapper word anywhere. The element loops (and
  the dictionary key/value loop next to them) ignored the element's null handling entirely whenever a
  converter was present, so *no* value of that decision reached the emitted element. They now compose the
  two, and the composition follows the destination rather than the source's kind: **an element the
  destination can hold a null in lifts — null element in, null element out** — and one it cannot keeps the
  documented `NullStrategy` unwrap. The same widening settles a sibling the element loops share their
  resolver with: a plain nested member `S? → D?` used to lift only when the *mirrored* type happened to be
  a struct too, and threw `"Source member 'X' was null"` when the same member's destination was declared a
  class or a record — a decision no caller could predict from the types, and one the documented
  `NullStrategy` sentence never covered (it governs nullable-value source into a **non-nullable** target).
  All four value-kind→reference-kind cells now lift like the diagonals always did. **This changes behaviour
  for code that today receives an exception**: an `InvalidOperationException` caught or relied on as a guard
  at one of these members stops arriving, and a null arrives at a destination whose declared type already
  accepts one. The shapes on the collection side emitted nothing that compiled, so no consumer can depend on
  those. (round 23, N1/I5)
- **A null nested value threw instead of arriving as a null, whenever the destination type was declared a
  different kind from the source's.** With the entry above, this completes the rule: `S1? M → D1? M` now
  lifts `null → null` for **all six** kind pairs, so the behaviour of a member follows what its destination
  can hold and not how the two types happen to be declared. The last one to move is the reverse of the
  entry above — a possibly-null **reference** source into a `Nullable<D1>` destination, which threw
  `"Cannot map a null 'S1' to value-type 'D1'."` from *inside* the generated helper. That helper returns a
  value type, so it has no way to say "null"; the call site is the only place that can, and it now tests
  first: `s.M is null ? null : Helper(s.M)`. The two diagonals that always worked are unchanged, and were
  measured before and after rather than assumed. **This changes behaviour for code that today receives an
  exception** — same shape of change as the entry above, and equally bounded: the destination member's
  declared type already accepts the null it now receives. `docs/options.md` states the rule the
  `NullStrategy` row had only half of: that option governs a nullable-value source into a **non-nullable**
  target; where the target can hold the null, the null is lifted regardless of the setting, across nested
  pairs and per element of a collection or dictionary. (round 23, N2/I7)
- **The same nested member lifted through `.Map` and took the whole mapper down through `.Project`.** Completing
  the two entries above at the *other* endpoint: `S1? M → D1? M` across a re-kinded pair projected only when
  the two types were declared the same kind. `struct → class`, `struct → record` and
  `record struct → class` were refused with `DWARF028` *"a nullable source mapped to a non-nullable target"* —
  wrong on its own terms, since the target `D1?` **is** nullable, only not a `Nullable<U>` — and
  `class → struct` with *"no translatable conversion found"*. Because `DWARF028` is an **error**, and an error
  suppressed the whole class, a mapper declaring both a `Map` and a `Project` over such a pair generated
  **nothing at all**: the consumer lost every method, not just the projection. The projection resolver now asks
  the same question the runtime one does — can the destination **hold** the null? — and emits the lift it
  already had the vocabulary for: `__s.M.HasValue ? new D1 { … } : null` for a value-typed source, and
  `__s.M == null ? null : (D1?)(new D1 { … })` for a reference one. All six kind pairs now project, run and
  agree with `.Map`, diagonals included; a collection of them (`List<S1?> → List<D1?>`) projects through the
  same widening. What is still refused is a target that genuinely **cannot** hold the null, and the message says
  that instead of naming the kind. (round 23, I14)
- **One untranslatable projection member stopped a mapper from generating *anything*.** The cascade behind the
  entry above, and it outlives it: a `HashSet` target, a `Use =` converter, a hook, `ReferenceHandling` — any
  `DWARF028` at all — suppressed the entire class, so every `Map` method on it lost its implementing part and
  the build filled with `CS8795`, with `DWARF078` announcing that nothing had been generated. The `Map` methods
  were collateral: nothing about them is translated by a query provider, so nothing about them can fail to
  translate. A projection refusal is now scoped to the method that carries it. The `Project` method is dropped
  (a projection missing the members that did not resolve would return them silently unset), the class is emitted
  with its `Map` methods — which keep their facade extensions, DI registration and ambient-registry entries —
  and exactly **one** `CS8795` follows, signposted by the new `DWARF096` instead of `DWARF078`. Class-level
  errors are unchanged: an ambiguous member or an unknown destination still suppresses everything, because those
  describe a model the emitter cannot trust. (round 23, I14)
- **A nullable object source projected into a value-type nested target emitted code that did not compile.**
  Found by I14's sibling hunt, not by sampling. `class Src { Nested? N }` → `class Dst { NestedStruct N }`
  through `Project` emitted `__s.N == null ? null : new NestedStruct { … }` — two arms with no common type,
  which is **`CS0037`** in a file the consumer cannot edit, with no DwarfMapper word anywhere. The nested-object
  resolver guarded on whether the *source* could be null without asking whether the *target* could hold the
  result. It now asks, and refuses with `DWARF028` naming the value-type target: the link needs a null decision,
  `.Map` answers it with `NullStrategy`, and `NullStrategy` is precisely what an expression tree cannot express.
  A nullable-annotated **reference** target keeps the long-standing conditional — it can hold the null.
  (round 23, I14)
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

- **`DWARF097` — the per-method twin of `DWARF078` for the `Map` endpoints (Warning).** Reported when one
  mapping method was not generated because a destination member of it has no source, and the rest of the
  mapper was. `DWARF078` says *"no code was generated for this mapper"*, which used to be true of a
  completeness failure and is not any more: the class is emitted, every other method with it, and exactly one
  `CS8795` follows on the withheld method (none at all when it is declared without accessibility modifiers,
  which C# allows to have no implementing part — there this warning is the only thing you see). That single `CS8795` needs the same signpost the class-wide wall
  has always had — it is a cascade, not a missing analyzer reference — and it needs different remedy text from
  `DWARF096`, which can suggest dropping the `Project` method and mapping at runtime: nonsense advice for a
  `Map` method whose destination simply has a member nobody mapped. Suppressible like any warning; it never
  appears beside `DWARF078`, because when a class-level error takes the class down anyway the scoped signpost
  stands down rather than claim a scope that is no longer true. (round 23, I17)

- **`DWARF096` — the per-method twin of `DWARF078` (Warning).** Reported when one `Project` method was not
  generated because a member of it cannot be translated, and the rest of the mapper was. `DWARF078` says
  *"no code was generated for this mapper"*, which used to be true of a projection refusal and is not any more:
  the class is emitted, its `Map` methods with it, and exactly one `CS8795` follows on the dropped `Project`.
  That single `CS8795` needs the same signpost the class-wide wall has always had — it is a cascade, not a
  missing analyzer reference — and `DWARF078` could no longer supply it without lying about the scope. A
  Warning, like `DWARF078`, and for the same reason: the `DWARF028` above it is the error and the thing to fix.
  The two never appear together: if another method on the same class also has an error, nothing is generated
  after all, so this one's claim would be false and it stands down in favour of `DWARF078`. (round 23, I14)
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
- **A zero-alloc span map takes the blit.** `void Map(ReadOnlySpan<S> src, Span<D> dst)` now shares the array/list
  blit's proof: when the element pair is layout-identical (`BlittableProof.CanReinterpret`/`CanReinterpretEnums`),
  the body is one `MemoryMarshal.Cast<S, D>(src).CopyTo(dst)` block copy after the length guard, instead of the
  per-element loop — zero allocation either way. Gated on the pair being the DEFAULT resolution, though: a
  user-declared element converter, or a pair-scoped `[MapIgnore<T>]`/`[MapProperty<S,T>]`/`[MapValue<T>]`/
  `[BeforeMap]`/`[AfterMap]` targeting this exact element pair, keeps the element loop — the registry that
  names the synthesized helper is keyed purely by the type pair, so any of those customizes the SAME name a
  block copy would otherwise bypass. A pair that narrowly misses gets `DWARF100`, worded for the method rather
  than a member (a span map has no target member to name); `DWARF100`'s title and description now cover both
  an array/list pair and a span-map element pair. (round 29, T0.2)

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
