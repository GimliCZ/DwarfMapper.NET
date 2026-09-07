<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The DwarfMapper Gallery, illustrated

Every example on one page, with its core code inline. The examples themselves live beside this
file as runnable C#; `dotnet run --project samples/DwarfMapper.Gallery` executes all of them.

**This page is GENERATED.** Every code block below is quoted from the example that runs, so a
block cannot drift from the code it documents — the snippet injector fails the build if an id
here names no region, and the orphan rule fails it if a region is quoted by nothing. Editing the
fences by hand is pointless: they are overwritten. Edit the example.

It exists because the orphan rule went STRICT in round 27. Before that, a region inside a
declared example was exempt from needing a quoting document, and 32 of them were quoted nowhere.
The alternative to this page was deleting those markers — removing evidence to satisfy a guard.

## Index

<!-- table: gallery-index -->
| # | Example | Shows |
|---|---|---|
| | **Basics** | |
| 01 | [`01_FlatMap.cs`](01_FlatMap.cs) — Flat map | the simplest map — `[GenerateMap<A,B>]`, same names and types |
| 02 | [`02_Rename.cs`](02_Rename.cs) — Rename a member | `[MapProperty(nameof(...), nameof(...))]` |
| 03 | [`03_BuiltInConversions.cs`](03_BuiltInConversions.cs) — Built-in conversions | automatic widening and enum-by-name |
| 04 | [`04_Nested.cs`](04_Nested.cs) — Nested objects | auto-nesting a nested `(S,T)` pair |
| 05 | [`05_Collections.cs`](05_Collections.cs) — Collections | lists and arrays, element-by-element and bulk copy |
| | **Configuration** | |
| 06 | [`06_DeepPaths.cs`](06_DeepPaths.cs) — Deep dotted paths | a dotted source path — what others reach with a lambda |
| 07 | [`07_Flatten.cs`](07_Flatten.cs) — Flatten | `[Flatten]` lifts sub-members to the top level |
| 08 | [`08_CustomConversion.cs`](08_CustomConversion.cs) — Custom conversion | `Use = nameof(Method)` — the method body is the "lambda" |
| 09 | [`09_ConditionalAndValue.cs`](09_ConditionalAndValue.cs) — Conditional and constant values | `When=`, `NullSubstitute=`, and `[MapValue]` |
| 10 | [`10_RecordTarget.cs`](10_RecordTarget.cs) — Immutable record target | constructor binding into a record with no parameterless ctor |
| 11 | [`11_Projection.cs`](11_Projection.cs) — IQueryable projection | the one place a `Select` lambda is generated for you |
| 12 | [`12_Ergonomics.cs`](12_Ergonomics.cs) — Extension method and DI | the generated `x.ToGemDto()` and `AddDwarfMappers()` |
| 13 | [`13_NestedListConfig.cs`](13_NestedListConfig.cs) — Configure a collection-element map | renaming a member of the element type inside a `List<T>` |
| 14 | [`14_NestedListConfigErgonomic.cs`](14_NestedListConfigErgonomic.cs) — The same, with no partial methods | pair-scoped `[MapProperty<S,T>]` on the class carries the nested rename |
| 40 | [`40_BeforeAfterHooks.cs`](40_BeforeAfterHooks.cs) — Before-map hook | running your own code before a map, matched by source type |
| 41 | [`41_AutoNestOverride.cs`](41_AutoNestOverride.cs) — Per-method auto-nest override | disabling nested-mapper synthesis for a single method |
| 42 | [`42_IgnoreSourceMember.cs`](42_IgnoreSourceMember.cs) — Intentionally unread source member | silencing DWARF039 for a source member no destination reads |
| 43 | [`43_ConstructorSelection.cs`](43_ConstructorSelection.cs) — Explicit constructor selection | marking the constructor a mapped type should be built through |
| | **Front doors** | |
| 15 | [`ex15/15_CoLocated.cs`](ex15/15_CoLocated.cs) — Co-located on the DTO | `[GenerateMap]` on a plain `sealed` DTO — no `partial`, no `[DwarfMapper]` |
| 16 | [`16_MapToRegistry.cs`](16_MapToRegistry.cs) — The `[MapTo]` registry | declaring the pair on the source type — no mapper class at all |
| | **Advanced** | |
| 17 | [`17_UpdateInto.cs`](17_UpdateInto.cs) — Update an existing instance | a two-parameter partial method preserves the target's identity |
| 18 | [`18_SpanMap.cs`](18_SpanMap.cs) — Zero-alloc span mapping | `void Map(ReadOnlySpan<S>, Span<D>)` into a caller-provided buffer |
| 19 | [`19_AsyncStream.cs`](19_AsyncStream.cs) — Async streaming | `IAsyncEnumerable<D> Map(IAsyncEnumerable<S>)` — element-by-element, no buffering |
| 20 | [`20_ReferenceCycles.cs`](20_ReferenceCycles.cs) — Reference cycles | `OnCycle = SetNull` breaks a cycle instead of overflowing the stack |
| 21 | [`21_BlittableSimd.cs`](21_BlittableSimd.cs) — Blittable bulk copy | a layout-identical array is bulk-copied, not looped — proven, never assumed |
| 22 | [`22_Reinterpret.cs`](22_Reinterpret.cs) — `[Reinterpret]` — asserted blit | bulk-copying layout-identical structs whose field NAMES differ |
| 23 | [`23_FlattenGraph.cs`](23_FlattenGraph.cs) — `[FlattenGraph]` — a graph becomes a list | breadth-first graph collapse with per-node-type mapping |
| 24 | [`24_MapDerivedType.cs`](24_MapDerivedType.cs) — `[MapDerivedType]` — polymorphic dispatch | one base-typed method that maps each concrete subtype to its own DTO |
| 27 | [`27_PatchMerge.cs`](27_PatchMerge.cs) — Patch-merge: a null source member leaves the destination alone | [MapNullSkip] scoping SkipNullSourceMembers to one map, so replace and patch coexist |
| 44 | [`44_FactoryConstruction.cs`](44_FactoryConstruction.cs) — Factory-based construction | constructing the destination through your own factory, pair-scoped |
| 45 | [`45_WrapperMaps.cs`](45_WrapperMaps.cs) — Wrapper maps | synthesising W<A> to W<B> for every declared payload pair |
| 46 | [`46_MergeCollectionByKey.cs`](46_MergeCollectionByKey.cs) — Merge a collection by key | updating list elements in place instead of rebuilding the list |
| 47 | [`47_RestateBaseConfig.cs`](47_RestateBaseConfig.cs) — Restated base configuration | checking a derived pair against the base pair it restates |
| 48 | [`48_ProvidedMapShape.cs`](48_ProvidedMapShape.cs) — Hand-written provided map | registering your own method as a resolvable map |
| 49 | [`49_MapShare.cs`](49_MapShare.cs) — `[MapShare]` — share an immutable reference | assigning a collection's reference instead of copying it, automatically where it is provable |
| | **Testing** | |
| 25 | [`25_RoundTrip.cs`](25_RoundTrip.cs) — `[RoundTrip]` verification | one attribute emits a fuzzing harness asserting `Back(Forward(x)) == x` |
| 26 | [`26_InformedDumps.cs`](26_InformedDumps.cs) — Informed failure dumps | a failed round trip names the diverging member path, not two object dumps |
| | **Guides** | |
| 30 | [`guides/30_CompositeMapper.cs`](guides/30_CompositeMapper.cs) — A composite mapper | rename, `Use=` conversion, and `[Flatten]` in one mapper |
| 31 | [`guides/31_GenerateMapPairs.cs`](guides/31_GenerateMapPairs.cs) — Several pairs on one class | `[GenerateMap<A,B>]` stacked — the AutoMapper `CreateMap` shape |
| 32 | [`guides/32_FourWaysToCall.cs`](guides/32_FourWaysToCall.cs) — Ways to call a mapper | instance, the generated extension method, and `AddDwarfMappers()` DI |
| 33 | [`guides/33_ExplicitDirectives.cs`](guides/33_ExplicitDirectives.cs) — Satisfying the completeness gate | `[MapProperty]` / `[MapValue]` / `[MapIgnore]` — the three answers to `DWARF001` |
| 34 | [`guides/34_ReverseMapAndHooks.cs`](guides/34_ReverseMapAndHooks.cs) — Inverse maps, injected dependencies, and hooks | `[ReverseMap]`, a primary-constructor dependency in a `Use=` converter, and `[AfterMap]` |
| 35 | [`guides/35_AmbientFacade.cs`](guides/35_AmbientFacade.cs) — The ambient IDwarfMapper facade | mapping when the caller cannot name the concrete mapper type |
<!-- endtable -->


