# Generator Testing Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a refactoring safety net plus a reusable generator-testing kit, so the three following maintainability sub-projects (shared engine core, emission layer, extractor split) can prove they change nothing observable.

**Architecture:** Four single-purpose units in `tests/DwarfMapper.Generator.Tests`: a generator-agnostic runner returning every emitted file, a cacheability battery, a pinned golden corpus fingerprinting source + diagnostics into one manifest, and ratchet gates. `src/` is touched only to add `WithTrackingName` calls and step-name constants — no behavioural change.

**Tech Stack:** C# / .NET 10 (runtime + tests), `netstandard2.0` generator, Roslyn (`Microsoft.CodeAnalysis.CSharp`), xUnit, Verify.Xunit + Verify.SourceGenerators.

## Global Constraints

- Every new file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, `EnforceCodeStyleInBuild=true`, `Nullable=enable`. A warning fails the build. Common traps in this repo: **CA1062** (validate public-method parameters — use `ArgumentNullException.ThrowIfNull(x)`), **CA1305** (pass `CultureInfo.InvariantCulture` to `GetMessage`/`ToString`), **CA1861** (hoist constant arrays to `static readonly`), **IDE0051** (unused private member).
- Generator project is `netstandard2.0` — do **not** use `net10` APIs in `src/DwarfMapper.Generator`. Test projects are `net10.0` and may use anything.
- Test method names use `Descriptive_snake_case_sentences`, matching the existing suite.
- Test class namespace: `DwarfMapper.Generator.Tests` (or `.SelfValidation` for gates), matching folder.
- Baseline before starting: **5,485 tests green, 0 warnings**. Every task must end green.
- Snapshot convention: `public partial class SnapshotSuite`, method returns `Task`, body ends `return Verify(generated);`, snapshots land in `tests/DwarfMapper.Generator.Tests/Snapshots/`.
- Full-suite command used throughout: `dotnet test DwarfMapper.NET.sln --nologo`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRun.cs` | Result record: diagnostics + outputs by hint name |
| `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRunner.cs` | Run any `IIncrementalGenerator`; tracked-driver factory |
| `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRegistry.cs` | The list of generators under test + their tracking names |
| `tests/DwarfMapper.Generator.Tests/Framework/GeneratorCacheAssert.cs` | The three cacheability assertions + `Battery` |
| `tests/DwarfMapper.Generator.Tests/Framework/GoldenCase.cs` | Pinned case record |
| `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs` | Case enumeration across the three axes |
| `tests/DwarfMapper.Generator.Tests/Framework/GoldenFingerprint.cs` | sha256 of source + diagnostics |
| `tests/DwarfMapper.Generator.Tests/Framework/GoldenManifest.cs` | Manifest load/write/compare + failure reporting |
| `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusTests.cs` | The invariance test |
| `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` | The manifest (generated in Task 6) |
| `tests/DwarfMapper.Generator.Tests/Framework/CacheBatteryTests.cs` | Battery applied to every registered generator |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorFrameworkSelfTests.cs` | Proves each assertion FIRES |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs` | Ratchet gates |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Golden.cs` | ~20 curated readable snapshots |

**Modified**

| File | Change |
|---|---|
| `src/DwarfMapper.Generator/DwarfGenerator.cs` | Tracking names for 3 untracked outputs + `AllStepNames` |
| `src/DwarfMapper.Generator/Registry/MapToGenerator.cs` | `ExtractStepName` + `WithTrackingName` + `AllStepNames` |

---

### Task 1: `GeneratorRunner` — generator-agnostic execution

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRun.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRunner.cs`
- Test: `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRunnerTests.cs`

**Interfaces:**
- Consumes: `GeneratorTestHarness.BuildCompilation(string assemblyName, string source, NullableContextOptions nullable)` — existing, public.
- Produces:
  - `internal sealed record GeneratorRun(ImmutableArray<Diagnostic> Diagnostics, ImmutableDictionary<string, string> OutputsByHintName)` with `string AllOutputsConcatenated { get; }`
  - `internal static GeneratorRun GeneratorRunner.Run(IIncrementalGenerator generator, string source, NullableContextOptions nullable = NullableContextOptions.Disable)`
  - `internal static GeneratorDriver GeneratorRunner.RunTracked(IIncrementalGenerator generator)`

- [ ] **Step 1: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRunnerTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Registry;

namespace DwarfMapper.Generator.Tests.Framework;

public class GeneratorRunnerTests
{
    private const string MapperSource = """
                                        using DwarfMapper;
                                        namespace Demo;
                                        public class A { public int X { get; set; } }
                                        public class B { public int X { get; set; } }
                                        [DwarfMapper] public partial class M { public partial B Map(A a); }
                                        """;

    [Fact]
    public void Runs_the_class_model_generator_and_returns_outputs_by_hint_name()
    {
        var run = GeneratorRunner.Run(new DwarfGenerator(), MapperSource);

        Assert.NotEmpty(run.OutputsByHintName);
        Assert.Contains(run.OutputsByHintName, kv => kv.Value.Contains("partial class M", StringComparison.Ordinal));
    }

    [Fact]
    public void Returns_the_assembly_wide_aggregate_outputs_too()
    {
        // The existing GeneratorTestHarness.Run FILTERS these out so per-mapper snapshots pick the right file.
        // The golden corpus depends on them being present, or the facade/DI/manifest emitters are never covered.
        var run = GeneratorRunner.Run(new DwarfGenerator(), MapperSource);

        Assert.Contains(run.OutputsByHintName.Keys,
            k => k.EndsWith("DwarfMapper.Extensions.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Runs_the_registry_generator_with_no_bespoke_method()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                           public class Dto { public int Id { get; set; } }
                           """;

        var run = GeneratorRunner.Run(new MapToGenerator(), src);

        Assert.Contains(run.OutputsByHintName, kv => kv.Value.Contains("ToDto", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GeneratorRunnerTests"`
Expected: FAIL to compile — `GeneratorRunner` does not exist (CS0103).

- [ ] **Step 3: Write minimal implementation**

Create `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRun.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>Everything one generator run produced: its diagnostics and EVERY file it emitted.</summary>
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableDictionary<string, string> OutputsByHintName)
{
    /// <summary>All outputs concatenated in hint-name order — the stable form used for fingerprinting.</summary>
    public string AllOutputsConcatenated =>
        string.Concat(OutputsByHintName.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => "// ==== " + kv.Key + " ====\n" + kv.Value));
}
```

Create `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRunner.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     Runs ANY <see cref="IIncrementalGenerator" />. Six sites previously hardcoded
///     <c>CSharpGeneratorDriver.Create(new DwarfGenerator())</c>, so adding the registry generator required a
///     bespoke harness method rather than passing the generator in. Reuses
///     <see cref="GeneratorTestHarness.BuildCompilation" /> because the metadata-reference set is cached there
///     and must stay single-sourced.
/// </summary>
internal static class GeneratorRunner
{
    public static GeneratorRun Run(IIncrementalGenerator generator, string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        ArgumentNullException.ThrowIfNull(generator);

        var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapperRunnerAsm", source, nullable);
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var outputs = output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToImmutableDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.ToString(),
                StringComparer.Ordinal);

        return new GeneratorRun(diagnostics, outputs);
    }

    /// <summary>
    ///     A driver with step tracking enabled, for the cacheability battery. Takes no Compilation on purpose —
    ///     the caller drives it with RunGenerators(compilation), and an unused parameter would fail the build
    ///     here (IDE0060 under AnalysisMode=All).
    /// </summary>
    public static GeneratorDriver RunTracked(IIncrementalGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        return CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GeneratorRunnerTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Run the full suite (nothing may regress)**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green, 5,488 tests (5,485 + 3), 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Framework/
git commit -m "test: generator-agnostic runner returning every emitted file

Six sites hardcoded CSharpGeneratorDriver.Create(new DwarfGenerator()), so adding the registry generator
needed a bespoke RunMapTo method instead of passing the generator in. GeneratorRunner takes the generator as a
parameter and returns EVERY emitted file keyed by hint name — deliberately unlike GeneratorTestHarness.Run,
which filters out the assembly-wide aggregates so per-mapper snapshots select the right file. The golden corpus
depends on those aggregates being present, or the facade/DI/ambient-manifest emitters would never be covered."
```

