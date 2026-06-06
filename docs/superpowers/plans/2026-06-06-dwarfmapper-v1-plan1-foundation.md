# DwarfMapper.NET — Plan 1: Defensive Foundation + Flat-Mapping Walking Skeleton

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the repository, security/CRA-aligned build infrastructure, and a working Roslyn incremental source generator that maps flat objects via *sort → pair → prove → emit*, turning an incomplete map into a **build error**.

**Architecture:** Three projects — `DwarfMapper` (runtime attributes, `netstandard2.0`, zero deps), `DwarfMapper.Generator` (`IIncrementalGenerator`, `netstandard2.0`), and test projects. The generator uses `ForAttributeWithMetadataName` and value-equatable record models (no `ISymbol`/`Location` stored in the pipeline) so incrementality is preserved. Diagnostics are emitted as errors by default. Emitted code uses object-initializer syntax (supports `init` setters) with a null guard.

**Tech Stack:** .NET 10 / C# latest, Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.14.0), xUnit, Verify.SourceGenerators (snapshot tests), Central Package Management, GitHub Actions (build/test/analyzers-as-errors/AOT+trim gate/CodeQL/CycloneDX SBOM).

**Out of scope (later plans):** type converters/enums/null strategies/nested (Plan 2), collections + SIMD/blit fast-path (Plan 3), `DwarfMapper.Testing` fixtures/fuzzer/`[RoundTrip]`/informed dumps (Plan 4).

**Conventions for every code file created in this plan:** the first line is the SPDX header `// SPDX-License-Identifier: GPL-2.0-only`.

---

## File structure (created/modified in this plan)

```
DwarfMapper.NET.sln                                  # modify: re-point projects
Directory.Build.props                                # create: shared defensive build settings + SPDX/package metadata
Directory.Packages.props                             # create: central package versions
.editorconfig                                        # create: analyzer severities, style
.github/workflows/ci.yml                             # create: CI + security gates
SECURITY.md                                          # create: vuln disclosure (CRA)
CONTRIBUTING.md                                      # create: GPLv2 contribution terms
src/DwarfMapper/DwarfMapper.csproj                   # create
src/DwarfMapper/DwarfMapperAttribute.cs              # create
src/DwarfMapper/MapIgnoreAttribute.cs                # create
src/DwarfMapper.Generator/DwarfMapper.Generator.csproj   # create
src/DwarfMapper.Generator/DwarfGenerator.cs          # create: IIncrementalGenerator entry
src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs   # create
src/DwarfMapper.Generator/Diagnostics/DiagnosticInfo.cs          # create
src/DwarfMapper.Generator/Diagnostics/LocationInfo.cs            # create
src/DwarfMapper.Generator/Collections/EquatableArray.cs         # create
src/DwarfMapper.Generator/Model/MemberMap.cs         # create
src/DwarfMapper.Generator/Model/MapMethodModel.cs    # create
src/DwarfMapper.Generator/Model/MapperClassModel.cs  # create
src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs   # create: semantic -> model + diagnostics
src/DwarfMapper.Generator/Pipeline/MapEmitter.cs     # create: model -> source text
tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj   # create
tests/DwarfMapper.Generator.Tests/GeneratorTestHarness.cs              # create
tests/DwarfMapper.Generator.Tests/FlatMappingTests.cs                  # create
tests/DwarfMapper.Generator.Tests/DiagnosticTests.cs                   # create
tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj  # create (replaces stub DwarfMapper.NET test project)
tests/DwarfMapper.IntegrationTests/FlatMappingRuntimeTests.cs           # create
samples/DwarfMapper.AotSample/DwarfMapper.AotSample.csproj              # create (AOT/trim gate)
samples/DwarfMapper.AotSample/Program.cs                                # create
```

The existing stub `DwarfMapper.NET/DwarfMapper.NET.csproj` and `UnitTest1.cs` are replaced by `tests/DwarfMapper.IntegrationTests`.

---

## Task 1: Solution restructure + shared build settings

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Modify: `DwarfMapper.NET.sln`
- Delete: `DwarfMapper.NET/DwarfMapper.NET.csproj`, `DwarfMapper.NET/UnitTest1.cs`

- [ ] **Step 1: Remove the scaffolded stub project from the solution and disk**

Run:
```powershell
dotnet sln DwarfMapper.NET.sln remove DwarfMapper.NET\DwarfMapper.NET.csproj
Remove-Item -Recurse -Force DwarfMapper.NET
```
Expected: solution no longer references the stub; folder gone.