## Basics

### 1. Flat map

*01_FlatMap.cs* — the simplest map — `[GenerateMap<A,B>]`, same names and types

<!-- snippet: flat-map -->
```csharp
[DwarfMapper]
[GenerateMap<Person, PersonDto>]
public partial class Mapper
{
}
```
<!-- endsnippet -->

### 2. Rename a member

*02_Rename.cs* — `[MapProperty(nameof(...), nameof(...))]`

<!-- snippet: rename -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]
    public partial CustomerDto ToDto(Customer c);
}
```
<!-- endsnippet -->

### 3. Built-in conversions

*03_BuiltInConversions.cs* — automatic widening and enum-by-name

<!-- snippet: built-in-conversions -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial HeroDto ToDto(Hero h); // int -> long (widen), Rank -> RankDto (by name)
}
```
<!-- endsnippet -->

### 4. Nested objects

*04_Nested.cs* — auto-nesting a nested `(S,T)` pair

<!-- snippet: nested -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial OrderDto ToDto(Order o); // ShipTo (Address -> AddressDto) auto-nested
}
```
<!-- endsnippet -->

### 5. Collections

*05_Collections.cs* — lists and arrays, element-by-element and bulk copy

<!-- snippet: collections -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial BasketDto ToDto(Basket b);
}
```
<!-- endsnippet -->


