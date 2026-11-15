<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 35 T24 — C# 15 union mapping: what it costs, and what the other libraries do with a union

The owner, 2026-11-14: "After that prepare performance comparison testing" — after T23, which taught the generator's
Roslyn 5.12 build to map C# 15 unions. The benchmark is
[`benchmarks/DwarfMapper.UnionBenchmarks`](../DwarfMapper.UnionBenchmarks) (needs the .NET 11 SDK; outside the
solution). Before measuring anything, the obvious first question was whether there is anything to compare against.
There is not: none of the three libraries the repository compares with maps a union (section 1). So the comparison is
against what a developer writes by hand, and against the class hierarchy that held the same data before unions.

**In short.**

- **A union map costs what the same code costs written by hand in the same shape.** DwarfMapper's generated map is a
  `switch` whose arms call the declared case maps; the same switch written by hand that way measured 8.12 ns against
  the generated 8.14 ns on Linux, 6.27 against 6.29 ns on Windows. Against a hand-written switch that builds each DTO
  inline, the generated map is 4 % to 11 % slower: the call per case and that method's own argument check, under a
  nanosecond per value. Allocations are identical in every arm, on both platforms.
- **By hand, a union maps faster than the class hierarchy that holds the same data**: the hierarchy took 9 % to 25 %
  longer in every run and category, on Linux and on Windows.
- **On the hierarchy, where the other libraries can take part**, DwarfMapper was ahead of Mapperly 4.3.1 in every run
  (Mapperly took 1 % to 4 % longer for one value, 14 % to 22 % for a batch), and Mapster 10.0.8 took about three times
  as long as everyone.
- **One thing measured slower**: for a batch, DwarfMapper's list fill (`SetCount` and a span) was 4 % to 7 % slower than
  plain `Add` for a union element (section 2).
- **None of Mapperly, Mapster or AutoMapper maps a union** — one returns an empty union with a warning, one throws, one
  maps the first case and throws on the others (section 1). There is no competitor number to put beside DwarfMapper's
  union map, because there is no competitor union map.

## 1. What Mapperly, Mapster and AutoMapper do with a C# 15 union

Measured, not read off documentation: SDK 11.0.100, the versions `DwarfMapper.Benchmarks` pins (Mapperly 4.3.1, Mapster
10.0.8, AutoMapper 14.0.0), `union Pet(Cat, Dog)` into `union PetDto(CatDto, DogDto)` — top level, and as a member of
a class. Each case's map is declared (Mapperly), created with `CreateMap` (AutoMapper) or left to convention (Mapster).
The same probe gave the same answer, line for line, on the newest releases as of 2026-11-14 — Mapster 10.0.14, the
latest stable — and on the prereleases Mapperly 5.0.0-next.13 and Mapster 10.1.0-pre04. (AutoMapper has no newer
release this repository can reference: 15 and later are RPL-1.5.)

| | A `Dog` (the second case) | A `Cat` (the first case) | A `Dog`, as a member | At build or configuration time |
|---|---|---|---|---|
| Mapperly 4.3.1 | **empty union** (`Value` null) | **empty union** | **empty union** | builds; warning `RMG066` "No members are mapped in the object mapping from Pet to PetDto" |
| Mapster 10.0.8 | throws `CompileException` | throws `CompileException` | throws `CompileException` | — (the map compiles on first use): "Cannot convert immutable type, please consider using 'MapWith' method to create mapping" |
| AutoMapper 14.0.0 | throws `AutoMapperMappingException` ("Dog -> CatDto", destination `.ctor(CatDto)`, parameter `value`) | **maps** | throws | `AssertConfigurationIsValid()` **passes** |
| DwarfMapper (5.12 build) | maps | maps | maps | — |

None of the three knows what a union is; each meets a struct with one constructor per case and a read-only `Value`.
**Mapperly** maps it as a struct with nothing settable and returns `default` — the data is gone, and a warning is the
only trace. **Mapster** refuses at the first map. **AutoMapper** binds the target's `PetDto(CatDto)` constructor to the
source's `Value`, so the first case maps and every other case throws, after a configuration validation that passed: a
test that maps one value of the first case passes, and production fails on the second case. That is the worst of the
three, because it looks like it works.