- [ ] **Step 2: Create `Directory.Packages.props` (central package management)**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
    <PackageVersion Include="Verify.Xunit" Version="28.16.0" />
    <PackageVersion Include="Verify.SourceGenerators" Version="2.5.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `Directory.Build.props` (defensive defaults applied to every project)**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors></WarningsNotAsErrors>

    <!-- Static analysis: maximum, defensive from the start -->
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <AnalysisMode>All</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- Reproducible / CRA: deterministic builds, source link -->
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>

    <!-- Package metadata (license is unambiguous in every artifact) -->
    <Authors>DwarfMapper contributors</Authors>
    <PackageLicenseExpression>GPL-2.0-only</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/REPLACE/DwarfMapper.NET</PackageProjectUrl>
    <RepositoryUrl>https://github.com/REPLACE/DwarfMapper.NET</RepositoryUrl>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Verify the solution restores with no projects**

Run: `dotnet restore DwarfMapper.NET.sln`
Expected: succeeds (no projects to restore yet, exit code 0).

- [ ] **Step 5: Commit**

```powershell
git add Directory.Build.props Directory.Packages.props DwarfMapper.NET.sln
git commit -m "build: central package mgmt + defensive Directory.Build.props; drop stub project"
```

---

## Task 2: Repo hygiene & CRA security docs

**Files:**
- Create: `.editorconfig`
- Create: `SECURITY.md`
- Create: `CONTRIBUTING.md`

- [ ] **Step 1: Create `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
indent_size = 4
trim_trailing_whitespace = true

[*.{yml,yaml,json}]
indent_size = 2

[*.cs]
# Treat nullable and reference-correctness issues as build-breaking.
dotnet_diagnostic.CS8600.severity = error
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8625.severity = error
# Require SPDX file header.
dotnet_diagnostic.IDE0073.severity = error
file_header_template = SPDX-License-Identifier: GPL-2.0-only
```

- [ ] **Step 2: Create `SECURITY.md` (CRA: coordinated vulnerability disclosure)**

```markdown
# Security Policy

## Supported versions
DwarfMapper is pre-1.0. Security fixes are applied to `main` and the latest tagged release.

## Reporting a vulnerability
Please report security issues privately via GitHub Security Advisories
("Report a vulnerability" on the repository Security tab). Do **not** open a
public issue for undisclosed vulnerabilities.

We aim to acknowledge reports within 72 hours and to provide a remediation
timeline within 7 days.

## Scope
DwarfMapper is a compile-time source generator. It performs no reflection and
no runtime code generation. Reports of interest include: generated code that
is not memory-safe, an analyzer that fails to block an unprovable `unsafe`
blit, or any supply-chain concern in the build/release pipeline.

## Build integrity
Releases are deterministic, ship a CycloneDX SBOM, and are produced from the
audited GitHub Actions workflow in `.github/workflows/`.
```

- [ ] **Step 3: Create `CONTRIBUTING.md`**

```markdown
# Contributing

Thank you for digging in. DwarfMapper is licensed **GPL-2.0-only**.

By submitting a contribution you agree it is licensed under GPL-2.0-only and
that you have the right to submit it (per the Developer Certificate of Origin).
Sign your commits with `git commit -s`.

## Ground rules
- Every source file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- Builds are warning-clean (`TreatWarningsAsErrors`). Fix analyzer findings;
  do not suppress without a justification comment.
- New mapping behavior requires: a generator snapshot test **and** a runtime
  integration test. A bug fix starts with a failing test.
- The generator must remain reflection-free and AOT/trim-safe; the AOT sample
  must publish clean.
```

- [ ] **Step 4: Verify header template is honored (sanity)**

Run: `dotnet format whitespace --verify-no-changes --folder . 2>$null; echo "editorconfig present"`
Expected: prints `editorconfig present` (formatting may report nothing since no .cs files exist yet).

- [ ] **Step 5: Commit**

```powershell
git add .editorconfig SECURITY.md CONTRIBUTING.md
git commit -m "docs: editorconfig (SPDX + analyzers), SECURITY.md (CRA disclosure), CONTRIBUTING"
```

---

## Task 3: CI workflow with security gates

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create the workflow**

> Note: action refs use tags for readability. Before the first release, pin each `uses:` to a full commit SHA (CRA/SLSA supply-chain hardening).

```yaml
name: ci
on:
  push:
    branches: [ main ]
  pull_request:
permissions:
  contents: read
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore DwarfMapper.NET.sln
      - run: dotnet build DwarfMapper.NET.sln -c Release --no-restore
      - run: dotnet test DwarfMapper.NET.sln -c Release --no-build --collect:"XPlat Code Coverage"

  aot-trim-gate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      # Fails the build on any IL2xxx (trim) or IL3xxx (AOT) warning.
      - run: dotnet publish samples/DwarfMapper.AotSample/DwarfMapper.AotSample.csproj -c Release -r linux-x64 -p:PublishAot=true -warnaserror

  codeql:
    runs-on: ubuntu-latest
    permissions:
      security-events: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - uses: github/codeql-action/init@v3
        with:
          languages: csharp
      - run: dotnet build DwarfMapper.NET.sln -c Release
      - uses: github/codeql-action/analyze@v3

  sbom:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet tool install --global CycloneDX
      - run: dotnet CycloneDX DwarfMapper.NET.sln -o ./sbom
      - uses: actions/upload-artifact@v4
        with:
          name: sbom
          path: ./sbom
```