## Configuration

### 6. Deep dotted paths

*06_DeepPaths.cs* — a dotted source path — what others reach with a lambda

<!-- snippet: deep-paths -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapProperty("Customer.Name", nameof(OrderSummary.CustomerName))]
    [MapProperty("Customer.Address.City", nameof(OrderSummary.City))]
    public partial OrderSummary ToSummary(Order o);
}
```
<!-- endsnippet -->

### 7. Flatten

*07_Flatten.cs* — `[Flatten]` lifts sub-members to the top level

<!-- snippet: flatten -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [Flatten(nameof(Person.Address))] // Address.City -> City, Address.Zip -> Zip
    public partial PersonDto ToDto(Person p);
}
```
<!-- endsnippet -->

### 8. Custom conversion

*08_CustomConversion.cs* — `Use = nameof(Method)` — the method body is the \

<!-- snippet: custom-conversion -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(FormatMoney))]
    public partial OrderDto ToDto(Order o);

    private static string FormatMoney(decimal d)
    {
        return d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }
}
```
<!-- endsnippet -->

### 9. Conditional and constant values

*09_ConditionalAndValue.cs* — `When=`, `NullSubstitute=`, and `[MapValue]`

<!-- snippet: conditional-and-value -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Member.Nickname), nameof(MemberDto.Nickname), NullSubstitute = "(none)")]
    [MapValue(nameof(MemberDto.Tier), "guild")]
    [MapProperty(nameof(Member.Score), nameof(MemberDto.Score), When = nameof(IsActive))]
    public partial MemberDto ToDto(Member m);

    private static bool IsActive(Member m)
    {
        return m.IsVip;
        // Score is copied only for VIPs; others keep 0
    }
}
```
<!-- endsnippet -->

### 10. Immutable record target

*10_RecordTarget.cs* — constructor binding into a record with no parameterless ctor

<!-- snippet: record-target -->
```csharp
public record PersonDto(int Id, string Name); // immutable, no parameterless ctor

[DwarfMapper]
public partial class Mapper
{
    public partial PersonDto ToDto(Person p); // emits new PersonDto(Id: p.Id, Name: p.Name)
}
```
<!-- endsnippet -->

### 11. IQueryable projection

*11_Projection.cs* — the one place a `Select` lambda is generated for you

<!-- snippet: projection -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial IQueryable<OrderDto> Project(IQueryable<Order> src);
}
```
<!-- endsnippet -->

### 12. Extension method and DI

*12_Ergonomics.cs* — the generated `x.ToGemDto()` and `AddDwarfMappers()`

<!-- snippet: ergonomics -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial GemDto ToDto(Gem g);
}
```
<!-- endsnippet -->

### 13. Configure a collection-element map

