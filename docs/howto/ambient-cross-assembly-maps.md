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

`IDwarfMapper` (`DwarfMapperFacade`) is registered for you by the generated `services.AddDwarfMappers()`. You
do **not** declare the map in the consuming assembly — it is declared once, anywhere, with `[GenerateMap<S,T>]`.

## How it works

- Every assembly's generated code includes a `[ModuleInitializer]` that **self-registers** its stateless,
  public-typed `T Map(S)` maps into the process-wide `DwarfMapperRegistry` at load (a typed delegate +
  dictionary — no reflection), plus a `[assembly: DwarfProvidesMap(typeof(S), typeof(T))]` manifest.
- Consumption (`Map<TDest>(src)` call sites and `[UsesMap<S,T>]`) is recorded as
  `[assembly: DwarfRequiresMap(...)]`.
- Mark your composition root `[assembly: DwarfMapperValidationRoot]`. There — the one place that references the
  whole graph — DwarfMapper cross-checks every required pair against every provided pair and raises a
  **compile-time `DWARF061`** for anything unprovided. So a missing cross-assembly map fails the **build**, not
  a request at runtime.
- Optionally call the generated `DwarfMap.Validate()` once at startup for a reflection-free runtime fail-fast
  (defense against trimming / not-yet-loaded assemblies).

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

See also: [diagnostics](../diagnostics.md) `DWARF061`/`DWARF062`/`DWARF063`.