- [ ] **Step 2: Validate YAML syntax locally**

Run: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('yaml ok')"`
Expected: `yaml ok`

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/ci.yml
git commit -m "ci: build/test, AOT+trim gate, CodeQL, CycloneDX SBOM (CRA-aligned)"
```

---

## Task 4: `DwarfMapper` runtime attributes (TDD)

**Files:**
- Create: `src/DwarfMapper/DwarfMapper.csproj`
- Create: `src/DwarfMapper/DwarfMapperAttribute.cs`
- Create: `src/DwarfMapper/MapIgnoreAttribute.cs`
- Create: `tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj` (test project, used here and later)
- Create: `tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs`

- [ ] **Step 1: Create the runtime project**

`src/DwarfMapper/DwarfMapper.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsAotCompatible>true</IsAotCompatible>
    <IsTrimmable>true</IsTrimmable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the test project (referencing the runtime + generator-test deps)**

`tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <!-- Generator pipeline correctness is the unit under test; keep nullable strict. -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
    <PackageReference Include="Verify.Xunit" />
    <PackageReference Include="Verify.SourceGenerators" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DwarfMapper\DwarfMapper.csproj" />
    <ProjectReference Include="..\..\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```
> The generator project does not exist yet (Task 5). For this task only, temporarily comment out the second `ProjectReference` line; Task 5 Step 5 re-enables it.

- [ ] **Step 3: Write the failing test**

`tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests;

public class AttributeContractTests
{
    [Fact]
    public void DwarfMapperAttribute_targets_classes_only()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(DwarfMapperAttribute), typeof(AttributeUsageAttribute))!;
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void MapIgnoreAttribute_exposes_target_member()
    {
        var attr = new MapIgnoreAttribute("Secret");
        Assert.Equal("Secret", attr.TargetMember);
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: FAIL — `DwarfMapperAttribute` / `MapIgnoreAttribute` do not exist (compile error).

- [ ] **Step 5: Implement the attributes**

`src/DwarfMapper/DwarfMapperAttribute.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a partial class as a DwarfMapper. The generator implements the
/// partial mapping methods declared on it at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperAttribute : Attribute
{
}
```

`src/DwarfMapper/MapIgnoreAttribute.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Explicitly excludes a destination member from completeness checking and
/// mapping. Required to silence DWARF001 for an intentionally unmapped member.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapIgnoreAttribute : Attribute
{
    public MapIgnoreAttribute(string targetMember) => TargetMember = targetMember;

    /// <summary>Name of the destination member to ignore.</summary>
    public string TargetMember { get; }
}
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS (2 tests).

- [ ] **Step 7: Add both projects to the solution and commit**

```powershell
dotnet sln add src\DwarfMapper\DwarfMapper.csproj
dotnet sln add tests\DwarfMapper.Generator.Tests\DwarfMapper.Generator.Tests.csproj
git add src/DwarfMapper tests/DwarfMapper.Generator.Tests DwarfMapper.NET.sln
git commit -m "feat(runtime): DwarfMapper + MapIgnore attributes with contract tests"
```

---

## Task 5: Generator project skeleton + analyzer packaging

**Files:**
- Create: `src/DwarfMapper.Generator/DwarfMapper.Generator.csproj`
- Create: `src/DwarfMapper.Generator/DwarfGenerator.cs`

- [ ] **Step 1: Create the generator project**

`src/DwarfMapper.Generator/DwarfMapper.Generator.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <DevelopmentDependency>true</DevelopmentDependency>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <!-- Pack the generator into the analyzer folder of the NuGet package. -->
  <ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true" PackagePath="analyzers/dotnet/cs" Visible="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create a minimal generator that registers but emits nothing yet**

`src/DwarfMapper.Generator/DwarfGenerator.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class DwarfGenerator : IIncrementalGenerator
{
    internal const string MarkerAttributeFullName = "DwarfMapper.DwarfMapperAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mappers = context.SyntaxProvider.ForAttributeWithMetadataName(
            MarkerAttributeFullName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, ct) => MapperExtractor.Extract(ctx, ct));

        context.RegisterSourceOutput(mappers, static (spc, model) => Execute(spc, model));
    }

    private static void Execute(SourceProductionContext spc, MapperClassModel model)
    {
        foreach (var diagnostic in model.Diagnostics)
        {
            spc.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        if (model.HasBlockingError)
        {
            return;
        }

        var source = MapEmitter.Emit(model);
        spc.AddSource($"{model.HintName}.g.cs", source);
    }
}
```
> This references `MapperExtractor`, `MapperClassModel`, and `MapEmitter`, created in Tasks 7–12. It will not compile until those exist; that is expected — Step 3 only verifies the project file is valid by restoring.

- [ ] **Step 3: Verify restore (not build) of the generator project**

Run: `dotnet restore src/DwarfMapper.Generator/DwarfMapper.Generator.csproj`
Expected: succeeds (exit 0).

- [ ] **Step 4: Add to solution**

Run: `dotnet sln add src\DwarfMapper.Generator\DwarfMapper.Generator.csproj`

- [ ] **Step 5: Re-enable the generator ProjectReference in the test project**

In `tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj`, ensure the generator is referenced **as an analyzer** so it runs against the test compilation:
```xml
    <ProjectReference Include="..\..\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