*13_NestedListConfig.cs* — renaming a member of the element type inside a `List<T>`

<!-- snippet: nested-list-config -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    // Top-level map. People (List<Person> -> List<PersonDto>) is routed through the nested method below.
    public partial PlaceDto ToDto(Place place);

    // The nested element mapping, configured right here: Person.Name -> PersonDto.FullName.
    [MapProperty(nameof(Person.Name), nameof(PersonDto.FullName))]
    public partial PersonDto ToDto(Person person);
}
```
<!-- endsnippet -->

### 14. The same, with no partial methods

*14_NestedListConfigErgonomic.cs* — pair-scoped `[MapProperty<S,T>]` on the class carries the nested rename

<!-- snippet: nested-list-config-ergonomic -->
```csharp
[DwarfMapper]
[GenerateMap<Place, PlaceDto>]
[MapProperty<Person, PersonDto>(nameof(Person.Name), nameof(PersonDto.FullName))]
public partial class Mapper
{
} // no methods — the pair-scoped attribute carries the nested rename
```
<!-- endsnippet -->

### 40. Before-map hook

*40_BeforeAfterHooks.cs* — running your own code before a map, matched by source type

<!-- snippet: before-map-hook -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial OreDto ToDto(Ore ore);

    // Runs before every map whose source is an Ore. Trim the name at the source, once, rather than at
    // each destination member.
    [BeforeMap]
    public void Normalise(Ore ore)
    {
        ArgumentNullException.ThrowIfNull(ore);
        ore.Name = ore.Name.Trim();
    }
}
```
<!-- endsnippet -->

### 41. Per-method auto-nest override

*41_AutoNestOverride.cs* — disabling nested-mapper synthesis for a single method

<!-- snippet: auto-nest-override -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    // Nested Anvil -> AnvilDto is synthesised for you.
    public partial ForgeDto ToDto(Forge forge);

    // The summary carries no nested member, so nothing needs synthesising for it. Saying so explicitly
    // keeps the intent on the method rather than in the reader's head.
    [AutoNest(false)]
    public partial ForgeSummaryDto ToSummary(Forge forge);
}
```
<!-- endsnippet -->

### 42. Intentionally unread source member

*42_IgnoreSourceMember.cs* — silencing DWARF039 for a source member no destination reads

<!-- snippet: ignore-source-member -->
```csharp
[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
public partial class Mapper
{
    [MapIgnoreSource(nameof(Miner.InternalNotes))]
    public partial MinerDto ToDto(Miner miner);
}
```
<!-- endsnippet -->

### 43. Explicit constructor selection

*43_ConstructorSelection.cs* — marking the constructor a mapped type should be built through

<!-- snippet: constructor-selection -->
```csharp
public sealed class GemDto
{
    // The policy would consider both constructors. The attribute says which one is the real entry point.
    [DwarfMapperConstructor]
    public GemDto(string kind, int carats)
    {
        Kind = kind;
        Carats = carats;
    }

    public GemDto(string kind)
        : this(kind, 0)
    {
    }

    public string Kind { get; }

    public int Carats { get; }
}

[DwarfMapper]
public partial class Mapper
{
    public partial GemDto ToDto(Gem gem);
}
```
<!-- endsnippet -->


## Front doors

### 15. Co-located on the DTO

*ex15/15_CoLocated.cs* — `[GenerateMap]` on a plain `sealed` DTO — no `partial`, no `[DwarfMapper]`

<!-- snippet: co-located-call -->
```csharp
var model = new Person
{
    Name = "John Doe",
    Age = 100
};

// No mapper class exists in this example — the generated extension is the whole call site.
var dto = model.ToPersonDto();
```
<!-- endsnippet -->

### 16. The `[MapTo]` registry

*16_MapToRegistry.cs* — declaring the pair on the source type — no mapper class at all

<!-- snippet: map-to-registry -->
```csharp
[MapTo(typeof(RuneDto))]
public sealed class Rune
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class RuneDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}
```
<!-- endsnippet -->

<!-- snippet: map-to-multi-target -->
```csharp
[MapTo(typeof(GateDto), typeof(GateSummary))]
public sealed class Gate
{
    public int Id { get; set; }

    [MapProperty("Name")]
    [MapProperty("Title")] // GateDto.Name ; GateSummary.Title
    public string Label { get; set; } = "";

