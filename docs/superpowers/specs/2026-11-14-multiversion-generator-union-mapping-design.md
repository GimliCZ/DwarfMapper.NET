# Multi-version generator, C# 15 union mapping, and a union performance comparison

Round 35, tasks T23 (multi-version generator + union mapping) and T24 (performance comparison). Written 2026-11-14.

## Intent

The owner, 2026-11-14: "Prepare multiversion and if detected .net 11 enable Union mapping (C# 15 unions are refused
today with DWARF113) would need 5.12's UnionCaseTypes. with tests afcourse. After that prepare performance comparison
testing."

.NET 11 and C# 15 shipped on 2026-11-10 with union types. DwarfMapper refuses every mapping that touches one
(DWARF113, round 31 T24) because it had no policy for them. The compiler's own facts about a union (`IsUnion`,
`UnionCaseTypes`) exist only in the Roslyn 5.12 API, and the generator is compiled against Roslyn 5.0.0 on purpose:
a generator built against a newer Roslyn does not load in an older compiler (CS9057), and README promises SDK
10.0.100 or newer. NuGet's Roslyn-versioned analyzer folders let both builds ship in one package.

## Goals

1. The package ships the generator twice, in `analyzers/dotnet/roslyn5.0/cs/` and `analyzers/dotnet/roslyn5.12/cs/`.
   Exactly one copy loads in any compiler: the 5.0 build in Roslyn 5.0 to 5.11, the 5.12 build in 5.12 and newer.
2. The 5.12 build maps unions when the compilation is C# 15 or newer. On code without unions it emits byte for byte
   what the 5.0 build emits.
3. Tests: in process on Roslyn 5.12; the golden corpus through the 5.12 build; folder selection proven from the
   packed package on the floor SDK, the pinned SDK and SDK 11; an end-to-end net11 consumer in the `net11` job.
4. A performance comparison of union mapping (T24).

## Non-goals

- Union mapping in a compiler older than 5.12, or in a C# 14 compilation.
- Mapping a union to or from a non-union type, except a case type into a union through the language's own union
  conversion.
- Unions in projections (`IQueryable`).
- DWARF114's same-compilation `closed` detection through `IsClosed`. The 5.12 build makes it cheap; it is a follow-up.
- A permanent mutation leg for the 5.12 build. One focused pass over the union files instead, with the gap named.

## Anti-goals

- Two generator copies loading in one compilation (double emission, duplicate diagnostics).
- Any change in what the generator emits for code without unions, on any host.
- Pairing union cases by member shape or by name. DWARF114's remarks already rule that arms are not inferred.
- A warning, or a `!`, in generated code.
- Reflection to reach a newer Roslyn API.

## Facts measured (2026-11-14, `mcr.microsoft.com/dotnet/sdk` images)

- Compilers: SDK 10.0.101 carries Roslyn 5.0.0, 10.0.404 carries 5.9.0, 11.0.100 carries 5.12.0. The SDK sets
  `CompilerApiVersion` (`roslyn5.0`, `roslyn5.9`, `roslyn5.12`) in `Microsoft.Managed.Core.CurrentVersions.targets`.
- Folder selection (`dotnet/sdk` `ResolvePackageAssets.cs`, `AnalyzerResolver`): an analyzer file outside any
  `roslynX.Y` folder is ALWAYS used; among versioned folders, only those of the highest version not above
  `CompilerApiVersion` are used. So no generator copy may remain in the unversioned `analyzers/dotnet/cs/`.
  `DwarfMapper.CodeFixes` references no generator type and stays unversioned.
- `union Pet(Cat, Dog)` lowers to a sealed struct with `[Union]`, `IUnion`, one public constructor per case type and
  `object? Value { get; }`. `UnionAttribute` and `IUnion` live in `System.Private.CoreLib` in .NET 11.
- Roslyn 5.12: `IsUnion` and `UnionCaseTypes` are right for union declarations, hand-written `[Union]` structs and
  member-provider classes (`IUnionMembers.Create`), from source and from metadata. Both are reported under C# 14
  too, so they say nothing about whether the compilation can use unions. `LanguageVersion.CSharp15` exists; Latest
  and Default map to it.
- `ClassifyCommonConversion(Cat, Pet)` is an implicit union conversion under C# 14 as well, while C# 14 code using
  one fails with CS9327 ("Feature 'unions' is not available in C# 14.0"). Every `HasImplicitConversion` site widens
  silently on a 5.9+ host unless union conversions are decided in one place.
