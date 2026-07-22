<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper diagnostics reference

Every DwarfMapper diagnostic (`DWARF001`–`DWARF074`) is listed here with what triggers it and how to
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
category `DwarfMapper.Registry`, distinct from the `[DwarfMapper]` class-model `DWARF###` codes below:

| Code | Meaning & fix |
|---|---|
| `DWARFR01` | **Invalid `[MapTo]` target** — the target type isn't a mappable class/struct. |
| `DWARFR02` | **Destination member is not mapped** — the registry's completeness gate (the `[MapTo]` counterpart of `DWARF001`). Add a source member, a `[MapProperty]` binding, or drop it. |
| `DWARFR03` | **Conflicting sources for one destination member** — more than one source claims it; give them distinct positional `[MapProperty]` names. |
| `DWARFR04` | **`[MapProperty]` value count doesn't match the targets** — supply one value (all targets) or exactly one per `[MapTo]` target, in order. |
| `DWARFR05` | **No conversion between mapped members** — the member types are incompatible; use the `[DwarfMapper]` class model for a custom `Use=` converter. |
| `DWARFR06` | **Recursive nested mapping is not supported by the registry** — the front door threads no reference context; use the `[DwarfMapper]` class model (`ReferenceHandling`/`OnCycle`) for cyclic graphs. |
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
