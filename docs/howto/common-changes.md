<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Common changes when swapping to DwarfMapper

These five changes happen on **every** migration, regardless of which library you're leaving. Each
per-library guide builds on them, so read this once first.

> **Mental model in one sentence:** in DwarfMapper the **generated class _is_ the configuration** —
> there is no runtime engine, no fluent builder, no `IMapper` to configure (the optional ambient `IDwarfMapper` is generated), and no config object to register. You
> annotate one `partial class`, the source generator fills in the bodies at compile time, and the mapping
> either compiles (and is provably complete) or it doesn't.

---

## Change 1 — Reference the packages, target net10

DwarfMapper ships as **one** package (it bundles the attributes, the source generator, and the IDE code
fixes), plus an optional **testing** package for `[RoundTrip]`.

| Package | Why | TFM |
|---|---|---|
| `DwarfMapper` | Everything: the attributes you annotate with **and** the bundled generator + code fixes (analyzer slot). Zero runtime dependencies. | `net10.0` |
| `DwarfMapper.Testing` | Optional, published from 1.1.0-rc.1. Powers `[RoundTrip]` and the seeded fuzzer. Test projects only. | `net10.0` |

**Install** — one package reference in **each project that declares mappers**, no analyzer wiring. The generator
does not flow through a project reference: a project that only references a project using DwarfMapper gets no
generated code. It's a release candidate, so use `--prerelease`:

```bash
dotnet add package DwarfMapper --prerelease
```

**Building from source instead?** Reference the projects directly. A project reference brings neither the generator
nor the package's build settings, so state both yourself:

```xml
<Import Project="path/to/src/DwarfMapper/build/DwarfMapper.props" />
<ItemGroup>
  <ProjectReference Include="path/to/src/DwarfMapper/DwarfMapper.csproj" />
  <!-- ONE generator build: this one for the .NET 10 SDK; on the .NET 11 SDK (C# 15 unions) reference
       src/DwarfMapper.Generator.Roslyn512/DwarfMapper.Generator.Roslyn512.csproj instead. -->
  <ProjectReference Include="path/to/src/DwarfMapper.Generator/DwarfMapper.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

- **The generator as an analyzer** is the line people miss. The package carries the generator twice, built for
  Roslyn 5.0 (every .NET 10 SDK) and for Roslyn 5.12 (the .NET 11 SDK), and the SDK loads one. A project reference
  cannot choose, so reference the build that matches your SDK, and never both. The Roslyn 5.12 build does not load
  in an older compiler (CS9057); the Roslyn 5.0 build loads in both but maps no unions
  ([DWARF113](../diagnostics.md#dwarf113)).
- **`build/DwarfMapper.props`** is the file the package imports for you. It adds `DwarfMapper.Generated` to
  `InterceptorsNamespaces`, so a `Dwarf.Map` call can be bound to the generated mapper at compile time (the compiler
  refuses an interceptor in a namespace not listed there), and makes `PublishAot` visible to the generator
  (`CompilerVisibleProperty`), which [DWARF115](../diagnostics.md#dwarf115) reads. Setting the two yourself works the
  same.
- The IDE code fixes are `src/DwarfMapper.CodeFixes`, added as an analyzer the same way if you want them.

The consuming project must target **`net10.0` or later** (the package ships one `net10.0` assembly; .NET 11 is
built and tested too; net8/net9 projects cannot reference it; only the bundled generator/code-fix
assemblies are `netstandard2.0`, because Roslyn loads generators into the compiler host — see [deploy-and-optimize.md](deploy-and-optimize.md#net-10-only--and-why-the-generator-is-netstandard20)).

---

## Change 2 — Collapse the runtime config into one partial class

Whatever shape your current config takes — an AutoMapper `Profile`, a Mapster `TypeAdapterConfig`, a
Mapperly `[Mapper]` class, or hand-written methods — it becomes a single annotated `partial class`. There
are two equivalent ways to declare a pair; pick per your taste:

<!-- fence-exempt: shows two declaration styles side by side; samples show each separately (Gallery 31 and 30) -->
```csharp
using DwarfMapper;