The probe, as run (a scratch project outside the repository: AutoMapper's NU1903 advisories stay warnings there, and no
audit suppression was added anywhere):

```csharp
public record class Cat(string Name);
public record class Dog(string Name);
public record class CatDto(string Name);
public record class DogDto(string Name);
public union Pet(Cat, Dog);
public union PetDto(CatDto, DogDto);
public class Owner { public Pet Pet { get; set; } }
public class OwnerDto { public PetDto Pet { get; set; } }

[Riok.Mapperly.Abstractions.Mapper]
public partial class MapperlyPets
{
    public partial PetDto ToDto(Pet pet);
    public partial OwnerDto ToDto(Owner owner);
    public partial CatDto ToDto(Cat cat);
    public partial DogDto ToDto(Dog dog);
}

// Mapster:    new Pet(new Dog("Rex")).Adapt<PetDto>()                       and owner.Adapt<OwnerDto>()
// AutoMapper: new MapperConfiguration(c => { c.CreateMap<Pet, PetDto>(); c.CreateMap<Cat, CatDto>();
//                 c.CreateMap<Dog, DogDto>(); })  then AssertConfigurationIsValid(), Map<PetDto>(pet)
```

```
PROBE Mapperly union->union: EMPTY union (Value null)
PROBE Mapperly member: EMPTY union (Value null)
PROBE Mapperly union->union, the FIRST case: EMPTY union (Value null)
PROBE Mapster union->union: THROWS CompileException: Error while compiling | innermost InvalidOperationException: Cannot convert immutable type, please consider using 'MapWith' method to create mapping
PROBE Mapster member: THROWS CompileException: Error while compiling | innermost InvalidOperationException: Cannot convert immutable type, please consider using 'MapWith' method to create mapping
PROBE Mapster union->union, the FIRST case: THROWS CompileException: Error while compiling | innermost InvalidOperationException: Cannot convert immutable type, please consider using 'MapWith' method to create mapping
PROBE AutoMapper union->union, no extra config: THROWS AutoMapperMappingException: Error mapping types. | innermost AutoMapperMappingException: Missing type map configuration or unsupported mapping.  Mapping types: Dog -> CatDto Dog -> CatDto  Destination Member: Void .ctor(CatDto), parameter value
PROBE AutoMapper configuration validation: valid
PROBE AutoMapper member: THROWS AutoMapperMappingException: Error mapping types. | innermost AutoMapperMappingException: Missing type map configuration or unsupported mapping.  Mapping types: Dog -> CatDto Dog -> CatDto  Destination Member: Void .ctor(CatDto), parameter value
PROBE AutoMapper union->union, the FIRST case: CatDto CatDto { Name = Tom }
```

Recorded in [`docs/COMPARISON.md`](../../docs/COMPARISON.md)'s capability matrix. It is not in the differential
harness (`tests/DwarfMapper.DifferentialTests`), which builds on the pinned .NET 10 SDK and cannot declare a union.

## 2. What it costs

Linux: `mcr.microsoft.com/dotnet/sdk:11.0.100`, Ubuntu 26.04.1, .NET 11.0.0 (11.0.26.53018), X64 RyuJIT AVX2, the
container pinned to four logical CPUs of an AMD Ryzen 5 5600 (`--cpuset-cpus 0-3`); BenchmarkDotNet 0.14.0 DefaultJob.
Ratio is against `Union_Hand` in each category. The second run added the control arm; the first run's figures are in
brackets, as a measure of how far one arm moves between two runs.