---

### Task 2: Tracking names for every pipeline

**Files:**
- Modify: `src/DwarfMapper.Generator/DwarfGenerator.cs` (add 3 step names + `AllStepNames`)
- Modify: `src/DwarfMapper.Generator/Registry/MapToGenerator.cs` (add `ExtractStepName` + `AllStepNames`)
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRegistry.cs`
- Test: `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRegistryTests.cs`

**Interfaces:**
- Produces:
  - `internal const string DwarfGenerator.AggregateStepName = "DwarfMapperAggregate"`
  - `internal const string DwarfGenerator.RequiresManifestStepName = "DwarfMapperRequiresManifest"`
  - `internal const string DwarfGenerator.AmbientRegistrationStepName = "DwarfMapperAmbientRegistration"`
  - `internal static readonly string[] DwarfGenerator.AllStepNames`
  - `public const string MapToGenerator.ExtractStepName = "MapToExtract"`
  - `public static readonly string[] MapToGenerator.AllStepNames`
  - `internal sealed record GeneratorUnderTest(string Name, Func<IIncrementalGenerator> Create, IReadOnlyList<string> TrackingNames)`
  - `internal static IReadOnlyList<GeneratorUnderTest> GeneratorRegistry.All`

- [ ] **Step 1: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRegistryTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Framework;

public class GeneratorRegistryTests
{
    [Fact]
    public void Both_generators_are_registered()
    {
        Assert.Equal(2, GeneratorRegistry.All.Count);
        Assert.Contains(GeneratorRegistry.All, g => g.Name == "DwarfGenerator");
        Assert.Contains(GeneratorRegistry.All, g => g.Name == "MapToGenerator");
    }

    [Fact]
    public void Every_registered_generator_declares_at_least_one_tracking_name()
    {
        // A generator with no tracking name cannot be addressed by a cacheability test at all — which is
        // exactly why MapToGenerator had zero caching coverage while DwarfGenerator had six tests.
        Assert.All(GeneratorRegistry.All, g => Assert.NotEmpty(g.TrackingNames));
    }

    [Fact]
    public void Every_declared_tracking_name_actually_appears_in_a_run()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class A { public int X { get; set; } }
                           public class B { public int X { get; set; } }
                           [DwarfMapper] public partial class M { public partial B Map(A a); }
                           [MapTo(typeof(B))] public class Src { public int X { get; set; } }
                           """;

        foreach (var g in GeneratorRegistry.All)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("TrackAsm", src);
            var driver = GeneratorRunner.RunTracked(g.Create()).RunGenerators(compilation);
            var tracked = driver.GetRunResult().Results[0].TrackedSteps;

            foreach (var name in g.TrackingNames)
                Assert.True(tracked.ContainsKey(name),
                    $"{g.Name} declares tracking name '{name}' but no such step ran. Either the WithTrackingName "
                    + "call is missing or the name is stale.");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GeneratorRegistryTests"`
Expected: FAIL to compile — `GeneratorRegistry` does not exist.

- [ ] **Step 3a: Add the step names to `MapToGenerator`**

In `src/DwarfMapper.Generator/Registry/MapToGenerator.cs`, immediately above the existing
`private const string MapToAttr = "DwarfMapper.MapToAttribute";`, insert:

```csharp
    /// <summary>
    ///     Tracking name for the extraction step. Without one the step is anonymous, so a test cannot address
    ///     this pipeline at all — which is why DwarfGenerator had six incremental-caching tests and this
    ///     generator had none. Its model could stop being value-equatable and nothing would notice.
    /// </summary>
    public const string ExtractStepName = "MapToExtract";

    /// <summary>Every tracked step in this generator, for the cacheability battery.</summary>
    public static readonly string[] AllStepNames = { ExtractStepName };

```

Then change the pipeline (currently ending `static (ctx, _) => Extract(ctx));`) to:

```csharp
            static (ctx, _) => Extract(ctx))
            .WithTrackingName(ExtractStepName);
```

- [ ] **Step 3b: Add the step names to `DwarfGenerator`**

In `src/DwarfMapper.Generator/DwarfGenerator.cs`, after the existing `CoLocatedExtractStepName` constant, add:

```csharp
    internal const string AggregateStepName = "DwarfMapperAggregate";
    internal const string RequiresManifestStepName = "DwarfMapperRequiresManifest";
    internal const string AmbientRegistrationStepName = "DwarfMapperAmbientRegistration";

    /// <summary>Every tracked step in this generator, for the cacheability battery.</summary>
    internal static readonly string[] AllStepNames =
    {
        ExtractStepName, CoLocatedExtractStepName,
        AggregateStepName, RequiresManifestStepName, AmbientRegistrationStepName,
    };
```

Then name the three currently-anonymous providers. Apply `.WithTrackingName(...)` to the provider expression
feeding each `RegisterSourceOutput`:

1. The aggregate output — change
   `mappers.Collect().Combine(coLocated.Collect()).Combine(aggregateOptions)` to
   `mappers.Collect().Combine(coLocated.Collect()).Combine(aggregateOptions).WithTrackingName(AggregateStepName)`.
2. The requires manifest — change `ownRequired` in its `RegisterSourceOutput` call to
   `ownRequired.WithTrackingName(RequiresManifestStepName)`.
3. The ambient registration/validation output — change
   `ownProvided.Combine(ownRequired).Combine(rootInfo)` to
   `ownProvided.Combine(ownRequired).Combine(rootInfo).WithTrackingName(AmbientRegistrationStepName)`.

- [ ] **Step 3c: Create the registry**

Create `tests/DwarfMapper.Generator.Tests/Framework/GeneratorRegistry.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>One generator under test: how to build it and which steps must stay cacheable.</summary>
internal sealed record GeneratorUnderTest(
    string Name,
    Func<IIncrementalGenerator> Create,
    IReadOnlyList<string> TrackingNames);

/// <summary>
///     Every generator the framework covers. A ratchet asserts this list matches the
///     <see cref="IIncrementalGenerator" /> types actually present in <c>src/</c>, so a new generator cannot
///     ship without cacheability and golden coverage — which is how MapToGenerator ended up with neither.
/// </summary>
internal static class GeneratorRegistry
{
    public static IReadOnlyList<GeneratorUnderTest> All { get; } = new[]
    {
        new GeneratorUnderTest("DwarfGenerator", () => new DwarfGenerator(), DwarfGenerator.AllStepNames),
        new GeneratorUnderTest("MapToGenerator", () => new MapToGenerator(), MapToGenerator.AllStepNames),
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GeneratorRegistryTests"`
Expected: PASS, 3 tests. If `Every_declared_tracking_name_actually_appears_in_a_run` fails, a `WithTrackingName`
from Step 3b was attached to the wrong provider — fix the provider, not the test.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green. Tracking names are metadata only — if any behavioural test changed, something else was
edited by mistake.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/DwarfGenerator.cs src/DwarfMapper.Generator/Registry/MapToGenerator.cs tests/DwarfMapper.Generator.Tests/Framework/
git commit -m "test: name every generator pipeline so cacheability is addressable

DwarfGenerator registered ~6 source outputs but named only 2, and MapToGenerator named none — so most of the
emission surface could not be asserted for cacheability even in principle. Adds tracking names to the
aggregate, requires-manifest and ambient-registration outputs plus the registry extract step, and an AllStepNames
list per generator. Metadata only; no behavioural change."
```

---

### Task 3: `GeneratorCacheAssert` — the battery

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GeneratorCacheAssert.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Framework/CacheBatteryTests.cs`