// (a) attribute-only — lowest ceremony, ideal for a bulk find-and-replace from CreateMap/NewConfig:
[DwarfMapper]
[GenerateMap<Order, OrderDto>]
[GenerateMap<Customer, CustomerDto>]
public partial class Mappers { }

// (b) named partial method — use when you want a specific method name, extra parameters,
//     or an explicit inverse for [ReverseMap]:
[DwarfMapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order src);     // body generated
}
```

- **`[GenerateMap<S,T>]`** emits `public T Map(S src)`, one overload per pair, resolved by **source type**.
  (Two pairs with the *same* source but different targets is the build error `DWARF060` — declare
  those as named `partial` methods instead.)
- **POCOs stay attribute-free.** You never annotate your `Order` / `OrderDto` classes — only the mapper.
- **Class-level options** ride on the `[DwarfMapper(...)]` attribute: `CaseInsensitive`, `NameConvention`,
  `EnumStrategy`, `NullStrategy`, `NullCollections`, `ReferenceHandling`, `OnCycle`, `MaxDepth`,
  `RequiredMapping`, `ImplicitConversions`, `AutoNest`. See each per-library guide for the ones you'll need.

Per-member tweaks become attributes on the method (or class), never lambdas:

| You had (any library) | DwarfMapper |
|---|---|
| rename `Y → X` | `[MapProperty(nameof(S.Y), nameof(D.X))]` |
| custom value via a function | `[MapProperty(nameof(S.Y), nameof(D.X), Use = nameof(Method))]` |
| skip a destination member | `[MapIgnore(nameof(D.X))]` |
| constant / no-source computed value | `[MapValue(nameof(D.X), "const")]` / `[MapValue(nameof(D.X), Use = nameof(Method))]` |
| before/after hook | `[BeforeMap]` / `[AfterMap]` named method |

The rule behind all of these: **attributes can't carry closures, so every lambda becomes a named method**
referenced by `Use=`, `When=`, or a hook attribute.

---

## Change 3 — Move the call sites

The mapper is a stateless, allocation-free class. Three ways to reach it directly (the fourth, the ambient
`IDwarfMapper` / `Dwarf.Map` call for a map declared in another assembly, is in
[ambient-cross-assembly-maps.md](ambient-cross-assembly-maps.md)):

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

The instance call drops any generic type argument — the overload is resolved by the source type:

```diff
- var dto = mapper.Map<OrderDto>(order);   // AutoMapper
- var dto = order.Adapt<OrderDto>();       // Mapster
+ var dto = mappers.Map(order);            // DwarfMapper instance
+ var dto = order.ToOrderDto();            // DwarfMapper extension (using DwarfMapper.Extensions;)
```

There is **no `AddAutoMapper(assembly)`-style reflection scan** — `AddDwarfMappers()` is generated code that
registers each concrete mapper explicitly, so it stays AOT-safe. (Disable the generated extensions for a
mapper with `[DwarfMapper(GenerateExtensions = false)]`.)

---

## Change 4 — Clear the build (the DWARF001 wall)

This is the change that surprises people, and it's the whole point of the library:

> **Every destination member must be mapped or explicitly ignored. An unmapped member is `DWARF001`, a
> build error — always.** Lowering its severity in a `.globalconfig` does not ship the map: the method body is
> still not generated, and the build fails with a compiler error instead
> ([how severities work](../diagnostics.md#how-severities-and-suppression-work)).

So after Change 2, your first build will likely fail, listing every destination member your old config
silently dropped. This is information you didn't have before. For each one, choose:

<!-- snippet: explicit-directives -->
```csharp
[MapProperty(nameof(Src.Existing), nameof(Dst.Renamed))] // it had a differently-named source
[MapValue(nameof(Dst.Source), "api-v2")] // it's a constant/computed value
[MapIgnore(nameof(Dst.PasswordHash))] // dropping it is intentional and audited
public partial Dst ToDst(Src s);
```
<!-- endsnippet -->

Practical consequences:

- **Delete your `AssertConfigurationIsValid()` / "did I wire everything?" tests.** Completeness is now a
  compile error — strictly stronger than a runtime/test assertion.
- The first build is a **free audit** of your existing maps. Members flagged here were silently unmapped
  before; decide each one deliberately.
- Source-side coverage is opt-in: add `[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]` to also
  be told (via the `DWARF039` suggestion) about **source** members nothing consumes — a forgotten/mis-wired
  field. Suppress an intentional one with `[MapIgnoreSource("Member")]`.

Other diagnostics you may meet on the first build, all with a concrete fix (full catalog in
[`../diagnostics.md`](../diagnostics.md)): `DWARF038` (a non-lossless conversion is being applied — visible,
not silent), `DWARF044` (a nullable hop in a dotted path can throw), `DWARF010` (a case-insensitive match is
ambiguous), `DWARF024` (a constructor parameter has no source).

---

## Change 5 — Prove the swap was lossless

Once it builds, verify behaviour with the two tools DwarfMapper gives you for free:

**Round-trip a forward/back pair** — tag the forward method `[RoundTrip]` and call the generated verifier
from one test (needs a `DwarfMapper.Testing` reference, published from 1.1.0-rc.1; without it the attribute is a
no-op):

<!-- snippet: round-trip -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    [RoundTrip] // emits VerifyRoundTrip_ToDto(seed, iterations)
    public partial LedgerDto ToDto(Ledger l);

    public partial Ledger FromDto(LedgerDto d); // the inverse it verifies against
}

public static class LedgerMapperTests
{
    // In a test project this is a [Fact]; the defaults are seed 12345 and 100 iterations.
    public static void LedgerRoundTrips() => new Mapper().VerifyRoundTrip_ToDto(seed: 7, iterations: 50);
}
```
<!-- endsnippet -->