    [MapProperty("Warden")]
    [MapIgnore] // mapped into GateDto ; ignored for GateSummary
    public string Keeper { get; set; } = "";
}
```
<!-- endsnippet -->


## Advanced

### 17. Update an existing instance

*17_UpdateInto.cs* — a two-parameter partial method preserves the target's identity

<!-- snippet: update-into -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial void Update(ForgeOrderDto src, ForgeOrder dest); // mutates dest in place
}
```
<!-- endsnippet -->

### 18. Zero-alloc span mapping

*18_SpanMap.cs* — `void Map(ReadOnlySpan<S>, Span<D>)` into a caller-provided buffer

<!-- snippet: span-map -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial void Map(ReadOnlySpan<int> src, Span<long> dst); // int -> long, widened per element
}
```
<!-- endsnippet -->

### 19. Async streaming

*19_AsyncStream.cs* — `IAsyncEnumerable<D> Map(IAsyncEnumerable<S>)` — element-by-element, no buffering

<!-- snippet: async-stream -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial IAsyncEnumerable<OreDto> Map(IAsyncEnumerable<Ore> src);
}
```
<!-- endsnippet -->

### 20. Reference cycles

*20_ReferenceCycles.cs* — `OnCycle = SetNull` breaks a cycle instead of overflowing the stack

<!-- snippet: reference-cycles -->
```csharp
[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class Mapper
{
    public partial DwarfDto ToDto(Dwarf d);
}
```
<!-- endsnippet -->

### 21. Blittable bulk copy

*21_BlittableSimd.cs* — a layout-identical array is bulk-copied, not looped — proven, never assumed

<!-- snippet: blittable-simd -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    // Vein[] -> VeinDto[]: unmanaged, same size, same field names and order, so the whole array is
    // bulk-copied rather than looped. Nothing here asks for that; the generator proves it.
    public partial SeamDto ToDto(Seam s);
}
```
<!-- endsnippet -->

### 22. `[Reinterpret]` — asserted blit

*22_Reinterpret.cs* — bulk-copying layout-identical structs whose field NAMES differ

<!-- snippet: reinterpret -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [Reinterpret(nameof(SurveyDto.Points))] // blit Coord[] -> CoordDto[]; I assert A->X, B->Y
    public partial SurveyDto ToDto(Survey s);
}
```
<!-- endsnippet -->

### 23. `[FlattenGraph]` — a graph becomes a list

*23_FlattenGraph.cs* — breadth-first graph collapse with per-node-type mapping

<!-- snippet: flatten-graph -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [FlattenGraph(nameof(Mine.Root), nameof(MineDto.Nodes))] // walk the graph, fill the list
    [MapDerivedType<Hall, HallDto>] // ...mapping each node type it meets
    [MapDerivedType<Vault, VaultDto>]
    public partial MineDto ToDto(Mine m);
}
```
<!-- endsnippet -->

### 24. `[MapDerivedType]` — polymorphic dispatch

*24_MapDerivedType.cs* — one base-typed method that maps each concrete subtype to its own DTO

<!-- snippet: map-derived-type -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapDerivedType<Axe, AxeDto>]
    [MapDerivedType<Pick, PickDto>]
    public partial ToolDto ToDto(Tool tool); // dispatches on the runtime type
}
```
<!-- endsnippet -->

### 27. Patch-merge: a null source member leaves the destination alone

*27_PatchMerge.cs* — [MapNullSkip] scoping SkipNullSourceMembers to one map, so replace and patch coexist

<!-- snippet: patch-merge -->
```csharp
// NullStrategy.SetDefault decides what a null BECOMES when it is written (int? -> int would otherwise
// throw); [MapNullSkip] decides whether it is written at all. The two are orthogonal and compose.
    [DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
    public partial class Mapper
    {
        /// <summary>Full replace — an absent field CLEARS the stored value.</summary>
        public partial void Replace(AnvilPatchDto src, Anvil dest);

        /// <summary>Patch-merge — an absent field LEAVES the stored value alone.</summary>
        [MapNullSkip]
        public partial void Patch(AnvilPatchDto src, Anvil dest);
    }
```
<!-- endsnippet -->

### 44. Factory-based construction

*44_FactoryConstruction.cs* — constructing the destination through your own factory, pair-scoped

