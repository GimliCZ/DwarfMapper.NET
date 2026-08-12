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

## What is asserted

22 assertions, each one a shape that a generator snapshot test cannot reach — because every test in that
suite is one assembly, one compilation, no DI and no runtime resolution.

| Area | The failure it guards |
|---|---|
| Ambient resolution | a map declared in an unreferenced assembly is unreachable through the facade |
| Collection shapes | `Map<ICollection<TDto>>(list)` throws unless the instantiation was declared by hand |
| Lazy sequences | a `.Where(…)` iterator implements `IEnumerable<T>` without deriving from it, so a base-chain lookup misses it and the call throws |
| Polymorphic elements | a derived element inside a base-typed list silently loses its derived member |
| Divergent nested pairs | two providers reaching one nested pair synthesize copies that quietly disagree |
| Construction | a `[MapConstructor]` factory owns construction and drops an `init`-only member |
| Non-public construction | an `internal` constructor behind `[InternalsVisibleTo]` — the supported replacement for a reflective mapper's bypass |
| Patch-merge | replace and patch semantics on one mapper, across the boundary |
| **`[ProvidesMap]`** | a hand-written map is correct code that nothing registers, so every facade call site for it throws |
| **Factory + collection** | the element route runs the bare factory without the member assignments, returning a list of blanks |
| **`EnumStringSource`** | one enum read two ways from two assemblies share a synthesized helper, and whichever loads first decides the persisted format for both |
| **Reserved keywords** | a member called `@class` emits unescaped and the provider assembly does not compile |
| DI | a registration in the container does not resolve |

The last four are new. Each corresponds to something this round changed in the generator, and none of them
had consumer-level coverage before — which is the same gap, one round later.

Note the factory-and-collection case asserts BOTH halves: that the factory ran (only it produces the `PART-`
prefix) and that the settable member was assigned. Either alone passes while the other is broken, and "a list
of objects of the right length" is exactly what the defect produced.
