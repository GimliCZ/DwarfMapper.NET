# Surface-coverage architecture — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Make it structurally impossible for a public DwarfMapper attribute or surface member to ship without
an executed, case-complete proof — by moving the two non-derivable facts (what makes an element observable,
which endpoints it claims) onto the declarations themselves and generalizing the existing option matrix to the
whole surface.

**Architecture:** An `internal` `[DwarfSurface(Category, AppliesTo, ProbeKey)]` attribute is applied to every
public attribute type in `src/DwarfMapper`. The test project reads it through `[InternalsVisibleTo]`, derives
each element's case-space by reflection, binds `ProbeKey` to a test-side fixture, and runs the generator twice
per cell to classify the effect. `SurfaceCategory` has no `Exempt` member — every category carries a
*different* mandatory obligation, which is what replaces today's six allowlist dictionaries.

**Tech Stack:** C# / .NET 10 (runtime package), netstandard2.0 (generator — do not upgrade), Roslyn source
generators, xUnit, Stryker.NET for mutation testing.

**Spec:** `docs/superpowers/specs/2026-08-13-surface-coverage-architecture-design.md` — read it before Task 1.

## Global Constraints

- **Never `git push`.** Commit locally only. Pushing requires explicit per-instance approval from the
  maintainer.
- **Nullable reference types are on.** Keep annotations correct. Do not silence warnings with `!` without a
  stated reason.
- **Warnings are errors** in this repo. Do not leave unused members, variables, or parameters behind.
- **Prefer `nameof(...)`** over hand-written member-name strings wherever the symbol is in reference.
- **Every new file starts with** `// SPDX-License-Identifier: GPL-2.0-only` followed by a blank line.
- **`src/DwarfMapper.Generator` stays `netstandard2.0`** (Roslyn analyzer-host requirement). Never retarget it.
- **`src/DwarfMapper` stays `net10.0`, `IsTrimmable`, `IsAotCompatible`, zero reflection at runtime.** The new
  `[DwarfSurface]` attribute is metadata only — it must never be read by shipped runtime code, only by tests.
- **For any change that alters generator diagnostics or emission, build the WHOLE solution**
  (`dotnet build DwarfMapper.NET.sln`), not just `dotnet test`. The `samples/` projects are not covered by
  `dotnet test` and are where emission regressions surface. This applies to Task 8 especially.
- **Match the surrounding code** — this repo has unusually dense explanatory doc comments on test
  infrastructure that state *why a gate exists and what defect it caught*. New infrastructure files are
  expected to carry the same. A bare `/// <summary>Does X.</summary>` is below the house standard here.
- **Do not delete an allowlist entry without redirecting its obligation.** Every entry removed in Task 6 must
  land as a category assignment whose obligation actually runs.

## File Structure

**Created in `src/DwarfMapper/`:**
- `DwarfSurfaceAttribute.cs` — the `internal` meta-attribute, `SurfaceCategory` enum, `SurfaceEndpoints` flags
  enum. One file: the three types are meaningless apart and change together.
- `AssemblyInfo.cs` — `[InternalsVisibleTo("DwarfMapper.Generator.Tests")]`.

**Modified in `src/DwarfMapper/`:** all 32 public attribute declarations gain one `[DwarfSurface(...)]` line.

**Created in `tests/DwarfMapper.Generator.Tests/Contracts/`:**
- `RepoPaths.cs` — repo-root and well-known directory resolution, shared. (Three test files currently carry a
  private copy of `RepoRoot()`; this is the one place it should live.)
- `SurfaceCatalog.cs` — reflects `[DwarfSurface]` types, derives the case-space. Data only, no assertions.
- `SurfaceFixtures.cs` — `ProbeKey` → source-fixture text, each marked `[SurfaceProbe("key")]`.
- `SurfaceProbe.cs` — classifies one cell by running the generator twice. Caches baselines.
- `SurfaceParityTests.cs` — the bidirectional matrix property.

**Created in `tests/DwarfMapper.Generator.Tests/SelfValidation/`:**
- `SurfaceDeclarationTests.cs` — every public surface element carries `[DwarfSurface]`; `ProbeKey` bijection;
  `SurfaceEndpoints` ↔ `Endpoint` bijection.
- `SurfaceObligationTests.cs` — the per-category obligations.

**Modified:**
- `Contracts/OptionCatalog.cs` — full enum domains (Task 7).
- `Contracts/OptionGaps.cs` — `KnownSilent` → `DeclaredDivergences`; `StructurallyInapplicable` deleted
  (Task 6).
- `SelfValidation/OptionSurfaceCoverageTests.cs` — both allowlist dictionaries deleted (Task 6).
- `SelfValidation/AssemblyScanTests.cs` — Scan4 replaced by the executed obligation (Task 6).
- `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs` + `AnalyzerReleases.Unshipped.md` +
  `docs/diagnostics.md` — DWARF086 (Task 8).
- `src/DwarfMapper/DwarfMapperRegistry.cs` — `IsUpdateAmbiguous` (Task 10).

**Created at repo root:** `stryker-config.runtime.json` (Task 9).

---

### Task 1: `[DwarfSurface]` and the declaration gate

**Files:**
- Create: `src/DwarfMapper/DwarfSurfaceAttribute.cs`
- Create: `src/DwarfMapper/AssemblyInfo.cs`
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs`
- Modify: all 32 attribute declarations under `src/DwarfMapper/`

**Interfaces:**
- Produces: `internal sealed class DwarfSurfaceAttribute` with ctor `(SurfaceCategory category)` and settable
  `SurfaceEndpoints AppliesTo` (default `SurfaceEndpoints.All`), `string? ProbeKey` (default `null`); readable
  `SurfaceCategory Category`.
- Produces: `internal enum SurfaceCategory { ConsumerDirective, GeneratorEmitted, BuildFailureOnly,
  EmissionShape, CrossAssembly, TestingOnly }` — **no `Exempt` member, ever**.
- Produces: `[Flags] internal enum SurfaceEndpoints` with members named exactly like
  `DwarfMapper.Generator.Tests.Contracts.Endpoint`: `CreateMap = 1, UpdateInto = 2, Projection = 4,
  SpanMap = 8, AsyncStream = 16, Registry = 32, CoLocatedHost = 64`, plus `All = 127`.

- [ ] **Step 1: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The forcing function for the whole surface-coverage architecture: a public attribute that carries no
///     <c>[DwarfSurface]</c> has no category, therefore no obligation, therefore no proof. Before this gate,
///     thirteen public attributes had zero consumer-shaped presence and nothing in the repository failed.
/// </summary>
public sealed class SurfaceDeclarationTests
{
    /// <summary>Every public attribute type shipped by the runtime package.</summary>
    public static IReadOnlyList<Type> PublicAttributeTypes { get; } =
        typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false } && typeof(Attribute).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void The_public_attribute_scan_is_not_vacuous()
    {
        Assert.True(PublicAttributeTypes.Count >= 30,
            $"Only {PublicAttributeTypes.Count} public attribute types reflected; the surface has ~32. "
            + "Either the package genuinely shrank (lower this floor deliberately) or the reflection stopped "
            + "seeing the surface and every gate built on it has gone vacuous.");
    }

    [Fact]
    public void Every_public_attribute_declares_a_DwarfSurface_category()
    {
        var undeclared = PublicAttributeTypes
            .Where(t => t.GetCustomAttribute<DwarfSurfaceAttribute>(inherit: false) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(undeclared.Count == 0,
            "Public attribute type(s) with no [DwarfSurface] declaration:\n  "
            + string.Join("\n  ", undeclared)
            + "\n\nAdd [DwarfSurface(SurfaceCategory.X, ...)] to the declaration. There is deliberately no "
            + "'exempt' category: every category carries a proof obligation, and choosing one is how the "
            + "obligation gets assigned. See docs/superpowers/specs/"
            + "2026-08-13-surface-coverage-architecture-design.md for the category table.");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceDeclarationTests"`

Expected: FAIL to **compile** — `DwarfSurfaceAttribute` does not exist. That is the correct first failure.

- [ ] **Step 3: Create the attribute**

Create `src/DwarfMapper/DwarfSurfaceAttribute.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     What kind of surface an element is, and therefore what must be proved about it.
///     <para>
///         There is deliberately no <c>Exempt</c> member. Every category below carries a DIFFERENT mandatory
///         obligation; classifying an element redirects its proof rather than waiving it. This replaces six
///         independent allowlist dictionaries, each of which was one person typing a reason once.
///     </para>
/// </summary>
internal enum SurfaceCategory
{
    /// <summary>
    ///     Hand-written by consumers and changes emitted code. Obligation: the executed cross-product (every
    ///     cell honoured, refused, or declared inapplicable), plus at least one consumer-assembly use and one
    ///     runnable-sample use.
    /// </summary>
    ConsumerDirective,

    /// <summary>
    ///     Emitted BY the generator onto the assembly; never hand-written (DWARF086 refuses hand-written use).
    ///     Obligation: produced by a generator run in a test AND consumed by the reading side.
    /// </summary>
    GeneratorEmitted,

    /// <summary>
    ///     Its whole observable effect is a build failure, so a passing sample cannot contain it. Obligation:
    ///     a NegativeCases row pinning the id AND its remedy wording, plus a positive row proving the
    ///     non-failing path compiles.
    /// </summary>
    BuildFailureOnly,

    /// <summary>
    ///     Changes the SHAPE or accessibility of generated code rather than runtime behaviour. Obligation: a
    ///     structural assertion over the generated text at every claimed endpoint.
    /// </summary>
    EmissionShape,

    /// <summary>
    ///     Only observable across an assembly boundary. Obligation: exercised by a multi-assembly consumer
    ///     fixture; single-assembly cells are inapplicable by category, not by allowlist.
    /// </summary>
    CrossAssembly,

    /// <summary>
    ///     Part of the testing surface consumers use to write their own tests. Obligation: consumer-shaped use
    ///     plus a DwarfMapper.Testing.Tests contract row.
    /// </summary>
    TestingOnly
}

/// <summary>
///     The mapping shapes a surface element can reach. Member names are kept identical to
///     <c>DwarfMapper.Generator.Tests.Contracts.Endpoint</c>; a test asserts the bijection, because two enums
///     that drift apart would silently repoint every claim at the wrong endpoint.
/// </summary>
[Flags]
internal enum SurfaceEndpoints
{
    None = 0,
    CreateMap = 1,
    UpdateInto = 2,
    Projection = 4,
    SpanMap = 8,
    AsyncStream = 16,
    Registry = 32,
    CoLocatedHost = 64,

    /// <summary>
    ///     Every endpoint. This is the DEFAULT on purpose. Over-claiming fails (a claimed endpoint must be
    ///     honoured or refused) and under-claiming fails too (an unclaimed endpoint must be silent or
    ///     uncompilable), so there is no value of <see cref="DwarfSurfaceAttribute.AppliesTo" /> that passes
    ///     vacuously — a wrong default is always caught rather than quietly ratified.
    /// </summary>
    All = CreateMap | UpdateInto | Projection | SpanMap | AsyncStream | Registry | CoLocatedHost
}

/// <summary>
///     Declares what must be proved about a public surface element, at the element's own declaration.
///     <para>
///         Two facts about a surface element cannot be reflected: what SHAPE makes it observable, and which
///         endpoints it legitimately does not reach. Both used to live in hand-kept dictionaries in the test
///         project, where nothing forced a new element to acquire an entry — an unprobed element read as "not
///         probed" and claimed nothing. Here they sit next to the code they describe and are verified in both
///         directions.
///     </para>
///     <para>
///         Internal, and read only by the test projects via <c>[InternalsVisibleTo]</c>. Nothing in the shipped
///         runtime reads it: this is metadata, and the package's zero-reflection, trim- and AOT-safe guarantees
///         are unaffected.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface
                | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
internal sealed class DwarfSurfaceAttribute : Attribute
{
    public DwarfSurfaceAttribute(SurfaceCategory category) => Category = category;

    /// <summary>What kind of surface this is, and therefore what must be proved about it.</summary>
    public SurfaceCategory Category { get; }

    /// <summary>
    ///     The endpoints this element CLAIMS to affect. Verified in both directions by
    ///     <c>SurfaceParityTests</c>: a claimed endpoint where the element does nothing observable fails, and
    ///     an unclaimed endpoint where it changes the output fails too.
    /// </summary>
    public SurfaceEndpoints AppliesTo { get; set; } = SurfaceEndpoints.All;

    /// <summary>
    ///     Names the test-side fixture whose type shape makes this element observable — e.g. an enum pair with
    ///     divergent member order, or a self-referencing graph. Bound dynamically: the test project must
    ///     contain exactly one <c>[SurfaceProbe]</c> fixture with this key, and every fixture must be claimed
    ///     by at least one element. Null means the default flat DTO pair suffices.
    /// </summary>
    public string? ProbeKey { get; set; }
}
```

