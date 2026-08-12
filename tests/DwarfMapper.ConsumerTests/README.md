<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper.ConsumerTests — the consumer-shaped harness

**This project is deliberately OPAQUE.** It references no other project in this repository — not the other
test projects, not `samples/`. No shared fixtures, no shared harness, no shared type corpus. It takes only
what a real consumer takes:

```xml
<ProjectReference Include="..\..\..\src\DwarfMapper\DwarfMapper.csproj" />
<ProjectReference Include="..\..\..\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Why it exists

Round 18 migrated a ~300-map codebase onto DwarfMapper and found four generator defects plus one blocking
runtime gap. **Not one was reachable from the 5,000-test generator suite** — because every test in that suite
is one assembly, one compilation, no DI container, no runtime registry resolution.

The blocking gap (47 latent runtime throws) slipped past *three* independent safety nets simultaneously:

| Net | Why it was blind |
|---|---|
| The consumer's own 706 tests | construct or mock services; never resolve through DI |
| The parity suite | replays only *declared* pairs |
| `DWARF061` validation root | needs the source type; at `Map<ICollection<T>>(data)` only the destination is static |

It surfaced only because a benchmark happened to build the real DI graph and count unresolvable
registrations.

## Why opaque

Reusing the existing corpus would import the existing corpus's blind spots — which is exactly what let those
defects through. An opaque project can only pass by exercising the real consumer surface.

## Shape

```
Contracts   domain types + DTOs. No mapper, no DwarfMapper reference beyond the runtime.
ProviderA   public [DwarfMapper] classes; self-register at module load.
ProviderB   a second provider. Overlapping nested pairs under DIFFERENT policy options.
Host        the DI container and the tests. Does NOT reference the provider mapper types.
```

`Host` not referencing the providers' mappers is the load-bearing constraint: that is the condition under
which the ambient registry is the only thing that can resolve a map, and therefore the condition under which
the 47-site gap existed at all.