<!-- snippet: factory-construction -->
```csharp
[DwarfMapper]
[GenerateMap<Rune, RuneDto>]
[MapConstructor<Rune, RuneDto>(nameof(CreateRune))]
public partial class Mapper
{
    // Any method taking the source and returning the target. Power is settable, so the generator still
    // assigns it afterwards — the factory only has to cover what the mapper cannot set.
    public static RuneDto CreateRune(Rune rune)
    {
        ArgumentNullException.ThrowIfNull(rune);
        return new RuneDto(rune.Glyph.ToUpperInvariant());
    }
}
```
<!-- endsnippet -->

### 45. Wrapper maps

*45_WrapperMaps.cs* — synthesising W<A> to W<B> for every declared payload pair

<!-- snippet: wrapper-maps -->
```csharp
[DwarfMapper]
[GenerateMap<Pick, PickDto>]
[GenerateWrapperMap(typeof(Envelope<>))]
public partial class Mapper
{
    // Envelope<Pick> -> Envelope<PickDto> is synthesised from the pair above; no second declaration.
    public partial Envelope<PickDto> ToDto(Envelope<Pick> envelope);
}
```
<!-- endsnippet -->

### 46. Merge a collection by key

*46_MergeCollectionByKey.cs* — updating list elements in place instead of rebuilding the list

<!-- snippet: merge-collection-by-key -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [MapCollectionKey(nameof(Cart.Lines), nameof(Line.Sku))]
    public partial void Merge(CartUpdate source, Cart destination);
}
```
<!-- endsnippet -->

### 47. Restated base configuration

*47_RestateBaseConfig.cs* — checking a derived pair against the base pair it restates

<!-- snippet: restate-base-config -->
```csharp
[DwarfMapper]
[MapProperty<Tool, ToolDto>(nameof(Tool.Maker), nameof(ToolDto.Forger))]
[MapProperty<Hammer, HammerDto>(nameof(Tool.Maker), nameof(ToolDto.Forger))]
[RestatesBase<Hammer, HammerDto>]
public partial class Mapper
{
    public partial ToolDto ToDto(Tool tool);

    public partial HammerDto ToDto(Hammer hammer);
}
```
<!-- endsnippet -->

### 48. Hand-written provided map

*48_ProvidedMapShape.cs* — registering your own method as a resolvable map

<!-- snippet: provides-map -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial QuoteDto ToQuote(Quote quote);

    // Not an inferable shape — a document mapped to the collection it holds. Declared as a provided map
    // so it is resolvable like any other.
    [ProvidesMap]
    public List<QuoteDto> ToQuotes(QuoteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Quotes.ConvertAll(ToQuote);
    }
}
```
<!-- endsnippet -->

### 49. `[MapShare]` — share an immutable reference

*49_MapShare.cs* — assigning a collection's reference instead of copying it, automatically where it is provable

<!-- snippet: map-share -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    // Alloys is shared automatically: same type both sides, and Alloy is provably immutable.
    // Runes is an INTERFACE, which the proof refuses — [MapShare] is the caller's own assertion.
    [MapShare(nameof(ForgeDto.Runes))]
    public partial ForgeDto ToDto(Forge f);
}
```
<!-- endsnippet -->


## Testing

### 25. `[RoundTrip]` verification

*25_RoundTrip.cs* — one attribute emits a fuzzing harness asserting `Back(Forward(x)) == x`

<!-- snippet: round-trip -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [RoundTrip] // emits VerifyRoundTrip_ToDto(seed, count)
    public partial LedgerDto ToDto(Ledger l);

    public partial Ledger FromDto(LedgerDto d); // the inverse it verifies against
}
```
<!-- endsnippet -->

### 26. Informed failure dumps

*26_InformedDumps.cs* — a failed round trip names the diverging member path, not two object dumps