- [ ] **Step 4: Grant the test project access**

Create `src/DwarfMapper/AssemblyInfo.cs` (mirrors `src/DwarfMapper.Generator/AssemblyInfo.cs`):

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DwarfMapper.Generator.Tests")]
```

- [ ] **Step 5: Run the test — it must now compile and FAIL on content**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceDeclarationTests"`

Expected: `The_public_attribute_scan_is_not_vacuous` PASSES;
`Every_public_attribute_declares_a_DwarfSurface_category` FAILS listing ~32 type names.

**Do not proceed until you have seen that list.** It is the authoritative inventory for the next step.

- [ ] **Step 6: Annotate all 32 attributes**

Add exactly one `[DwarfSurface(...)]` line immediately **above** each existing `[AttributeUsage(...)]` line.
Use this table verbatim. Leave `AppliesTo` and `ProbeKey` off for now — defaults are correct at this stage and
Tasks 3 and 5 will narrow them where the matrix demands it.

| File | Type | Line to add |
|---|---|---|
| `DwarfMapperAttribute.cs` | `DwarfMapperAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `DwarfMapperDefaultsAttribute.cs` | `DwarfMapperDefaultsAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `AfterMapAttribute.cs` | `AfterMapAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `BeforeMapAttribute.cs` | `BeforeMapAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `AutoNestAttribute.cs` | `AutoNestAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `FlattenAttribute.cs` | `FlattenAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `FlattenGraphAttribute.cs` | `FlattenGraphAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `GenerateMapAttribute.cs` | `GenerateMapAttribute<TSource, TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `GenerateWrapperMapAttribute.cs` | `GenerateWrapperMapAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapCollectionKeyAttribute.cs` | `MapCollectionKeyAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapDerivedTypeAttribute.cs` | `MapDerivedTypeAttribute<TSource, TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapDerivedTypeAttribute.cs` | `MapDerivedTypeAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapIgnoreAttribute.cs` | `MapIgnoreAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `PairScopedAttributes.cs` | `MapIgnoreAttribute<TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapIgnoreSourceAttribute.cs` | `MapIgnoreSourceAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapNullSkipAttribute.cs` | `MapNullSkipAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapNullSkipAttribute.cs` | `MapNullSkipAttribute<TSource, TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapPropertyAttribute.cs` | `MapPropertyAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `PairScopedAttributes.cs` | `MapPropertyAttribute<TSource, TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `PairScopedAttributes.cs` | `MapConstructorAttribute<TSource, TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `PairScopedAttributes.cs` | `MapValueAttribute<TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapToAttribute.cs` | `MapToAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `MapValueAttribute.cs` | `MapValueAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `DwarfMapperConstructorAttribute.cs` | `DwarfMapperConstructorAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `ReinterpretAttribute.cs` | `ReinterpretAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `RestatesBaseAttribute.cs` | `RestatesBaseAttribute<TSource, TTarget>` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `ReverseMapAttribute.cs` | `ReverseMapAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `ProvidesMapAttribute.cs` | `ProvidesMapAttribute` | `[DwarfSurface(SurfaceCategory.ConsumerDirective)]` |
| `DwarfMapperOptionsAttribute.cs` | `DwarfMapperOptionsAttribute` | `[DwarfSurface(SurfaceCategory.EmissionShape)]` |
| `AmbientMapAttributes.cs` | `DwarfProvidesMapAttribute` | `[DwarfSurface(SurfaceCategory.GeneratorEmitted)]` |
| `AmbientMapAttributes.cs` | `DwarfRequiresMapAttribute` | `[DwarfSurface(SurfaceCategory.GeneratorEmitted)]` |
| `AmbientMapAttributes.cs` | `UsesMapAttribute` | `[DwarfSurface(SurfaceCategory.CrossAssembly)]` |
| `AmbientMapAttributes.cs` | `UsesMapAttribute<TSource, TDestination>` | `[DwarfSurface(SurfaceCategory.CrossAssembly)]` |
| `AmbientMapAttributes.cs` | `DwarfMapperValidationRootAttribute` | `[DwarfSurface(SurfaceCategory.BuildFailureOnly)]` |
| `RoundTripAttribute.cs` | `RoundTripAttribute` | `[DwarfSurface(SurfaceCategory.TestingOnly)]` |

If Step 5's failure list contains a type **not** in this table, add it with
`SurfaceCategory.ConsumerDirective` and note it in the commit message — the table was built from a snapshot
and the surface may have moved.

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceDeclarationTests"`

Expected: both tests PASS.

- [ ] **Step 8: Verify the whole suite still builds**

Run: `dotnet build DwarfMapper.NET.sln`
Expected: no new warnings. (`IsTrimmable`/`IsAotCompatible` on `src/DwarfMapper` must not start warning — the
new attribute is metadata and is never read at runtime. If a trim warning appears, the attribute has been
referenced from runtime code by mistake.)

- [ ] **Step 9: Commit**

```bash
git add src/DwarfMapper tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs
git commit -m "test(surface): a public attribute without a declared category has no obligation"
```

---

