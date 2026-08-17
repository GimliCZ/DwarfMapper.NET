<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper diagnostics reference

> Machine-generated companions: [diagnostics index](generated/diagnostics-index.md)
> (every id, severity and title, rendered from the descriptors) and the
> [API reference](generated/api-reference.md) (the public surface, from the assembly and its XML
> summaries), and the [option support matrix](generated/option-support-matrix.md) (what each
> `[DwarfMapper]` option actually does at each endpoint, measured by compiling with and
> without it). Both fail the build if they drift from the code.

Every DwarfMapper diagnostic (`DWARF001`–`DWARF088`) is listed here with what triggers it and how to
fix it. The IDE "learn more" link on each build error points at the matching `#dwarfNNN` anchor below.
These are **compile-time**; for what a generated mapper can throw **at runtime**, see
[Runtime exceptions](#runtime-exceptions) at the bottom.

## How severities and suppression work

| Severity | Meaning | Can I turn it off? |
|---|---|---|
| **Error** | The mapping is incomplete or the configuration is invalid — the build fails. | No global override. Resolve it (often a one-line `[MapIgnore]`/`[MapProperty]`/`[MapValue]`). |
| **Warning** | The configuration compiles but something was skipped or won't behave as you might expect. | `dotnet_diagnostic.DWARFxxx.severity = none` in `.editorconfig`. |
| **Info / suggestion** | Visible in the IDE, never build-breaking — surfaces a footgun without forcing a change. | Suppress with `.editorconfig`, or escalate to `error` there. |

The headline rule, **`DWARF001` (completeness)**, is enforced **by construction**: you *can* set its severity in `.editorconfig`, but that doesn't ship an incomplete map — the method body isn't generated, so the build still fails (with a rawer compiler error instead of the helpful `DWARF001`). See
[`CORRECTNESS.md`](CORRECTNESS.md). Everything else is suppressible or escalatable through `.editorconfig`,
e.g.:

```ini
# .editorconfig — make an opt-in suggestion strict, or silence a known-safe one
dotnet_diagnostic.DWARF039.severity = error   # only fires under [DwarfMapper(RequiredMapping = Both)]; escalates its Info to Error
dotnet_diagnostic.DWARF044.severity = none    # I accept the nullable-path risk here
```

### `#pragma warning disable` does **not** work for any `DWARF…` id

This is the one suppression mechanism that is unavailable, and it is worth stating plainly because it is the
first thing most people reach for:

<!-- fence-exempt: demonstrates a suppression that does NOT work; a compiling sample cannot express "has no effect" -->
```csharp
#pragma warning disable DWARF076   // has no effect — the diagnostic is still reported
```

Pragmas are applied by the **compiler's** diagnostic filtering, and diagnostics reported by a *source
generator* never pass through it. That is a Roslyn platform limitation, not something DwarfMapper can honour,
and it applies uniformly to every id in this file.

So the suppression options are:

| Scope | Mechanism | Availability |
|---|---|---|
| Whole project | `dotnet_diagnostic.DWARFxxx.severity = none` in `.editorconfig` | **every** id |
| One mapper class, in the file | `[SuppressMessage("DwarfMapper", "DWARFxxx:…")]` on the mapper | `DWARF076` today; see the note below |
| One line | `#pragma warning disable` | **never** — see above |

`[SuppressMessage]` is an ordinary attribute the generator can read, so it *can* be honoured where the
diagnostic is reported against a symbol the generator has in hand. It is wired for `DWARF076`, whose whole
point is acknowledging a deliberate same-type clone. Ids reported against a *member* rather than the class
mostly have a better in-place answer already — `[MapIgnore]`, `[MapValue]`, `[MapProperty]` — which states
*what you meant* rather than merely silencing the message.

> Round 18 context: `docs/diagnostics.md` previously promised `#pragma` for `DWARF076`. Four independent
> migration agents tried it, and a warnings-as-errors consumer was left with no in-file escape hatch at all.
> The promise was removed and `[SuppressMessage]` was implemented, with a test pinning the pragma behaviour so
> a future Roslyn change would be noticed.

`DWARF004`, `DWARF006`, `DWARF019`, and `DWARF029` are retired/reserved ids and are never emitted.

The `[MapTo]` registry front door emits a **separate** `DWARFR01`–`DWARFR06` family — see
[Registry diagnostics](#registry-diagnostics-mapto) just below.

### Adopting incrementally (the strictness valve)

DwarfMapper is strict by default so that a mapping decision you didn't make is a build-time signal, not a
runtime surprise. When retrofitting it onto an existing codebase you can dial that strictness up or down, per
rule, without giving up the guarantees you *do* want:

1. **Loosen the noise, keep the guarantees.** Turn opt-in *suggestions* off where they don't fit
   (`dotnet_diagnostic.DWARF039.severity = none`), while the correctness rules (`DWARF001`, conversions,
   depth guard) stay on. `DWARF001` is enforced *by construction* — you cannot accidentally ship an incomplete
   map even if you silence its id.
2. **One-click resolutions.** The common completeness diagnostics carry code fixes: `DWARF001` → *Add
   [MapIgnore]*, `DWARF072` → *Map it* / *Ignore it*, `DWARF052` → *scaffold the inverse*. Adopt member by
   member from the IDE lightbulb rather than hand-editing attributes.
3. **Tighten as you go.** Escalate a suggestion to a build error once a module is clean
   (`dotnet_diagnostic.DWARF038.severity = error` for strict, Mapperly-style conversions;
   `[DwarfMapper(RequiredMapping = Both)]` to also flag unused source members), or reach for the
   trust-boundary guard (`AutoMatchMembers = false`) on the maps that cross one.
4. **Migrating from another mapper?** Start from the matching guide under
   [`howto/`](howto/) (AutoMapper / Mapperly / Mapster / handwritten) — each maps that library's knobs onto
   these.

Every diagnostic below documents its own fix; this table is just the order to apply them in.

---

## Registry diagnostics (`[MapTo]`)

The `[MapTo]` registry front door (attribute-on-the-source, no mapper class) has its own `DWARFR##` codes,
category `DwarfMapper.Registry`, distinct from the `[DwarfMapper]` class-model `DWARF###` codes below. **`[MapTo]`
is a prototype/experimental tier**: its diagnostics are deliberately not release-tracked and are exempt from the
DWARF0xx self-validation scans that the rest of this reference is held to.

| Code | Meaning & fix |
|---|---|
| `DWARFR01` | **Invalid `[MapTo]` target** — the target type isn't a mappable class/struct. |
| `DWARFR02` | **Destination member is not mapped** — the registry's completeness gate (the `[MapTo]` counterpart of `DWARF001`). Add a source member, a `[MapProperty]` binding, or drop it. Member enumeration now walks the base-type chain exactly as the `[DwarfMapper]` class model does, so this gate also covers **inherited destination members** — a base-class member that was never mapped before now trips `DWARFR02`. Because `DWARFR02` is Error severity, this can turn a project that built yesterday into a build failure today. **Fix:** supply the inherited member (a source member, a `[MapProperty]` binding) or `[MapIgnore]` it. |
| `DWARFR03` | **Conflicting sources for one destination member** — more than one source claims it; give them distinct positional `[MapProperty]` names. Inherited members now participate too: a member the source class picks up from a base class can conflict with one declared directly, and a derived member renamed onto a name its base also supplies is now a conflict where it previously wasn't. |
| `DWARFR04` | **`[MapProperty]` value count doesn't match the targets** — supply one value (all targets) or exactly one per `[MapTo]` target, in order. Two arities can be wrong and both report this code. **How many attributes are stacked:** `[MapProperty]` on a base class is read for every derived `[MapTo]` source, not just the class that declares it — so a base annotated for a 2-target derived type can emit `DWARFR04` on a 1-target sibling derived type that inherits the same attribute. **How many values one of them carries:** `[MapProperty("A", "X")]` on a source member is the `[DwarfMapper]` class model's *method* form; the member form takes the single destination name this member supplies. It used to bind nothing at all and the member fell back to its own name — silently. Drop the first argument. |
| `DWARFR05` | **No conversion between mapped members** — the member types are incompatible; use the `[DwarfMapper]` class model for a custom `Use=` converter. |
| `DWARFR06` | **Recursive nested mapping is not supported by the registry** — the front door threads no reference context; use the `[DwarfMapper]` class model (`ReferenceHandling`/`OnCycle`) for cyclic graphs. |
| `DWARFR08` | **Two `[MapTo]` targets generate the same method name** — targets whose *simple* names collide (`Foo.Order` and `Bar.Order`) would each emit `ToOrder(this Src)` into one static class (CS0111). Rename a target, or use the `[DwarfMapper]` class model where every method is named explicitly. |
| `DWARFR09` | **`[MapTo]` target has no accessible parameterless constructor** — the registry constructs targets with `new T { … }`. Add a public parameterless constructor, or use the `[DwarfMapper]` class model, which supports constructor mapping. |
| `DWARFR07` | **Lossy implicit numeric conversion** (Info) — the conversion is implicit in C# but crosses numeric categories (`long`→`double`, `int`→`float`, `long`→`decimal`) and loses precision for large magnitudes. The `[DwarfMapper]` class model reports the same thing as `DWARF038`; map through an explicit member type if the precision matters. |

---

## dwarf001
**Destination member is not mapped** · Error

A destination member has no matching source member and no explicit handling. This is the core completeness
gate. **Fix:** map it (`[MapProperty(src, "{member}")]`), give it a value (`[MapValue("{member}", …)]`), or
intentionally drop it (`[MapIgnore("{member}")]`). See [`CORRECTNESS.md`](CORRECTNESS.md).

## dwarf002
**Mapper type must be partial** · Error

A class annotated `[DwarfMapper]` is not `partial`, so the generator can't add the method bodies.
**Fix:** add the `partial` modifier to the class.

## dwarf003
**Invalid mapping method signature** · Error

A mapping method isn't a `partial` instance method with exactly one parameter and a non-void return type.
**Fix:** match a supported shape — `partial TTarget Map(TSource s)`, `partial void Update(S s, T d)`,
`partial IQueryable<T> Project(IQueryable<S> q)`, span, or async-streaming.

## dwarf005
**No implicit conversion between mapped members** · Error

A source and destination member are paired but their types can't be bridged automatically and no converter
applies. **Fix:** declare a mapping method for the two types, or use `[MapProperty(src, tgt, Use = nameof(M))]`.

## dwarf007
**Destination member is read-only** · Error

A writable-looking pairing targets a read-only member, so the value would be lost. **Fix:** make the member
settable, or `[MapIgnore("{member}")]` if dropping it is intentional.

## dwarf008
**MapProperty target not found** · Error

`[MapProperty]`'s destination member doesn't exist or isn't writable. **Fix:** correct the name (consider
`nameof(...)`), or make the member settable.

## dwarf009
**MapProperty source not found** · Error

`[MapProperty]`'s source member doesn't exist or isn't readable. **Fix:** correct the name, or use a dotted
source path if you meant to read through the graph.

## dwarf010
**Ambiguous source member** · Error

Under `CaseInsensitive = true`, a destination member matches more than one source member. **Fix:** rename
one, or pin the intended one with `[MapProperty]`.

## dwarf011
**Duplicate explicit mapping** · Error

A destination member has more than one `[MapProperty]`. **Fix:** keep a single mapping for it.

## dwarf012
**Conflicting [MapIgnore] and [MapProperty]** · Error

A member is both ignored and mapped. **Fix:** remove one of the two attributes.

## dwarf013
**Ambiguous conversion method** · Error

More than one mapping method can convert the member's types. **Fix:** disambiguate with
`[MapProperty(src, tgt, Use = nameof(M))]`.

## dwarf014
**Conversion method not found** · Error

The `Use =` method doesn't exist or has an incompatible signature. **Fix:** it must take the source member
type and return the destination member type.

## dwarf015
**Incomplete enum mapping** · Error

By-name enum mapping (the default) found a source enum member with no same-named destination member.
**Fix:** add the missing member, or switch that mapper to `EnumStrategy.ByValue`.

## dwarf016
**Invalid flatten source** · Error

`[Flatten]`'s root doesn't exist, isn't readable, or exposes no readable sub-members. **Fix:** correct the
root name or map the leaves with dotted `[MapProperty]`.

## dwarf017
**Ambiguous flattened member** · Error

A destination member is flattened from more than one source root. **Fix:** disambiguate with `[MapProperty]`.

## dwarf018
**Invalid mapping hook signature** · Error

A `[BeforeMap]`/`[AfterMap]` method has the wrong shape. **Fix:** hooks return `void`; `[BeforeMap]` takes one
parameter, `[AfterMap]` takes one or two.

## dwarf020
**No inverse for [RoundTrip]** · Error

A `[RoundTrip]` method has no inverse mapping method. **Fix:** declare the inverse (a partial method with the
source/destination types swapped).

## dwarf021
**Ambiguous inverse for [RoundTrip]** · Error

A `[RoundTrip]` method has more than one candidate inverse. **Fix:** remove or rename the extra candidate.

## dwarf022
**Invalid [Reinterpret] target** · Error

`[Reinterpret]` must map an array to an array of an **unmanaged (blittable)** element type — e.g.
`int[] → int[]`. Arrays of reference types, or of structs that contain references, can't be reinterpreted.
`[Reinterpret]` forces the blittable bulk-copy fast-path (it reinterprets one array's memory as another), which
is only sound when both element types are unmanaged and the same size. **Fix:** remove `[Reinterpret]` — the
generator already falls back to a safe element-by-element copy.

## dwarf023
**[AfterMap] value-type target must be passed by ref** · Error

An `[AfterMap]` on a value-type target takes it by value, so changes are lost. **Fix:** take the target
parameter by `ref`.

## dwarf024
**Constructor parameter has no mappable source member** · Error

A required constructor parameter has no matching source member. **Fix:** add a matching source member, or
redirect one with `[MapProperty("Source", "<paramName>")]`.

## dwarf025
**Ambiguous constructor** · Error

Either several constructors tie for the most parameters, **or** more than one constructor is annotated
with `[DwarfMapperConstructor]`. **Fix:** for a tie, mark exactly one with `[DwarfMapperConstructor]`; for
duplicate annotations, remove all but one.

## dwarf026
**No mappable constructor** · Error

The destination type has no accessible, non-obsolete instance constructor to map into. **Fix:** add one, or
map to a different type.

## dwarf027
**Unsupported collection/dictionary target type** · Error

The collection/dictionary target type isn't supported. **Fix:** use a supported target (`T[]`, `List<T>`,
`HashSet<T>`, `Queue<T>`, `Stack<T>`, `Dictionary<K,V>`, the collection interfaces, and the immutable family),
supply `[MapProperty(Use = ...)]`, or map it manually.

> `Queue<T>` and `Stack<T>` map with their enumeration order preserved (mapping a sequence keeps the sequence).
> `Queue<T>` is FIFO so this is natural; `Stack<T>` is LIFO, so the source is reversed on construction — which
> means `List → Stack → List` round-trips to the original order, rather than silently reversing.

## dwarf028
**Projection member cannot be translated to a database query** · Error

An `IQueryable` projection becomes an expression tree your database/ORM provider translates into a query. A
member that needs a runtime conversion, a custom converter, a non-translatable collection/dictionary target
(`HashSet`/`ISet`/immutable/`Dictionary` — `List<T>`/`T[]` targets *do* translate), or reference handling has no
query equivalent. The build error names the specific reason (narrowing, parse, by-name, converter, collection
kind, hook, reference handling, …). **Fix:** map those members with a runtime mapper (an ordinary `Map` method)
rather than `Project`.

## dwarf030
**Constructor parameter is part of a reference cycle** · Error

A member set through a constructor parameter or `init`-only property takes part in a reference cycle under
`ReferenceHandling=Preserve`. A cycle can only be reconstructed when the looping member is assigned *after* the
object is created (the mapper records each object before filling it, so cycles can point back to it). **Fix:**
make the member a settable property, or break the cycle.

## dwarf031
**Mapping nests too deeply** · Error

The generator reached its limit of 512 synthesized nested mappers. **Fix:** declare explicit mapping methods
for the deeply-nested types to bound the recursion.

## dwarf032
**Custom converter can't preserve reference identity** · Error

A `[MapProperty(Use = ...)]` converter is opaque to the generator, so under `ReferenceHandling=Preserve` the
mapper can't track the converted value's identity: it won't be shared with other references to the same object,
and a cycle through it won't be reconnected. **Fix:** map it without a custom converter to keep identity, or
keep the converter if a duplicated (non-shared) value is acceptable.

## dwarf033
**Abstract or interface source type in auto-nested mapping** · Error

Auto-nesting an abstract/interface source maps only declared members and would silently drop members that exist
only on derived runtime types. **Fix:** declare an explicit mapper (or `[MapDerivedType]`), `[MapIgnore]` it, or
make the source concrete.

## dwarf034
**Invalid [FlattenGraph] configuration** · Error

A `[FlattenGraph]` is misconfigured; the message states the specific problem. **Fix:** follow the message
(usually a target-shape or root mismatch).

## dwarf035
**Invalid [MapDerivedType] configuration** · Error

A `[MapDerivedType]` arm is invalid; the message states the specific problem. **Fix:** correct the source/target
derived-type pair.

## dwarf036
**Ambiguous [MapDerivedType] dispatch arms** · Error

Two `[MapDerivedType]` arms overlap so dispatch is ambiguous. **Fix:** make the arms mutually exclusive (most
specific type wins).

## dwarf037
**OnCycle is ignored under ReferenceHandling.Preserve** · Warning

`OnCycle = SetNull` is set together with `ReferenceHandling = Preserve`. `OnCycle` only applies in `None` mode
(Preserve already reconstructs cycles), so it has no effect. **Fix:** drop `OnCycle`, or switch to `None` mode if
you wanted cycle-breaking.

## dwarf038
**Implicit type conversion applied** · Warning for lossy conversions, Info otherwise (escalates to Error)

A non-lossless conversion is being applied. It's visible, not silent. **Lossy** sub-cases — numeric
narrowing/sign-change, parse/format (`string ↔ T`, which can throw `FormatException` / `OverflowException` at
runtime), and cross-category numeric (precision loss) — are **Warnings**. A user-defined explicit conversion
operator stays **Info** (you defined it deliberately). **Fix (optional):** make it explicit with
`[MapProperty(Use = nameof(...))]`. Set `[DwarfMapper(ImplicitConversions = false)]` to turn all such
conversions into build errors. To silence a specific instance, downgrade in `.editorconfig`:
`dotnet_diagnostic.DWARF038.severity = suggestion` (a `-warnaserror` build treats the Warning as an error
until downgraded). A conversion on a nested/collection element may be reported without a file location; for a
blanket downgrade across a project use `<WarningsNotAsErrors>DWARF038</WarningsNotAsErrors>` (keep the warning,
don't fail the build) or `<NoWarn>DWARF038</NoWarn>` (suppress) in the `.csproj`.

## dwarf039
**Source member is read by no destination member** · Info

Emitted only under `RequiredMapping = Both`: a source member is consumed by nothing. **Fix:** map it, or mark
it `[MapIgnoreSource("{member}")]`. Escalate to `error` in `.editorconfig` to make source-coverage strict.

## dwarf040
**Constant [MapValue] is not assignable to the destination** · Error

A constant `[MapValue]` literal can't be assigned to the destination type. **Fix:** supply an attribute-legal
value assignable to that type.

## dwarf041
**[MapValue(Use=)] provider method is invalid** · Error

A `[MapValue(Use = ...)]` provider is wrong. **Fix:** it must be a **parameterless** method whose return type is
assignable to the destination.

## dwarf042
**Conflicting or invalid [MapValue]** · Error

`[MapValue]` conflicts with `[MapProperty]`/`[MapIgnore]` on the same target, targets an unknown member, or
targets a constructor parameter. **Fix:** remove the conflict or correct the target.

## dwarf043
**[MapProperty] source path segment not found** · Error

A dotted source path has a segment that doesn't resolve. **Fix:** correct the path; member names never contain
dots, so each segment must be a real member.

## dwarf044
**[MapProperty] source path traverses a nullable member** · Warning

A dotted source path passes through a nullable member, which can throw `NullReferenceException` at runtime — a
data-integrity hazard, so it is a **Warning** (item 8), consistent with the other runtime-throwing diagnostics.
**Fix (optional):** guard the value, or downgrade in `.editorconfig` if you know it's non-null here:
`dotnet_diagnostic.DWARF044.severity = suggestion` (or `none`). A `-warnaserror` build treats the Warning as an
error until downgraded.

## dwarf045
**Invalid [MapProperty] unflatten target path** · Error

A dotted *target* path is invalid (deeper than one level, or the intermediate has no public parameterless
constructor). **Fix:** use a single-level target path into a class with a parameterless constructor.

## dwarf046
**Conflicting [MapProperty] unflatten target** · Error

An unflatten target conflicts with a direct mapping of the same root. **Fix:** keep one or the other.

## dwarf047
**Additional mapping parameter is unused** · Info

An extra method parameter matched no destination member by name. **Fix:** rename it to match a destination
member, remove it, or suppress via `.editorconfig`.

## dwarf048
**Ambiguous member match under NameConvention.Flexible** · Error

Under `NameConvention.Flexible`, two source members normalize to the same destination member. **Fix:** rename
one, or pin the mapping with `[MapProperty]` (which stays exact).

## dwarf049
**Invalid [MapProperty(NullSubstitute=)]** · Error

The `NullSubstitute` value isn't valid for the member. **Fix:** supply a value assignable to the destination
type.

## dwarf050
**Invalid [MapProperty(When=)] predicate** · Error

The `When` predicate is wrong. **Fix:** it must be a `bool` method taking the source.

## dwarf051
**[ReverseMap] cannot auto-invert this configuration** · Warning

A forward `[MapProperty]` couldn't be auto-inverted (it uses `Use=`, a dotted path, `NullSubstitute`, or `When`).
**Fix:** declare the reverse rename explicitly on the inverse method.

## dwarf052
**[ReverseMap] has no inverse mapping method** · Error

A `[ReverseMap]` has no inverse method to attach to. **Fix:** declare the inverse partial method (types swapped).

## dwarf053
**Generic mapping methods are not supported** · Error

A mapping method declares type parameters; a generator can't emit a body for an unbound type. **Fix:** use a
closed `[GenerateMap<A,B>]` or a non-generic partial method.

## dwarf054
**Mapping is not supported on a generic class** · Error

The class carrying the mapping (a [DwarfMapper] class or a co-located [GenerateMap<>] host) is generic.
**Fix:** declare the mapping on a non-generic class.

## dwarf055
**Mapper is very large; consider splitting it** · Info

A single mapper resolves a very large number of members, which can add IDE/compile latency. **Fix (optional):**
split it into several mappers, or suppress via `.editorconfig`.

## dwarf056
**Pair-scoped attribute matches no mapped pair** · Warning

A class-level pair-scoped attribute — `[MapProperty<TSource, TTarget>(…)]`, `[MapIgnore<TTarget>(…)]`, `[MapValue<TTarget>(…)]`, or `[MapConstructor<TSource, TTarget>(…)]` —
matched no mapped pair, so it silently does nothing (usually a typo'd type argument or a missing
`[GenerateMap]`). A pair-scoped linkage applies wherever its `(TSource → TTarget)` pair is actually mapped —
a top-level `[GenerateMap]` pair or an auto-synthesized nested/collection-element pair. **Fix:** add the
`[GenerateMap<TSource, TTarget>]` (or the mapping that nests it), correct the type arguments, or remove the
attribute.

> **Note — a host with *no* trigger is inert.** Pair-scoped attributes only take effect on a class that also
> declares a mapping: a `[GenerateMap<>]` pair (or a `[DwarfMapper]` mapper). On a class carrying *only*
> pair-scoped attributes (no `[GenerateMap]`, no `[DwarfMapper]`) they currently do nothing and raise no
> diagnostic — make sure the host carries a `[GenerateMap<>]`.

## dwarf057
**Generated mapper name collides with an existing type** · Error

A co-located `[GenerateMap<>]` host would emit a generated mapper named `<Host>Mapper`, but a type with that
name already exists (a hand-written mapper, or a `[DwarfMapper]` class of the same name). DwarfMapper reports
this rather than emitting the colliding type (which would be a raw C# error against generated code, a silent
partial-merge, or — if it clashed with another generated mapper's file — abort all generation). **Fix:** rename
the existing type, or declare the mapping on a `[DwarfMapper]` mapper class instead of co-locating it.

## dwarf058
**Convenience extension method was not generated (ambiguous)** · Info

Two or more mappers would produce the same `source.ToTarget()` convenience extension (same source type, same
target-derived name), so it was **not** generated for either — the mapping still works through the instance
methods. **Fix (optional):** call the mapper instance method (`new XMapper().Map(x)`), or disable one mapper's
extensions with `[DwarfMapper(GenerateExtensions = false)]`. Suppress via `.editorconfig` if intentional.

## dwarf059
**Constructor factory method not found** · Error

`[MapConstructor<TSource, TTarget>("Method")]` names a factory that does not exist on the mapper or whose
signature is incompatible. The factory must take a value assignable from `TSource` and return one assignable to
`TTarget` (it may invoke any constructor or logic). **Fix:** add the method, correct its name, or fix its
parameter/return types — e.g. `private static AliasCommand MakeAlias(CommandDto s) => new(s.CommandFormat, s.Alias!);`.

## dwarf060
**Conflicting map methods from the same source type** · Error

Two maps from the **same** source type to **different** targets — e.g. `[GenerateMap<Order, OrderDto>]` and
`[GenerateMap<Order, OrderSummary>]`, or a `[GenerateMap]` plus a partial `OrderSummary Map(Order o)` — would
both generate a method named `Map` taking `Order`, differing only by return type. C# cannot overload by return
type (CS0111). DwarfMapper reports this instead of emitting opaque generated-code errors, and suppresses the
duplicate so this is the only diagnostic. **Fix:** give one a distinct name with a partial method —
`public partial OrderSummary ToSummary(Order o);`. Its pair-scoped `[MapProperty]`/`[MapIgnore]` attributes still
apply. (Update-into `void Map(S, T)`, span, and async-stream maps take distinct parameter lists and never collide.)

## dwarf061
**Required ambient map is not provided** · Error

In the assembly marked `[assembly: DwarfMapperValidationRoot]` (the composition root, which references the
whole graph), DwarfMapper checks that every ambient map consumed through `IDwarfMapper` — auto-detected from
`Map<TDest>(src)` call sites or declared with `[UsesMap<S,T>]` — is provided by *some* assembly in the graph.
A pair with no provider would otherwise throw `DwarfMapMissingException` at runtime; this reports it at build
time. **Fix:** declare `[GenerateMap<S,T>]` in a referenced assembly (it self-registers into the ambient
registry via a module initializer), or reference the assembly that already declares it. Only effectively-public
types participate (internal types have no cross-assembly meaning).

## dwarf062
**Mapper not added to the ambient registry** · Info

The ambient registry is populated by generated module initializers, which run with no DI context and can only
construct mappers that have an accessible parameterless constructor. A mapper with constructor dependencies
(e.g. an object factory) is therefore left out of ambient `IDwarfMapper` resolution. **Fix:** inject the
concrete mapper directly, or give it a parameterless constructor. (Informational — the mapper still works.)

## dwarf063
**Ambiguous ambient map provider** · Warning

Two assemblies in the graph both provide an ambient map for the same `(source, destination)`. The registry
keeps the first registration and ignores the rest. **Fix:** ensure the duplication is intentional, or remove
all but one definition. Reported at the validation root.

## dwarf064
**[MapValue] shadows an auto-matchable source member** · Info

A `[MapValue]` supplies a constant/provider for a target that *also* has a same-named readable source member,
so the real source value is never read — usually a leftover stub from before the source member existed. **Fix:**
remove the `[MapValue]` to map the source member, or `[MapIgnoreSource("{member}")]` if the shadow is intended.

## dwarf065
**Update-into replaces a nested member instead of merging it** · Info

An update-into `Map(S src, T dest)` maps a nested object member by replacing `dest`'s existing instance with a
freshly-mapped one (its identity discarded), not by deep-merging into it. **Fix:** map the leaf members directly
if you need to preserve the nested instance, or accept the replacement. (Collections are always rebuilt.)

## dwarf066
**[MapProperty(When=)] can leave a non-nullable member at its default** · Info

A `[MapProperty(When=)]` guards a non-nullable reference target: when the predicate is false the member is not
assigned and keeps its default (which for a non-nullable reference is `null`). **Fix:** give the member a default
initializer, make it nullable, or confirm the unset default is intended.

## dwarf067
**[GenerateWrapperMap] wrapper is not a single-payload generic** · Error

`[GenerateWrapperMap(typeof(W<>))]` requires `W` to be a generic type with exactly one type parameter and exactly
one member of that parameter's type (a `List<T>` payload is not a single non-collection payload). **Fix:** point
the attribute at a single-payload envelope (`Result<T>`/`Page<T>`/`Envelope<T>`), or declare the wrapper pairs
explicitly with `[GenerateMap<W<A>, W<B>>]`.

## dwarf068
**Unsupported MapConfig expression** · Error

A `MapConfig<S,T>` convention method's fluent call uses a selector that isn't a member-access chain (e.g. a
method call like `s => Identity(s.A)`), or a converter/factory/predicate argument that isn't a method group (e.g.
an inline lambda `v => v`). **Fix:** extract a named method (a method group), or use a plain member selector
(`t => t.A.B`).

## dwarf069
**Conflicting member configuration** · Error

The same destination member is configured more than once — by both an attribute (`[MapProperty<,>]`/
`[MapValue<>]`) and a `MapConfig<S,T>` `.Map`/`.Value` call, or twice within the same `MapConfig<S,T>` method.
**Fix:** remove one of the two configurations for that member.

---

## dwarf070
**Nullable source member is assigned to a non-nullable target member** · Warning

The source member is a nullable reference (`string?`) but the destination member is not (`string`), so a null
would be stored in a member whose type says it cannot be null. `NullStrategy` does **not** cover this: it
governs nullable *value* types (`int?`) only, and a nullable *reference* source raw-assigns by design.

Left alone, that raw assignment also made the **compiler** emit `CS8601` ("possible null reference assignment")
from inside the *generated* file — a warning you cannot fix, in code you cannot edit, and a hard build break
under `TreatWarningsAsErrors`. DwarfMapper now suppresses that `CS8601` and reports this instead, against your
own DTO, where you can act on it.

**Fix** — pick the one that matches your intent:

| You want | Use |
|---|---|
| a fallback value when the source is null | `[MapProperty(nameof(Src.Name), nameof(Dst.Name), NullSubstitute = "(none)")]` |
| the destination to keep its own default | `[DwarfMapper(SkipNullSourceMembers = true)]` |
| null to be a legal value here | make the destination member nullable (`string?`) |
| to accept the null knowingly | `dotnet_diagnostic.DWARF070.severity = none` |

Only fires for a genuinely annotated source (`string?`) flowing into an annotated non-nullable target. Code in
a `#nullable disable` context is *oblivious*, the compiler raises no `CS8601` there, and neither does this — a
legacy codebase is not flooded with warnings about a contract it never opted into.

---

## dwarf071
**Source type has derived types whose members would be dropped** · Info

The source member is declared as a concrete class that something else in your compilation *derives from*. A
mapper resolves members at **compile time from the declared type**, so if that member holds a derived instance
at run time, the members declared only on the derived type are dropped — silently, with no error:

<!-- fence-exempt: illustrates a type shape that triggers DWARF042; a fragment, not a compilable unit -->
```csharp
public class Animal { public string Name { get; set; } = ""; }
public sealed class Dog : Animal { public string Breed { get; set; } = ""; }

public class Kennel { public Animal Pet { get; set; } = new(); }   // may actually hold a Dog
// -> the mapper copies Name and drops Breed.
```

This is the one place where being a compile-time mapper is a genuine disadvantage against a reflective one,
which can dispatch on the runtime type. So DwarfMapper does the next best thing: it tells you.

**Fix** — pick the one that matches your intent:

| You want | Use |
|---|---|
| dispatch on the runtime type | `[MapDerivedType<Dog, DogDto>]` on the mapping method |
| the declared type to be the only type | `sealed` on the source class |
| base members only — that *is* the intent | `dotnet_diagnostic.DWARF071.severity = none` |

Related: **`DWARF033`** is the same hazard when the source is *abstract or an interface*. That one is an
**Error**, not an Info, and the difference is principled — an abstract source is *necessarily* a derived
instance at run time, so the drop is certain, whereas a concrete base may genuinely hold exactly the base type.

Deliberately quiet: it does not fire for a `sealed` source, nor for a non-sealed class that nothing in the
compilation actually derives from.

---

## dwarf072
**Member has a source match but auto-matching is disabled** · Error

The mapper is declared `[DwarfMapper(AutoMatchMembers = false)]` (explicit-only), and this destination member
has a **same-named source member** — so it *would* have auto-wired, but the mode refuses to do it silently.

This is the **trust-boundary / anti-over-posting guard**. By-name auto-matching is how mass assignment
([OWASP API6](https://owasp.org/API-Security/editions/2019/en/0xa6-mass-assignment/)) happens: an
attacker-controlled field (`IsAdmin`, `Balance`, `Id`) that lines up by name with a protected entity member is
copied with no diagnostic — and `DWARF001` never catches it, because the field *is* mapped. Turning
auto-matching off makes every such wire an explicit, reviewable decision.

<!-- fence-exempt: illustrates the trust-boundary configuration DWARF072 guards; a sample would have to fail the build -->
```csharp
[DwarfMapper(AutoMatchMembers = false)]        // explicit-only: nothing auto-wires
[MapIgnore("IsAdmin")]                          // protected — never assigned
public partial class AccountUpdateMapper
{
    [MapProperty(nameof(Input.DisplayName), nameof(Account.DisplayName))]  // allowed
    public partial Account Map(Input input);
}
```

**Fix** — for each member the mapper reports:

| You want | Use |
|---|---|
| this field to be mapped | `[MapProperty(nameof(Src.X), nameof(Dst.X))]` |
| this field to be a fixed/computed value | `[MapValue(nameof(Dst.X), …)]` |
| this field left alone (protected) | `[MapIgnore("X")]` |

Related: `DWARF001` is the *other* completeness case — a destination member that has **no** source at all.
`DWARF072` is specifically "a source exists, but I won't auto-wire it here." Constructor parameters,
`[Flatten]`, and additional mapping parameters still resolve in explicit-only mode — they are already explicit
or structurally required; only the implicit by-name matching of settable members is disabled. See
[`SECURITY.md`](SECURITY.md#over-posting--mass-assignment-guidance-consumer-responsibility).

---

## dwarf073
**`[MapProperty(StringFormat=)]` is not applicable here** · Error

A `StringFormat` was given where it cannot apply. It formats a value into a string —
`source.ToString(format, InvariantCulture)` — so it is valid **only** when the destination member is `string`
and the source implements `System.IFormattable` (`int`, `decimal`, `DateTime`, `Guid`, `TimeSpan`, …), and
**not** alongside `Use=` (the converter already produces the value).

<!-- fence-exempt: attribute fragments shown to contrast StringFormat targets; not a compilable unit -->
```csharp
[MapProperty(nameof(Src.When), nameof(Dst.When), StringFormat = "yyyy-MM-dd")]   // DateTime -> string ✓
[MapProperty(nameof(Src.Amount), nameof(Dst.Amount), StringFormat = "F2")]        // decimal  -> string ✓
```

The provider is always `InvariantCulture`, by design — the formatted output must be stable across deployments
and threads, not shift with the ambient culture (see [`SECURITY.md`](SECURITY.md), the culture-footgun row).
**Fix:** map to a `string` member, drop `Use=`, or remove the `StringFormat`.

---

## dwarf074
**`[MapCollectionKey]` cannot be applied here** · Error

`[MapCollectionKey("Member", "Key")]` merges an update-into collection member by key instead of replacing it,
but the v1 upsert has a defined scope and refuses anything outside it rather than silently replacing:

- the method must be **update-into** (`void Map(TSource, TTarget)`);
- the named member must be a **`List<T>`** on both source and destination;
- the element type must be the **same** on both sides (the common Entity↔Entity update);
- the key must be a **readable member** of the element type.

<!-- fence-exempt: attribute fragment illustrating [MapCollectionKey]; no sample covers update-into merge yet -->
```csharp
[MapCollectionKey(nameof(Order.Lines), nameof(OrderLine.Id))]
public partial void Merge(OrderUpdate src, Order dst);   // dst.Lines merged by Id, not replaced
```

Merged in place: an element whose key matches an existing one updates that slot, a new key is added, and
existing elements the update didn't mention are kept — so the list instance and its untouched elements survive
(contrast `DWARF065`, whole-collection replacement). **Fix:** meet the scope above, or drop the attribute to
accept whole-collection replacement.

---

## dwarf075
**`[FlattenGraph]` leaf member was not flattened** · Warning

A graph node carried a **data-bearing complex leaf** — a nested object, collection, or dictionary member such as
`List<string> Tags` or `Money Price` — that could not be flattened, so the destination member is left at its
default. This is reported only under `ReferenceHandling = Preserve`: there, the helper synthesized for such a
leaf may later be force-marked recursion-capable (three parameters) while the flat-node helper calls it with
one, so emitting the call would not compile. Outside Preserve the leaf **is** flattened normally.

Note this is different from **edge** members (the navigation properties named in `[FlattenGraph(...)]`), which
are deliberately set to `null` — that topology degradation is the whole point of flattening a graph. A *data*
leaf going missing is not, which is why it is now said out loud instead of silently dropped.

**Fix:** map the member explicitly (e.g. a `[MapProperty]` binding or a manual assignment in an `AfterMap` hook),
or use `ReferenceHandling = None` for this mapper if the graph does not need reference preservation.

## dwarf076
**Source and target are the same type** · Warning

A declared create-map maps a type to **itself** — `[GenerateMap<Dto, Dto>]`, or `partial Dto Map(Dto s)` — so the
generated method is just a **shallow copy** of every member. The completeness gate cannot object here: a type
trivially satisfies itself, so every destination member resolves and the map is "complete" and silent. In
practice this is almost always a mistyped type argument (`[GenerateMap<Dto, Dto>]` where the entity was meant),
and the cost of the silence is that the mapper looks wired up while mapping nothing across.

**Deliberately exempt:**

- **Update-into** — `partial void Update(Order src, Order dest)` (or the fluent `partial Order Update(…)`).
  Copying values onto an *existing* instance of the same type is a real pattern (refreshing a tracked entity
  from a detached one), so it is never reported.
- **Auto-synthesized nested pairs** — if `Src.Child` and `Dst.Child` are both `Leaf`, the generator synthesizes a
  `Leaf → Leaf` pair. That reflects the shape of your graph rather than anything you typed, and there would be
  nothing to fix, so it stays quiet.

**Fix:** correct the type argument to the target you meant. If a shallow clone genuinely *is* what you want, say
so by suppressing the id at that site — either `[SuppressMessage("DwarfMapper", "DWARF076:…")]` on the mapper
class, or `dotnet_diagnostic.DWARF076.severity = none` in `.editorconfig`. This is a Warning, so a
warnings-as-errors build still fails until you make that choice explicit.

> **`#pragma warning disable DWARF076` does not work**, and neither does a pragma for any other `DWARF…` id.
> Pragmas are applied by the compiler's diagnostic filtering, which source-generator-reported diagnostics do
> not pass through — a Roslyn limitation rather than something DwarfMapper can honour. Use `[SuppressMessage]`
> for an in-file, next-to-the-code hatch, or `.editorconfig` to change the rule project-wide.

---

## dwarf077
**Explicit-only mapping is not enforced element-wise** · Error

A mapper declared `[DwarfMapper(AutoMatchMembers = false)]` also declares a **span** or **async-stream**
method. Those endpoints do not resolve members themselves — they map the *element* pair through an
auto-synthesized mapper, and explicit-only deliberately does **not** propagate into synthesized mappers
(a synthesized mapper has no `[MapProperty]` to satisfy it, so propagating would make nested objects
unmappable). The result was that one class-level option guarded `.Map` and silently did **not** guard
`.MapSpan` on the same mapper — half a trust boundary, which is how over-posting reaches production. It is
now refused rather than applied selectively.

<!-- fence-exempt: illustrates the DWARF077 gap between endpoints; a sample would have to fail the build -->
```csharp
[DwarfMapper(AutoMatchMembers = false)]
public partial class M
{
    public partial Dst Map(Src s);                              // guarded: DWARF072 per auto-matched member
    public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);  // DWARF077 — not guarded element-wise
}
```

**Fix:** declare a dedicated `[DwarfMapper(AutoMatchMembers = false)]` mapper for the element pair and call
it from your own span/stream loop, so the boundary is enforced where the members are actually resolved. If
this mapper is not a trust boundary, remove `AutoMatchMembers = false` from it and keep the guard on the
mapper that does cross one — see [`SECURITY.md`](SECURITY.md#over-posting--mass-assignment-guidance-consumer-responsibility).

---

## dwarf078
**No code was generated for this mapper** · Warning

Reported once per mapper class that had at least one DwarfMapper **error**. It is not a problem in its own
right — it is a **signpost for the wall of `CS8795` that is about to appear**.

When any diagnostic on a class is an error, the generator emits **nothing at all** for that class. That is the
right call (half-generated code produces worse errors than none), but it means every `partial` mapping method
on the class loses its implementing part simultaneously, and the build fills with
`CS8795: … must have an implementing part`.

That wall is ambiguous, and both readings are common:

| What you see | Actual cause |
|---|---|
| Many `CS8795`, **plus** `DWARF078` and the errors it names | the generator ran and refused — fix the `DWARF…` errors |
| Many `CS8795`, **no** `DWARF…` diagnostics at all | the generator never ran — the project is missing the analyzer reference |

The second case is easy to hit, because DwarfMapper's analyzer is marked `PrivateAssets="all"` and so does
**not** flow transitively: every project that declares mappers needs both the runtime reference and the
generator as an `Analyzer`. See [`MIGRATION.md`](MIGRATION.md).

**Fix:** fix the errors listed in the `DWARF078` message; the `CS8795`s disappear with them. Do not start by
investigating the `CS8795`s — they are a cascade, and there will be one per mapping method regardless of how
many real problems there are.

---

## dwarf079
**[MapIgnore] cannot ignore a required member** · Error

`[MapIgnore("X")]` where `X` is declared `required` on the destination. Ignoring a member means omitting it
from the generated object initializer, and C# refuses that with **`CS9035`** — reported against *generated*
code, with nothing linking it back to the attribute that caused it.

<!-- fence-exempt: illustrates the shape that TRIGGERS DWARF079; a compiling sample would defeat the point -->
```csharp
public class Doc { public required string Id { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class M
{
    [MapIgnore(nameof(Doc.Id))]     // DWARF079 — Id is required
    public partial Doc Map(Src s);
}
```

**Fix — pick the one that matches your intent:**

| You mean | Write |
|---|---|
| "the caller assigns it right after mapping" | `[MapValue(nameof(Doc.Id), "")]` — a placeholder that is immediately overwritten |
| "it comes from somewhere else in the source" | `[MapProperty(nameof(Src.Something), nameof(Doc.Id))]` |
| "it genuinely has no value here" | drop `required` from the member |

The `[MapValue]` route is the usual answer when migrating: a repository or factory sets the real id on the next
line. Say so in a comment — a placeholder that is never observed is fine, one that reaches storage is not.

> **Why this is common in migrating code.** AutoMapper constructed destinations *reflectively*, which bypasses
> the `required` rule entirely, so `.Ignore()` on a required member simply left it `null`. DwarfMapper emits
> ordinary C# and cannot. This was the single most-repeated friction point of a ~300-map migration.

Not reported when the chosen constructor carries `[SetsRequiredMembers]`, or when the member is supplied as a
constructor argument — in both cases the member is already satisfied and ignoring it is legitimate.

---

## dwarf080
**A [MapConstructor] factory cannot assign this member** · Info

The pair names a `[MapConstructor]` factory, and an `init`-only or `required` destination member has a matching
source member. The factory owns construction, so the generator cannot assign that member afterwards
(`CS8852` forbids it) — the mapped source value is **discarded** and the member keeps whatever the factory set.

Only reported when a source member would actually have supplied a value. A factory-owned member that nothing
maps to loses nothing.

<!-- fence-exempt: illustrates the shape that TRIGGERS DWARF080; a compiling sample would need a whole factory + type pair to make one attribute line legible -->
```csharp
// Identifier is init-only, so Create() owns it — and Src.Identifier is silently dropped.
[GenerateMap<Src, Dst>]
[MapConstructor<Src, Dst>(nameof(Create))]   // DWARF080 on Identifier
```

**Fix — prefer the first:**

1. **Bind the constructor parameters instead of naming a factory.** Direct construction fills an *object
   initializer*, and `init` members **are** assignable there:

   <!-- fence-exempt: the contrasting FIX for the shape above; paired with it for legibility -->
   ```csharp
   [GenerateMap<Src, Dst>]
   [MapProperty<Src, Dst>(nameof(Src.Index), "number")]   // binds the ctor PARAMETER
   ```

   This is the better default generally — a factory is also invisible to synthesized element mappers, so a
   collection pair over the same types cannot reuse it.
2. Have the factory take the value as a parameter.
3. `[MapIgnore("X")]` to state that the factory's value is intended. The point is that the choice be
   explicit, not that factories be forbidden.

**Why Info and not Warning.** The generator cannot read the factory body, so it cannot tell a factory that
deliberately supplies its own value from one that forgot — and both are legitimate designs. Escalate with
`dotnet_diagnostic.DWARF080.severity = warning` where the stricter reading is wanted.

> Found twice in one codebase during Round 18 — an entity lost its `Identifier` through an `.Empty` factory
> that minted a fresh `Guid`, and a second map "compiled green but silently dropped Identifier, TotalArguments
> and IsCoreCommand" and had to be backed out.

---

## dwarf081
**The same nested pair is synthesized two different ways** · Info

Two mappers each auto-synthesize a private helper for the same nested `(S, T)` pair, and the two copies do not
agree. A synthesized helper inherits the policy of the mapper that **reached** it, so a class-level option set
on one mapper and not the other produces one pair of types mapped two different ways in one assembly.

Both copies compile. Both are correct in isolation. Nothing else in the build reports it.

<!-- fence-exempt: the point is the two DECLARATIONS differing; a runnable sample would bury one attribute under a type graph -->
```csharp
[DwarfMapper]                                   // Outer -> OuterDto, and Inner -> InnerDto with it
[GenerateMap<Outer, OuterDto>]
public partial class Replace;

[DwarfMapper(SkipNullSourceMembers = true)]     // …and a SECOND Inner -> InnerDto, null-guarded
[GenerateMap<Outer, OuterDto>]
public partial class Patch;                     // DWARF081: the two copies treat Note differently
```

**Fix — pick the one that matches the intent:**

1. **Narrow the differing option to the pair that needs it.** `[MapNullSkip<Inner, InnerDto>]` on both mappers
   makes the nested pair's null policy a property of the *pair* rather than of whichever class reached it, so
   the copies agree again while the classes still differ at the top level.
2. **Declare the pair once and share it** — a partial method, or `[GenerateMap]` on one mapper — so there is
   only one mapping to disagree about.
3. **Accept it deliberately**, and say so where the two mappers are declared.

The message names both mappers, the pair, and the member whose treatment differs — the helper itself is private
and generated, so naming *that* would send you to code you never wrote.

**Why Info and not Warning.** Two mappers configured differently is a design, not a defect. And the reported
divergence is detected by comparing the generated *models*, so it fires for a difference from any cause —
`NullStrategy`, `CaseInsensitive`, `EnumStrategy`, a converter reserved in one mapper and not the other — not
only for the options someone thought to enumerate.

> Found in Round 18. `SkipNullSourceMembers` was class-scoped, a profile mixing patch-merge maps with ordinary
> ones had to be split into two mapper classes, and the split silently produced one null-guarded and one
> unguarded copy of the same nested pair — "a real behavioural difference, not a cosmetic one", because the
> store could deserialize nulls into those members. `[MapNullSkip]` now removes the reason for the split; this
> reports the consequence if it happens for any other reason.

---

## dwarf084
**[RestatesBase] cannot identify the base pair** · Error

`[RestatesBase<TSource, TTarget>]` declares that a pair restates the configuration of the pair for their base
types, so the two can be checked against each other. It changes no emitted code — it exists so `DWARF085` has
something to compare. This fires when there is nothing to compare against:

| Cause | Fix |
|---|---|
| the named pair is not declared on this mapper | add `[GenerateMap<TSource, TTarget>]` (or a partial method), or fix the type arguments |
| no base pair is declared | declare the base pair on the same mapper, or drop the attribute |
| two base pairs sit at the same depth | declare the one you mean and remove the other |

**Fix:** whichever row above applies — the message names the case.

A base pair is one whose **source is a base class of `TSource`** and whose **target is a base class of
`TTarget`**, declared on the same mapper. Only base classes are walked: an interface base has no single
distance, so "the nearest one" would be a coin toss — and a coin toss deciding which configuration a drift
check compares against is worse than refusing.

> **Why refused rather than skipped.** The author asked for a check. A check that silently does not run is
> precisely the drift risk they were guarding against — the same reasoning as `DWARF082`.

---

## dwarf085
**Restated base configuration has drifted** · Warning

A pair declared with `[RestatesBase]` no longer maps a member the way its base pair does — either it maps it
**differently**, or it does not map it **at all**.

<!-- fence-exempt: the drift is the difference between two DECLARATIONS; a runnable sample would bury it -->
```csharp
[GenerateMap<Command, CommandDto>]
[MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]

[GenerateMap<AliasCommand, AliasCommandDto>]
[RestatesBase<AliasCommand, AliasCommandDto>]
[MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]  // DWARF085: no Use=
```

**Fix:**

1. **Restate the base configuration here.** The mechanical answer, and usually the right one.
2. **`[RestatesBase<S, T>(Overrides = new[] { "Member" })]`** if the difference is intended. An override is a
   decision, and a decision should be visible where it is made — listing the member exempts exactly that one
   and leaves every other member guarded.

### Why this exists instead of `IncludeBase`

DwarfMapper has no inheritance primitive, deliberately: every pair's configuration stays literally visible at
its own declaration, so a reader of one pair never has to go and find what some other pair decided on its
behalf. The cost is restatement, and that cost splits in two — **typing it** is mechanical, annoying and over
once; **drifting from the base later** is silent, and it only ever drifts toward wrong data.

This closes the second half without introducing override semantics that would have to interact with
pair-scoped attributes, `[MapDerivedType]` and the policy layer. See `Issues/Rount18/Decisions.md` §D4.

### What is compared

The **resolved mappings**, not the attribute text. That is what catches a restatement which is present but no
longer does the same thing — the case a `MIRRORS BASE` comment convention cannot see, and the convention a
real migration invented for itself before this existed.

Auto-matched members are compared too and agree trivially. A member the derived target **redeclares with a
different type** (`new`) is exempt: two different members wearing one name, whose mappings are supposed to
differ.

> Round 18 restated base configuration by hand at 15 sites in one project, 5 in another and 6 in a third.

---

## dwarf082
**[ProvidesMap] method cannot be registered** · Error

`[ProvidesMap]` marks a **hand-written** method for registration into the ambient registry, so a shape the
generator cannot express is still reachable through `IDwarfMapper`. The registry holds every map as a
`Func<object, object>`, so the method must match that shape:

| Requirement | Why |
|---|---|
| `public` | the registration is called from generated code in the same assembly, and the pair is resolvable from any other |
| exactly one parameter | the registry passes one source value |
| returns a value | a `void` method has nothing to hand back |
| both types publicly nameable | a cross-assembly consumer has to be able to name the pair |

Static and instance methods are both fine — a static one is invoked on the type and needs no cached instance.

**Fix:** adjust the signature, or remove the attribute if the method is a helper rather than a map.

> **Why this is refused rather than skipped.** The author asked for a registration. Dropping a mis-shaped one
> quietly would leave every facade call site for that pair throwing `DwarfMapMissingException`, with nothing
> to explain why — which is the exact failure `[ProvidesMap]` exists to prevent.

### When to reach for `[ProvidesMap]`

Some conversions are legitimately not mapping *shapes*. The common one is an object that **holds** a
collection, mapped to the collection itself — one side is a container, the other an element sequence:

<!-- fence-exempt: the shape is the point; a fuller sample would bury it -->
```csharp
[ProvidesMap]
public ICollection<QuoteData> ToQuotes(UserQuotesDocument document) =>
    document.Quotes.Select(Map).ToList();     // delegates to the generated element map
```

Without the attribute this is an ordinary method: nothing registers it, so `mapper.Map<ICollection<QuoteData>>(doc)`
throws and a parity harness reports the pair as "not registered" even though the code is correct. A real
migration hit that on five pairs and recorded it as *"the code is fine; the harness cannot see it."*

Note the registry does **not** wrap your method — it calls it. A hand-written map carries its own argument
guards, unlike a generated one.

---

## dwarf083
**Enum maps to strings that are not its member identifiers** · Info

A declared enum↔string mapping involves an enum whose string form is redirected by
`[EnumMember(Value = "…")]` or `[Description("…")]`. Those are the values that will be **written and read**.

The precedence is deliberate and useful — it is how `InProgress` serializes as `"in_progress"` with no custom
converter. This exists because of the case where it is *not* what anyone intended:

<!-- fence-exempt: shows the hazard; a "correct" version would not demonstrate it -->
```csharp
public enum DispatchChannel
{
    [Description("Next-Day")] NextDay,   // put here for a combo-box label…
    Standard
}
```

`[Description]` is overwhelmingly a **display** annotation. Here it becomes the **persistence** format: the
map writes `"Next-Day"` where a store built by a previous mapper's `.ToString()` holds `"NextDay"` — and the
string→enum direction stops parsing the existing values for the same reason.

**Fix — pick the one that matches your intent:**

| You mean | Do |
|---|---|
| the attribute IS the wire format | nothing; this is working as intended |
| the attribute is for display only | remove it from the members you persist, or map through `[MapProperty(Use = …)]` with a converter that returns the identifier |

Reported **once per enum**, naming the first few divergent members and the total, because an enum annotated
for display usually annotates most of its members.

Not reported for `[Flags]` enums: their string form is the comma-joined list `Enum.ToString` builds from
identifiers, so the attributes do not apply.

> Round 18 came within one code review of shipping the `Next-Day` case into a live MongoDB collection.

---

## dwarf086
**Manifest attribute is emitted by the generator** · Error

`[assembly: DwarfProvidesMap(…)]` and `[assembly: DwarfRequiresMap(…)]` are **output, not input**. The
generator writes one entry per map an assembly registers or consumes, and the `[DwarfMapperValidationRoot]`
compilation reads those entries back out of referenced metadata to decide [`DWARF061`](#dwarf061). Writing one
by hand is therefore not a shortcut — it is an assertion about this assembly that nothing checked:

<!-- fence-exempt: the shape IS the error; a compiling sample cannot demonstrate a refusal -->
```csharp
[assembly: DwarfProvidesMap(typeof(Legacy), typeof(Modern))]   // DWARF086
```

That row satisfies a `DwarfRequiresMap` row for a pair no assembly actually registers, so the whole-graph
check passes and the failure moves back to the first call site at run time (`DwarfMapMissingException`) —
which is precisely what `DWARF061` exists to pull forward.

**Fix — declare the thing itself and let the generator write the manifest:**

| You meant | Write this instead |
|---|---|
| this assembly **consumes** a cross-assembly map | `[assembly: UsesMap<TSource, TDestination>]` — or nothing at all, since a direct `IDwarfMapper.Map<…>` call site is detected automatically |
| this assembly **provides** a map | declare the map: `[GenerateMap<TSource, TTarget>]` on a `[DwarfMapper]` class, or `[ProvidesMap]` on a hand-written method |
| I was mirroring a referenced assembly's manifest | delete it; the root reads that assembly's own metadata directly |

Only **hand-written** occurrences are refused. The generator's own emission arrives in a `.g.cs` file and is
recognised as its own, so an ordinary multi-assembly build never sees this diagnostic.

> **Why this is refused rather than ignored.** Ignoring it would still leave the attribute reading, to the
> next person, like a supported way of declaring a map — and the one thing it is guaranteed not to do is make
> the map exist.

---

## dwarf087
**Duplicate `[FlattenGraph]` destination collection** · Error

`[FlattenGraph]` is `AllowMultiple`, so one method may flatten **several** graphs — but each directive
contributes its own initializer for the collection it names, and two directives naming the **same** collection
therefore assign it twice:

<!-- fence-exempt: the shape IS the error; a compiling sample cannot demonstrate a refusal -->
```csharp
[FlattenGraph("Entry", "Nodes")]
[FlattenGraph("Entry", "Nodes")]   // DWARF087 — 'Nodes' is filled twice
public partial RootDto Map(Root r);
```

Before this diagnostic existed the generator **accepted** that shape and emitted
`new RootDto { Nodes = …, Nodes = … }`, so the consumer's build failed with
`CS1912: Duplicate initialization of member 'Nodes'` — pointing at `Demo.M.g.cs`, a generated file they never
wrote and cannot edit. That is why this is an `Error` rather than a `Warning`: there was no version of the
program that built.

**Fix:**

| You meant | Do |
|---|---|
| the second directive is a copy-paste | delete it |
| flatten a second graph as well | name a **different** collection member on the destination type, as [`FlattenGraph`](options.md) intends |
| merge two navigations into one collection | not supported; give each its own collection, or flatten one and map the other with `[MapProperty]` |

Keyed on the **destination collection**, not on the two directives being character-identical:
`[FlattenGraph("Entry", "Nodes")]` beside `[FlattenGraph("Other", "Nodes")]` produced the same `CS1912` from
two directives that are not duplicates of each other at all. Both shapes are refused.

> **Refused rather than collapsed**, matching [`DWARF011`](#dwarf011) on `[MapProperty]`. Silently keeping one
> of the two would hide a copy-paste mistake from the only person able to fix it — and if the intent was the
> second directive rather than the first, the collapse would quietly pick the wrong one.

---

## dwarf088
**Member-placement directive written on a mapper** · Warning

`[MapProperty]` and `[MapIgnore]` each cover **two placements** behind one name, and each placement has its
own constructor. The member form belongs on a member of a type that declares its own mapping — a `[MapTo]`
source, a `[GenerateMap]` host — where "the annotated member" is a real thing:

<!-- fence-exempt: the shape IS the diagnostic; a compiling sample cannot demonstrate a refusal -->
```csharp
[MapProperty("Name")]              // DWARF088 — member form: binds 'Name' to itself
[MapIgnore]                        // DWARF088 — member form: names nothing to ignore
public partial Target Map(Source s);
```

On a mapper class or a mapping method there **is** no annotated member: the mapping is declared by the method
and the DTO pair is two ordinary types. Both directives were previously discarded without a word.

**Fix — supply the argument the method form takes:**

| You wrote | You meant | Write |
|---|---|---|
| `[MapProperty("Name")]` | map a source member to a differently-named destination | `[MapProperty("Name", "FullName")]` |
| `[MapProperty("Name", Use = nameof(F))]` | convert a member with `F` | `[MapProperty("Name", "Name", Use = nameof(F))]` |
| `[MapIgnore]` | exclude a destination member from `DWARF001` | `[MapIgnore("Extra")]` |

The `[MapProperty]` half is the sharper of the two, because the named arguments ride on that **same**
one-argument constructor: `[MapProperty("Name", Use = "F")]` is `ctor(1)` plus a property initializer, so the
converter, the `When` predicate, the `NullSubstitute` and the `StringFormat` were discarded along with the
binding. A caller named a conversion method and got auto-matching.

> **Refused whichever way you read it.** Honouring `[MapProperty("Name")]` at a method would bind `Name` to
> itself — the identity binding auto-matching already produces, so it is a no-op by construction and cannot be
> what the caller wanted. Discarding it evaporates a binding they wrote explicitly. Only saying so lets them
> fix it.

> **Why a Warning here and an `Error` for its registry mirror `DWARFR04`.** A blocking DwarfMapper error
> suppresses the whole class's emission, so the partial mapping method loses its implementing part and the
> refusal arrives buried under a wall of `CS8795` (see [`DWARF078`](#dwarf078)). A warning states it where you
> can act on it and still hands you a mapper that builds. The registry emits free-standing extension methods
> and has no partial declaration to strand, so it refuses outright. Escalate with
> `dotnet_diagnostic.DWARF088.severity = error` where the stricter reading is wanted.

---

## dwarf089
**Directive on a co-located host member cannot be applied** · Warning

The inverse of [`DWARF088`](#dwarf088). A co-located `[GenerateMap<S, T>]` host **declares its own mapping**,
so a member of the host *is* part of that declaration and takes the **member** form. The host is the
destination, so the one argument names the **source** member it is filled from — the mirror image of the
`[MapTo]` registry, where the annotated type is the source and the argument names the destination.
`[MapProperty("Full")]` on a `Name` member means *`Name` comes from `Person.Full`*; a bare `[MapIgnore]` means
*never assign this member*. Anything else acts on nothing:

<!-- fence-exempt: the shape IS the diagnostic; a compiling sample cannot demonstrate a refusal -->
```csharp
[GenerateMap<Person, PersonDto>]
public sealed class PersonDto
{
    [MapProperty("Full", "Name")] public string Name { get; set; } = "";  // DWARF089 — method form
    [MapIgnore("Age")]            public int Age { get; set; }            // DWARF089 — names it twice
}
```

Refused rather than dropped, in all four shapes:

| You wrote, on a host member | Why it cannot apply | Write |
|---|---|---|
| `[MapProperty("Full", "Name")]` | the method form names a source *and* a destination; here the destination is the annotated member | `[MapProperty("Full")]` |
| `[MapIgnore("Age")]` | the method/class form names what to exclude; here that is the annotated member | `[MapIgnore]`, or `[MapIgnore("Age")]` on the **host class** |
| two directives, one declared pair | stacked directives bind **positionally**, one per pair in source order | one directive (it applies to every pair), or exactly one per pair |
| any member directive on a host that is not the destination of a pair it declares | its members are part of no mapping | move it to the destination type, or use `[MapProperty<TSource, TTarget>]` / `[MapIgnore<TTarget>]` on the class |

The named arguments ride on that same one-argument constructor, so `Use`, `When`, `NullSubstitute` and
`StringFormat` are carried with the binding — and were discarded with it before this check existed.

> **Why a Warning.** A blocking error suppresses the whole host's emission, so the generated `<Host>Mapper`,
> its convenience extension and its DI registration all vanish and every call site meets `CS1061` instead of
> the refusal — the same reasoning as [`DWARF088`](#dwarf088). The offending directive is dropped and the rest
> of the host's mapping is emitted as though it had not been written. Escalate with
> `dotnet_diagnostic.DWARF089.severity = error` where the stricter reading is wanted.

---

## dwarf090
**Member directive is not applied element-wise** · Warning

The generalization of [`DWARF077`](#dwarf077), and the same root cause. A **span map** and an **async-stream
map** resolve no members of their own: they map the *element* pair through a mapper the generator synthesizes
per `(source, target)`, and that mapper is shared by every route which reaches that pair. So it takes its
configuration only from directives that **name the pair**. A directive written without a pair scope belongs to
the declaration it sits on, and never arrives:

<!-- fence-exempt: the shape IS the diagnostic; a compiling sample cannot demonstrate a refusal -->
```csharp
[DwarfMapper]
[MapIgnore("Id")]                                  // reaches Map and Update, not MapSpan — DWARF090
public partial class M
{
    public partial Dst Map(Src s);                 // Id excluded here

    [MapProperty("Id", "Name")]                    // DWARF090 — dropped element-wise
    public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
}
```

Before this check existed, one mapper excluded a member on three of its overloads and copied it on the other
two, saying nothing — surface-matrix findings `D1` and `D2`.

**Fix:** write the directive in its **pair-scoped** form on the mapper class. Those forms *are* matched against
every synthesized pair, this element pair included, and are measured **applying** at both endpoints:

| You wrote | Write instead | What the matrix measures for the replacement |
|---|---|---|
| `[MapIgnore("Id")]` on the method or the class | `[MapIgnore<Dst>("Id")]` on the class | `Honoured` |
| `[MapProperty("Id", "Name")]` on the method | `[MapProperty<Src, Dst>("Id", "Name")]` on the class | `Refused` — the rename **is** applied, and the added diagnostic is `DWARF038` about the `int → string` conversion that results. The classifier tests for a new diagnostic before it compares output, so an applied-and-warned cell reads the same as a refused one (see [B19](../Issues/round20/TASKS.md)) |

> **Why refused rather than propagated.** For the reason [`DWARF077`](#dwarf077) already states: the
> synthesized element mapper is keyed by `(source, target)` and shared. Pushing one method's unscoped directive
> into it would silently re-configure a nested mapping some **other** method owns — a worse defect than the
> silence, and invisible from the declaration that caused it. The pair-scoped forms exist precisely so the
> caller can say which pair they mean.

> **Why a Warning.** A blocking error suppresses the whole class's emission, so every partial mapping method on
> it loses its implementing part and this refusal arrives buried under a wall of `CS8795` (see
> [`DWARF078`](#dwarf078)) — the same reasoning as [`DWARF088`](#dwarf088). The directive is dropped for this
> endpoint and the rest of the mapper is emitted. Escalate with
> `dotnet_diagnostic.DWARF090.severity = error` where the stricter reading is wanted.

---

## dwarf091
**Mapping hook on a partial method with no body** · Warning

`[BeforeMap]` and `[AfterMap]` mark a method **you** wrote to run around the mapping. Written on a **partial
mapping method** they mark a method whose body the generator writes, so there is no code of yours there to run:

<!-- fence-exempt: the shape IS the diagnostic; a compiling sample cannot demonstrate a refusal -->
```csharp
[DwarfMapper]
public partial class M
{
    [AfterMap]                                          // DWARF091 — no body to run
    public partial void Update(Src s, Dst d);
}
```

C# **erases** a partial method with no implementing part, along with every call to it — so as a hook it can
only ever be a no-op. And where DwarfMapper supplies the missing part, the call is into the method being
generated. Neither is what `[AfterMap]` means, at any endpoint, which is why this is refused before the
signature is considered at all.

Until it was, the signature filter alone decided, and gave three different answers to one mistake:

| Written on | What used to happen |
|---|---|
| `Dst Map(Src s)` | [`DWARF018`](#dwarf018) complained the hook was not `void` — a signature complaint about a signature that was never the problem |
| `void MapSpan(ReadOnlySpan<Src>, Span<Dst>)` | fitted the two-parameter after-hook shape exactly, was registered, and was **never called** |
| `void Update(Src, Dst)` | fitted **and was called**: the generated body of `Update` ended in `Update(s, d);` — unconditional **infinite recursion**, which compiled |

**Fix:** move the attribute to an ordinary method on the mapper — one with a body — whose parameters are the
mapped types: `void Hook(TSource)` for `[BeforeMap]`, `void Hook(TTarget)` or `void Hook(TSource, TTarget)` for
`[AfterMap]`. Or remove it, if the mapping method was the fixup you meant.

> **Why a Warning.** An error would strand every partial mapping method on the class behind `CS8795`, the same
> reasoning as [`DWARF088`](#dwarf088). The hook is not registered — which is what you already had at three of
> the five endpoints, minus the recursion at the fourth — and the mapper is emitted. Escalate with
> `dotnet_diagnostic.DWARF091.severity = error` where the stricter reading is wanted.

---

## Runtime exceptions

The diagnostics above are **compile-time**. A generated mapper is **strict at runtime for conversions**: rather
than silently truncating or defaulting, out-of-range or malformed input **throws** — there is no lenient /
non-throwing mode. **DwarfMapper is not an input-validation layer — validate untrusted input (request bodies,
external data) before you map it.**

> **"Strict" is not "total" — a few paths still lose data silently** (no throw, no diagnostic): an in-range but
> **undefined** `integral → enum` value yields an undefined enum; a `Dictionary<K,V>` target is built
> last-writer-wins, so a **lossy key conversion drops entries**; and a `HashSet<T>`/set target **de-duplicates**.

The throws you can encounter:

| When | Exception | Notes |
|---|---|---|
| `null` passed to a **public** map method (or update-into `void Map(S,T)`) | `ArgumentNullException` | public entry points open with `ArgumentNullException.ThrowIfNull(source)` (synthesized nested mappers do not) |
| A **null source member → non-nullable target** under the default `NullStrategy.Throw` | `InvalidOperationException` | the most common case (`int? → int` when null); also a null **collection element**, a null **dictionary entry**, or a null reference-source mapped to a **value-type** target. Avoid with `NullStrategy.SetDefault`, `[MapProperty(NullSubstitute=…)]`, or a nullable target |
| Numeric **narrowing** out of range (`long→int`; `enum`↔integral overflow) | `OverflowException` | `CreateChecked` — never silent truncation |
| `string → int/Guid/DateTime/…` on unparseable input | `FormatException` (or `OverflowException`) | `Parse(…, InvariantCulture)`; there is **no `TryParse`/lenient mode** |
| `string → enum` unrecognized name, or `enum → enum` **by-name** with an undefined/cast source value | `ArgumentOutOfRangeException` | the generated `switch` has a throwing default — these are **not total** (`DWARF015` only checks *declared* members) |
| Nullable **interior hop** on a deep source path (`"Customer.Name"`, `Customer` null) | `NullReferenceException` | only `DWARF044` (Info) at build; a raw NRE at runtime |
| `[Flatten]` root is `null` | `NullReferenceException` | no diagnostic — guard the root or accept the risk |
| `[MapDerivedType]` dispatch hits a runtime subtype with no registered arm | `ArgumentException` | "no `[MapDerivedType]` registered for runtime type …" |
| Span/destination too small (span overloads) | `ArgumentException` | |
| Cycle under the default depth guard (no `ReferenceHandling`) | `DwarfMappingDepthException` | catchable; carries `MaxDepth`/`ActualDepth` (never a `StackOverflow`) |
| Ambient `IDwarfMapper.Map<T>(src)` with no registered provider (e.g. provider assembly not yet loaded) | `DwarfMapMissingException` | `DWARF061` proves the reference **graph** at build, not runtime **load order** — see [ambient maps](howto/ambient-cross-assembly-maps.md) |
| `DwarfMap.Validate()` finds an unregistered required pair (call it explicitly, via `services.AddDwarfMappers().ValidateDwarfMaps()`, or automatically with `[DwarfMapperValidationRoot(AutoValidate = true)]`) | `DwarfMapValidationException` | reflection-free fail-fast listing the missing pairs |

**Handling.** `DwarfMappingDepthException`, `DwarfMapMissingException`, and `DwarfMapValidationException` are the
DwarfMapper-typed exceptions you catch by type; the rest are standard BCL types (`InvalidOperationException`,
`OverflowException`, `FormatException`, `ArgumentOutOfRangeException`, `ArgumentException`, `NullReferenceException`).
For untrusted input, validate before mapping (or map into a string-shaped DTO and validate after) — do not rely on
the mapper to reject bad data gracefully.