<!-- snippet: informed-dumps -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial CoinDto ToDto(Coin c);

    [MapIgnore(nameof(Coin.Mint))] // nothing to restore it from — stated, not forgotten
    public partial Coin FromDto(CoinDto d);
}
```
<!-- endsnippet -->


## Guides

### 30. A composite mapper

*guides/30_CompositeMapper.cs* — rename, `Use=` conversion, and `[Flatten]` in one mapper

<!-- snippet: composite-mapper -->
```csharp
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))] // rename
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))] // conversion
    [Flatten(nameof(Customer.Address))] // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    private static string FormatMoney(decimal d)
    {
        return d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }
}
```
<!-- endsnippet -->

### 31. Several pairs on one class

*guides/31_GenerateMapPairs.cs* — `[GenerateMap<A,B>]` stacked — the AutoMapper `CreateMap` shape

<!-- snippet: generate-map-pairs -->
```csharp
[DwarfMapper]
[GenerateMap<Order, OrderRow>]
[GenerateMap<Customer, CustomerRow>]
public partial class Mappers
{
}
```
<!-- endsnippet -->

### 32. Ways to call a mapper

*guides/32_FourWaysToCall.cs* — instance, the generated extension method, and `AddDwarfMappers()` DI

<!-- snippet: four-ways-to-call -->
```csharp
// 1. Instance — new it (it holds no state, so this is free) or inject it.
var byInstance = new CallStyles().Map(order);

// 2. Convenience extension method, generated by default into DwarfMapper.Extensions.
var byExtension = order.ToOrderView(); // named after the target type

// 3. Dependency injection, when Microsoft.Extensions.DependencyInjection is referenced.
using var provider = new ServiceCollection().AddDwarfMappers().BuildServiceProvider();
var byDi = provider.GetRequiredService<CallStyles>().Map(order);
```
<!-- endsnippet -->

### 33. Satisfying the completeness gate

*guides/33_ExplicitDirectives.cs* — `[MapProperty]` / `[MapValue]` / `[MapIgnore]` — the three answers to `DWARF001`

<!-- snippet: explicit-directives -->
```csharp
[MapProperty(nameof(Src.Existing), nameof(Dst.Renamed))] // it had a differently-named source
[MapValue(nameof(Dst.Source), "api-v2")] // it's a constant/computed value
[MapIgnore(nameof(Dst.PasswordHash))] // dropping it is intentional and audited
public partial Dst ToDst(Src s);
```
<!-- endsnippet -->

### 34. Inverse maps, injected dependencies, and hooks

*guides/34_ReverseMapAndHooks.cs* — `[ReverseMap]`, a primary-constructor dependency in a `Use=` converter, and `[AfterMap]`

<!-- snippet: reverse-map -->
```csharp
[DwarfMapper]
public partial class ReversibleOrderMapper
{
    [ReverseMap]
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapIgnore(nameof(OrderDto.Source))]
    public partial OrderDto ToDto(Order o);

    public partial Order FromDto(OrderDto d); // inherits the inverted Name -> FullName rename
}
```
<!-- endsnippet -->

<!-- snippet: ctor-injection -->
```csharp
[DwarfMapper]
public partial class RatedOrderMapper(IRateService rates) // primary constructor
{
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(ToLocal))]
    [MapValue(nameof(OrderDto.Source), "api-v2")]
    public partial OrderDto ToDto(Order o);

    private decimal ToLocal(decimal amount)
    {
        return rates.Convert(amount);
    }
}
```
<!-- endsnippet -->

<!-- snippet: after-map-hook -->
```csharp
[DwarfMapper]
public partial class StampedOrderMapper
{
    [MapProperty(nameof(Order.FullName), nameof(OrderReceipt.Name))] // rename
    [MapProperty(nameof(Order.Total), nameof(OrderReceipt.Total), Use = nameof(Round))] // transform
    [MapValue(nameof(OrderReceipt.Source), "api-v2")] // constant
    [MapIgnore(nameof(OrderReceipt.Checksum))] // filled below
    public partial OrderReceipt ToReceipt(Order o);

    private static decimal Round(decimal d)
    {
        return Math.Round(d, 2);
    }

    [AfterMap] // the imperative tail you couldn't express declaratively
    private static void Stamp(Order o, OrderReceipt r)
    {
        r.Checksum = $"{o.Id:x8}";
    }
}
```
<!-- endsnippet -->

### 35. The ambient IDwarfMapper facade

*guides/35_AmbientFacade.cs* — mapping when the caller cannot name the concrete mapper type

<!-- snippet: ambient-facade -->
```csharp
public sealed class SettingsService(IDwarfMapper mapper)
{
    public CustomerSummary Summarise(Customer customer)
    {
        return mapper.Map<CustomerSummary>(customer);
    }
}
```
<!-- endsnippet -->