**Interfaces:**
- Consumes: `GeneratorRunner.RunTracked`, `GeneratorRegistry.All`, `GeneratorTestHarness.BuildCompilation`.
- Produces:
  - `internal static void GeneratorCacheAssert.FullyCachedOnRerun(IIncrementalGenerator g, string source, IReadOnlyList<string> trackingNames)`
  - `internal static void GeneratorCacheAssert.CachedAfterUnrelatedEdit(IIncrementalGenerator g, string source, IReadOnlyList<string> trackingNames)`
  - `internal static void GeneratorCacheAssert.NoSymbolsInPipeline(IIncrementalGenerator g, string source, IReadOnlyList<string> trackingNames)`
  - `internal static void GeneratorCacheAssert.Battery(GeneratorUnderTest g, string source)`

- [ ] **Step 1: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/Framework/CacheBatteryTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The cacheability battery, applied to EVERY registered generator. Before this, MapToGenerator had no
///     caching coverage at all: its model could stop being value-equatable, incrementality would silently die,
///     and every test would still pass.
/// </summary>
public class CacheBatteryTests
{
    private const string BothGeneratorsSource = """
                                                using DwarfMapper;
                                                using System.Collections.Generic;
                                                namespace Demo;
                                                public class Addr { public string City { get; set; } = ""; }
                                                public class A { public int X { get; set; } public Addr Address { get; set; } = new(); public List<int> N { get; set; } = new(); }
                                                public class B { public int X { get; set; } public string City { get; set; } = ""; public List<long> N { get; set; } = new(); }
                                                [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                [MapTo(typeof(B2))] public class Src2 { public int X { get; set; } }
                                                public class B2 { public int X { get; set; } }
                                                """;

