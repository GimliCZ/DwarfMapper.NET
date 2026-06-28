<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper diagnostics reference

Every DwarfMapper diagnostic (`DWARF001`–`DWARF060`) is listed here with what triggers it and how to
fix it. The IDE "learn more" link on each build error points at the matching `#dwarfNNN` anchor below.

## How severities and suppression work

| Severity | Meaning | Can I turn it off? |
|---|---|---|
| **Error** | The mapping is incomplete or the configuration is invalid — the build fails. | No global override. Resolve it (often a one-line `[MapIgnore]`/`[MapProperty]`/`[MapValue]`). |
| **Warning** | The configuration compiles but something was skipped or won't behave as you might expect. | `dotnet_diagnostic.DWARFxxx.severity = none` in `.editorconfig`. |
| **Info / suggestion** | Visible in the IDE, never build-breaking — surfaces a footgun without forcing a change. | Suppress with `.editorconfig`, or escalate to `error` there. |

The headline rule, **`DWARF001` (completeness)**, is an **Error by design with no severity override** — see
[`CORRECTNESS.md`](CORRECTNESS.md). Everything else is suppressible or escalatable through `.editorconfig`,
e.g.:

```ini
# .editorconfig — make an opt-in suggestion strict, or silence a known-safe one
dotnet_diagnostic.DWARF039.severity = error   # require every source member to be consumed
dotnet_diagnostic.DWARF044.severity = none    # I accept the nullable-path risk here
```

`DWARF004`, `DWARF006`, `DWARF019`, and `DWARF029` are retired/reserved ids and are never emitted.

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

The destination type has several constructors tied for the most parameters. **Fix:** mark the intended one
with `[DwarfMapperConstructor]`.

## dwarf026
**No mappable constructor** · Error

The destination type has no accessible, non-obsolete instance constructor to map into. **Fix:** add one, or
map to a different type.

## dwarf027
**Unsupported collection/dictionary target type** · Error

The collection/dictionary target type isn't supported. **Fix:** use a supported target (`T[]`, `List<T>`,
`HashSet<T>`, `Dictionary<K,V>`), supply `[MapProperty(Use = ...)]`, or map it manually.

## dwarf028
**Projection member cannot be translated to a database query** · Error

An `IQueryable` projection becomes an expression tree your database/ORM provider translates into a query. A
member that needs a runtime conversion, a custom converter, a collection rebuild, or reference handling has no
query equivalent. The build error names the specific reason (narrowing, parse, by-name, converter, collection,
hook, reference handling, …). **Fix:** map those members with a runtime mapper (an ordinary `Map` method)
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
**Implicit type conversion applied** · Info (escalates to Error)

A non-lossless conversion (narrowing, parse/format, cross-category numeric) is being applied. It's visible, not
silent. **Fix (optional):** make it explicit with `[MapProperty(Use = nameof(...))]`. Set
`[DwarfMapper(ImplicitConversions = false)]` to turn all such conversions into build errors.

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
**[MapProperty] source path traverses a nullable member** · Info

A dotted source path passes through a nullable member, which can throw `NullReferenceException` at runtime.
**Fix (optional):** guard the value, or suppress with `.editorconfig` if you know it's non-null here.

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

A class-level pair-scoped attribute — `[MapProperty<TSource, TTarget>(…)]` or `[MapIgnore<TTarget>(…)]` —
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
