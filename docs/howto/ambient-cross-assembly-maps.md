# Ambient cross-assembly maps (the global `IDwarfMapper`)

DwarfMapper normally generates a concrete mapper class per `[DwarfMapper]` host, and you reference it
directly — fully compile-checked, zero indirection. But sometimes a consumer needs a map declared in a
*different* assembly it does not (and should not) reference — the situation AutoMapper handled with one global
`IMapper`. DwarfMapper offers the same ergonomics with the **ambient registry**, but compile-time/codegen and
**zero-reflection (AOT-safe)**, and without giving up loud failure.

## Using it

Inject `IDwarfMapper` and call `Map<TDestination>(source)`:

```csharp
public sealed class SettingsService(IDwarfMapper mapper)
{
    public BotSettings Load(UserSettingsDocument doc) => mapper.Map<BotSettings>(doc);
}
```

`IDwarfMapper` (`DwarfMapperFacade`) is registered for you by the generated `services.AddDwarfMappers()` — but that method is generated **only in an assembly that declares at least one mapper**, in that assembly's own namespace. A pure-consumer/composition-root assembly with no local `[GenerateMap]`/`[DwarfMapper]` must call a **referenced provider assembly's** `AddDwarfMappers()` (or register `DwarfMapper.DwarfMapperFacade.Instance` directly) to get `IDwarfMapper` into DI. You
do **not** declare the map in the consuming assembly — it is declared once, anywhere, with `[GenerateMap<S,T>]`.

## How it works

- Each assembly that declares an eligible stateless, public-typed map emits a `[ModuleInitializer]` that **self-registers** its stateless,
  public-typed `T Map(S)` maps into the process-wide `DwarfMapperRegistry` at load (a typed delegate +
  dictionary — no reflection), plus a `[assembly: DwarfProvidesMap(typeof(S), typeof(T))]` manifest.
- Consumption (`Map<TDest>(src)` call sites and `[UsesMap<S,T>]`) is recorded as
  `[assembly: DwarfRequiresMap(...)]`.
- Mark your composition root `[assembly: DwarfMapperValidationRoot]`. There — the one place that references the
  whole graph — DwarfMapper cross-checks every required pair against every provided pair and raises a
  **compile-time `DWARF061`** for anything unprovided — a missing cross-assembly map fails the **build**. Note
  this proves the reference *graph*: a provider's map only actually registers once its assembly loads (its
  `[ModuleInitializer]` runs), so with trimming or lazy-loaded plugins an `IDwarfMapper.Map<TDest>` call can
  still throw `DwarfMapMissingException` at runtime even though the build passed — the next bullet closes that gap.
- Run the generated `DwarfMap.Validate()` for a reflection-free runtime fail-fast: it throws
  **`DwarfMapValidationException`** listing every required ambient pair not registered in the process. It checks
  exactly the pairs the graph consumes, so a map used both directions is validated both ways automatically — no
  configuration. Three ways to invoke it:
  - **Explicit:** call `DwarfMapper.DwarfMap.Validate()` once at startup.
  - **DI (recommended):** `services.AddDwarfMappers().ValidateDwarfMaps();` — runs **synchronously at that
    call site** (during service registration). Chain it after every `AddDwarfMappers()` that loads a provider
    assembly, so their maps are registered first.
  - **Automatic:** `[assembly: DwarfMapperValidationRoot(AutoValidate = true)]` emits a `[ModuleInitializer]`
    that calls `Validate()` on load. Reliable for **trimmed / eagerly-referenced** providers; for a genuinely
    *lazy*-loaded plugin whose module initializer hasn't run yet, prefer the DI form or call `Validate()` after
    the plugin loads (an on-load call would false-positive).

## When to use which

| Situation | Use |
|---|---|
| Both types in this assembly (or a referenced one) | the concrete generated mapper / `order.ToOrderDto()` extension — fully compile-checked |
| Map declared in an assembly you don't reference | `IDwarfMapper.Map<TDest>(src)` (ambient) |
| Collections | project at the call site: `list.Select(mapper.Map<Dst>)` (the ambient registry holds single-object maps) |

## Limits

- **Public types only** — internal types cannot be named by another assembly, so they are in-assembly only.
- **Stateless mappers only** — a mapper with constructor dependencies is not ambient-registered (`DWARF062`);
  inject it directly.
- **One provider per pair** — two assemblies providing the same `(S,T)` is `DWARF063` (the first wins).
- **Base-type resolution is a runtime fallback** — `DwarfMapperRegistry.Map` matches by the object's runtime
  type, then walks its base types. If only `Base→Dto` is registered and you call `Map<Dto>(derivedInstance)`,
  the **base map runs and members that exist only on the derived type are silently dropped** (the compile-time
  completeness gate can't see this). Register the derived pair, or use `[MapDerivedType]`, when derived-only
  data must survive.

See also: [diagnostics](../diagnostics.md) `DWARF061`/`DWARF062`/`DWARF063`, and
[Runtime exceptions](../diagnostics.md#runtime-exceptions) for `DwarfMapMissingException`/`DwarfMapValidationException`.