```

- [ ] **Step 6: Commit (build will be restored to green by Task 12)**

```powershell
git add src/DwarfMapper.Generator tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj DwarfMapper.NET.sln
git commit -m "feat(gen): incremental generator skeleton + analyzer packaging"
```

---

## Task 6: Generator test harness

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/GeneratorTestHarness.cs`

- [ ] **Step 1: Create the harness (runs the generator over source text, returns result)**

`tests/DwarfMapper.Generator.Tests/GeneratorTestHarness.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Immutable;
using System.Linq;
using DwarfMapper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

internal static class GeneratorTestHarness
{
    public static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "DwarfMapperTestAsm",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        var generated = output.SyntaxTrees
            .Where(t => t.FilePath.EndsWith(".g.cs", System.StringComparison.Ordinal))
            .Select(t => t.ToString())
            .FirstOrDefault() ?? string.Empty;

        return (genDiagnostics, generated);
    }
}
```

- [ ] **Step 2: Verify it compiles in isolation (no build of generator yet means this won't run; defer execution)**

This file references `DwarfGenerator` (exists) but the generator project does not build until Task 12. No command here.

- [ ] **Step 3: Commit**

```powershell
git add tests/DwarfMapper.Generator.Tests/GeneratorTestHarness.cs
git commit -m "test(gen): add generator test harness (CSharpGeneratorDriver)"
```

---

## Task 7: Equatable infrastructure

**Files:**
- Create: `src/DwarfMapper.Generator/Collections/EquatableArray.cs`
- Create: `src/DwarfMapper.Generator/Diagnostics/LocationInfo.cs`
- Create: `src/DwarfMapper.Generator/Diagnostics/DiagnosticInfo.cs`

- [ ] **Step 1: Create `EquatableArray<T>` (structural equality for incrementality)**

`src/DwarfMapper.Generator/Collections/EquatableArray.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DwarfMapper.Generator.Collections;

/// <summary>
/// An <see cref="ImmutableArray{T}"/> wrapper with value (structural) equality,
/// so it can be used inside incremental-generator models without defeating caching.
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[]? array) => _array = array;

    public int Count => _array?.Length ?? 0;

    public T this[int index] => _array![index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array is null || other._array is null)
        {
            return _array is null && other._array is null;
        }
        return _array.AsSpan().SequenceEqual(other._array.AsSpan());
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null) return 0;
        var hash = 17;
        foreach (var item in _array)
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static EquatableArray<T> From(IEnumerable<T> items) => new(items.ToArray());
}
```

- [ ] **Step 2: Create `LocationInfo` (serializable location for deferred diagnostics)**

`src/DwarfMapper.Generator/Diagnostics/LocationInfo.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DwarfMapper.Generator.Diagnostics;

/// <summary>Value-equatable replacement for <see cref="Location"/> in pipeline models.</summary>
public sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location location)
    {
        if (location.SourceTree is null) return null;
        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }
}
```

- [ ] **Step 3: Create `DiagnosticInfo`**

`src/DwarfMapper.Generator/Diagnostics/DiagnosticInfo.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

/// <summary>Value-equatable diagnostic carrier; converted to a real <see cref="Diagnostic"/> at output time.</summary>
public sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, string MessageArg)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, MessageArg);

    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;
}
```

- [ ] **Step 4: Verify the runtime + these files restore (build deferred to Task 12)**

Run: `dotnet build src/DwarfMapper/DwarfMapper.csproj -c Debug`
Expected: PASS (runtime project still builds independently).

- [ ] **Step 5: Commit**

```powershell
git add src/DwarfMapper.Generator/Collections src/DwarfMapper.Generator/Diagnostics
git commit -m "feat(gen): equatable array, location info, diagnostic info"
```

---

## Task 8: Diagnostic descriptors

**Files:**
- Create: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`

- [ ] **Step 1: Create the descriptors**

`src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

public static class DiagnosticDescriptors
{
    private const string Category = "DwarfMapper";

    public static readonly DiagnosticDescriptor MapperNotPartial = new(
        "DWARF002",
        "Mapper type must be partial",
        "Mapper type '{0}' must be declared 'partial'",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidMapMethod = new(
        "DWARF003",
        "Invalid mapping method signature",
        "Mapping method '{0}' must be a partial instance method with a non-void return type and exactly one parameter",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedMember = new(
        "DWARF001",
        "Destination member is not mapped",
        "Destination member '{0}' has no matching source member; map it or annotate the method with [MapIgnore(\"{0}\")]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoImplicitConversion = new(
        "DWARF005",
        "No implicit conversion between mapped members",
        "Cannot map to '{0}': no implicit conversion exists from the source member type (custom converters arrive in a later release)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        "DWARF006",
        "Destination type is not constructible",
        "Destination type '{0}' must have an accessible parameterless constructor",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
```

- [ ] **Step 2: Commit**

```powershell
git add src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs
git commit -m "feat(gen): DWARF001-006 diagnostic descriptors"
```

---

## Task 9: Pipeline models

**Files:**
- Create: `src/DwarfMapper.Generator/Model/MemberMap.cs`
- Create: `src/DwarfMapper.Generator/Model/MapMethodModel.cs`
- Create: `src/DwarfMapper.Generator/Model/MapperClassModel.cs`

- [ ] **Step 1: Create `MemberMap`**

`src/DwarfMapper.Generator/Model/MemberMap.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>One resolved destination&lt;-source member assignment.</summary>
public sealed record MemberMap(string TargetName, string SourceName) : System.IEquatable<MemberMap>;
```

- [ ] **Step 2: Create `MapMethodModel`**

`src/DwarfMapper.Generator/Model/MapMethodModel.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper.Generator.Collections;

namespace DwarfMapper.Generator.Model;

/// <summary>A single partial mapping method to implement.</summary>
public sealed record MapMethodModel(
    string MethodName,
    string Accessibility,
    string ReturnTypeFullName,
    string ParameterTypeFullName,
    string ParameterName,
    bool ParameterIsReferenceType,
    EquatableArray<MemberMap> Members) : IEquatable<MapMethodModel>;
```

- [ ] **Step 3: Create `MapperClassModel`**

`src/DwarfMapper.Generator/Model/MapperClassModel.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;

namespace DwarfMapper.Generator.Model;

/// <summary>The full, value-equatable description of one [DwarfMapper] class.</summary>
public sealed record MapperClassModel(
    string Namespace,
    string ClassName,
    string Accessibility,
    EquatableArray<MapMethodModel> Methods,
    EquatableArray<DiagnosticInfo> Diagnostics) : IEquatable<MapperClassModel>
{
    public string HintName => string.IsNullOrEmpty(Namespace) ? ClassName : $"{Namespace}.{ClassName}";

    public bool HasBlockingError => Diagnostics.Any(d => d.IsError);
}
```

- [ ] **Step 4: Build the runtime + confirm models compile via a throwaway build of the generator once Task 11/12 exist.** No command now.

- [ ] **Step 5: Commit**

```powershell
git add src/DwarfMapper.Generator/Model
git commit -m "feat(gen): value-equatable pipeline models"
```

---

## Task 10: Extraction — discovery, partial/signature validation (DWARF002/003)

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Create: `tests/DwarfMapper.Generator.Tests/DiagnosticTests.cs`

- [ ] **Step 1: Write the failing diagnostic tests**

`tests/DwarfMapper.Generator.Tests/DiagnosticTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;

namespace DwarfMapper.Generator.Tests;

public class DiagnosticTests
{
    [Fact]
    public void NonPartial_mapper_reports_DWARF002()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper]
            public class PersonMapper            // not partial
            {
                public PersonDto ToDto(Person p) => new();
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF002");
    }

    [Fact]
    public void Unmapped_destination_member_reports_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF001" && d.GetMessage().Contains("Name"));
    }

    [Fact]
    public void Ignored_member_suppresses_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper
            {
                [MapIgnore("Name")]
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter DiagnosticTests`
Expected: FAIL (extractor/emitter not implemented; build error or no diagnostics).

- [ ] **Step 3: Implement `MapperExtractor` (sort → pair → prove)**

`src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal static class MapperExtractor
{
    public static MapperClassModel Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        // DWARF002: the mapper class must be partial.
        var isPartial = classSyntax.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MapperNotPartial,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));
        }

        var classIgnores = ReadIgnores(classSymbol);
        var methods = new List<MapMethodModel>();

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition)
            {
                continue;
            }

            var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None);

            // DWARF003: must return non-void and take exactly one parameter.
            if (method.ReturnsVoid || method.Parameters.Length != 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            var targetType = (INamedTypeSymbol)method.ReturnType;
            var sourceType = method.Parameters[0].Type;

            // DWARF006: destination must be constructible.
            if (!HasAccessibleParameterlessCtor(targetType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NoParameterlessConstructor, methodLocation, targetType.Name));
                continue;
            }

            var ignores = new HashSet<string>(classIgnores);
            foreach (var i in ReadIgnores(method)) ignores.Add(i);

            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics);

            methods.Add(new MapMethodModel(
                method.Name,
                Accessibility(method.DeclaredAccessibility),
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Name,
                sourceType.IsReferenceType,
                EquatableArray<MemberMap>.From(members)));
        }

        return new MapperClassModel(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            Accessibility(classSymbol.DeclaredAccessibility),
            EquatableArray<MapMethodModel>.From(methods),
            EquatableArray<DiagnosticInfo>.From(diagnostics));
    }

    private static IReadOnlyList<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        var sources = ReadableProperties(sourceType)
            .ToDictionary(p => p.Name, p => p, System.StringComparer.Ordinal);

        // SORT: canonical, declaration-order-independent ordering by member name.
        var targets = SettableProperties(targetType)
            .OrderBy(p => p.Name, System.StringComparer.Ordinal)
            .ToList();

        var result = new List<MemberMap>();
        foreach (var target in targets)
        {
            if (ignores.Contains(target.Name))
            {
                continue;
            }

            // PAIR: equal resolved name.
            if (!sources.TryGetValue(target.Name, out var source))
            {
                // PROVE (completeness): every destination member must be accounted for.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }

            // PROVE (safety): an implicit conversion must exist (no silent coercion).
            var conversion = compilation.ClassifyCommonConversion(source.Type, target.Type);
            if (!conversion.IsImplicit || conversion.IsUserDefined)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, target.Name));
                continue;
            }

            result.Add(new MemberMap(target.Name, source.Name));
        }

        return result;
    }

    private static IEnumerable<IPropertySymbol> ReadableProperties(ITypeSymbol type) =>
        type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Microsoft.CodeAnalysis.Accessibility.Public });

    private static IEnumerable<IPropertySymbol> SettableProperties(ITypeSymbol type) =>
        type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.SetMethod is { DeclaredAccessibility: Microsoft.CodeAnalysis.Accessibility.Public });

    private static bool HasAccessibleParameterlessCtor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public);

    private static IEnumerable<string> ReadIgnores(ISymbol symbol) =>
        symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.MapIgnoreAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)!
            .Cast<string>();

    private static string Accessibility(Microsoft.CodeAnalysis.Accessibility a) => a switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        Microsoft.CodeAnalysis.Accessibility.Private => "private",
        _ => "public",
    };
}
```

- [ ] **Step 4: Do not run yet — the emitter (Task 12) is required for the generator to compile.** Proceed to Task 11.

- [ ] **Step 5: Commit**

```powershell
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs tests/DwarfMapper.Generator.Tests/DiagnosticTests.cs
git commit -m "feat(gen): extraction with sort/pair/prove + DWARF001/002/003/005/006 (tests pending emitter)"
```

---

## Task 11: `ClassifyCommonConversion` helper verification

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` (only if the API name differs)

