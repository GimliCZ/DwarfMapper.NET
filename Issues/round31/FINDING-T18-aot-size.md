# T18 — what ambient registration costs a NativeAOT binary (measured 2026-09-27)

Subject: `samples/DwarfMapper.AotSample` (20 `[DwarfMapper]` classes, ~127 ambient-registered shapes counted from its
`DwarfProvidesMap` manifest), `dotnet publish -c Release -r win-x64`, `PublishAot=true` from the csproj, SDK 10.0.101,
ILCompiler 10.0.1, at `feat/round31` after T17.

| build | `DwarfMapper.AotSample.exe` | behavioural gate |
|---|---:|---|
| as shipped | **1,795,072 B** | passes |
| prototype: generator patched to emit NO `DwarfMapper.AmbientRegistration.g.cs` | **1,615,360 B** | passes (the sample never calls the facade) |
| difference | **179,712 B = 10.0 %** — ≈ 1.4 KB per registered shape | |

The prototype patch (a static flag skipping the one `AddNormalizedSource` call in `DwarfGenerator.EmitAggregates`) was
never committed; the generator was restored from git immediately after the publish.

## Why it costs that much

Each registration is a `static` lambda in the module initializer, so every registered shape roots its mapper method,
its element mapper and — for the six collection shapes per pair — a `DwarfCollectionMap.ToList/ToArray<S, D>`
instantiation. NativeAOT compiles everything reachable, and the module initializer makes all of it reachable whether or
not anything ever resolves it through `IDwarfMapper`.

## Decision

**Material** — 10 % of a small binary, growing linearly with the number of mapped pairs — so the question is worth
pursuing. But not in the shape the task sketched:

- **Registering only destinations with call sites, decided at the validation root, is rejected.** Registration happens
  in each PROVIDING assembly's module initializer, compiled before the root exists; the root cannot reach back and
  prune it. Doing it would move registration from providers to the root — the same architectural inversion T14's
  root-generated dispatchers were not built for — and it would still be wrong for a caller the root cannot see
  (a plugin, a `Map(object, Type)` whose destination is data).
- **Recommended next step — an explicit, per-assembly opt-out:** e.g. `[assembly: DwarfMapperOptions(AmbientRegistration
  = false)]`, which skips both the module initializer and the `DwarfProvidesMap` manifest (so a validation root still
  reports a consumer of that assembly's maps as DWARF061 — nothing claims to provide what is not registered), paired
  with a DWARF error when the same compilation contains an `IDwarfMapper.Map` call site (the collector already finds
  them). That buys the full 10 % for an app that maps only through its injected mapper classes, with no silent path.
  It adds a public option, so it is an owner decision; nothing was changed on that basis in round 31.