**Or verify ad-hoc** without the attribute:

<!-- snippet: informed-dumps -->
```csharp
[DwarfMapper]
public partial class Mapper
{
    public partial CoinDto ToDto(Coin c);

    [MapIgnore(nameof(Coin.Mint))] // nothing to restore it from — stated, not forgotten
    public partial Coin FromDto(CoinDto d);
}

public static class CoinMapperTests
{
    // The ad-hoc form, for a pair without [RoundTrip]: throws RoundTripException naming Coin.Mint.
    public static void CoinRoundTrips()
    {
        var mapper = new Mapper();
        RoundTrip.Verify<Coin, CoinDto>(mapper.ToDto, mapper.FromDto, seed: 7, iterations: 20);
    }
}
```
<!-- endsnippet -->

For a belt-and-braces migration, keep your *old* mapper around for one release and assert old-output ≡
new-output over a sample of real inputs before deleting it. This needs no extra package. The repo's own
[`LibraryParityRuntimeTests.cs`](../../tests/DwarfMapper.IntegrationTests/LibraryParityRuntimeTests.cs)
is exactly this pattern, proving DwarfMapper matches each competitor feature.

---

## What you give up (so the guides don't have to repeat it)

A handful of capabilities are **deliberate non-goals** because they require runtime reflection or closures
(which break AOT). Each has a static replacement and surfaces a diagnostic rather than failing silently:

| Non-goal | Why | Replacement |
|---|---|---|
| Inline lambdas in config | attributes can't carry closures | named methods (`Use=`, `When=`, hooks) |
| Ambient per-call context (`Items`/`State`) | dynamic bag / reflection | extra typed method parameter (matched to a dest member by name) |
| Open generics / assembly scanning | needs reflection (breaks AOT) | one `[GenerateMap<closed>]` per used instantiation |
| Generic mapper method/class | a generator can't emit a body for an unbound `T` | `DWARF053`/`DWARF054`; declare closed pairs |
| Deep merge into existing nested objects | ambiguous identity semantics | update **replaces** nested; merge by hand if truly needed |
| Silent float→int truncation | data-loss footgun | explicit `Use=` converter (visible, opt-in) |
| Turning the completeness check off | resilience-first stance | `[MapIgnore]` per intentional drop (auditable) |

Full list with the idiomatic replacement for each: [`../MIGRATION.md` §4](../MIGRATION.md#4-deliberate-non-goals-all-libraries--and-the-idiomatic-replacement).