> `Compilation.ClassifyCommonConversion(ITypeSymbol, ITypeSymbol)` returns a `CommonConversion` with `IsImplicit` and `IsUserDefined`. This is the language-agnostic conversion API available on `Compilation`. This task confirms the call compiles against the pinned Roslyn version.

- [ ] **Step 1: Confirm the API exists in the pinned Roslyn package**

Run: `dotnet build src/DwarfMapper/DwarfMapper.csproj -c Debug`
Then inspect the Roslyn surface:
Run: `dotnet build src/DwarfMapper.Generator/DwarfMapper.Generator.csproj -c Debug 2>&1 | Select-String -Pattern "ClassifyCommonConversion|error CS" `
Expected: no `CS` error referencing `ClassifyCommonConversion`. (Other `CS` errors for the not-yet-existing `MapEmitter` are expected and handled in Task 12.)

- [ ] **Step 2: If `ClassifyCommonConversion` is unavailable, substitute the C#-specific API**

Only if Step 1 reported `ClassifyCommonConversion` as missing, change the call in `ResolveMembers` to use the C# semantic model conversion classification by casting the compilation:
```csharp
var csharpCompilation = (Microsoft.CodeAnalysis.CSharp.CSharpCompilation)compilation;
var conversion = csharpCompilation.ClassifyConversion(source.Type, target.Type);
// CSharp Conversion exposes .IsImplicit and .IsUserDefined
if (!conversion.IsImplicit || conversion.IsUserDefined) { ... }
```