    public static TheoryData<string> Generators()
    {
        var data = new TheoryData<string>();
        foreach (var g in GeneratorRegistry.All) data.Add(g.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void Battery_passes_for_every_registered_generator(string generatorName)
    {
        var g = GeneratorRegistry.All.Single(x => x.Name == generatorName);
        GeneratorCacheAssert.Battery(g, BothGeneratorsSource);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~CacheBatteryTests"`
Expected: FAIL to compile — `GeneratorCacheAssert` does not exist.

- [ ] **Step 3: Write the implementation**


> **Correction (applied during execution, review of Task 3).** The two `continue`-on-missing-step lines below
> made any declared tracking name whose pipeline did not run pass VACUOUSLY — `DwarfMapperCoLocatedExtract`
> was never asserted at all, because the fixture had no `[GenerateMap<>]` usage and
> `ForAttributeWithMetadataName` omits the step from `TrackedSteps` entirely in that case. 5 of 6 declared
> steps were genuinely checked. This contradicted the spec's own "fail loud, never self-heal" and
> per-source non-vacuity requirements, so the spec governed. The implemented code instead COLLECTS missing
> names and asserts none are missing, and the fixture gained a co-located `[GenerateMap<,>]` host so the step
> actually runs. Treat the shipped code as authoritative over the snippet below.

Create `tests/DwarfMapper.Generator.Tests/Framework/GeneratorCacheAssert.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     Cacheability assertions runnable against any generator. Incrementality is invisible to every other test
///     (they each run the generator once), so a model that stops being value-equatable — or that leaks an
///     ISymbol — silently disables caching and roots old compilations while the suite stays green.
/// </summary>
internal static class GeneratorCacheAssert
{
    private const string UnrelatedEdit = "namespace Other { public class Unrelated { public int Z; } }";

    public static void Battery(GeneratorUnderTest generator, string source)
    {
        ArgumentNullException.ThrowIfNull(generator);

        FullyCachedOnRerun(generator.Create(), source, generator.TrackingNames);
        CachedAfterUnrelatedEdit(generator.Create(), source, generator.TrackingNames);
        NoSymbolsInPipeline(generator.Create(), source, generator.TrackingNames);
    }

    public static void FullyCachedOnRerun(IIncrementalGenerator generator, string source,
        IReadOnlyList<string> trackingNames)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("CacheAsm", source);
        var driver = GeneratorRunner.RunTracked(generator).RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);

        AssertStepsCached(driver, trackingNames, "an identical re-run");
    }

    public static void CachedAfterUnrelatedEdit(IIncrementalGenerator generator, string source,
        IReadOnlyList<string> trackingNames)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("CacheAsm", source);
        var driver = GeneratorRunner.RunTracked(generator).RunGenerators(compilation);

        var modified = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(UnrelatedEdit));
        driver = driver.RunGenerators(modified);

        AssertStepsCached(driver, trackingNames, "an unrelated edit");
    }

    public static void NoSymbolsInPipeline(IIncrementalGenerator generator, string source,
        IReadOnlyList<string> trackingNames)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("CacheAsm", source);
        var driver = GeneratorRunner.RunTracked(generator).RunGenerators(compilation);
        var tracked = driver.GetRunResult().Results[0].TrackedSteps;

        foreach (var name in trackingNames)
        {
            if (!tracked.TryGetValue(name, out var steps)) continue;

            foreach (var value in steps.SelectMany(s => s.Outputs).Select(o => o.Value))
                Assert.True(IsSymbolFree(value),
                    $"Step '{name}' produced a {value?.GetType().Name} — pipeline models must not hold "
                    + "ISymbol, SyntaxNode, Location or Compilation. They are never equatable (so caching dies) "
                    + "and they root old compilations, forcing Roslyn to retain memory it could free.");
        }
    }

    private static bool IsSymbolFree(object? value)
    {
        return value is not (ISymbol or SyntaxNode or Location or Compilation);
    }

    private static void AssertStepsCached(GeneratorDriver driver, IReadOnlyList<string> trackingNames,
        string what)
    {
        var tracked = driver.GetRunResult().Results[0].TrackedSteps;

        foreach (var name in trackingNames)
        {
            if (!tracked.TryGetValue(name, out var steps)) continue;

            Assert.All(steps, step => Assert.All(step.Outputs, output =>
                Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{name}' was {output.Reason} after {what}; expected Cached/Unchanged. A "
                    + "non-value-equatable field (raw ImmutableArray, leaked symbol, unstable collection) "
                    + "likely broke the model's equality and disabled incremental caching.")));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~CacheBatteryTests"`
Expected: PASS, 2 tests (one per generator).

If `MapToGenerator` fails `CachedAfterUnrelatedEdit`, that is a **real finding**, not a test bug — its model was
never checked before. Stop, record the failure, and report it rather than weakening the assertion.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Framework/
git commit -m "test: cacheability battery applied to every registered generator

Three assertions per generator: fully-cached-on-rerun, cached-after-unrelated-edit, and the cookbook's
no-leaked-symbol check (no ISymbol/SyntaxNode/Location/Compilation in tracked step outputs — they are never
equatable and root old compilations). The leak check is new for BOTH generators; MapToGenerator had no
cacheability coverage of any kind."
```

---

### Task 4: Golden case model and corpus enumeration

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GoldenCase.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs`
- Test: `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusShapeTests.cs`

**Interfaces:**
- Consumes: `CombinatorialSchema.DepthOneMatrix()`, `CombinatorialSchema.DepthTwoMatrix()` (both return
  `IEnumerable<MatrixCell>`; `MatrixCell` has `BasicType`, `ShapeName`, `Variant`, `Source`),
  `SyntheticSchema.Generate(int seed)`, `GeneratorRegistry.All`.
- Produces:
  - `internal sealed record GoldenCase(string Id, string Source, string GeneratorName)`
  - `internal static IReadOnlyList<GoldenCase> GoldenCorpus.Cases()`
  - `internal const int GoldenCorpus.SyntheticSeedCount = 40`

- [ ] **Step 1: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusShapeTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

public class GoldenCorpusShapeTests
{
    [Fact]
    public void Case_ids_are_unique_and_stable()
    {
        var cases = GoldenCorpus.Cases();
        var ids = cases.Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        // Running twice must produce the identical ordered id list — the manifest depends on it.
        Assert.Equal(ids, GoldenCorpus.Cases().Select(c => c.Id).ToList());
    }

    [Fact]
    public void Every_axis_contributes_cases()
    {
        var ids = GoldenCorpus.Cases().Select(c => c.Id).ToList();

        Assert.Contains(ids, id => id.StartsWith("cmb:", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.StartsWith("syn:", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.StartsWith("feat:", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_generators_contribute_cases()
    {
        var byGenerator = GoldenCorpus.Cases().Select(c => c.GeneratorName).Distinct(StringComparer.Ordinal);

        Assert.Contains("DwarfGenerator", byGenerator, StringComparer.Ordinal);
        Assert.Contains("MapToGenerator", byGenerator, StringComparer.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusShapeTests"`
Expected: FAIL to compile — `GoldenCorpus` does not exist.

- [ ] **Step 3: Write the implementation**

Create `tests/DwarfMapper.Generator.Tests/Framework/GoldenCase.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>One pinned corpus case: a stable id, the source to run, and which generator runs it.</summary>
internal sealed record GoldenCase(string Id, string Source, string GeneratorName);
```

Create `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Tests.Fuzzing;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The pinned corpus. Ids are stable so schema growth appears as explicit added/removed manifest lines
///     rather than an unreadable reshuffle. Three axes, because the type axis alone would miss the entire
///     feature surface (FlattenGraph, projection, span, async-stream, derived types, hooks, ambient registry)
///     and the second generator.
/// </summary>
internal static class GoldenCorpus
{
    /// <summary>Fixed seed range for the fuzz axis. Fixed, not random, or the manifest could never be stable.</summary>
    public const int SyntheticSeedCount = 40;

    public static IReadOnlyList<GoldenCase> Cases()
    {
        var cases = new List<GoldenCase>();

        // ── Type axis: the combinatorial matrices ────────────────────────────
        foreach (var cell in CombinatorialSchema.DepthOneMatrix().Concat(CombinatorialSchema.DepthTwoMatrix()))
            cases.Add(new GoldenCase(
                $"cmb:{cell.BasicType}|{cell.ShapeName}|{cell.Variant}",
                cell.Source,
                "DwarfGenerator"));

        // ── Fuzz axis: a FIXED seed range ────────────────────────────────────
        for (var seed = 0; seed < SyntheticSeedCount; seed++)
            cases.Add(new GoldenCase(
                "syn:seed-" + seed.ToString("D4", CultureInfo.InvariantCulture),
                SyntheticSchema.Generate(seed),
                "DwarfGenerator"));

        // ── Feature axis: one case per feature, incl. the registry generator ─
        foreach (var (id, source, generatorName) in FeatureCases())
            cases.Add(new GoldenCase("feat:" + id, source, generatorName));

        // Deterministic order: the manifest is compared line by line.
        return cases.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<(string Id, string Source, string GeneratorName)> FeatureCases()
    {
        yield return ("Basic", Mapper("public partial B Map(A a);"), "DwarfGenerator");

        yield return ("UpdateInto", Mapper("public partial void Update(A a, B b);"), "DwarfGenerator");

        yield return ("Projection", """
                                    using DwarfMapper;
                                    using System.Linq;
                                    namespace Demo;
                                    public class A { public int X { get; set; } }
                                    public class B { public int X { get; set; } }
                                    [DwarfMapper] public partial class M { public partial IQueryable<B> Project(IQueryable<A> q); }
                                    """, "DwarfGenerator");

        yield return ("SpanMap", """
                                 using DwarfMapper;
                                 using System;
                                 namespace Demo;
                                 [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }
                                 """, "DwarfGenerator");

        yield return ("AsyncStream", """
                                     using DwarfMapper;
                                     using System.Collections.Generic;
                                     namespace Demo;
                                     public class A { public int X { get; set; } }
                                     public class B { public int X { get; set; } }
                                     [DwarfMapper] public partial class M { public partial IAsyncEnumerable<B> Map(IAsyncEnumerable<A> src); }
                                     """, "DwarfGenerator");

        yield return ("FlattenGraph", """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      namespace Demo;
                                      public class Node { public int Id { get; set; } public List<string> Tags { get; set; } = new(); public Node? Next { get; set; } }
                                      public class NodeDto { public int Id { get; set; } public List<string> Tags { get; set; } = new(); }
                                      public class Root { public Node? Entry { get; set; } }
                                      public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                                      [DwarfMapper] public partial class M { [FlattenGraph("Entry", "Nodes")] public partial RootDto Map(Root r); }
                                      """, "DwarfGenerator");

        yield return ("Flatten", """
                                 using DwarfMapper;
                                 namespace Demo;
                                 public class Addr { public string City { get; set; } = ""; }
                                 public class A { public Addr Address { get; set; } = new(); }
                                 public class B { public string City { get; set; } = ""; }
                                 [DwarfMapper] public partial class M { [Flatten(nameof(A.Address))] public partial B Map(A a); }
                                 """, "DwarfGenerator");

        yield return ("ConstructorMapping", """
                                            using DwarfMapper;
                                            namespace Demo;
                                            public class A { public int X { get; set; } }
                                            public class B { public B(int x) { X = x; } public int X { get; } }
                                            [DwarfMapper] public partial class M { public partial B Map(A a); }
                                            """, "DwarfGenerator");

        yield return ("EnumByName", """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public enum SrcColor { Red = 1, Green = 2 }
                                    public enum DstColor { Red = 1, Green = 2 }
                                    public class A { public SrcColor C { get; set; } }
                                    public class B { public DstColor C { get; set; } }
                                    [DwarfMapper(EnumStrategy = EnumStrategy.ByName)] public partial class M { public partial B Map(A a); }
                                    """, "DwarfGenerator");

        yield return ("FlagsEnumFromString", """
                                             using DwarfMapper;
                                             using System;
                                             namespace Demo;
                                             [Flags] public enum Perm { None = 0, Read = 1, Write = 2 }
                                             public class A { public string P { get; set; } = ""; }
                                             public class B { public Perm P { get; set; } }
                                             [DwarfMapper(EnumStrategy = EnumStrategy.ByName)] public partial class M { public partial B Map(A a); }
                                             """, "DwarfGenerator");

        yield return ("NullStrategyThrow", """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public class A { public int? V { get; set; } }
                                           public class B { public int V { get; set; } }
                                           [DwarfMapper(NullStrategy = NullStrategy.Throw)] public partial class M { public partial B Map(A a); }
                                           """, "DwarfGenerator");

        yield return ("PreserveReferences", """
                                            using DwarfMapper;
                                            namespace Demo;
                                            public class Node { public int V { get; set; } public Node? Next { get; set; } }
                                            public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                                            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)] public partial class M { public partial NodeDto Map(Node n); }
                                            """, "DwarfGenerator");

        yield return ("DerivedTypes", """
                                      using DwarfMapper;
                                      namespace Demo;
                                      public class A { public int X { get; set; } }
                                      public class ADerived : A { public int Y { get; set; } }
                                      public class B { public int X { get; set; } }
                                      public class BDerived : B { public int Y { get; set; } }
                                      [DwarfMapper] public partial class M { [MapDerivedType(typeof(ADerived), typeof(BDerived))] public partial B Map(A a); }
                                      """, "DwarfGenerator");

        yield return ("Hooks", """
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int X { get; set; } }
                               public class B { public int X { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   public partial B Map(A a);
                                   [AfterMap] private static void After(A a, B b) { }
                               }
                               """, "DwarfGenerator");

        yield return ("ReverseMap", """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class A { public int X { get; set; } }
                                    public class B { public int X { get; set; } }
                                    [DwarfMapper] public partial class M
                                    {
                                        [RoundTrip] public partial B ToB(A a);
                                        public partial A FromB(B b);
                                    }
                                    """, "DwarfGenerator");

        yield return ("CoLocatedGenerateMap", """
                                              using DwarfMapper;
                                              namespace Demo;
                                              public class A { public int X { get; set; } }
                                              [GenerateMap<A, B>] public sealed class B { public int X { get; set; } }
                                              """, "DwarfGenerator");

        yield return ("RegistryBasic", """
                                       using DwarfMapper;
                                       namespace Demo;
                                       [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                       public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                                       """, "MapToGenerator");

        yield return ("RegistryCollection", """
                                            using DwarfMapper;
                                            using System.Collections.Generic;
                                            namespace Demo;
                                            [MapTo(typeof(Dto))] public class Src { public List<int> Xs { get; set; } = new(); }
                                            public class Dto { public List<long> Xs { get; set; } = new(); }
                                            """, "MapToGenerator");

        yield return ("RegistryNested", """
                                        using DwarfMapper;
                                        namespace Demo;
                                        public class Inner { public int V { get; set; } }
                                        public class InnerDto { public int V { get; set; } }
                                        [MapTo(typeof(Dto))] public class Src { public Inner I { get; set; } = new(); }
                                        public class Dto { public InnerDto I { get; set; } = new(); }
                                        """, "MapToGenerator");
    }

    private static string Mapper(string methodDeclaration)
    {
        return $$"""
                 using DwarfMapper;
                 namespace Demo;
                 public class A { public int X { get; set; } public string Name { get; set; } = ""; }
                 public class B { public int X { get; set; } public string Name { get; set; } = ""; }
                 [DwarfMapper] public partial class M { {{methodDeclaration}} }
                 """;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusShapeTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Framework/ tests/DwarfMapper.Generator.Tests/Golden/
git commit -m "test: pinned golden corpus across type, fuzz and feature axes

Ids are pinned so schema growth shows as explicit added/removed lines rather than an unreadable reshuffle. The
feature axis exists because the combinatorial/fuzz schemas cover only the TYPE surface — without it the corpus
would miss FlattenGraph, projection, span, async-stream, derived types, hooks, the ambient registry and the
whole [MapTo] generator."
```

---

### Task 4b: Prove every feature case actually triggers its feature

**Why this task exists.** A feature case whose source does not actually trigger its feature still produces a
perfectly valid fingerprint — of near-empty output — gets pinned into the manifest in Task 6, and passes
forever. The corpus would then claim to cover projection, span maps and FlattenGraph while covering none of
them, permanently and invisibly. This must run BEFORE the manifest is written, because pinning is what makes
the gap invisible.

**Known trap:** the repo's `[AfterMap]` precedent is an INSTANCE method
(`tests/DwarfMapper.IntegrationTests/AutoMapperPatternsRuntimeTests.cs:131` — `private void FillDoc(S s, D d)`).
If the `Hooks` case in `GoldenCorpus` uses `private static void`, and static hooks are not supported, this task
will catch it. Fix the CASE SOURCE to match the supported form; do not delete the assertion.

**Files:**
- Test: `tests/DwarfMapper.Generator.Tests/Golden/GoldenFeatureCoverageTests.cs`
- Possibly modify: `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs` (only to correct a case source
  that fails to trigger its feature)

**Interfaces:**
- Consumes: `GoldenCorpus.Cases()`, `GeneratorRegistry.All`, `GeneratorRunner.Run`, `GeneratorRun.AllOutputsConcatenated`.
- Produces: nothing later tasks consume.

- [ ] **Step 1: Write the test**

Create `tests/DwarfMapper.Generator.Tests/Golden/GoldenFeatureCoverageTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Golden;

/// <summary>
///     Proves each feature case EXERCISES the feature it is named for. A case that silently generates nothing
///     still yields a valid fingerprint, so once Task 6 pins it the gap becomes permanent and invisible — the
///     corpus would advertise coverage it does not have.
/// </summary>
public class GoldenFeatureCoverageTests
{
    /// <summary>featureId -> a marker that can only appear if the feature actually fired.</summary>
    public static TheoryData<string, string> FeatureMarkers() => new()
    {
        { "Basic", "Map(" },
        { "UpdateInto", "void Update(" },
        { "Projection", "IQueryable" },
        { "SpanMap", "Span<" },
        { "AsyncStream", "await foreach" },
        { "FlattenGraph", "__DwarfMap_FlattenGraph" },
        { "Flatten", "City = " },
        { "ConstructorMapping", "new global::Demo.B(" },
        { "EnumByName", "DstColor" },
        { "FlagsEnumFromString", "MemoryExtensions" },
        { "NullStrategyThrow", "throw" },
        { "PreserveReferences", "ctx" },
        { "DerivedTypes", "is global::Demo.ADerived" },
        { "Hooks", "After" },
        { "ReverseMap", "FromB" },
        { "CoLocatedGenerateMap", "class B" },
        { "RegistryBasic", "ToDto" },
        { "RegistryCollection", "__DwarfMapColl_" },
        { "RegistryNested", "__DwarfMapObj_" },
    };

    [Theory]
    [MemberData(nameof(FeatureMarkers))]
    public void Feature_case_generates_output_that_proves_the_feature_fired(string featureId, string marker)
    {
        var c = GoldenCorpus.Cases().Single(x => x.Id == "feat:" + featureId);
        var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
        var run = GeneratorRunner.Run(generator.Create(), c.Source);
        var output = run.AllOutputsConcatenated;

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.False(string.IsNullOrWhiteSpace(output),
            $"Feature case '{featureId}' generated NOTHING. Pinned into the manifest it would pass forever "
            + "while covering nothing.");
        Assert.True(output.Contains(marker, StringComparison.Ordinal),
            $"Feature case '{featureId}' generated output that does not contain '{marker}', so the feature "
            + "did not fire. Fix the CASE SOURCE (check the attribute/signature shape against the existing "
            + "dedicated tests for that feature) — do not delete this assertion.\n\n--- generated ---\n"
            + output);
    }

    [Fact]
    public void Every_feature_case_in_the_corpus_has_a_marker()
    {
        // Otherwise a new feature case could be added and never proved to fire.
        var corpusIds = GoldenCorpus.Cases()
            .Where(c => c.Id.StartsWith("feat:", StringComparison.Ordinal))
            .Select(c => c.Id["feat:".Length..])
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var markered = FeatureMarkers().Select(row => (string)row[0])
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(corpusIds.SequenceEqual(markered, StringComparer.Ordinal),
            "Feature cases and markers are out of sync.\n  corpus:  " + string.Join(", ", corpusIds)
            + "\n  markers: " + string.Join(", ", markered));
    }
}
```

- [ ] **Step 2: Run it — expect real failures**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenFeatureCoverageTests"`

Some cases are EXPECTED to fail on the first run — that is the point of the task. For each failure, read the
generated output in the assertion message and fix the CASE SOURCE in `GoldenCorpus.FeatureCases()` so the
feature actually fires. Cross-check the shape against that feature's existing dedicated test file (e.g.
`SpanMapGeneratorTests.cs`, `AsyncStreamMapGeneratorTests.cs`, `ProjectionTests.cs`,
`FlattenGraphGeneratorTests.cs`, `ConstructorMappingTests.cs`, `RegistryDiagnosticsGenTests.cs`).

If a marker itself is wrong (the feature fires but emits different text than I guessed), correct the MARKER —
but only after confirming from the generated output that the feature genuinely fired.

- [ ] **Step 3: Re-run until green**

Run the same command. Expected: all 20 tests pass.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: green, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Golden/ tests/DwarfMapper.Generator.Tests/Framework/
git commit -m "test: prove every golden feature case actually triggers its feature

A case whose source does not fire its feature still produces a valid fingerprint of near-empty output. Once the
manifest pins it, it passes forever while covering nothing — the corpus would advertise projection, span and
FlattenGraph coverage it does not have. Each case must now emit a feature-specific marker, and every case must
have a marker, so a new case cannot skip the proof."
```

---

### Task 5: Fingerprint and manifest

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GoldenFingerprint.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Framework/GoldenManifest.cs`
- Test: `tests/DwarfMapper.Generator.Tests/Golden/GoldenFingerprintTests.cs`

**Interfaces:**
- Consumes: `GeneratorRunner.Run`, `GeneratorRegistry.All`, `GoldenCorpus.Cases()`.
- Produces:
  - `internal static string GoldenFingerprint.Compute(GoldenCase c)`
  - `internal static string GoldenManifest.Path { get; }`
  - `internal static IReadOnlyDictionary<string, string> GoldenManifest.Load()`
  - `internal static void GoldenManifest.Write(IReadOnlyDictionary<string, string> entries)`
  - `internal const string GoldenManifest.UpdateEnvVar = "DWARF_GOLDEN_UPDATE"`

- [ ] **Step 1: Write the failing test**

Create `tests/DwarfMapper.Generator.Tests/Golden/GoldenFingerprintTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

public class GoldenFingerprintTests
{
    private static readonly GoldenCase Sample = new(
        "test:sample",
        """
        using DwarfMapper;
        namespace Demo;
        public class A { public int X { get; set; } }
        public class B { public int X { get; set; } }
        [DwarfMapper] public partial class M { public partial B Map(A a); }
        """,
        "DwarfGenerator");

    [Fact]
    public void Fingerprint_is_deterministic()
    {
        Assert.Equal(GoldenFingerprint.Compute(Sample), GoldenFingerprint.Compute(Sample));
    }

    [Fact]
    public void Fingerprint_changes_when_generated_output_changes()
    {
        // Renaming a mapped member changes the emitted assignment, so the fingerprint MUST move. If this ever
        // passes-by-equality the whole safety net is inert.
        var altered = Sample with
        {
            // Rename the mapped member on BOTH sides: the emitted assignment changes (X = -> Renamed =) while
            // the mapping stays complete. That isolates OUTPUT sensitivity from DIAGNOSTIC sensitivity, which
            // the next test covers separately.
            Source = Sample.Source.Replace("int X { get; set; }", "int Renamed { get; set; }",
                StringComparison.Ordinal),
        };

        Assert.NotEqual(GoldenFingerprint.Compute(Sample), GoldenFingerprint.Compute(altered));
    }

    [Fact]
    public void Fingerprint_includes_diagnostics()
    {
        // B.Orphan has no source member -> DWARF001. Same generated output shape, different diagnostics.
        var withDiagnostic = Sample with
        {
            Source = Sample.Source.Replace(
                "public class B { public int X { get; set; } }",
                "public class B { public int X { get; set; } public int Orphan { get; set; } }",
                StringComparison.Ordinal),
        };

        Assert.NotEqual(GoldenFingerprint.Compute(Sample), GoldenFingerprint.Compute(withDiagnostic));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenFingerprintTests"`
Expected: FAIL to compile — `GoldenFingerprint` does not exist.

- [ ] **Step 3: Write the implementation**

Create `tests/DwarfMapper.Generator.Tests/Framework/GoldenFingerprint.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     sha256 over EVERYTHING observable for a case: every emitted file plus the full diagnostics, message text
///     included. Message text is in deliberately — nothing observable may move undetected. The cost is churn
///     when a message is reworded; the curated snapshots carry the readable diff for those.
/// </summary>
internal static class GoldenFingerprint
{
    public static string Compute(GoldenCase c)
    {
        ArgumentNullException.ThrowIfNull(c);

        var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
        var run = GeneratorRunner.Run(generator.Create(), c.Source);

        var sb = new StringBuilder();
        sb.Append(run.AllOutputsConcatenated);
        sb.Append("\n|DIAGNOSTICS|\n");

        // Emission order is not guaranteed stable, so sort before hashing.
        foreach (var d in run.Diagnostics
                     .OrderBy(d => d.Id, StringComparer.Ordinal)
                     .ThenBy(d => d.Location.ToString(), StringComparer.Ordinal)
                     .ThenBy(d => d.GetMessage(CultureInfo.InvariantCulture), StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture,
                $"{d.Id}:{d.Severity}:{d.Location}:{d.GetMessage(CultureInfo.InvariantCulture)}\n");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
```

Create `tests/DwarfMapper.Generator.Tests/Framework/GoldenManifest.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The manifest file: one sorted line per case, "<id> <sha256>". Never auto-created — a missing manifest
///     fails with instructions, because a self-healing golden file lets CI silently bless whatever it produced.
/// </summary>
internal static class GoldenManifest
{
    public const string UpdateEnvVar = "DWARF_GOLDEN_UPDATE";

    public static string Path =>
        System.IO.Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests", "Golden",
            "output-manifest.txt");

    public static IReadOnlyDictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(Path)) return result;

        foreach (var line in File.ReadAllLines(Path))
        {
            if (line.Length == 0 || line[0] == '#') continue;

            var space = line.LastIndexOf(' ');
            if (space <= 0) continue;

            result[line[..space]] = line[(space + 1)..];
        }

        return result;
    }

    public static void Write(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"# DwarfMapper golden output manifest — {entries.Count} cases.\n");
        sb.Append("# One line per pinned case: <case-id> <sha256 of generated source + full diagnostics>.\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"# Regenerate deliberately with {UpdateEnvVar}=1; never edit by hand.\n");

        foreach (var kv in entries.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value}\n");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, sb.ToString());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenFingerprintTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Framework/ tests/DwarfMapper.Generator.Tests/Golden/
git commit -m "test: golden fingerprint (source + full diagnostics) and manifest I/O

Fingerprint covers every emitted file plus full diagnostics including message text — the strictest option,
chosen deliberately so nothing observable can move undetected. Diagnostics are sorted before hashing because
emission order is not guaranteed stable. The manifest is never auto-created: a self-healing golden file would
let CI silently bless whatever it produced."
```

---

### Task 6: The invariance test and first manifest

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusTests.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` (generated, then committed)

**Interfaces:**
- Consumes: `GoldenCorpus.Cases()`, `GoldenFingerprint.Compute`, `GoldenManifest.Load/Write/UpdateEnvVar`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the test**

Create `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

/// <summary>
///     The refactoring safety net. Sub-projects 2-4 (shared engine core, emission layer, extractor split) are
///     behaviour-preserving restructurings; this is what proves it. Every pinned case's generated output and
///     diagnostics are fingerprinted and compared against the committed manifest.
/// </summary>
public class GoldenCorpusTests
{
    /// <summary>
    ///     Floor for the manifest size. Lowering it is a deliberate act needing justification — a corpus that
    ///     silently shrinks is the "green but checking nothing" failure this repo keeps finding.
    /// </summary>
    private const int MinimumCases = 850;

    [Fact]
    public void Generated_output_matches_the_golden_manifest()
    {
        var cases = GoldenCorpus.Cases();
        var actual = cases.ToDictionary(c => c.Id, GoldenFingerprint.Compute, StringComparer.Ordinal);

        if (Environment.GetEnvironmentVariable(GoldenManifest.UpdateEnvVar) == "1")
        {
            GoldenManifest.Write(actual);
            return;
        }

        var expected = GoldenManifest.Load();

        Assert.True(expected.Count > 0,
            $"No golden manifest at {GoldenManifest.Path}. It is never auto-created — generate it deliberately "
            + $"with {GoldenManifest.UpdateEnvVar}=1 and commit the result after reviewing it.");

        var added = actual.Keys.Where(k => !expected.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var removed = expected.Keys.Where(k => !actual.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var changed = actual.Where(kv => expected.TryGetValue(kv.Key, out var e) && e != kv.Value)
            .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(added.Count == 0 && removed.Count == 0 && changed.Count == 0,
            BuildFailure(added, removed, changed));
    }

    [Fact]
    public void The_corpus_has_not_silently_shrunk()
    {
        var cases = GoldenCorpus.Cases();

        Assert.True(cases.Count >= MinimumCases,
            $"Golden corpus has {cases.Count} cases, floor is {MinimumCases}. A shrinking corpus passes every "
            + "other test while covering less — lower the floor only deliberately.");

        // Each source must still contribute: one axis growing can mask another vanishing.
        Assert.Contains(cases, c => c.Id.StartsWith("cmb:", StringComparison.Ordinal));
        Assert.Contains(cases, c => c.Id.StartsWith("syn:", StringComparison.Ordinal));
        Assert.Contains(cases, c => c.Id.StartsWith("feat:", StringComparison.Ordinal));
        Assert.Contains(cases, c => c.GeneratorName == "MapToGenerator");
    }

    private static string BuildFailure(List<string> added, List<string> removed, List<string> changed)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Golden output moved. {changed.Count} changed, {added.Count} added, {removed.Count} removed.\n");

        if (changed.Count > 0) sb.Append("CHANGED:\n  ").AppendJoin("\n  ", changed).Append('\n');
        if (added.Count > 0) sb.Append("ADDED (new case needs review):\n  ").AppendJoin("\n  ", added).Append('\n');
        if (removed.Count > 0) sb.Append("REMOVED (corpus shrank):\n  ").AppendJoin("\n  ", removed).Append('\n');

        sb.Append('\n')
            .Append("If this change is INTENTIONAL: review the curated snapshots in Snapshots/ (they show real\n")
            .Append("text; hashes do not diff readably), then regenerate with ")
            .Append(GoldenManifest.UpdateEnvVar).Append("=1 and commit.\n")
            .Append("If it is NOT intentional, a refactor changed observable output — that is the bug.\n");

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Run to verify it fails (no manifest yet)**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusTests"`
Expected: `Generated_output_matches_the_golden_manifest` FAILS with "No golden manifest at …".
`The_corpus_has_not_silently_shrunk` should PASS (expected ~954 cases). If it fails, report the actual count
rather than lowering the floor silently.

- [ ] **Step 3: Generate the manifest**

Run (bash):
```bash
DWARF_GOLDEN_UPDATE=1 dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Generated_output_matches_the_golden_manifest"
```
Expected: PASS, and `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` now exists.

- [ ] **Step 4: Inspect the manifest before trusting it**

Run: `head -20 tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt && wc -l < tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt`
Expected: a header comment plus one `<id> <hash>` line per case; line count = cases + 3 header lines.
Confirm ids from all three axes and both generators appear.

- [ ] **Step 5: Re-run WITHOUT the env var to confirm it now passes**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Prove the net actually catches a change**

Temporarily add a trailing space to an emitted string in
`src/DwarfMapper.Generator/Pipeline/MapEmitter.cs` (for example change `"{\n"` to `"{ \n"` in the mapper-body
emission), then run the golden test.
Expected: FAIL listing many CHANGED case ids.
Then `git checkout -- src/DwarfMapper.Generator/Pipeline/MapEmitter.cs` and re-run.
Expected: PASS. **Do not commit the temporary edit.**

- [ ] **Step 7: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Golden/
git commit -m "test: golden invariance test + first committed manifest

The safety net for sub-projects 2-4. Failure names exactly which case ids changed/were added/removed and points
at the curated snapshots for a readable diff, since hashes do not diff usefully. Verified non-vacuous by
perturbing an emitted string and watching the manifest go red.

Depends on LF-normalised output (ISSUE-024): before that fix the same input hashed differently on Windows and
Linux and a shared manifest was impossible."
```

---

### Task 7: Curated readable snapshots

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Golden.cs`
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotSuite.Snap_Golden_*.verified.txt` (generated)

**Interfaces:**
- Consumes: `GoldenCorpus.Cases()`, `GeneratorRunner.Run`, `GeneratorRegistry.All`. Existing `SnapshotSuite`
  partial class convention: method returns `Task`, body ends `return Verify(generated);`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the snapshot tests**

Create `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Golden.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Readable counterparts to the golden manifest. The manifest gives breadth but its hashes do not diff, so
///     when an intentional emission change lands these are what a human actually reads to review it.
/// </summary>
public partial class SnapshotSuite
{
    private static string GoldenFeature(string featureId)
    {
        var c = GoldenCorpus.Cases().Single(x => x.Id == "feat:" + featureId);
        var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);

        return GeneratorRunner.Run(generator.Create(), c.Source).AllOutputsConcatenated;
    }

    [Fact] public Task Snap_Golden_Basic() => Verify(GoldenFeature("Basic"));
    [Fact] public Task Snap_Golden_UpdateInto() => Verify(GoldenFeature("UpdateInto"));
    [Fact] public Task Snap_Golden_Projection() => Verify(GoldenFeature("Projection"));
    [Fact] public Task Snap_Golden_SpanMap() => Verify(GoldenFeature("SpanMap"));
    [Fact] public Task Snap_Golden_AsyncStream() => Verify(GoldenFeature("AsyncStream"));
    [Fact] public Task Snap_Golden_FlattenGraph() => Verify(GoldenFeature("FlattenGraph"));
    [Fact] public Task Snap_Golden_Flatten() => Verify(GoldenFeature("Flatten"));
    [Fact] public Task Snap_Golden_ConstructorMapping() => Verify(GoldenFeature("ConstructorMapping"));
    [Fact] public Task Snap_Golden_EnumByName() => Verify(GoldenFeature("EnumByName"));
    [Fact] public Task Snap_Golden_FlagsEnumFromString() => Verify(GoldenFeature("FlagsEnumFromString"));
    [Fact] public Task Snap_Golden_NullStrategyThrow() => Verify(GoldenFeature("NullStrategyThrow"));
    [Fact] public Task Snap_Golden_PreserveReferences() => Verify(GoldenFeature("PreserveReferences"));
    [Fact] public Task Snap_Golden_DerivedTypes() => Verify(GoldenFeature("DerivedTypes"));
    [Fact] public Task Snap_Golden_Hooks() => Verify(GoldenFeature("Hooks"));
    [Fact] public Task Snap_Golden_ReverseMap() => Verify(GoldenFeature("ReverseMap"));
    [Fact] public Task Snap_Golden_CoLocatedGenerateMap() => Verify(GoldenFeature("CoLocatedGenerateMap"));
    [Fact] public Task Snap_Golden_RegistryBasic() => Verify(GoldenFeature("RegistryBasic"));
    [Fact] public Task Snap_Golden_RegistryCollection() => Verify(GoldenFeature("RegistryCollection"));
    [Fact] public Task Snap_Golden_RegistryNested() => Verify(GoldenFeature("RegistryNested"));
}
```

- [ ] **Step 2: Run to produce `.received.txt` files**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Snap_Golden"`
Expected: FAIL — 19 tests, each reporting a missing verified file and writing a `.received.txt`.

- [ ] **Step 3: Review then accept the snapshots**

Inspect two or three received files first — they must contain real generated C#, not empty strings:

```bash
head -30 tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotSuite.Snap_Golden_FlattenGraph.received.txt
```

Then accept all:

```bash
cd tests/DwarfMapper.Generator.Tests/Snapshots
for f in SnapshotSuite.Snap_Golden_*.received.txt; do mv "$f" "${f%.received.txt}.verified.txt"; done
cd -
```

- [ ] **Step 4: Re-run to confirm they pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Snap_Golden"`
Expected: PASS, 19 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/Snapshots/
git commit -m "test: curated readable snapshots for each golden feature case

The manifest gives breadth in one file but its hashes do not diff. These 19 snapshots are the human-readable
half of the hybrid: when an intentional emission change lands, this is what gets reviewed."
```

---

### Task 8: Framework self-tests — prove every assertion fires

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorFrameworkSelfTests.cs`

**Interfaces:**
- Consumes: `GeneratorCacheAssert`, `GoldenFingerprint`, `GoldenCase`, `GeneratorRunner`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the self-tests**

Create `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorFrameworkSelfTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Tests the framework itself. It concentrates a large number of assertions into a few helpers, so a helper
///     that silently passed everything would neuter the whole safety net while the suite stayed green — the
///     exact vacuity failure this repo keeps finding. Every assertion is proven to FIRE.
/// </summary>
public class GeneratorFrameworkSelfTests
{
    private const string Valid = """
                                 using DwarfMapper;
                                 namespace Demo;
                                 public class A { public int X { get; set; } }
                                 public class B { public int X { get; set; } }
                                 [DwarfMapper] public partial class M { public partial B Map(A a); }
                                 """;

    /// <summary>A generator whose model is a plain class with reference equality — caching cannot work.</summary>
    private sealed class NonEquatableGenerator : IIncrementalGenerator
    {
        public const string StepName = "NonEquatableStep";

        private sealed class Model
        {
            public string Name { get; init; } = "";
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                    static (ctx, _) => new Model { Name = ctx.Node.ToString() })
                .WithTrackingName(StepName);

            context.RegisterSourceOutput(models, static (spc, m) => spc.AddSource(
                "NonEquatable_" + m.Name.GetHashCode(System.StringComparison.Ordinal) + ".g.cs", "// x"));
        }
    }

    [Fact]
    public void FullyCachedOnRerun_FIRES_for_a_non_equatable_model()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            GeneratorCacheAssert.FullyCachedOnRerun(
                new NonEquatableGenerator(), Valid, new[] { NonEquatableGenerator.StepName }));

        Assert.Contains("Cached/Unchanged", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FullyCachedOnRerun_passes_for_the_real_generator()
    {
        GeneratorCacheAssert.FullyCachedOnRerun(new DwarfGenerator(), Valid, DwarfGenerator.AllStepNames);
    }

    [Fact]
    public void Fingerprint_is_sensitive_to_a_single_character_of_output()
    {
        var a = new GoldenCase("self:a", Valid, "DwarfGenerator");
        var b = a with { Source = Valid.Replace("public int X", "public int Xx", StringComparison.Ordinal) };

        Assert.NotEqual(GoldenFingerprint.Compute(a), GoldenFingerprint.Compute(b));
    }

    [Fact]
    public void Runner_returns_every_emitted_file_not_just_the_first()
    {
        var run = GeneratorRunner.Run(new DwarfGenerator(), Valid);

        Assert.True(run.OutputsByHintName.Count > 1,
            "Expected the per-mapper file AND the assembly-wide aggregates. If only one file comes back the "
            + "runner has inherited GeneratorTestHarness.Run's aggregate filter and the golden corpus would "
            + "never cover the facade/DI/manifest emitters.");
    }
}
```

- [ ] **Step 2: Run the self-tests**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GeneratorFrameworkSelfTests"`
Expected: PASS, 4 tests. `FullyCachedOnRerun_FIRES_for_a_non_equatable_model` passing proves the assertion is
not vacuous.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/SelfValidation/
git commit -m "test: prove every framework assertion fires

The framework concentrates hundreds of assertions into a few helpers; one that silently passed everything would
neuter the net while the suite stayed green. A deliberately non-equatable generator proves the caching
assertion fires, and a one-character source change proves the fingerprint is sensitive."
```

---

### Task 9: Ratchet gates

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs`

**Interfaces:**
- Consumes: `GeneratorRegistry.All`, `GoldenCorpus.Cases()`.
- Produces: nothing.

- [ ] **Step 1: Write the gates**

Create `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Ratchets that keep the framework applied. Deduplicating once fixes today; without a gate the next
///     generator ships uncovered — which is exactly how MapToGenerator ended up with no cacheability tests at
///     all while DwarfGenerator had six.
/// </summary>
public class GeneratorTestingScanTests
{
    [Fact]
    public void Every_generator_in_src_is_registered_for_testing()
    {
        var declared = typeof(DwarfGenerator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var registered = GeneratorRegistry.All.Select(g => g.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(declared.SequenceEqual(registered, StringComparer.Ordinal),
            "Generators in src/ do not match GeneratorRegistry.All.\n  declared:   "
            + string.Join(", ", declared) + "\n  registered: " + string.Join(", ", registered)
            + "\nAdd the new generator to GeneratorRegistry so it gets cacheability and golden coverage.");
    }

    [Fact]
    public void Every_tracking_name_constant_is_registered_in_the_battery()
    {
        // A WithTrackingName the battery never asserts is decoration.
        foreach (var (assemblyType, registeredName) in new[]
                 {
                     (typeof(DwarfGenerator), "DwarfGenerator"),
                     (typeof(DwarfMapper.Generator.Registry.MapToGenerator), "MapToGenerator"),
                 })
        {
            var declared = assemblyType
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                            && f.Name.EndsWith("StepName", StringComparison.Ordinal))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            var registered = GeneratorRegistry.All.Single(g => g.Name == registeredName).TrackingNames;

            foreach (var name in declared)
                Assert.True(registered.Contains(name),
                    $"{registeredName} declares step name '{name}' but AllStepNames does not include it, so the "
                    + "battery never asserts that step is cacheable.");
        }
    }

    [Fact]
    public void The_golden_corpus_covers_every_registered_generator()
    {
        var covered = GoldenCorpus.Cases().Select(c => c.GeneratorName).Distinct(StringComparer.Ordinal).ToList();

        foreach (var g in GeneratorRegistry.All)
            Assert.True(covered.Contains(g.Name, StringComparer.Ordinal),
                $"Generator '{g.Name}' contributes no golden cases, so a refactor could change its output "
                + "undetected. Add feature cases for it to GoldenCorpus.FeatureCases().");
    }
}
```

- [ ] **Step 2: Run the gates**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GeneratorTestingScanTests"`
Expected: PASS, 3 tests. If `Every_generator_in_src_is_registered_for_testing` fails, a generator exists that is
not registered — register it rather than relaxing the gate.

- [ ] **Step 3: Prove the gates are non-vacuous**

Temporarily comment out the `MapToGenerator` entry in `GeneratorRegistry.All`, then run the gates.
Expected: FAIL on both `Every_generator_in_src_is_registered_for_testing` and
`The_golden_corpus_covers_every_registered_generator`.
Restore the entry and re-run. Expected: PASS. **Do not commit the temporary edit.**

- [ ] **Step 4: Run the full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: all green, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/SelfValidation/
git commit -m "test: ratchets keeping the generator framework applied

Every IIncrementalGenerator in src/ must be registered, every declared StepName must be in the battery, and
every registered generator must contribute golden cases. Verified non-vacuous by unregistering a generator and
watching two gates go red."
```

---

## Self-Review

**Spec coverage** — every section maps to a task:

| Spec section | Task |
|---|---|
| §4.1 `GeneratorRunner`, all outputs by hint name | 1 |
| §4.2 `GeneratorCacheAssert` (3 assertions) | 3 |
| §4.3 corpus: type/fuzz/feature axes, both generators, pinned ids | 4 |
| §4.3 fingerprint (source + full diagnostics), manifest, update path | 5, 6 |
| §4.3 curated snapshots | 7 |
| §4.4 ratchets | 9 |
| §5 determinism (fixed seeds, sorted diagnostics, LF dependency) | 4 (seeds), 5 (sort), 6 (commit note) |
| §6 error handling (missing/added/removed/changed, floor, non-vacuity) | 6 |
| §7 testing the framework itself | 8 |
| src tracking names | 2 |

**Placeholder scan:** none — every code step contains complete, runnable code.

**Type consistency:** `GeneratorRun.AllOutputsConcatenated` (defined Task 1) is used in Tasks 5 and 7.
`GeneratorUnderTest.Create/Name/TrackingNames` (Task 2) is used in Tasks 3, 5, 7, 9. `GoldenCase.Id/Source/GeneratorName`
(Task 4) is used in Tasks 5, 6, 7, 8. `GoldenManifest.UpdateEnvVar` (Task 5) is used in Task 6.
`DwarfGenerator.AllStepNames` / `MapToGenerator.AllStepNames` (Task 2) are used in Tasks 2, 3, 8, 9.

**Known risk to watch during execution:** Task 3 may reveal that `MapToGenerator` is genuinely not cacheable.
That is a real finding, not a test defect — record it and report rather than weakening the assertion.