### Task 2: `SurfaceCatalog` — derive the case-space

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Contracts/RepoPaths.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceCatalog.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs`

**Interfaces:**
- Consumes: `DwarfSurfaceAttribute`, `SurfaceCategory`, `SurfaceEndpoints` from Task 1;
  `DwarfMapper.Generator.Tests.Contracts.Endpoint` (existing, `Contracts/Endpoints.cs`).
- Produces: `record SurfaceElement(Type Type, string UsageName, SurfaceCategory Category,
  SurfaceEndpoints AppliesTo, string? ProbeKey, AttributeTargets ValidOn, bool AllowMultiple)`.
- Produces: `record SurfaceCase(SurfaceElement Element, AttributeTargets Site, string Rendered, string Axis)`
  — `Rendered` is the attribute as written in source (e.g. `[MapIgnore("Name")]`), `Axis` is a short label for
  the failure message (e.g. `"ctor(string)"`, `"AutoNest=false"`, `"×2"`).
- Produces: `static IReadOnlyList<SurfaceElement> SurfaceCatalog.Elements`.
- Produces: `static IReadOnlyList<SurfaceCase> SurfaceCatalog.CasesFor(SurfaceElement element)`.
- Produces: `static class RepoPaths` with `string Root`, `string Src`, `string Tests`, `string Samples`,
  `string GeneratorSrcDir`, `string PipelineDir`, and `string[] ConsumerRoots`.

- [ ] **Step 1: Write `RepoPaths`**

Create `tests/DwarfMapper.Generator.Tests/Contracts/RepoPaths.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     Repository locations, resolved once. Three test files carried a private copy of this walk; a fourth
///     copy is how one of them ends up pointed at a directory that has since moved, passing vacuously.
/// </summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Src => Path.Combine(Root, "src");
    public static string Tests => Path.Combine(Root, "tests");
    public static string Samples => Path.Combine(Root, "samples");
    public static string GeneratorSrcDir => Path.Combine(Src, "DwarfMapper.Generator");
    public static string PipelineDir => Path.Combine(GeneratorSrcDir, "Pipeline");

    /// <summary>The four consumer-shaped assemblies — projects that USE DwarfMapper rather than test it.</summary>
    public static string[] ConsumerRoots { get; } =
    [
        Path.Combine(Tests, "DwarfMapper.ConsumerTests"),
        Path.Combine(Tests, "DwarfMapper.DifferentialTests"),
        Path.Combine(Tests, "DwarfMapper.NegativeCases"),
        Path.Combine(Tests, "DwarfMapper.IntegrationTests")
    ];

    /// <summary>Every .cs file under <paramref name="dir" />, skipping build output.</summary>
    public static IEnumerable<string> SourceFiles(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !p.Contains($"{sep}bin{sep}", StringComparison.Ordinal));
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
```

**Note:** round 19 named `DwarfMapper.ConsumerTests.CleanCorpus` as a separate assembly. It is not — it is a
folder inside `tests/DwarfMapper.ConsumerTests` (alongside `Host`, `Contracts`, `ProviderA`, `ProviderB`), so
the single root above covers it recursively. Confirm this before relying on it: run
`ls tests/DwarfMapper.ConsumerTests` and check the layout has not changed. Every entry in `ConsumerRoots` must
pass `Directory.Exists` — Task 6 Step 1 adds `Every_corpus_is_non_empty` so a typo cannot silently make the
corpus empty and pass every obligation by finding nothing to check.

- [ ] **Step 2: Write the failing test for the catalog**

Append to `SurfaceDeclarationTests.cs`:

```csharp
    [Fact]
    public void SurfaceEndpoints_and_Endpoint_name_the_same_seven_endpoints()
    {
        // Two enums that drift apart would silently repoint every AppliesTo claim at the wrong endpoint,
        // and the matrix would keep passing while measuring the wrong cell.
        var flags = Enum.GetNames<SurfaceEndpoints>()
            .Where(n => n is not ("None" or "All"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var endpoints = Enum.GetNames<Contracts.Endpoint>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(endpoints, flags);
    }

    [Fact]
    public void Every_catalog_element_yields_at_least_one_case()
    {
        var barren = Contracts.SurfaceCatalog.Elements
            .Where(e => Contracts.SurfaceCatalog.CasesFor(e).Count == 0)
            .Select(e => e.UsageName)
            .ToList();

        Assert.True(barren.Count == 0,
            "Surface element(s) that produce NO probe case, so the matrix silently skips them entirely:\n  "
            + string.Join("\n  ", barren)
            + "\n\nThis is the vacuity failure the whole arrangement exists to prevent: an element with no "
            + "cases passes every cell it has, which is none.");
    }

    [Fact]
    public void The_catalog_produces_a_case_count_in_the_expected_order_of_magnitude()
    {
        var total = Contracts.SurfaceCatalog.Elements.Sum(e => Contracts.SurfaceCatalog.CasesFor(e).Count);
        Assert.InRange(total, 60, 4000);
    }
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceDeclarationTests"`
Expected: FAIL to compile — `SurfaceCatalog` does not exist.

- [ ] **Step 4: Write `SurfaceCatalog`**

Create `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceCatalog.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>One public surface element, as declared.</summary>
/// <param name="UsageName">The name as written in source, with "Attribute" and any generic arity stripped.</param>
public sealed record SurfaceElement(
    Type Type,
    string UsageName,
    SurfaceCategory Category,
    SurfaceEndpoints AppliesTo,
    string? ProbeKey,
    AttributeTargets ValidOn,
    bool AllowMultiple);

/// <summary>
///     One cell input: this element, written this way, at this declaration site.
/// </summary>
/// <param name="Rendered">The attribute exactly as it appears in source, brackets included.</param>
/// <param name="Axis">A short label naming what this case varies, for the failure message.</param>
public sealed record SurfaceCase(SurfaceElement Element, AttributeTargets Site, string Rendered, string Axis);

/// <summary>
///     The shipped surface and its case-space, DERIVED rather than listed.
///     <para>
///         An earlier arrangement kept the option list by hand and caught omissions with a growth ratchet,
///         which is not the same as not having the problem. Everything derivable is derived here: the element
///         set is the assembly's <c>[DwarfSurface]</c>-marked types; the declaration sites are
///         <c>AttributeUsage.ValidOn</c> decomposed; the constructor cases are the public constructors; the
///         property cases are each writable property crossed with its full value domain. Add attribute 33 and
///         it appears in the matrix with no list to remember to update.
///     </para>
/// </summary>
public static class SurfaceCatalog
{
    public static IReadOnlyList<SurfaceElement> Elements { get; } = Build();

    private static readonly Dictionary<SurfaceElement, IReadOnlyList<SurfaceCase>> CaseCache = new();

    public static IReadOnlyList<SurfaceCase> CasesFor(SurfaceElement element)
    {
        lock (CaseCache)
        {
            if (CaseCache.TryGetValue(element, out var cached)) return cached;
            var built = BuildCases(element);
            CaseCache[element] = built;
            return built;
        }
    }

    /// <summary>Every element whose category demands the executed cross-product.</summary>
    public static IReadOnlyList<SurfaceElement> CrossProductElements { get; } =
        Elements.Where(e => e.Category is SurfaceCategory.ConsumerDirective or SurfaceCategory.EmissionShape)
            .ToList();

    private static List<SurfaceElement> Build()
    {
        return typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false } && typeof(Attribute).IsAssignableFrom(t))
            .Select(t => (Type: t, Surface: t.GetCustomAttribute<DwarfSurfaceAttribute>(inherit: false)))
            .Where(x => x.Surface is not null)
            .Select(x =>
            {
                var usage = x.Type.GetCustomAttribute<AttributeUsageAttribute>(inherit: true);
                return new SurfaceElement(
                    x.Type,
                    UsageName(x.Type.Name),
                    x.Surface!.Category,
                    x.Surface.AppliesTo,
                    x.Surface.ProbeKey,
                    usage?.ValidOn ?? AttributeTargets.All,
                    usage?.AllowMultiple ?? false);
            })
            .OrderBy(e => e.UsageName, StringComparer.Ordinal)
            .ThenBy(e => e.Type.GetGenericArguments().Length)
            .ToList();
    }

    /// <summary>The declaration sites this element is legal on, one flag at a time.</summary>
    public static IReadOnlyList<AttributeTargets> SitesOf(SurfaceElement element) =>
        Enum.GetValues<AttributeTargets>()
            .Where(t => t != AttributeTargets.All && int.PopCount((int)t) == 1)
            .Where(t => (element.ValidOn & t) == t)
            .ToList();

    private static List<SurfaceCase> BuildCases(SurfaceElement element)
    {
        var cases = new List<SurfaceCase>();
        var sites = SitesOf(element);
        var ctors = element.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        foreach (var site in sites)
        {
            // Axis 1 — each public constructor overload, with no properties set.
            foreach (var ctor in ctors)
            {
                var args = string.Join(", ", ctor.GetParameters().Select(SampleArgument));
                var rendered = Render(element, args, "");
                cases.Add(new SurfaceCase(element, site, rendered, $"ctor({ctor.GetParameters().Length})"));
            }

            // Axis 2 — each writable property crossed with its FULL value domain, on the shortest ctor.
            var shortest = ctors.OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            var baseArgs = shortest is null
                ? ""
                : string.Join(", ", shortest.GetParameters().Select(SampleArgument));

            foreach (var p in element.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p is { CanWrite: true, CanRead: true }
                                     && p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            foreach (var value in ValueDomain(p, element.Type))
            {
                var rendered = Render(element, baseArgs, $"{p.Name} = {value}");
                cases.Add(new SurfaceCase(element, site, rendered, $"{p.Name}={value}"));
            }

            // Axis 3 — multiplicity, where the attribute permits it.
            if (element.AllowMultiple && ctors.Length > 0)
            {
                var one = Render(element, string.Join(", ",
                    ctors[0].GetParameters().Select(SampleArgument)), "");
                var two = Render(element, string.Join(", ",
                    ctors[0].GetParameters().Select(p => SampleArgument(p, variant: 2))), "");
                cases.Add(new SurfaceCase(element, site, one + "\n" + two, "×2"));
            }
        }

        return cases;
    }

    /// <summary>
    ///     Every value a property can take that differs from its default — the FULL domain, not the first
    ///     alternative. A three-member enum whose second and third members were never probed reads exactly
    ///     like one that was fully covered.
    /// </summary>
    private static IEnumerable<string> ValueDomain(PropertyInfo p, Type declaring)
    {
        object? def = null;
        try
        {
            var ctor = declaring.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
            var instance = ctor.Invoke(ctor.GetParameters().Select(SampleValue).ToArray());
            def = p.GetValue(instance);
        }
        catch (Exception)
        {
            // Some attributes cannot be constructed reflectively (open generics). The domain is still the
            // type's full value set; the default is simply unknown, so every value is emitted.
        }

        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

        if (t == typeof(bool))
            return new[] { "true", "false" }.Where(v => def is null || !v.Equals(def.ToString()?.ToLowerInvariant(),
                StringComparison.Ordinal));

        if (t.IsEnum)
            return Enum.GetValues(t).Cast<object>()
                .Where(v => def is null || !v.Equals(def))
                .Select(v => $"{t.Name}.{v}");

        if (t == typeof(int))
            return [def is int i ? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "1"];

        if (t == typeof(string))
            return ["\"probe\""];

        if (t == typeof(Type))
            return ["typeof(Dst)"];

        throw new InvalidOperationException(
            $"No value domain for {declaring.Name}.{p.Name} of type {t.Name}. Add one rather than letting the "
            + "property silently fall out of the matrix — an unprobed property is exactly the hole this "
            + "catalogue exists to close.");
    }

    /// <summary>A compilable literal for a constructor parameter.</summary>
    private static string SampleArgument(ParameterInfo p, int variant = 1)
    {
        var t = p.ParameterType;
        if (t == typeof(string)) return variant == 1 ? "\"Name\"" : "\"Id\"";
        if (t == typeof(Type)) return "typeof(Dst)";
        if (t == typeof(bool)) return "true";
        if (t == typeof(int)) return variant.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (t.IsEnum) return $"{t.Name}.{Enum.GetNames(t)[0]}";
        throw new InvalidOperationException(
            $"No sample argument for parameter '{p.Name}' of type {t.Name}. Add one; skipping it would drop "
            + "the constructor overload from the matrix without saying so.");
    }

    private static object? SampleValue(ParameterInfo p)
    {
        var t = p.ParameterType;
        if (t == typeof(string)) return "Name";
        if (t == typeof(Type)) return typeof(object);
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return 1;
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        throw new InvalidOperationException($"No sample value for {p.ParameterType.Name}.");
    }

    /// <summary>Writes the attribute as it appears in source, including any generic type arguments.</summary>
    private static string Render(SurfaceElement element, string ctorArgs, string namedArgs)
    {
        var generics = element.Type.GetGenericArguments().Length switch
        {
            0 => "",
            1 => "<Dst>",
            2 => "<Src, Dst>",
            _ => throw new InvalidOperationException(
                $"{element.UsageName} has arity {element.Type.GetGenericArguments().Length}; add a rendering.")
        };

        var args = string.Join(", ", new[] { ctorArgs, namedArgs }.Where(s => !string.IsNullOrEmpty(s)));
        return args.Length == 0
            ? $"[{element.UsageName}{generics}]"
            : $"[{element.UsageName}{generics}({args})]";
    }

    private static string UsageName(string typeName)
    {
        var tick = typeName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0) typeName = typeName[..tick];
        return typeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? typeName[..^"Attribute".Length]
            : typeName;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceDeclarationTests"`

Expected: all five tests PASS. If `ValueDomain` or `SampleArgument` throws for a real property or parameter
type, **add the case to the switch** rather than catching the exception — the throw is deliberate and its
message says why.

- [ ] **Step 6: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Contracts/RepoPaths.cs \
        tests/DwarfMapper.Generator.Tests/Contracts/SurfaceCatalog.cs \
        tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs
git commit -m "test(surface): derive the case-space from the declaration instead of listing it"
```

---

### Task 3: Probe fixtures and the `ProbeKey` bijection

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceFixtures.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs`
- Modify: attribute declarations that need a `ProbeKey`

**Interfaces:**
- Produces: `[AttributeUsage(AttributeTargets.Field)] internal sealed class SurfaceProbeAttribute(string key)`
  with `string Key { get; }`.
- Produces: `internal static class SurfaceFixtures` with `IReadOnlyDictionary<string, string> All` (key → DTO
  source text) and `string? Get(string? key)`.
- Produces: `internal static IReadOnlyDictionary<string, string> OptionCatalog.ProbeKeys` — option property
  name → fixture key. This is the second demand source; it is NOT duplication of
  `[DwarfSurface(ProbeKey)]`, which sits on a type and cannot address a single property.

**Visibility:** `internal`, not `public` — Task 2 established that these types carry `internal`-typed members
(`SurfaceCategory`, `SurfaceEndpoints`), and a `public` record exposing them does not compile (CS0051).

- [ ] **Step 1: Write the failing bijection test**

Append to `SurfaceDeclarationTests.cs`:

```csharp
    [Fact]
    public void Every_declared_ProbeKey_binds_to_exactly_one_fixture()
    {
        // There are TWO demand sources, and they are not interchangeable. An element-level key sits on an
        // attribute TYPE via [DwarfSurface(ProbeKey = ...)]. A property-level key comes from OptionCatalog's
        // option->key map, because the class-level options are PROPERTIES of one type (DwarfMapperAttribute)
        // and a type-level attribute structurally cannot express a per-property shape. Checking only the
        // first source reports every property-level fixture as an orphan.
        var demanded = Contracts.SurfaceCatalog.Elements
            .Select(e => e.ProbeKey)
            .Where(k => k is not null)
            .Select(k => k!)
            .Concat(Contracts.OptionCatalog.ProbeKeys.Values)
            .ToHashSet(StringComparer.Ordinal);

        var supplied = Contracts.SurfaceFixtures.All.Keys.ToHashSet(StringComparer.Ordinal);

        var unbound = demanded.Except(supplied).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(unbound.Count == 0,
            "ProbeKey(s) demanded with no fixture in SurfaceFixtures: " + string.Join(", ", unbound)
            + ". The declaration states a demand; the fixture is the supply. An unbound key means the probe "
            + "silently falls back to the flat DTO pair, which cannot trigger it, and the cell reads "
            + "'no effect' while the feature works perfectly. Check both demand sources: "
            + "[DwarfSurface(ProbeKey = ...)] in src, and OptionCatalog.ProbeKeys.");

        var orphaned = supplied.Except(demanded).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(orphaned.Count == 0,
            "Fixture(s) in SurfaceFixtures claimed by neither a [DwarfSurface(ProbeKey = ...)] nor an "
            + "OptionCatalog.ProbeKeys entry: " + string.Join(", ", orphaned)
            + ". An orphaned fixture is dead weight that reads as coverage.");
    }
```

**Both sets must come out at 14.** `TriggeringShapes` holds 14 fixtures, all of them class-level option
shapes; the nine element-level `ProbeKey` declarations in Step 4 reuse six of those keys rather than adding
new ones. Checking only the element-level source reports eight false orphans — that is a defect in an earlier
draft of this plan, corrected here.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "Every_declared_ProbeKey"`
Expected: FAIL to compile — `SurfaceFixtures` does not exist.

- [ ] **Step 3: Write `SurfaceFixtures`**

Create `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceFixtures.cs`. Seed it by **moving**
`OptionCatalog.TriggeringShapes` here verbatim (do not retype the fixtures — they carry hard-won comments
explaining why each shape triggers its option; those comments are the value). Give each entry a stable key:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>Marks a fixture field as the supply for a <c>[DwarfSurface(ProbeKey = ...)]</c> demand.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class SurfaceProbeAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}

/// <summary>
///     The one thing that genuinely cannot be derived: the SHAPE that makes a surface element observable. No
///     amount of reflection over <c>AutoNest</c> yields "you need a nested class pair here". These are inputs
///     to the experiment, not a description of the API — and an element whose declared key has no fixture is
///     reported rather than quietly assumed fine.
///     <para>
///         Every fixture must declare types named <c>Src</c> and <c>Dst</c>, because
///         <c>EndpointSources.Build</c> writes method signatures against those names.
///     </para>
/// </summary>
public static class SurfaceFixtures
{
    [SurfaceProbe("nested-pair")]
    private static readonly string NestedPair = """
        public sealed class Inner { public int X { get; set; } }
        public sealed class InnerDto { public int X { get; set; } }
        public sealed class Src { public int Id { get; set; } public Inner Child { get; set; } = new(); }
        public sealed class Dst { public int Id { get; set; } public InnerDto Child { get; set; } = new(); }
        """;

    [SurfaceProbe("recursive-graph")]
    private static readonly string RecursiveGraph = """
        public sealed class Node { public int Id { get; set; } public Node? Next { get; set; } }
        public sealed class NodeDto { public int Id { get; set; } public NodeDto? Next { get; set; } }
        public sealed class Src { public int Id { get; set; } public Node? Root { get; set; } }
        public sealed class Dst { public int Id { get; set; } public NodeDto? Root { get; set; } }
        """;

    // … carry over the remaining TriggeringShapes entries from OptionCatalog with these keys:
    //   AllowNonPublic        -> "internal-member"
    //   NameConvention        -> "snake-case-member"
    //   CaseInsensitive       -> "case-mismatched-member"
    //   IgnoreObsoleteMembers -> "obsolete-member"
    //   SkipNullSourceMembers -> "nullable-source-nonnull-target"
    //   NullStrategy          -> "nullable-value-to-nonnull"
    //   RequiredMapping       -> "unconsumed-source-member"
    //   EnumStrategy          -> "divergent-order-enums"
    //   EnumStringSource      -> "described-enum-to-string"
    //   NullCollections       -> "nullable-collection-rebuild"
    //   ImplicitConversions   -> "narrowing-conversion"
    //   ReferenceHandling     -> "shared-reference-graph"
    //   AutoNest / MaxDepth / OnCycle reuse "nested-pair" and "recursive-graph" above.

    public static IReadOnlyDictionary<string, string> All { get; } =
        typeof(SurfaceFixtures)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(f => (Probe: f.GetCustomAttribute<SurfaceProbeAttribute>(), Value: f.GetValue(null)))
            .Where(x => x.Probe is not null)
            .ToDictionary(x => x.Probe!.Key, x => (string)x.Value!, StringComparer.Ordinal);

    /// <summary>The fixture for a key, or null for "the default flat DTO pair is sufficient".</summary>
    public static string? Get(string? key) =>
        key is not null && All.TryGetValue(key, out var text) ? text : null;
}
```

- [ ] **Step 4: Declare the keys in `src`**

Add `ProbeKey` to the `[DwarfSurface]` line of the elements that need a shape. Class-level options live on
`DwarfMapperAttribute` itself, which needs a *different* fixture per property — that is Task 5's job, handled
by `OptionCatalog` continuing to own the per-property mapping. At **element** level, set:

| Type | `ProbeKey` |
|---|---|
| `AutoNestAttribute` | `"nested-pair"` |
| `FlattenAttribute` | `"nested-pair"` |
| `FlattenGraphAttribute` | `"nested-pair"` |
| `MapCollectionKeyAttribute` | `"nullable-collection-rebuild"` |
| `ReinterpretAttribute` | `"narrowing-conversion"` |
| `MapNullSkipAttribute` | `"nullable-source-nonnull-target"` |
| `MapNullSkipAttribute<TSource, TTarget>` | `"nullable-source-nonnull-target"` |
| `MapIgnoreSourceAttribute` | `"unconsumed-source-member"` |
| `DwarfMapperConstructorAttribute` | `"internal-member"` |

Leave every other element's `ProbeKey` unset — the flat pair in `EndpointSources.Types` is sufficient, and an
unnecessary fixture is an orphan the bijection test will reject.

- [ ] **Step 5: Run the bijection test**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceDeclarationTests"`
Expected: all PASS. Iterate on the two lists in the failure message until both are empty.

- [ ] **Step 6: Point `OptionCatalog` at the shared fixtures**

Modify `Contracts/OptionCatalog.cs`: delete the `TriggeringShapes` dictionary body and replace lookups with
`SurfaceFixtures.Get(keyForOption)`, keeping a small `option name → probe key` map in `OptionCatalog` (that
mapping is per-property, which `[DwarfSurface]` cannot express — it sits on the type, not the property).

Run the existing option matrix to confirm nothing regressed:
`dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~Contracts"`
Expected: PASS, with the same result as before the move.

- [ ] **Step 7: Commit**

```bash
git add src/DwarfMapper tests/DwarfMapper.Generator.Tests/Contracts \
        tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceDeclarationTests.cs
git commit -m "test(surface): bind probe fixtures to declarations, both directions"
```

---

### Task 4: `SurfaceProbe` — classify one cell by running the generator

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceProbe.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/Contracts/Endpoints.cs`

**Interfaces:**
- Consumes: `SurfaceCase`, `SurfaceFixtures` (Task 3); `EndpointSources.Build`, `Endpoint`,
  `GeneratorTestHarness.RunAll(string) → (ImmutableArray<Diagnostic>, string)` (existing).
- Produces: `enum SurfaceEffect { Honoured, Refused, Silent, UnhonouredButLoud, NotCompilable, NoSuchSite }`.
- Produces: `static (SurfaceEffect Effect, string Detail) SurfaceProbe.Classify(SurfaceCase c, Endpoint e)`.

- [ ] **Step 1: Extend `EndpointSources.Build` with a site parameter**

`Build` currently accepts `memberAttribute` (goes on the method) and `classAttribute`. The matrix needs to
place an attribute at an arbitrary `AttributeTargets` site. Add:

```csharp
    /// <summary>
    ///     Places <paramref name="rendered" /> at the declaration site <paramref name="site" /> for this
    ///     endpoint, or returns null when the endpoint has no such site at all — a co-located host has no
    ///     mapping METHOD to annotate, and the registry front door has no mapper CLASS. Null is a distinct
    ///     answer from "the attribute did nothing": one means there was no cell, the other means the cell was
    ///     empty.
    /// </summary>
    public static string? BuildAt(Endpoint endpoint, AttributeTargets site, string rendered,
        string? types = null)
    {
        return site switch
        {
            AttributeTargets.Class when endpoint is Endpoint.Registry
                => null, // the registry has no mapper class; intent lives on the source type
            AttributeTargets.Class
                => Build(endpoint, classAttribute: rendered, types: types),
            AttributeTargets.Method when endpoint is Endpoint.Registry or Endpoint.CoLocatedHost
                => null, // neither endpoint declares a mapping method to annotate
            AttributeTargets.Method
                => Build(endpoint, memberAttribute: rendered, types: types),
            AttributeTargets.Property or AttributeTargets.Field
                => Build(endpoint, memberAttribute: rendered, types: types),
            // The assembly attribute must sit AFTER the using block and BEFORE the file-scoped namespace.
            // Prepending it to Build's output is CS1529 ("a using clause must precede all other elements")
            // for every assembly-site cell — and a syntactically broken template is indistinguishable from
            // a genuine NotCompilable verdict from the outside, so verify this one by compiling it.
            AttributeTargets.Assembly
                => InsertAssemblyAttribute(Build(endpoint, types: types), rendered),
            AttributeTargets.Struct or AttributeTargets.Constructor
                => null, // no fixture in the endpoint set declares one; add a shape before claiming the site
            _ => null
        };
    }
```

**Note:** `AttributeTargets.Property`/`Field` for the non-registry endpoints places the attribute on the
mapping method, which is wrong for member-form attributes. If a cell fails for that reason, extend
`EndpointSources` with a real member slot rather than working around it in the probe — the whole point is that
adding a site is a single edit in `Endpoints.cs`.

**Two template defects to fix while you are here** (both found empirically in the first implementation):

- The `CoLocatedHost` template in `Build` consumes only `classAttribute` and ignores `memberAttribute`, so a
  member-form attribute vanishes and the cell reads `Silent` while never having been tested. Add a real member
  slot to that template.
- The `Assembly` branch above, if written as a prepend, is CS1529 for every assembly-site cell.

**Then close the gap that let both hide** — add this guard, which catches any future template that forgets a
slot:

```csharp
    [Theory]
    [MemberData(nameof(EveryEndpointAndSite))]
    public void BuildAt_either_declines_the_cell_or_actually_places_the_attribute(
        Endpoint endpoint, AttributeTargets site)
    {
        // A vacuous Silent is worse than a missing cell. If BuildAt returns a source at all, it is claiming
        // this cell exists — and a source that silently drops the attribute reads as "the element did
        // nothing" when in truth the element was never there. Task 5 triages Silent cells into "structurally
        // inapplicable, narrow the claim", so a dropped attribute would permanently narrow an AppliesTo on
        // the strength of a measurement bug.
        const string rendered = "[MapIgnore(\"Name\")]";
        var source = EndpointSources.BuildAt(endpoint, site, rendered);
        if (source is null) return;   // NoSuchSite — an honest refusal to judge

        Assert.Contains(rendered, source, StringComparison.Ordinal);
    }
```

A genuine `NotCompilable` verdict and a syntactically broken harness template look identical from the outside.
Verify the assembly-site fix by actually compiling one, not by reading it.

- [ ] **Step 2: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceProbeTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

public sealed class SurfaceProbeTests
{
    [Fact]
    public void A_MapIgnore_on_a_create_map_is_observed_as_honoured()
    {
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "MapIgnore", StringComparison.Ordinal)
            && e.Type.GetGenericArguments().Length == 0);
        var c = new SurfaceCase(element, AttributeTargets.Method, "[MapIgnore(\"Name\")]", "ctor(1)");

        var (effect, detail) = SurfaceProbe.Classify(c, Endpoint.CreateMap);

        Assert.True(effect is SurfaceEffect.Honoured or SurfaceEffect.Refused,
            $"[MapIgnore] at CreateMap classified as {effect} ({detail}). If this reads Silent, the probe is "
            + "not observing correctly and every cell built on it is vacuous.");
    }

    [Fact]
    public void A_class_only_attribute_on_a_method_is_NotCompilable()
    {
        // Pins AttributeUsage to reality. Nothing else in the repository does: the declaration says
        // AttributeTargets.Class and no test ever tries the illegal site to confirm the compiler agrees.
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "DwarfMapper", StringComparison.Ordinal));
        var c = new SurfaceCase(element, AttributeTargets.Method, "[DwarfMapper]", "illegal-site");

        var (effect, _) = SurfaceProbe.Classify(c, Endpoint.CreateMap);

        Assert.Equal(SurfaceEffect.NotCompilable, effect);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceProbeTests"`
Expected: FAIL to compile — `SurfaceProbe` does not exist.

- [ ] **Step 4: Write `SurfaceProbe`**

Create `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceProbe.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>What a surface element DOES at one endpoint, observed rather than declared.</summary>
public enum SurfaceEffect
{
    /// <summary>The element changed the emitted output.</summary>
    Honoured,

    /// <summary>The element produced a diagnostic — a refusal the caller can see.</summary>
    Refused,

    /// <summary>Accepted, changed nothing observable, compiled. The dangerous one.</summary>
    Silent,

    /// <summary>
    ///     Changed nothing, but the build fails at this endpoint anyway. Distinguished from
    ///     <see cref="Silent" /> deliberately: silence only ships wrong data when the code COMPILES.
    /// </summary>
    UnhonouredButLoud,

    /// <summary>
    ///     The site is illegal per <c>AttributeUsage</c> and the compiler rejects it — the declaration is
    ///     telling the truth.
    /// </summary>
    NotCompilable,

    /// <summary>The endpoint has no such declaration site, so there is no cell here to judge.</summary>
    NoSuchSite
}

/// <summary>
///     Observes what one surface case does at one endpoint, by compiling the same source with and without it.
///     <para>
///         Generalizes <see cref="OptionProbe" /> from the class-level option surface to every declared
///         element. The baseline compile is MEMOIZED per (endpoint, fixture): every case sharing a fixture
///         shares one baseline, which roughly halves a matrix of this size.
///     </para>
/// </summary>
public static class SurfaceProbe
{
    private static readonly ConcurrentDictionary<string, (string[] Keys, string Generated)> Baselines = new();

    public static (SurfaceEffect Effect, string Detail) Classify(SurfaceCase c, Endpoint endpoint)
    {
        var types = SurfaceFixtures.Get(c.Element.ProbeKey);
        var source = EndpointSources.BuildAt(endpoint, c.Site, c.Rendered, types);
        if (source is null) return (SurfaceEffect.NoSuchSite, "endpoint has no such declaration site");

        var (diagnostics, generated) = GeneratorTestHarness.RunAll(source);

        // A CS-prefixed error means the compiler rejected the placement — AttributeUsage said no. That is a
        // different answer from any DWARF diagnostic, which means the generator saw it and objected.
        var compilerError = diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
        if (compilerError is not null)
            return (SurfaceEffect.NotCompilable, compilerError.Id);

        var (baseKeys, baseline) = Baseline(endpoint, c.Element.ProbeKey, types);

        var added = diagnostics
            .Where(d => !baseKeys.Contains(d.Id + ":" + d.Severity, StringComparer.Ordinal))
            // DWARF078 is the cascade signpost that accompanies ANY blocking error; including it appends a
            // meaningless suffix to every refused cell and tells the reader nothing about the element.
            .Where(d => !string.Equals(d.Id, "DWARF078", StringComparison.Ordinal))
            .Select(d => d.Severity == DiagnosticSeverity.Error ? d.Id : $"{d.Id} ({d.Severity})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (added.Count > 0) return (SurfaceEffect.Refused, string.Join(",", added));
        if (!string.Equals(generated, baseline, StringComparison.Ordinal))
            return (SurfaceEffect.Honoured, "output differs");
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return (SurfaceEffect.UnhonouredButLoud, "unhonoured, but the build fails regardless");

        return (SurfaceEffect.Silent, "no diagnostic, identical output, compiles");
    }

    private static (string[] Keys, string Generated) Baseline(Endpoint endpoint, string? probeKey,
        string? types)
    {
        var cacheKey = $"{endpoint}|{probeKey ?? "<flat>"}";
        return Baselines.GetOrAdd(cacheKey, _ =>
        {
            var (d, g) = GeneratorTestHarness.RunAll(EndpointSources.Build(endpoint, types: types));
            return (d.Select(x => x.Id + ":" + x.Severity).Distinct(StringComparer.Ordinal).ToArray(), g);
        });
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceProbeTests"`
Expected: both PASS.

**It does report `Silent`, and the remedy below is the correct one — treat it as part of the task, not a
contingency.** `RunAll` returns only the generator's self-reported diagnostics, never the final compilation's
compiler verdict. Use `GeneratorTestHarness.RunAndGetCompilationErrors` for the `NotCompilable` determination
and keep `RunAll` for the effect comparison.

**And subtract the baseline's CS errors before declaring `NotCompilable`.** Whenever a fixture already carries
a blocking DWARF error, the baseline compilation also carries `CS8795` (an unimplemented partial method),
because the generator declined to implement it. Attributing that to the element under test misclassifies
*every case sharing that fixture* as `NotCompilable` — a whole fixture's worth of cells silently exempted from
judgement. Only NEW compiler errors count, exactly as only new diagnostics count in `OptionProbe`. This is the
same principle applied to a second channel, and it is the difference between a matrix that measures the
element and one that measures the fixture.

- [ ] **Step 6: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Contracts
git commit -m "test(surface): classify a cell by running the generator, not by reading a list"
```

---

### Task 5: The bidirectional parity matrix

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceParityTests.cs`
- Modify: `src/DwarfMapper/*.cs` — narrow `AppliesTo` where the matrix proves the claim wrong

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces: `static class DeclaredDivergences` moved here in Task 7; until then this task references
  `OptionGaps.KnownSilent`.

- [ ] **Step 1: Write the matrix**

Create `tests/DwarfMapper.Generator.Tests/Contracts/SurfaceParityTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     The executed cross-product: every surface element, at every legal declaration site, in every case its
///     declaration admits, at every endpoint.
///     <para>
///         Verified in BOTH directions against the element's own <c>AppliesTo</c> claim. A claimed endpoint
///         where the element does nothing observable fails (the claim over-reaches); an unclaimed endpoint
///         where it changes the output fails too (the claim under-reaches and the matrix would otherwise skip
///         a live cell). There is therefore no value of <c>AppliesTo</c> that passes vacuously.
///     </para>
///     <para>
///         Traited so it can run as its own CI leg: this is roughly seven times the work of the option matrix.
///     </para>
/// </summary>
[Trait("Category", "SurfaceMatrix")]
public sealed class SurfaceParityTests
{
    public static TheoryData<string, string, string, Endpoint> Cells()
    {
        var data = new TheoryData<string, string, string, Endpoint>();
        foreach (var element in SurfaceCatalog.CrossProductElements)
        foreach (var c in SurfaceCatalog.CasesFor(element))
        foreach (var endpoint in EndpointSources.All)
            data.Add(element.UsageName, c.Axis, c.Site.ToString(), endpoint);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Every_cell_matches_the_elements_own_AppliesTo_claim(
        string usageName, string axis, string site, Endpoint endpoint)
    {
        var (element, c) = Resolve(usageName, axis, site);
        var claimed = (element.AppliesTo & ToFlag(endpoint)) != 0;
        var (effect, detail) = SurfaceProbe.Classify(c, endpoint);

        // No cell to judge: the endpoint has no such site, or AttributeUsage forbids it and the compiler
        // agrees. Both are the declaration telling the truth.
        if (effect is SurfaceEffect.NoSuchSite or SurfaceEffect.NotCompilable) return;

        if (claimed)
        {
            if (effect is SurfaceEffect.Honoured or SurfaceEffect.Refused
                or SurfaceEffect.UnhonouredButLoud) return;

            // Task 7 renames this to DeclaredDivergences.Reasons. Use the current name here so this task
            // compiles on its own; update the reference as part of that rename, not before it.
            if (OptionGaps.KnownSilent.TryGetValue($"{usageName}@{endpoint}", out var why))
            {
                Assert.False(string.IsNullOrWhiteSpace(why));
                return;
            }

            Assert.Fail(
                $"{c.Rendered} on a {site} CLAIMS {endpoint} (AppliesTo) but is SILENT there: no diagnostic, "
                + $"and output byte-identical to the same source without it. ({detail})\n\n"
                + "The caller wrote something, the generator accepted it, changed nothing, and said nothing. "
                + "Three ways out, in order of preference:\n"
                + $"  1. honour it at {endpoint};\n"
                + $"  2. refuse it there with a diagnostic;\n"
                + $"  3. drop {endpoint} from this element's AppliesTo — but only if it STRUCTURALLY cannot "
                + "apply, not because it currently does not.");
        }
        else
        {
            if (effect is SurfaceEffect.Silent or SurfaceEffect.UnhonouredButLoud) return;

            Assert.Fail(
                $"{c.Rendered} on a {site} does NOT claim {endpoint} (AppliesTo) but is {effect} there "
                + $"({detail}). The claim under-reaches: this cell is live and the matrix was told to skip "
                + $"it. Add {endpoint} to the element's AppliesTo.");
        }
    }

    [Fact]
    public void The_matrix_is_not_vacuous()
    {
        // If Classify() broke so every cell read NoSuchSite, the theory would return at the first guard and
        // pass without comparing anything.
        var acting = 0;
        foreach (var element in SurfaceCatalog.CrossProductElements)
        {
            var c = SurfaceCatalog.CasesFor(element).FirstOrDefault();
            if (c is null) continue;
            if (SurfaceProbe.Classify(c, Endpoint.CreateMap).Effect
                is SurfaceEffect.Honoured or SurfaceEffect.Refused) acting++;
        }

        Assert.True(acting >= 15,
            $"Only {acting} elements visibly act at CreateMap. Either the fixtures stopped triggering their "
            + "elements or Classify() is not observing correctly — in both cases the matrix is passing "
            + "without testing anything.");
    }

    private static (SurfaceElement, SurfaceCase) Resolve(string usageName, string axis, string site)
    {
        var element = SurfaceCatalog.CrossProductElements
            .First(e => string.Equals(e.UsageName, usageName, StringComparison.Ordinal));
        var c = SurfaceCatalog.CasesFor(element)
            .First(x => string.Equals(x.Axis, axis, StringComparison.Ordinal)
                        && string.Equals(x.Site.ToString(), site, StringComparison.Ordinal));
        return (element, c);
    }

    private static SurfaceEndpoints ToFlag(Endpoint e) =>
        Enum.Parse<SurfaceEndpoints>(e.ToString());
}
```

- [ ] **Step 2: Run it and expect a long red**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceParityTests"`

Expected: **FAIL, with many failures.** This is the design working. Do not suppress them.

- [ ] **Step 3: Triage every failure into one of three buckets**

Work through the failures. For each, decide and record:

- **Under-reach** (`does NOT claim X but is Honoured there`) → add the endpoint to that element's `AppliesTo`
  in `src`. Mechanical, no judgement needed.
- **Structural** (`CLAIMS X but is SILENT`, and the element genuinely cannot apply there — e.g. an
  update-into map has no `source.ToTarget()` form to suppress) → remove the endpoint from `AppliesTo` and put
  the reason in the XML doc comment on that `[DwarfSurface]` line.
- **A real divergence** (`CLAIMS X but is SILENT`, and it *should* work there) → **stop and report it to the
  maintainer.** Do not add it to `DeclaredDivergences` yourself. These are the findings this whole exercise
  exists to produce, and each one is a maintainer decision about whether to fix the generator or record the
  gap.

Write the triage into `Issues/round20/SURFACE-MATRIX-FINDINGS.md` as you go: one row per failure with the
bucket, the cell, and the reason. That file is the deliverable of this task as much as the code is.

- [ ] **Step 4: Re-run until green or blocked**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceParityTests"`

Green means every remaining cell is either honoured, refused, structurally inapplicable per a declared claim,
or a maintainer-approved `DeclaredDivergences` row.

- [ ] **Step 5: Add the CI leg**

Add a job (or a step in the existing workflow) running only this trait:

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj \
  --filter "Category=SurfaceMatrix"
```

and exclude it from the default leg with `--filter "Category!=SurfaceMatrix"` so ordinary runs stay fast.
Find the existing workflow under `.github/workflows/` and follow its structure.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper tests/DwarfMapper.Generator.Tests/Contracts \
        Issues/round20/SURFACE-MATRIX-FINDINGS.md .github/workflows
git commit -m "test(surface): the executed cross-product, verified against each element's own claim"
```

---

### Task 6: Category obligations replace the allowlists

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceObligationTests.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/OptionSurfaceCoverageTests.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/AssemblyScanTests.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/Contracts/OptionGaps.cs`

- [ ] **Step 1: Write the obligation tests**

Create `tests/DwarfMapper.Generator.Tests/SelfValidation/SurfaceObligationTests.cs`. One `[Theory]` per
category, each iterating `SurfaceCatalog.Elements.Where(e => e.Category == X)`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     One obligation per category. There is deliberately no category with no obligation: this file is what
///     replaced six allowlist dictionaries, and it replaced them by REDIRECTING each excuse into a different
///     demand rather than by accepting it.
///     <para>
///         "Cannot be shown in a runnable sample because its effect is a build error" used to be a waiver.
///         Here it is <see cref="SurfaceCategory.BuildFailureOnly" />, which demands a NegativeCases row
///         pinning the id AND its remedy wording, plus a positive row proving the non-failing path compiles.
///     </para>
/// </summary>
public sealed class SurfaceObligationTests
{
    private static string ConsumerCorpus { get; } = string.Concat(
        RepoPaths.ConsumerRoots.SelectMany(RepoPaths.SourceFiles).Select(File.ReadAllText));

    private static string SampleCorpus { get; } =
        string.Concat(RepoPaths.SourceFiles(RepoPaths.Samples).Select(File.ReadAllText));

    private static string NegativeCorpus { get; } = string.Concat(
        RepoPaths.SourceFiles(Path.Combine(RepoPaths.Tests, "DwarfMapper.NegativeCases"))
            .Select(File.ReadAllText));

    public static TheoryData<string> Of(SurfaceCategory category)
    {
        var data = new TheoryData<string>();
        foreach (var e in SurfaceCatalog.Elements.Where(e => e.Category == category))
            data.Add(e.UsageName);
        return data;
    }

    public static TheoryData<string> ConsumerDirectives() => Of(SurfaceCategory.ConsumerDirective);
    public static TheoryData<string> BuildFailureOnly() => Of(SurfaceCategory.BuildFailureOnly);
    public static TheoryData<string> CrossAssembly() => Of(SurfaceCategory.CrossAssembly);
    public static TheoryData<string> GeneratorEmitted() => Of(SurfaceCategory.GeneratorEmitted);

    [Fact]
    public void Every_corpus_is_non_empty()
    {
        // A mistyped path makes every obligation below pass by finding nothing to check.
        Assert.False(string.IsNullOrWhiteSpace(ConsumerCorpus), "Consumer corpus is empty — check RepoPaths.ConsumerRoots.");
        Assert.False(string.IsNullOrWhiteSpace(SampleCorpus), "Sample corpus is empty — check RepoPaths.Samples.");
        Assert.False(string.IsNullOrWhiteSpace(NegativeCorpus), "NegativeCases corpus is empty.");
    }

    [Theory]
    [MemberData(nameof(ConsumerDirectives))]
    public void A_consumer_directive_is_used_in_a_consumer_assembly_and_a_runnable_sample(string usageName)
    {
        Assert.True(ConsumerCorpus.Contains("[" + usageName, StringComparison.Ordinal),
            $"[{usageName}] is a ConsumerDirective with NO use in any consumer-shaped assembly "
            + $"({string.Join(", ", RepoPaths.ConsumerRoots.Select(Path.GetFileName))}). Thirteen public "
            + "attributes sat in exactly this state, including the documented remedy for DWARF039. Add a "
            + "corpus row with a stated MAPS/OPT-IN/REFUSES expectation — or, if the element genuinely is "
            + "not consumer-authored, change its category and take on that category's obligation instead.");

        Assert.True(SampleCorpus.Contains("[" + usageName, StringComparison.Ordinal)
                    || SampleCorpus.Contains("assembly: " + usageName, StringComparison.Ordinal),
            $"[{usageName}] is a ConsumerDirective demonstrated in NO runnable sample. Add a Conformance "
            + "feature (samples/DwarfMapper.Conformance) asserting its observable runtime difference, or a "
            + "Gallery example if it deserves prose.");
    }

    [Theory]
    [MemberData(nameof(BuildFailureOnly))]
    public void A_build_failure_only_element_has_a_pinned_diagnostic_and_a_passing_control(string usageName)
    {
        Assert.True(NegativeCorpus.Contains("[" + usageName, StringComparison.Ordinal)
                    || NegativeCorpus.Contains("assembly: " + usageName, StringComparison.Ordinal),
            $"[{usageName}] is BuildFailureOnly — its whole observable effect is a diagnostic — but no "
            + "NegativeCases row exercises it. That category is not a waiver: it REDIRECTS the obligation "
            + "from 'demonstrate it in a sample' to 'pin the diagnostic and its remedy wording'. Add the row.");
    }

    [Theory]
    [MemberData(nameof(CrossAssembly))]
    public void A_cross_assembly_element_is_exercised_by_a_multi_assembly_fixture(string usageName)
    {
        var multiAssembly = string.Concat(
            RepoPaths.SourceFiles(Path.Combine(RepoPaths.Tests, "DwarfMapper.IntegrationTests"))
                .Select(File.ReadAllText));

        Assert.True(multiAssembly.Contains("[" + usageName, StringComparison.Ordinal)
                    || multiAssembly.Contains("assembly: " + usageName, StringComparison.Ordinal),
            $"[{usageName}] is CrossAssembly — only observable across an assembly boundary — but no "
            + "multi-assembly fixture uses it. A single-assembly probe validates nothing here, which is "
            + "precisely why it is not on the cross-product obligation.");
    }

    [Theory]
    [MemberData(nameof(GeneratorEmitted))]
    public void A_generator_emitted_element_is_produced_by_a_generator_run_and_consumed(string usageName)
    {
        var testCorpus = string.Concat(
            RepoPaths.SourceFiles(Path.Combine(RepoPaths.Tests, "DwarfMapper.Generator.Tests"))
                .Select(File.ReadAllText));

        // Produced: a test asserts the generator EMITS it. Consumed: a test asserts the reading side acts on
        // it. Both halves, or the manifest is written and never read.
        Assert.True(testCorpus.Contains(usageName, StringComparison.Ordinal),
            $"[{usageName}] is GeneratorEmitted but no generator test mentions it — nothing proves the "
            + "generator emits it, and nothing proves the reading side consumes it.");
    }
}
```

- [ ] **Step 2: Run it and expect red**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~SurfaceObligationTests"`

Expected: FAIL, listing roughly the thirteen attributes round 19 named plus any others. Each failure is a
corpus row to write, **not** an entry to allowlist.

- [ ] **Step 3: Satisfy the obligations**

Add the missing corpus rows. Priority order (highest real-world frequency first): `[BeforeMap]`,
`[DwarfMapperDefaults]`, `[DwarfMapperOptions]`, `[MapIgnoreSource]`, `[FlattenGraph]`, then the rest.

Each consumer-corpus row must carry a stated expectation in a comment — `// MAPS:`, `// OPT-IN:` or
`// REFUSES:` — matching the existing corpus convention. Read a few existing rows in
`tests/DwarfMapper.ConsumerTests` first and match them.

- [ ] **Step 4: Delete the allowlists**

Now — and only now, with the obligations passing — remove:

- `OptionSurfaceCoverageTests.NotDemonstrable` (3 entries) and its whole
  `Every_class_level_option_is_demonstrated_in_a_runnable_sample` test. Its job is now
  `A_consumer_directive_is_used_in_a_consumer_assembly_and_a_runnable_sample`. The three excused options —
  `MaxDepth`, `GenerateExtensions`, `ImplicitConversions` — must be re-homed: `ImplicitConversions` and
  `MaxDepth` are `BuildFailureOnly` behaviours on `[DwarfMapper]`, `GenerateExtensions` is `EmissionShape`.
  Since these are *properties* of one element rather than elements, add a small
  `PropertyCategoryOverrides` map in `SurfaceObligationTests` keyed `"DwarfMapper.MaxDepth"` etc., and assert
  the redirected obligation for each. **Do not simply drop them.**
- `OptionSurfaceCoverageTests.AttributeNotDemonstrable` (5 entries) and its test. Each of the five is now
  covered by its category: `DwarfProvidesMap`/`DwarfRequiresMap` → `GeneratorEmitted`;
  `DwarfMapperValidationRoot` → `BuildFailureOnly`; `DwarfMapperOptions` → `EmissionShape`; `UsesMap` →
  `CrossAssembly`.
- `AssemblyScanTests.Scan4_Every_public_attribute_type_has_a_test_reference` — superseded. Replace the method
  body with a one-line comment pointing at `SurfaceObligationTests`, or delete it and note the supersession in
  the file's header comment.
- `OptionGaps.StructurallyInapplicable` — superseded by `AppliesTo`. Delete the dictionary and the
  `Every_exemption_names_a_real_option_and_endpoint` test that reads it; update
  `OptionEndpointParityTests` to consult `AppliesTo` instead.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln`
Expected: PASS. Any doc-reconciliation test that referenced a deleted allowlist will fail — fix the doc, not
the test.

- [ ] **Step 6: Commit**

```bash
git add tests src
git commit -m "test(surface): categories carry obligations, so the allowlists have nothing left to excuse"
```

---

### Task 7: Full enum domains and `DeclaredDivergences`

**Files:**
- Modify: `tests/DwarfMapper.Generator.Tests/Contracts/OptionCatalog.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/Contracts/OptionGaps.cs`

- [ ] **Step 1: Write the failing test**

Append to `Contracts/OptionContractTests.cs`:

```csharp
    [Fact]
    public void Every_member_of_every_option_enum_is_probed_by_the_matrix()
    {
        // NonDefaultFor picked FirstOrDefault(v => !v.Equals(def)) — complete for a two-member enum, and
        // silently skipping members two and three on anything larger. A partially-probed enum reads exactly
        // like a fully-covered one.
        var missing = new List<string>();

        foreach (var p in typeof(DwarfMapperAttribute).GetProperties().Where(p => p.CanWrite))
        {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!t.IsEnum) continue;

            var probed = OptionCatalog.Options
                .Where(o => string.Equals(o.Name, p.Name, StringComparison.Ordinal))
                .Select(o => o.NonDefault)
                .ToList();

            foreach (var member in Enum.GetNames(t))
                if (!probed.Any(s => s.EndsWith("." + member, StringComparison.Ordinal)))
                    missing.Add($"{p.Name}.{member}");
        }

        Assert.True(missing.Count == 0,
            "Enum option member(s) never probed by any matrix cell: " + string.Join(", ", missing)
            + ". Every member is a distinct behaviour; probing one alternative and calling the option covered "
            + "is how a member ships with no test at all.");
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "Every_member_of_every_option_enum"`
Expected: FAIL, naming the skipped members (the enums with three or more members).

- [ ] **Step 3: Make `OptionCatalog` emit one `OptionInfo` per enum member**

Change `Build()` so an enum-typed property yields **one `OptionInfo` per non-default member** rather than one
per property. `OptionInfo.Name` stays the property name (the parity tests key on it); add a
`string ValueLabel` field so two rows for the same option are distinguishable in test output, and include it
in the `TheoryData` keys in `OptionEndpointParityTests.Cells()` so xUnit does not collapse duplicate rows.

Replace `NonDefaultFor`'s enum branch:

```csharp
        if (t.IsEnum)
            return Enum.GetValues(t).Cast<object>()
                .Where(v => !v.Equals(def))
                .Select(v => $"{p.Name} = {t.Name}.{v}")
                .ToList();
```

and have the bool branch return both values where they differ from the default, keeping int and string
single-valued.

- [ ] **Step 4: Run the contract and parity suites**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~Contracts"`

Expected: the new test PASSES. **Expect new parity failures** from the previously-unprobed enum members —
triage them exactly as in Task 5 Step 3 and append to `Issues/round20/SURFACE-MATRIX-FINDINGS.md`.

- [ ] **Step 5: Rename `KnownSilent` → `DeclaredDivergences`**

In `Contracts/OptionGaps.cs`: rename the type `OptionGaps` → `DeclaredDivergences` and the member
`KnownSilent` → `Reasons`. Keep both existing entries and their full prose verbatim — the `NullCollections`
entry records three candidate resolutions and a reverted attempt, and that is the most valuable text in the
file.

Update the class doc comment to state the new rules explicitly:

```csharp
/// <summary>
///     Known, recorded, not-yet-fixed divergences. NOT an exemption from testing: the cell IS probed, and the
///     test asserts the divergence still exists — so a fixed gap turns the build red until its row is deleted.
///     <para>
///         Rules: one entry per KNOWN DEFECT; each carries a link to its Issues/ write-up; the count ratchets
///         DOWNWARD only. This cannot reach zero while NullCollections@Projection is an open design decision
///         with three candidate resolutions and none chosen.
///     </para>
/// </summary>
```

Add the ratchet:

```csharp
    [Fact]
    public void The_divergence_count_only_shrinks()
    {
        const int Baseline = 2;
        Assert.True(DeclaredDivergences.Reasons.Count <= Baseline,
            $"DeclaredDivergences has grown to {DeclaredDivergences.Reasons.Count} (baseline {Baseline}). "
            + "A new divergence is a maintainer decision, not a way past a red build: fix the endpoint, "
            + "refuse it with a diagnostic, or get the entry approved and lower this baseline deliberately.");
    }
```

Update every reference (`OptionEndpointParityTests`, `SurfaceParityTests`, and the generated option-support
matrix in `src/DwarfMapper.DocTooling` — grep for `OptionGaps` and `KnownSilent`).

- [ ] **Step 6: Run the full suite and regenerate docs**

Run: `dotnet test DwarfMapper.NET.sln`

If a doc-currency test fails, regenerate rather than editing the generated file by hand — check
`scripts/housekeeping.ps1` for the regeneration entry point.

- [ ] **Step 7: Commit**

```bash
git add tests src docs Issues
git commit -m "test(contracts): probe every enum member, and say what the divergence list actually is"
```

---

### Task 8: DWARF086 — hand-written manifest attributes are refused

**Files:**
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`
- Modify: the generator stage that reads `[DwarfProvidesMap]` / `[DwarfRequiresMap]`
- Modify: `docs/diagnostics.md`
- Create: a NegativeCases row pinning DWARF086

**Interfaces:**
- Consumes: `SurfaceCategory.GeneratorEmitted` from Task 1.
- Produces: diagnostic id `DWARF086` (verified free: descriptors currently top out at DWARF085 — re-check
  with `Scan1f_Descriptor_Id_has_no_gaps_except_reserved` before choosing).

- [ ] **Step 1: Write the failing test**

Add to `tests/DwarfMapper.NegativeCases` following that project's existing row convention (read two
neighbouring rows first — the project pins both the id and the remedy phrase):

```csharp
// REFUSES: DWARF086 — [DwarfProvidesMap] is emitted BY the generator onto the assembly. Hand-writing it
// declares a map the generator never produced, so the cross-assembly manifest lies and DWARF061's root
// validation trusts the lie.
[assembly: DwarfProvidesMap(typeof(Src), typeof(Dst))]
```

and the generator-side pin:

```csharp
    [Fact]
    public void Hand_written_DwarfProvidesMap_is_refused_with_DWARF086()
    {
        const string src = """
                           using DwarfMapper;
                           [assembly: DwarfProvidesMap(typeof(Demo.Src), typeof(Demo.Dst))]
                           namespace Demo;
                           public sealed class Src { public int Id { get; set; } }
                           public sealed class Dst { public int Id { get; set; } }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.RunAll(src);

        var d = Assert.Single(diagnostics, x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
        Assert.Contains("emitted by the generator", d.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_generator_emitted_manifest_does_not_trip_DWARF086()
    {
        // The negative control. A refusal that fires on the generator's OWN emission would break every
        // multi-assembly build and would still satisfy the test above.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public sealed class Src { public int Id { get; set; } }
                           public sealed class Dst { public int Id { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.RunAll(src);

        Assert.DoesNotContain(diagnostics,
            x => string.Equals(x.Id, "DWARF086", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run to verify both fail appropriately**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "DWARF086"`
Expected: the first FAILS (no such diagnostic); the second PASSES trivially.

- [ ] **Step 3: Add the descriptor**

In `DiagnosticDescriptors.cs`, follow the shape of the neighbouring descriptors exactly (category
`DwarfMapper`, id format `DWARFddd` — `Scan1d`/`Scan1e` enforce both):

```csharp
    public static readonly DiagnosticDescriptor HandWrittenManifestAttribute = new(
        "DWARF086",
        "Manifest attribute is emitted by the generator",
        "'{0}' is emitted by the generator onto the assembly and must not be hand-written; "
        + "declare the dependency with [UsesMap<TSource, TDestination>] instead",
        "DwarfMapper",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "[DwarfProvidesMap] and [DwarfRequiresMap] form the cross-assembly manifest the DWARF061 validation "
        + "root trusts. A hand-written entry claims a map the generator never produced, so the root "
        + "validates against a manifest that does not describe the assembly.");
```

- [ ] **Step 4: Report it from the generator**

Find where the generator reads these attributes (grep `DwarfProvidesMap` under `src/DwarfMapper.Generator`).
Report `DWARF086` when the attribute's syntax reference is in a tree the generator did **not** author. The
reliable discriminator is the file path: generator output arrives with a `.g.cs` hint name and its syntax tree
has no on-disk path in the user's compilation. Use whatever the surrounding code already uses to distinguish
generated trees — if it has no such helper, add one next to the other syntax helpers rather than inline.

- [ ] **Step 5: Sync the ancillary files**

`Scan1a`/`Scan1b`/`Scan1c`/`Scan7`/`Scan8` will fail until all three are updated:
- `AnalyzerReleases.Unshipped.md` — one row, severity matching the descriptor exactly.
- `docs/diagnostics.md` — a section with the remedy. Note that the diagnostics doc's C# fences are among the
  eight documented hand-written exemptions (they must NOT compile, since they illustrate the shape that
  triggers the error); match the existing style there.

- [ ] **Step 5b: Close REG-05 — no id ships without a remedy-wording pin**

81/81 live ids are pinned in NegativeCases today, but the property holds by diligence: nothing fails when the
next id ships with an id-only assertion. DWARF086 is that next id, so close the class while you are here.

Extend `SelfValidation/DiagnosticMessageContractTests` with the completeness direction:

```csharp
    [Fact]
    public void Every_live_descriptor_has_a_remedy_wording_pin()
    {
        var live = AllDescriptors().Select(d => d.Id)
            .Except(RetiredIds)
            .ToHashSet(StringComparer.Ordinal);

        var pinned = RemedyContracts.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var unpinned = live.Except(pinned).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(unpinned.Count == 0,
            "Diagnostic(s) shipping with no wording pin: " + string.Join(", ", unpinned)
            + ". Add a RemedyContracts row asserting the id AND its remedy phrase. Pinning the id alone lets "
            + "the remedy prose rot into something that no longer tells the reader what to do, with every "
            + "test still green.");
    }
```

`AllDescriptors()`, `RetiredIds` and `RemedyContracts` are that file's existing members — read it first and use
its real names rather than these. The documented retired set is `DWARF006`, `DWARF019`, `DWARF029`.

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "Every_live_descriptor_has_a_remedy"`
Expected: FAILS naming `DWARF086` until you add its row, then PASSES.

- [ ] **Step 6: Build the WHOLE solution**

Run: `dotnet build DwarfMapper.NET.sln`

**This step is not optional and `dotnet test` does not substitute for it.** The `samples/` projects are where
an over-eager refusal surfaces, and they are not covered by the test run.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln`
Expected: PASS, including both DWARF086 tests and every Scan1*/Scan7/Scan8 gate.

- [ ] **Step 8: Commit**

```bash
git add src docs tests
git commit -m "feat(gen): a manifest attribute the generator emits may not be hand-written"
```

---

### Task 9: Mutation-test the runtime assembly

**Files:**
- Create: `stryker-config.runtime.json`
- Modify: `scripts/housekeeping.ps1`

- [ ] **Step 1: Write the config**

Create `stryker-config.runtime.json` at the repo root, mirroring the two existing configs:

```json
{
  "$schema": "https://raw.githubusercontent.com/stryker-mutator/stryker-net/master/src/Stryker.Core/Stryker.Core/Schemas/stryker-config.json",
  "stryker-config": {
    "comment": "Mutation testing for the SHIPPED RUNTIME assembly. A third config because Stryker mutates one 'project' per run. Registry members, MapConfig and the exception types have no derivable case-space the way an attribute does — there is no AttributeUsage to decompose and no endpoint matrix to cross them against — so a surviving mutant is the only non-textual proof that a case is untested. This is what makes AmbiguousInterfaces and DestinationType (zero references in any test family, round 18) impossible rather than merely noticed.",
    "project": "DwarfMapper.csproj",
    "test-projects": [
      "tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj",
      "tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj"
    ],
    "mutate": [
      "src/DwarfMapper/DwarfMapperRegistry.cs",
      "src/DwarfMapper/MapConfig.cs",
      "src/DwarfMapper/DwarfMapExceptions.cs",
      "src/DwarfMapper/DwarfMappingDepthException.cs",
      "src/DwarfMapper/DwarfRefContext.cs"
    ],
    "thresholds": { "high": 90, "low": 80, "break": 70 },
    "reporters": ["progress", "html"]
  }
}
```

- [ ] **Step 2: Wire it into housekeeping**

Read `scripts/housekeeping.ps1`, find the `-Mutation` branch that runs the two existing configs, and add a
third invocation using the same pattern. Do not restructure the script.

- [ ] **Step 3: Run it and record the real score**

Run: `dotnet stryker --config-file stryker-config.runtime.json`
(Needs `dotnet tool install -g dotnet-stryker`. If the tool is unavailable, stop and report that — do not
guess a threshold.)

- [ ] **Step 4: Set `break` to the measured score, rounded down**

Whatever the run reports, set `"break"` to the achieved score rounded **down** to the nearest whole percent,
and add a line to the `comment` recording the date and score. That is the ratchet: it can only be raised
later, never lowered without a deliberate edit.

Then report the surviving mutants to the maintainer — each one names a public runtime member whose behaviour
no assertion pins. Expect `RegisterUpdate` to be prominent; Task 10 addresses it directly.

- [ ] **Step 5: Commit**

```bash
git add stryker-config.runtime.json scripts/housekeeping.ps1
git commit -m "test(runtime): mutate the shipped assembly, where no case-space can be derived"
```

---

### Task 10: `RegisterUpdate` contract and torture (round 19 REG-06 / H1)

**Independent of Tasks 1–9.** This is the one *actual defect* round 19 found — `RegisterUpdate` is referenced
exactly twice in the repo (its declaration and the generator emitting calls to it) and has no contract test
anywhere. It can land first if fixing the bug matters more than the architecture.

**Files:**
- Modify: `src/DwarfMapper/DwarfMapperRegistry.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/` — the existing `RegistryConcurrencyTortureTests`
- Create: `tests/DwarfMapper.Generator.Tests/RegistryUpdateContractTests.cs`

- [ ] **Step 1: Read the map-table half first**

Read `src/DwarfMapper/DwarfMapperRegistry.cs` lines 40–235 and the existing `RegistryConcurrencyTortureTests`
in full. The update table must mirror the map table's contract exactly; you cannot mirror what you have not
read. Note the four torture invariants by name.

- [ ] **Step 2: Write the failing contract test**

Create `tests/DwarfMapper.Generator.Tests/RegistryUpdateContractTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The update table's contract, which nothing pinned. <c>RegisterUpdate</c> executed only IMPLICITLY —
///     module initializers register update maps and consumer tests reach them through <c>Update</c> — so
///     first-wins versus overwrite, ambiguity marking, and interface resolution were all unasserted on the
///     same shared static the map table guards carefully.
/// </summary>
public sealed class RegistryUpdateContractTests
{
    private sealed class USrc { public int Id { get; set; } }
    private sealed class UDst { public int Id { get; set; } }

    [Fact]
    public void A_duplicate_update_registration_is_first_wins_and_marked_ambiguous()
    {
        DwarfMapperRegistry.RegisterUpdate(typeof(USrc), typeof(UDst), (_, d) => ((UDst)d).Id = 1);
        DwarfMapperRegistry.RegisterUpdate(typeof(USrc), typeof(UDst), (_, d) => ((UDst)d).Id = 2);

        var dst = new UDst();
        DwarfMapperRegistry.Update(new USrc(), dst, typeof(USrc), typeof(UDst));

        Assert.Equal(1, dst.Id);
        Assert.True(DwarfMapperRegistry.IsUpdateAmbiguous(typeof(USrc), typeof(UDst)),
            "A duplicate update registration must be MARKED, exactly as the map table marks its duplicates. "
            + "Silent last-loses (or silent first-wins) on the update table is the asymmetry the map table "
            + "already solved.");
    }

    [Fact]
    public void The_update_table_does_not_alias_the_map_table()
    {
        DwarfMapperRegistry.Register(typeof(USrc), typeof(UDst), _ => new UDst());

        Assert.False(DwarfMapperRegistry.IsUpdateProvided(typeof(USrc), typeof(UDst)),
            "A Register on the map table must not surface as an update registration — a caller would get a "
            + "create where they asked for an in-place update.");
    }
}
```

**Note:** `DwarfMapperRegistry` is a shared static, so these tests must not race the torture tests. Put them in
the **same xUnit collection** as `RegistryConcurrencyTortureTests` (read how that file declares its collection
and reuse it) and use type markers unique to each test, following the `Mint<T>` pattern that file already
establishes.

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~RegistryUpdateContractTests"`
Expected: FAIL to compile — `IsUpdateAmbiguous` does not exist. That absence is the finding.

- [ ] **Step 4: Mirror the map table's ambiguity handling**

In `src/DwarfMapper/DwarfMapperRegistry.cs`, add an update-side ambiguity set and accessor mirroring the
existing map-table members exactly (read `Register`/`IsAmbiguous` at lines 66 and 92 and follow their
structure — same collection type, same key type, same first-wins semantics):

```csharp
    private static readonly ConcurrentDictionary<Key, byte> UpdateAmbiguous = new();

    /// <summary>
    ///     Whether more than one update map was registered for this pair. Mirrors
    ///     <see cref="IsAmbiguous" /> on the create table: a duplicate is first-wins and MARKED, never
    ///     silently overwritten.
    /// </summary>
    public static bool IsUpdateAmbiguous(Type source, Type destination)
        => UpdateAmbiguous.ContainsKey(new Key(source, destination));
```

and in `RegisterUpdate`, mark the key when `TryAdd` fails.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --filter "FullyQualifiedName~RegistryUpdateContractTests"`
Expected: both PASS.

- [ ] **Step 6: Mirror the four torture invariants**

Extend `RegistryConcurrencyTortureTests` with the update-table twin of each of its four map-table invariants:
same-pair race is first-wins and marked ambiguous; distinct pairs lose none under concurrent registration;
churn is monotonic; base/interface resolution switches exactly once. Reuse that file's existing
`Mint<T>`/`RunAll` infrastructure — do not build a second harness.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln`
Expected: PASS.

`IsUpdateAmbiguous` is new **public** runtime surface, so it inherits the obligations from Task 6 — if Tasks
1–9 have landed, expect `SurfaceObligationTests` or the Task 9 mutation leg to demand a consumer-corpus row.
That is the architecture working on its first new member.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "fix(registry): the update table marks duplicates, like the create table always did"
```

---

## Landing order and independence

Tasks 1 → 7 are strictly sequential; each depends on the one before. Tasks 8, 9 and 10 are independent of each
other and of 1–7:

- **Task 10 can go first** — it is the one real defect, and it is small.
- **Task 9 can run at any point** and will produce findings that inform everything else.
- **Task 8** only depends on Task 1 for the category that makes its guarantee meaningful; the diagnostic
  itself stands alone.

## Stop-and-report conditions

Stop and report to the maintainer rather than working around, in these cases:

1. **A real divergence in Task 5 Step 3** — a cell that claims an endpoint, is silent there, and *should*
   work. Do not add it to `DeclaredDivergences` yourself.
2. **`dotnet stryker` unavailable** in Task 9 — report rather than guessing a threshold.
3. **An `AppliesTo` narrowing you cannot justify structurally.** Removing an endpoint from a claim because the
   cell is currently red is exactly the ratification-of-current-behaviour failure this whole design exists to
   prevent.
4. **A category assignment that seems wrong** once its obligation runs. Changing the assignment is often the
   right answer, but it changes what gets proved — say which one you changed and why in the commit message.