- [ ] **Step 3: Commit only if a change was needed**

```powershell
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs
git commit -m "fix(gen): use available Roslyn conversion-classification API"
```

---

## Task 12: Emitter + green build (TDD turns green here)

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`
- Create: `tests/DwarfMapper.Generator.Tests/FlatMappingTests.cs`

- [ ] **Step 1: Write the failing emit test**

`tests/DwarfMapper.Generator.Tests/FlatMappingTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Tests;

public class FlatMappingTests
{
    [Fact]
    public void Emits_sorted_object_initializer_with_null_guard()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);

        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("partial global::Demo.PersonDto ToDto", generated);
        Assert.Contains("ArgumentNullException", generated);
        // Sorted by name: Age assigned before Name.
        var ageIdx = generated.IndexOf("Age =", System.StringComparison.Ordinal);
        var nameIdx = generated.IndexOf("Name =", System.StringComparison.Ordinal);
        Assert.True(ageIdx > 0 && nameIdx > ageIdx);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter FlatMappingTests`
Expected: FAIL — `MapEmitter` does not exist (build error).

- [ ] **Step 3: Implement `MapEmitter`**

`src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Text;
using DwarfMapper.Generator.Model;

namespace DwarfMapper.Generator.Pipeline;

internal static class MapEmitter
{
    public static string Emit(MapperClassModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// SPDX-License-Identifier: GPL-2.0-only");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var indent = "";
        if (!string.IsNullOrEmpty(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"{model.Accessibility} partial class {model.ClassName}");
        sb.AppendLine("{");

        foreach (var method in model.Methods)
        {
            EmitMethod(sb, method, indent + "    ");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitMethod(StringBuilder sb, MapMethodModel method, string indent)
    {
        sb.AppendLine(
            $"{indent}{method.Accessibility} partial {method.ReturnTypeFullName} {method.MethodName}({method.ParameterTypeFullName} {method.ParameterName})");
        sb.AppendLine($"{indent}{{");

        if (method.ParameterIsReferenceType)
        {
            sb.AppendLine($"{indent}    if ({method.ParameterName} is null) throw new global::System.ArgumentNullException(nameof({method.ParameterName}));");
        }

        sb.AppendLine($"{indent}    return new {method.ReturnTypeFullName}");
        sb.AppendLine($"{indent}    {{");
        foreach (var member in method.Members)
        {
            sb.AppendLine($"{indent}        {member.TargetName} = {method.ParameterName}.{member.SourceName},");
        }
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine($"{indent}}}");
    }
}
```

- [ ] **Step 4: Build the whole solution (first full green build)**

Run: `dotnet build DwarfMapper.NET.sln -c Debug`
Expected: PASS, warning-clean.

- [ ] **Step 5: Run the full generator test suite**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS — `AttributeContractTests`, `DiagnosticTests`, `FlatMappingTests` all green.

- [ ] **Step 6: Commit**

```powershell
git add src/DwarfMapper.Generator/Pipeline/MapEmitter.cs tests/DwarfMapper.Generator.Tests/FlatMappingTests.cs
git commit -m "feat(gen): emitter (sorted object-initializer + null guard); flat mapping green"
```

---

## Task 13: Runtime integration test (real mapping executes)

**Files:**
- Create: `tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj`
- Create: `tests/DwarfMapper.IntegrationTests/FlatMappingRuntimeTests.cs`

- [ ] **Step 1: Create the integration project (consumes the generator as an analyzer)**

`tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DwarfMapper\DwarfMapper.csproj" />
    <ProjectReference Include="..\..\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the failing runtime test**

`tests/DwarfMapper.IntegrationTests/FlatMappingRuntimeTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class PersonMapper
{
    public partial PersonDto ToDto(Person p);
}

public class FlatMappingRuntimeTests
{
    [Fact]
    public void Maps_all_flat_members()
    {
        var mapper = new PersonMapper();
        var dto = mapper.ToDto(new Person { Age = 41, Name = "Durin" });
        Assert.Equal(41, dto.Age);
        Assert.Equal("Durin", dto.Name);
    }

    [Fact]
    public void Null_source_throws_ArgumentNullException()
    {
        var mapper = new PersonMapper();
        Assert.Throws<System.ArgumentNullException>(() => mapper.ToDto(null!));
    }
}
```

- [ ] **Step 3: Run to verify it passes (generator implements `ToDto` at compile time)**

Run: `dotnet test tests/DwarfMapper.IntegrationTests -c Debug`
Expected: PASS (2 tests). If the generated partial is missing, the project would fail to compile — proving the generator ran.

- [ ] **Step 4: Add to solution and commit**

```powershell
dotnet sln add tests\DwarfMapper.IntegrationTests\DwarfMapper.IntegrationTests.csproj
git add tests/DwarfMapper.IntegrationTests DwarfMapper.NET.sln
git commit -m "test: end-to-end runtime mapping integration test"
```

---

## Task 14: AOT/trim sample + gate verification

**Files:**
- Create: `samples/DwarfMapper.AotSample/DwarfMapper.AotSample.csproj`
- Create: `samples/DwarfMapper.AotSample/Program.cs`

- [ ] **Step 1: Create the AOT sample project**

`samples/DwarfMapper.AotSample/DwarfMapper.AotSample.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <IsPackable>false</IsPackable>
    <!-- Any reflection introduced by the library would surface as IL2xxx/IL3xxx here. -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DwarfMapper\DwarfMapper.csproj" />
    <ProjectReference Include="..\..\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `Program.cs`**

`samples/DwarfMapper.AotSample/Program.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

var mapper = new SampleMapper();
var dto = mapper.ToDto(new Source { Id = 7, Label = "vein" });
System.Console.WriteLine($"{dto.Id}:{dto.Label}");

public class Source { public int Id { get; set; } public string Label { get; set; } = ""; }
public class Target { public int Id { get; set; } public string Label { get; set; } = ""; }

[DwarfMapper]
public partial class SampleMapper
{
    public partial Target ToDto(Source s);
}
```

- [ ] **Step 3: Verify the AOT publish is warning-clean (the CRA/AOT gate)**

Run: `dotnet publish samples/DwarfMapper.AotSample/DwarfMapper.AotSample.csproj -c Release -warnaserror`
Expected: PASS with **no** IL2xxx/IL3xxx warnings. (On a machine without the native toolchain, run without `-r`; the trim/AOT analyzers still execute during publish.)

- [ ] **Step 4: Add to solution and commit**

```powershell
dotnet sln add samples\DwarfMapper.AotSample\DwarfMapper.AotSample.csproj
git add samples/DwarfMapper.AotSample DwarfMapper.NET.sln
git commit -m "samples: AOT/trim gate sample; verifies reflection-free codegen"
```

---

## Task 15: Snapshot regression test (Verify) for generated output

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/SnapshotTests.cs`
- Create (auto on first run): `tests/DwarfMapper.Generator.Tests/Snapshots/*.verified.txt`

- [ ] **Step 1: Add a Verify-based snapshot test**

`tests/DwarfMapper.Generator.Tests/SnapshotTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;

namespace DwarfMapper.Generator.Tests;

[UsesVerify]
public class SnapshotTests
{
    [Fact]
    public Task Flat_mapper_output_is_stable()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }
}
```

- [ ] **Step 2: Run to accept the initial snapshot**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter SnapshotTests`
Expected: First run FAILS and writes a `.received.txt`. Inspect it, then accept by renaming to `.verified.txt` (or run `dotnet verify accept` if the tool is installed). Re-run → PASS.

- [ ] **Step 3: Commit the verified snapshot**

```powershell
git add tests/DwarfMapper.Generator.Tests/SnapshotTests.cs tests/DwarfMapper.Generator.Tests/*.verified.txt
git commit -m "test(gen): snapshot regression for flat mapper output"
```

---

## Self-Review (completed by plan author)

**Spec coverage check against `2026-06-06-dwarfmapper-design.md`:**
- *sort → pair → prove → emit* → Tasks 10 (sort/pair/prove) + 12 (emit). ✅
- Completeness diagnostics as build errors → DWARF001 (error), Task 8/10/10-tests. ✅
- Zero reflection / AOT & trim-safe → Task 14 AOT gate + `IsAotCompatible`/`IsTrimmable`. ✅
- Over-posting protection → only resolved members emitted; unmapped = error (Task 10). ✅
- Provably-safe conversion (precursor to blit proof) → DWARF005 requires implicit, non-user-defined conversion (Task 10). ✅
- Declarative-only config → attributes only (Task 4). ✅
- Defensive/CRA: deterministic builds, SBOM, CodeQL, SECURITY.md, central package mgmt, warnings-as-errors, SPDX headers → Tasks 1–3. ✅
- Blittable bulk/SIMD fast-path, collections, converters, `DwarfMapper.Testing`, `[RoundTrip]` → **explicitly deferred to Plans 2–4** (stated in header). ✅
- Mapperly-style partial methods + class → Task 4/10/12. ✅

**Placeholder scan:** no TBD/TODO; every code step shows complete code. The only `REPLACE` token is the repository URL in `Directory.Build.props`, which the implementer fills with the real GitHub slug.

**Type consistency:** `MapperClassModel`, `MapMethodModel`, `MemberMap`, `DiagnosticInfo`, `LocationInfo`, `EquatableArray<T>`, `MapperExtractor.Extract`, `MapEmitter.Emit`, `DwarfGenerator.Execute` are referenced consistently across Tasks 5/7/9/10/12. `HintName`, `HasBlockingError`, `IsError`, `ToDiagnostic`, `ToLocation` defined before use.

**Ambiguity check:** member matching is fixed as **ordinal exact-name** with canonical name sort (case-insensitive matching and renames are Plan 2). Members are **public properties only** (fields are Plan 2). Construction uses an **object initializer requiring a public parameterless ctor** (DWARF006), which supports both `set` and `init`.

**Known follow-on order (each its own plan + spec slice):**
- **Plan 2:** case-insensitive matching + `[MapProperty]` renames, type converters (`[MapWith]`), enums, null strategies, nested object mapping, fields.
- **Plan 3:** collections/arrays/spans; analyzer-gated blittable bulk copy (`MemoryMarshal.Cast`/`Unsafe.CopyBlock`) + SIMD; `MapTo(src, ref dest)` zero-alloc overloads.
- **Plan 4:** `DwarfMapper.Testing` — fixture builders, seeded property-based fuzzer, `[RoundTrip]` harness generation, informed (mapping-aware) dumps.
