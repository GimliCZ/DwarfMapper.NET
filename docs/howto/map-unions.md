<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: map C# 15 unions

C# 15 (.NET 11) adds union types: `union Pet(Cat, Dog)` is a value that holds a `Cat` or a `Dog`. DwarfMapper maps
one union into another case by case, at compile time, under the same rule as everything else it generates: nothing is
dropped silently, and nothing is paired by guesswork.

The examples on this page are the runnable program in
[`samples/DwarfMapper.Unions`](../../samples/DwarfMapper.Unions) — `cd samples/DwarfMapper.Unions` and `dotnet run`.

## What you need

- **The .NET 11 SDK** (or Visual Studio 18.12 or newer). Its compiler, Roslyn 5.12, is the first whose API says what a
  union is, so the DwarfMapper package carries its generator twice: built for Roslyn 5.0 (every .NET 10 SDK) in
  `analyzers/dotnet/roslyn5.0/cs`, and for Roslyn 5.12 in `analyzers/dotnet/roslyn5.12/cs`. The SDK loads exactly one
  of them — you do nothing.
- **C# 15.** It is the default for `net11.0`. A union map is made of union conversions and pattern matches over a
  union's contents, so a project compiled as C# 14 gets DWARF113 instead of code that would not compile.
- With an older compiler, every mapping that involves a union is refused with **DWARF113**, which says what is missing.

## One union into another

Declare the union map, and a map for each case:

<!-- snippet: union-map -->
```csharp
[DwarfMapper]
public partial class PetMapper
{
    public partial PetDto ToDto(Pet pet); // a switch over Pet's cases

    public partial CatDto ToDto(Cat cat); // pairs the Cat case with CatDto

    public partial DogDto ToDto(Dog dog); // pairs the Dog case with DogDto
}
```
<!-- endsnippet -->

The generated `ToDto(Pet)` is a `switch` over `Pet`'s case types, most-derived first. Each arm produces one of
`PetDto`'s case types, and the language's own union conversion wraps it into a `PetDto`.

## How a case finds its target

A source case is paired, in this order:

1. by an explicit `[MapDerivedType<Case, TargetCase>]` arm on the union map;
2. by the **same type** among the target's cases — the value is passed through, the same instance;
3. by **exactly one declared method** from the case type to one of the target's case types.

Nothing else pairs a case. Two records that both have a `Name` are not evidence that they mean the same thing, so a
case is never paired by its members or its name. A case nothing pairs — or a case two declared methods could each
pair — is **DWARF114**, naming it and both remedies.

<!-- snippet: union-explicit-arms -->
```csharp
[DwarfMapper]
public partial class MessageMapper
{
    [MapDerivedType<Email, EmailView>]
    [MapDerivedType<Sms, SmsView>]
    public partial MessageView ToView(Message message); // Ticket: the same type in both unions, passed through
}
```
<!-- endsnippet -->

A pair an arm names is mapped like any nested pair: here member by member, with no method of its own.

## Value-type cases

An `int` case is not a `long` case, so the pair is named. The generic `[MapDerivedType<,>]` is for classes; use the
`typeof` form:

<!-- snippet: union-value-cases -->
```csharp
[DwarfMapper]
public partial class ReadingMapper
{
    [MapDerivedType(typeof(int), typeof(long))]
    public partial ReadingDto ToDto(Reading reading);
}
```
<!-- endsnippet -->

## Unions inside other types

A union member, a list, array or dictionary of unions, a `Nullable<T>` union member and an update-into member all map.
With no union map declared, the generator builds a private one from the case maps; a case-typed member assigned into a
union member is the language's own union conversion:

<!-- snippet: union-members -->
```csharp
[DwarfMapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order order); // Paid, Refunds and Preferred go through PaymentDto's cases

    public partial CardDto ToDto(Card card);

    public partial TransferDto ToDto(Transfer transfer);
}
```
<!-- endsnippet -->

Under `ReferenceHandling = Preserve` the case values keep their identity through cycles, and `OnCycle = SetNull`
cuts a cycle that runs through a case value. The union itself is a value: it has no identity of its own to keep.

## The empty union

A union struct can be empty — `default(Shape)`, its `Value` null. An empty source maps to an empty target, and a
nullable union member keeps "no value" apart from "an empty union":

<!-- snippet: union-empty -->
```csharp
[DwarfMapper]
public partial class ShapeMapper
{
    public partial ShapeDto ToDto(Shape shape); // default(Shape) -> default(ShapeDto)

    public partial SketchDto ToDto(Sketch sketch); // Outline: null -> null, empty -> empty

    public partial CircleDto ToDto(Circle circle);

    public partial SquareDto ToDto(Square square);
}
```
<!-- endsnippet -->

A class union (one written by hand, with `[Union]`) as the destination: an empty source maps to `null` when the
destination is nullable. When it is not, the map has no arm for an empty source when none of the source's case types
is nullable — the switch is exhaustive, and an empty source throws as the same switch written by hand does — and is
**DWARF113** when one of them is.

## What is refused, and why

| Diagnostic | When |
|---|---|
| `DWARF113` | A compiler older than Roslyn 5.12; a C# 14 project; a union in a projection (`IQueryable`); a union into or out of a type that is neither a union nor one of its cases (`Pet` → `object`); a union with no case types; a union map declared between nullable unions (declare it between the union types); an empty source into a non-nullable class union. |
| `DWARF114` | A source case no rule pairs with a target case, or two declared methods could pair. |
| `DWARF035` | An explicit arm whose source is no case type of the source union, or whose target does not convert to the target union. |

`[MapTo]` maps no union pair: the same union is copied, and a case type converts into its union under C# 15. Map
anything else in a `[DwarfMapper]` class. Full texts: [`diagnostics.md`](../diagnostics.md).