| Arm | One payment | | Batch of 1,000 | |
|---|---:|---:|---:|---:|
| Union, hand-written (baseline) | 7.82 ns | 1.00 (7.91) | 10.65 µs | 1.00 (10.18) |
| Union, hand-written in the generated structure (control) | 8.12 ns | 1.04 | 10.81 µs | 1.01 |
| **Union, DwarfMapper** | **8.14 ns** | **1.04** (8.74, 1.11) | **11.23 µs** | **1.05** (11.22, 1.10) |
| Hierarchy, hand-written | 9.02 ns | 1.15 (8.65) | 12.22 µs | 1.15 (12.70) |
| Hierarchy, DwarfMapper | 9.69 ns | 1.24 (9.70) | 11.01 µs | 1.03 (11.26) |
| Hierarchy, Mapperly 4.3.1 | 10.06 ns | 1.29 (10.01) | 12.92 µs | 1.21 (12.81) |
| Hierarchy, Mapster 10.0.8 | 23.81 ns | 3.05 (23.97) | 30.94 µs | 2.90 (31.57) |

Every payment arm allocates the same: 40 B for one payment (the DTO), 48,080 B for a batch (the list and the 1,000
DTOs). Nothing is allocated that the mapping does not hand back.

| Value cases (`Reading`) | One reading | | Batch of 1,000 | |
|---|---:|---:|---:|---:|
| Hand-written (baseline) | 4.59 ns | 1.00 (4.71) | 7.23 µs | 1.00 (7.25) |
| **DwarfMapper** | **4.66 ns** | **1.01** (4.67, 0.99) | **7.42 µs** | **1.03** (7.46, 1.03) |

Both allocate 21 B per reading on average (29,440 B per batch): a value case lives boxed in `Value`, so an `int`
widened to a `long` or a `double` passed on is unboxed and boxed again, and a `string` is passed on as it is — the same
work in both arms.

Windows, on the same machine: Windows 10 22H2 (10.0.19045), the user-local SDK 11.0.100, .NET 11.0.0, X64 RyuJIT AVX2,
all twelve logical CPUs, the same snapshot built fresh; BenchmarkDotNet 0.14.0 DefaultJob. The repository's other
published figures are Windows figures from this machine, so this is the table to read beside them.

| Arm | One payment | | Batch of 1,000 | |
|---|---:|---:|---:|---:|
| Union, hand-written (baseline) | 5.86 ns | 1.00 | 8.83 µs | 1.00 |
| Union, hand-written in the generated structure (control) | 6.27 ns | 1.07 | 9.17 µs | 1.04 |
| **Union, DwarfMapper** | **6.29 ns** | **1.08** | **9.85 µs** | **1.12** |
| Hierarchy, hand-written | 7.25 ns | 1.24 | 9.77 µs | 1.11 |
| Hierarchy, DwarfMapper | 8.24 ns | 1.41 | 8.78 µs | 0.99 |
| Hierarchy, Mapperly 4.3.1 | 8.34 ns | 1.42 | 10.75 µs | 1.22 |
| Hierarchy, Mapster 10.0.8 | 20.16 ns | 3.44 | 27.98 µs | 3.17 |
| Value cases, hand-written / **DwarfMapper** | 3.35 / **3.18** ns | **0.95** | 6.06 / **6.21** µs | **1.02** |

The allocations are byte for byte the Linux ones.

**Reading the table.**

- The control is what settles the first question. It is the generated map's own shape written by hand — the switch
  calls a public method per case, and each case method checks its argument, which is that method's contract when
  something else calls it. It measures what the generated map measures, on both platforms (8.12 against 8.14 ns on
  Linux, 6.27 against 6.29 ns on Windows). So the 4 % to 11 % by which the generated map trails the inline switch is
  that shape — under a nanosecond per value — and not anything the generator adds on top. The case methods are the
  user's declared maps; calling them is what makes a declared map's configuration apply to the case it maps, so the
  shape is deliberate.
- The batch adds DwarfMapper's list fill: an exactly-sized list filled through `CollectionsMarshal.SetCount` and a span,
  where the control calls `Add`. For this element — a union, a struct holding one reference — that fill measured 4 %
  (Linux) and 7 % (Windows) slower than `Add`. The span fill was built for value elements such as `long`, where the
  main suite measured it ahead of every competitor; here it does not pay. Recorded as measured; no change is made in
  T24.