- Dispatch shapes, compiled by Roslyn 5.12 against the net11 BCL:
  - `p switch { Cat s => ..., Dog s => ..., null => default(PetDto) }` is clean. An untyped `null => default` is
    CS8604 when every arm has the same type.
  - A wildcard after the case arms and a null arm is CS8510, an error. Without a null arm, a union with a nullable
    case is CS8655.
  - Arms must run most-derived first (CS8510 otherwise). Union matching does not look into a nested union (CS8121).
  - A pass-through arm (`int s => s` into `LongOrString`) and an identity arm (`Cat s => s`) convert through the
    union conversion.
  - `null => default(ProvPetDto)` into a non-nullable class union is CS8603. Without a null arm it is clean.
  - An explicit cast in an arm compiles, but a cast prefers a user-defined explicit operator over the union conversion.
- `default(Pet)` in an exhaustive switch expression throws `SwitchExpressionException`; `default(Pet) is null` is true.

## Approaches

**A. Two builds from one source tree (chosen).** `DwarfMapper.Generator.csproj` stays the Roslyn 5.0 build, and a
new `DwarfMapper.Generator.Roslyn512.csproj` compiles the same files against Roslyn 5.12, plus the files only it has.
This is how `System.Text.Json` and `Microsoft.Extensions.Logging` ship their generators. Cost: the package grows by
the generator's compressed size (about 260 KB), and the 5.12 build needs a test project of its own.

**B. One build, reflection for the newer API.** No package growth. Rejected: the owner asked for multi-version, a
reflected property breaks silently when Roslyn renames it, and reflection to get around a compile-time reference is
the trade this repository refuses.

**C. Re-derive union facts on Roslyn 5.0 from the lowered shape.** No second build. Rejected: it re-implements what
the compiler already decides (constructors versus `IUnionMembers.Create` factories, inherited members, `in`
parameters), and union behaviours need C# 15, which first shipped in Roslyn 5.12, so no older host gains anything.

## Design

### Packaging

- `src/DwarfMapper.Generator.Roslyn512/DwarfMapper.Generator.Roslyn512.csproj`: netstandard2.0, `AssemblyName`
  `DwarfMapper.Generator`, the same analyzer settings, RIDs and lock-file regime as the 5.0 project.
  `Microsoft.CodeAnalysis.CSharp` 5.12.0 through `VersionOverride`. It compiles `..\DwarfMapper.Generator\**\*.cs`
  except `*.Floor.cs`, plus the files in its own folder.
- The seam is files, not `#if`. A `*.Floor.cs` file is compiled into the 5.0 build only; a file in the Roslyn512
  folder into the 5.12 build only. Shared code calls one small surface (`UnionSupport`) whose two implementations
  live in those files. Union extraction and emission live entirely in 5.12-only files, so the 5.0 assembly carries no
  branch it cannot reach.
- `DwarfMapper.csproj` packs the two builds into `analyzers/dotnet/roslyn5.0/cs` and `analyzers/dotnet/roslyn5.12/cs`.
  CodeFixes stays in `analyzers/dotnet/cs`.
- Projects inside this repository keep referencing the 5.0 project as an analyzer. The `net11` job rebuilds them
  against the 5.12 build too (one switch in `Directory.Build.targets`), so every sample and consumer test also passes
  through the 5.12 build in a real 5.12 compiler.

### Union support in the 5.12 build

- **When.** A type is a union when `IsUnion` says so, and its cases are `UnionCaseTypes`. Union mapping is on when the
  compilation's language version is C# 15 or newer. Otherwise every differing pair with a union on either side stays
  DWARF113, with the reason in the message.
- **Conversions.** A union conversion counts as implicit only when union mapping is on. That rule sits in the one
  helper every site already calls, and in the 5.0 build it never counts. This also closes a latent hole in today's
  code: on a 5.9+ host the `Use=` check, hook matching and `[MapDerivedType]` validation all see union conversions as
  implicit.
- **Union to union** (two different union types) is a case dispatch:

  ```csharp
  return source switch
  {
      global::Demo.Cat __s => Map(__s),     // a converter arm
      global::Demo.Dog __s => __s,          // an identity arm: Dog is a case of the target too
      null => default(global::Demo.PetDto), // the empty union
  };
  ```

  Arms run most-derived first. Each arm's value has exactly its target case type and becomes the target union by the
  language's union conversion; arm values carry no cast. A pass-through arm whose case types differ (`int` into
  `long`) is written with a conversion to the target case type. The null arm is typed, and there is no wildcard.
- **Pairing a source case with a target case.** An explicit `[MapDerivedType]` arm wins. Without one: the same type
  as a case of the target (identity); otherwise exactly one declared mapping method from that case type to a case
  of the target. Anything else is DWARF114 (the case has no arm, or two declared methods compete), with the remedy:
  name the arm, or declare the method. Value-type cases use the `typeof` form of the attribute, because the generic
  form is constrained to classes.