- The union against the hierarchy, by hand: the same data, the same leaf records, only the container differs. The
  hierarchy took 9 % to 25 % longer than the union in every run, on both platforms, in both categories. Through
  DwarfMapper the union is the faster for one value and the hierarchy for a batch — the union batch pays the fill
  above, and DwarfMapper's hierarchy batch is quick on its own account (it beat the hand-written hierarchy batch on
  both platforms).
- On the hierarchy, DwarfMapper was ahead of Mapperly in all three runs — Mapperly took 1 % to 4 % longer for one
  value, 14 % to 22 % for a batch. The non-Mapster rows sit within about 25 % of each other and some change order
  between categories;
  differences of that size at this scale belong partly to how the JIT laid each method out, which is why every claim
  above is one that held in every run. Mapster's 3x is not of that size: it maps through its configuration at run
  time, where the other arms run code compiled for the pair.

## 3. How it was measured

- **The data.** One draw feeds both shapes: the hierarchy's element `i` holds the union's element `i`, so a difference
  between the shapes is the shape's. The CONTENT of every case comes from `ObjectFactoryV2`, the fixture and fuzz
  source the test suites use (nulls below the root, boundary numbers, empty and varied strings), as in the main
  suite's `RealisticPayloads`; the MIX is 60 % cards, 30 % transfers, 10 % cash, in a seeded order, so the branch
  predictor meets a skewed sequence rather than a rotation it can learn. Single values cycle through a ring of 512
  distinct draws (the main suite's rule since round 26: no benchmark maps one static object); a batch is one `List` of
  1,000, mapped whole. `Payloads.AssertRealistic` fails the run on a degenerate draw.
- **The leaf types are the same kind in both shapes** — positional records, built by their constructors — so the
  shapes differ only in the container (a union struct holding its case in `Value`, or a reference to an abstract base)
  and the dispatch over it. One concession, for Mapster alone: the hierarchy's DESTINATION base is a concrete record,
  because compiling `Include` arms into an abstract record destination throws (section 4).
- **Each library is written the way its documentation writes the shape**: DwarfMapper's union map pairs each case by
  the declared method that maps it (the how-to's first example) and its hierarchy map uses `[MapDerivedType]` arms;
  Mapperly uses `[MapDerivedType]`; Mapster uses `Include` on its own `TypeAdapterConfig`, compiled in setup; the
  hand-written arms are `switch` expressions.
- **The dry run comes first.** `dotnet run -c Release -- dry` maps every payload through every arm, by the benchmark
  methods themselves, and compares each result with the hand-written arm's (the case type and every member): all 16
  comparisons agreed before anything was timed. A fast arm that drops a case is wrong, not fast.
- **BenchmarkDotNet 0.14.0** (the version the repository pins), its DefaultJob, `[MemoryDiagnoser]`, one baseline per
  category (`Union_Hand`, the hand-written union switch).

## 4. Found on the way

- **Mapster cannot compile `Include` arms into an abstract record destination** — `Mapster.CompileException`, a
  `NullReferenceException` in `RecordTypeAdapter.CreateInstantiationExpression` → `GetConstructorModel`: an abstract
  record has no public constructor for it to bind. Measured on 10.0.8 and on 10.0.14, the latest stable; the
  10.1.0-pre04 prerelease maps it. The hierarchy's destination base is concrete for that reason (`Model.cs` says so
  where it is declared); DwarfMapper, Mapperly and the hand-written arms map either.
- **The SDK 11.0.100 analyzers misread a union `switch` statement**: in `Payloads.AssertRealistic`, switching over the
  union itself drew CA1508 ("'transfers == 0' is always 'true'", the same for cash) — the case sections after the
  first read as dead code. Switching over `Value`, the same loop is clean. A false positive in the analyzer, an error
  under this repository's warnings-as-errors; DwarfMapper's generated code is not analysed by these rules.