- **The empty union** maps to the empty target when the target can hold one: `default` for a struct union, `null` for
  a nullable class union. Into a non-nullable class union, the switch has no null arm when the source cannot be empty
  by declaration (an empty source then throws `SwitchExpressionException`, as the same switch written by hand does);
  when the source can be empty (a nullable case type, or a nullable class union), the pair is DWARF113.
- **Where it applies:** a declared create map (arms allowed), a nested member (a synthesized helper, or the declared
  method for that pair when one exists), collection and dictionary elements (through the helper), `Nullable<union>`
  (the outer `Nullable` handled as for any struct pair), update-into members. Hooks on a declared union map behave as
  on a derived dispatch. Under Preserve and SetNull, the context reaches every arm whose converter needs it.
- **A case type into a union member** is the compiler's union conversion, assigned directly, under C# 15 only.
- **Still refused (DWARF113, the reason named):** a compiler older than 5.12 (the 5.0 build), C# older than 15, a
  union into or out of a non-union type other than the case-type direction above, projections, a union type with no
  case types, and an empty source into a non-nullable class union.

### Diagnostics

- DWARF113 is re-titled "A union type cannot be mapped here". Its message carries the reason.
- DWARF114 is re-titled "A closed set of types has a member with no [MapDerivedType] arm" and gains the union message.
- DWARF035 (invalid `[MapDerivedType]`) gains two union messages: the arm's source matches no case type of the
  source union, or its target does not convert to the target union.
- No new ids. The negative-case ratchet compiles its cases with the 5.0 build, which can never raise a 5.12-only id.
  DWARF113 and DWARF114 keep their 5.0 case files, and the 5.12 messages are pinned by the 5.12 test project.

### Tests

- `tests/DwarfMapper.Generator.Roslyn512.Tests`: net10.0, `Microsoft.CodeAnalysis.CSharp` 5.12.0, referencing the
  5.12 build. Sources are parsed as C# 15. `UnionAttribute` and `IUnion` are declared in the test sources, because the
  net10 BCL lacks them and the compiler accepts source-declared ones.
  - A matrix of endpoint kind × union kind × reference mode. Each cell either compiles clean and runs, or is a named
    refusal. A `[Union]` type with no case types is one of the union kinds.
  - The red-green pair across builds: the `UnionTypeRefusalTests` sources stay DWARF113 on the 5.0 build and map on
    the 5.12 build.
  - Neutrality: every golden-corpus case through the 5.12 build, byte-identical to the manifest.
  - The conversion gate at C# 14 and C# 15 (`Use=` with a union parameter, a hook taking the union).
- Folder selection: the package is packed, and a consumer is restored from that local pack and prints its analyzer
  list. The pinned SDK and the floor SDK must load the roslyn5.0 build, SDK 11 the roslyn5.12 build, and each exactly
  one `DwarfMapper.Generator.dll`.
- The `net11` job: `samples/DwarfMapper.UnionSample` (net11.0, `union` syntax, outside the solution) consumes the
  packed package under warnings as errors, runs, and checks its results, behind a vacuity guard.
- Coverage of the 5.12-only files comes from the new project; ReportGenerator and Codecov merge by assembly name and
  path. One focused Stryker pass over the union files, with any gap named in the pull request.

### Gates this touches

The package-size ceiling (re-measured: the second build adds about 260 KB), `scripts/repro-pack-check.py` (two
generator DLLs), the `net11` job, CONTRIBUTING, README, `docs/diagnostics.md`, the analyzer release files and
CHANGELOG.

## Performance comparison (T24)

- `benchmarks/DwarfMapper.UnionBenchmarks`: net11.0, outside the pinned solution and outside
  `allocation-baseline.json` (which is per SDK). Run in the SDK 11 container.
- Arms: a hand-written switch over the same union (the floor; the boxing belongs to the union, not the mapper),
  DwarfMapper's union map, and DwarfMapper's `[MapDerivedType]` dispatch over the equivalent class hierarchy. The
  competitors (Mapperly, Mapster, AutoMapper) are checked for union support first; where they have none, they are
  measured on the class hierarchy and labelled so.
- Payloads from a seeded generator with a realistic case mix: one value, and a list of 1,000; a union with a
  value-type case too. MemoryDiagnoser on. Figures labelled as Linux-container figures.

## Calls made in the owner's absence (overrulable)

- Strict pairing: no inference from member shape or name.
- An empty union maps to an empty union.
- `[MapDerivedType]` names union arms; there is no new attribute.
- No new diagnostic ids.
- The package grows by about 260 KB.

## Left to the builder

Helper naming, reuse of the existing arm sorting, the exact test file layout, and which nested routes need code
beyond the helper.
