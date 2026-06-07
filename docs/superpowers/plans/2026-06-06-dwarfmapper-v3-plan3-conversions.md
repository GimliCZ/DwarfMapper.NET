# DwarfMapper.NET — Plan 3: User-Defined Conversions & Nested Mapping

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `DWARF005` ("no implicit conversion") *fixable*. When a source and destination member have different types, bridge them with **(a) an explicitly named conversion method** via `[MapProperty(Use = ...)]`, or **(b) an auto-discovered mapping method** declared in the same mapper (which is exactly how **nested object mapping** works — the nested type's mapper method is found by signature and called).

**Architecture:** This plan adds an optional converter to the resolved member model and a new emit branch. `MemberMap` gains `string? ConverterMethod`; `MapEmitter` emits `Target = Method(src.Source)` when set (instead of `Target = src.Source`). `MapperExtractor` gains a single unifying resolver, `TryResolveConversion`, used by both the explicit and auto-match loops: it picks **Use method → direct implicit assignment → auto-discovered mapping method → DWARF005**. Auto-discovery is restricted to the mapper's **declared partial mapping methods** (safe, no accidental grabbing of unrelated helpers); arbitrary helper methods are only used when named via `Use`. Generated converter calls are unqualified (`Method(x)`), valid for both instance and static methods inside the partial class.

**Tech Stack:** Same as Plans 1–2 (.NET 10, Roslyn 4.14.0, xUnit, Verify, CPM).

**Builds on Plan 2.** Current state: `MapperExtractor.ResolveMembers(sourceType, targetType, ignores, compilation, location, diagnostics, bool caseInsensitive, IReadOnlyList<(string Source, string Target)> explicitMaps)` resolves members (properties + fields), uses `HasImplicitConversion` for the conversion proof, and reports DWARF001/002/003/005/006/007/008/009/010/011/012. `MemberMap` is `(string TargetName, string SourceName)`. `MapEmitter` emits `Target = param.Source,`. 39 tests pass.

**Out of scope (later plans):** enums (enum↔enum/string) and null-handling strategies → **Plan 4**; collections/arrays/spans + SIMD/blit fast-path → **Plan 5**; `DwarfMapper.Testing` (fixtures/fuzzer/`[RoundTrip]`/informed dumps) → **Plan 6**. Also out of scope: **auto-synthesizing** a nested mapper for a type pair that has NO declared mapping method (Plan 3 requires you to declare the nested mapper method; the generator then wires it automatically).

**Conventions:** SPDX header `// SPDX-License-Identifier: GPL-2.0-only` on every new `.cs` file. CPM (no `Version` on `PackageReference`). Warnings-as-errors + all analyzers + `EnforceExtendedAnalyzerRules` — end every task warning-clean. New diagnostics MUST be added to `AnalyzerReleases.Unshipped.md` (RS2008).

---

## New diagnostics

| ID | Severity | Meaning |
|----|----------|---------|
| `DWARF013` | Error | Multiple conversion methods apply to a member's type pair (ambiguous); disambiguate with `[MapProperty(Use = ...)]` |
| `DWARF014` | Error | `[MapProperty(Use = ...)]` names a method that does not exist or has an incompatible signature |

`DWARF005`'s message is updated (its meaning is unchanged) to mention the new escape hatches.

## File structure (this plan)

```
src/DwarfMapper/MapPropertyAttribute.cs                          # modify: add Use property
src/DwarfMapper.Generator/Model/MemberMap.cs                     # modify: add ConverterMethod
src/DwarfMapper.Generator/Pipeline/MapEmitter.cs                 # modify: converter-call emit branch
src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs            # modify: TryResolveConversion, Use, auto-discovery
src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs   # modify: +DWARF013/014, DWARF005 msg
src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md          # modify: +DWARF013/014
tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs      # modify: Use contract
tests/DwarfMapper.Generator.Tests/ConverterTests.cs              # create
tests/DwarfMapper.Generator.Tests/NestedMappingTests.cs         # create
tests/DwarfMapper.IntegrationTests/ConversionRuntimeTests.cs     # create
README.md                                                        # modify: docs
```

---

## Task 1: Explicit converters via `[MapProperty(Use = ...)]`

Adds the converter to the model + emitter and the explicit-`Use` resolution path (no auto-discovery yet).

**Files:**
- Modify: `src/DwarfMapper/MapPropertyAttribute.cs`
- Modify: `src/DwarfMapper.Generator/Model/MemberMap.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs`
- Create: `tests/DwarfMapper.Generator.Tests/ConverterTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the existing `public class AttributeContractTests` in `tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs`:
```csharp
    [Fact]
    public void MapPropertyAttribute_Use_defaults_null_and_is_settable()
    {
        Assert.Null(new MapPropertyAttribute("a", "b").Use);
        Assert.Equal("Conv", new MapPropertyAttribute("a", "b") { Use = "Conv" }.Use);
    }
```

Create `tests/DwarfMapper.Generator.Tests/ConverterTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class ConverterTests
{
    [Fact]
    public void Use_method_bridges_incompatible_types()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string Amount { get; set; } = ""; }
            public class Target { public int Amount { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Amount", "Amount", Use = nameof(ParseInt))]
                public partial Target Map(Source s);
                private static int ParseInt(string v) => int.Parse(v);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Amount = ParseInt(s.Amount)", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Unknown_Use_method_reports_DWARF014()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string Amount { get; set; } = ""; }
            public class Target { public int Amount { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Amount", "Amount", Use = "Nope")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF014" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Use_method_with_wrong_signature_reports_DWARF014()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string Amount { get; set; } = ""; }
            public class Target { public int Amount { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Amount", "Amount", Use = nameof(Bad))]
                public partial Target Map(Source s);
                private static string Bad(int v) => v.ToString();  // wrong types
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF014");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~ConverterTests|FullyQualifiedName~AttributeContractTests"`
Expected: FAIL — `Use` property and DWARF014 don't exist; converter not emitted.

- [ ] **Step 3: Add `Use` to `MapPropertyAttribute`**

In `src/DwarfMapper/MapPropertyAttribute.cs`, add inside the class (after the `Target` property), keeping SPDX + XML docs (the runtime project requires XML docs):
```csharp
    /// <summary>
    /// Optional name of a conversion method declared in the mapper class used to
    /// transform the source value into the destination type. The method must take
    /// the source member type and return the destination member type.
    /// </summary>
    public string? Use { get; set; }
```

- [ ] **Step 4: Add `ConverterMethod` to `MemberMap`**

Replace `src/DwarfMapper.Generator/Model/MemberMap.cs` body with:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// One resolved destination&lt;-source member assignment. When
/// <see cref="ConverterMethod"/> is non-null, the source value is passed through
/// that method (e.g. <c>Target = Convert(src.Source)</c>); otherwise it is
/// assigned directly.
/// </summary>
public sealed record MemberMap(string TargetName, string SourceName, string? ConverterMethod = null)
    : System.IEquatable<MemberMap>;
```

- [ ] **Step 5: Update the emitter to call the converter when present**

In `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`, REPLACE the `foreach (var member in method.Members)` loop body (lines ~51-55) with:
```csharp
        foreach (var member in method.Members)
        {
            sb.Append(indent).Append("        ").Append(member.TargetName).Append(" = ");
            if (member.ConverterMethod is null)
            {
                sb.Append(method.ParameterName).Append('.').Append(member.SourceName);
            }
            else
            {
                sb.Append(member.ConverterMethod).Append('(')
                  .Append(method.ParameterName).Append('.').Append(member.SourceName).Append(')');
            }
            sb.AppendLine(",");
        }
```

- [ ] **Step 6: Add the DWARF014 descriptor + update DWARF005 message**

In `DiagnosticDescriptors.cs`, change the `NoImplicitConversion` (DWARF005) message string to:
```csharp
        "Cannot map to '{0}': no implicit conversion and no usable conversion method; declare a mapping method for the types or use [MapProperty(Use = ...)]",
```
and add:
```csharp
    public static readonly DiagnosticDescriptor UseMethodInvalid = new(
        "DWARF014",
        "Conversion method not found",
        "[MapProperty(Use = ...)] method '{0}' was not found or has an incompatible signature (it must take the source member type and return the destination member type)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
```

- [ ] **Step 7: Register DWARF014 in release tracking**

Add to `AnalyzerReleases.Unshipped.md` New Rules table (after DWARF012):
```
DWARF014 | DwarfMapper | Error | Conversion method not found
```

- [ ] **Step 8: Implement Use resolution in `MapperExtractor`**

8a. Extend `ReadExplicitMaps` to also read the `Use` named argument. Replace it with:
```csharp
    private static List<(string Source, string Target, string? Use)> ReadExplicitMaps(ISymbol method)
    {
        var maps = new List<(string Source, string Target, string? Use)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "DwarfMapper.MapPropertyAttribute")
            {
                continue;
            }
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string s
                && attr.ConstructorArguments[1].Value is string t)
            {
                string? use = null;
                foreach (var na in attr.NamedArguments)
                {
                    if (na.Key == "Use" && na.Value.Value is string u)
                    {
                        use = u;
                    }
                }
                maps.Add((s, t, use));
            }
        }
        return maps;
    }
```

8b. Collect the class's candidate methods. In `Extract`, after `var caseInsensitive = ReadCaseInsensitive(ctx.Attributes);`, add:
```csharp
        var allMethods = CollectMethods(classSymbol);
```
and add this helper (near the other helpers):
```csharp
    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 1)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }
```

8c. Pass `allMethods` into `ResolveMembers` (update the call):
```csharp
            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods);
```

8d. Add the unifying resolver helper (place near `HasImplicitConversion`). NOTE: Task 2 will extend this with auto-discovery; for Task 1 it handles Use → implicit → DWARF005:
```csharp
    private static bool TryResolveConversion(
        Compilation compilation, ITypeSymbol srcType, ITypeSymbol tgtType, string? useMethod,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics,
        out string? converterMethod)
    {
        converterMethod = null;

        if (useMethod is not null)
        {
            foreach (var m in allMethods)
            {
                if (string.Equals(m.Name, useMethod, System.StringComparison.Ordinal)
                    && HasImplicitConversion(compilation, srcType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                {
                    converterMethod = m.Name;
                    return true;
                }
            }
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
            return false;
        }

        if (HasImplicitConversion(compilation, srcType, tgtType))
        {
            return true; // direct assignment
        }

        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
        return false;
    }
```

8e. Update `ResolveMembers` signature and both loops to use `TryResolveConversion`. Replace `ResolveMembers` with:
```csharp
    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        var sourceGroups = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var writableByName = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType))
        {
            writableByName[m.Name] = m.Type;
        }

        var result = new List<MemberMap>();
        var handledTargets = new HashSet<string>(System.StringComparer.Ordinal);

        // EXPLICIT: [MapProperty] pairs take precedence and are matched by exact name.
        var explicitSeen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var (srcName, tgtName, useMethod) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            handledTargets.Add(tgtName);

            if (ignores.Contains(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }

            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }

            var srcMatch = ReadableMembers(sourceType)
                .Where(m => System.StringComparer.Ordinal.Equals(m.Name, srcName))
                .Select(m => (ITypeSymbol?)m.Type)
                .FirstOrDefault();
            if (srcMatch is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, location, tgtName, diagnostics, out var conv))
            {
                result.Add(new MemberMap(tgtName, srcName, conv));
            }
        }

        // AUTO: remaining writable targets matched by name under the comparer.
        var targets = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();
        foreach (var target in targets)
        {
            if (handledTargets.Contains(target.Name) || ignores.Contains(target.Name))
            {
                continue;
            }

            if (!sourceGroups.TryGetValue(target.Name, out var matches))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, target.Name));
                continue;
            }

            var source = matches[0];
            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, location, target.Name, diagnostics, out var conv))
            {
                result.Add(new MemberMap(target.Name, source.Name, conv));
            }
        }

        // READ-ONLY destinations with a matching source (silent-loss guard).
        foreach (var readOnly in ReadOnlyMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (handledTargets.Contains(readOnly.Name) || ignores.Contains(readOnly.Name))
            {
                continue;
            }
            if (sourceGroups.ContainsKey(readOnly.Name))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReadOnlyDestinationMember, location, readOnly.Name));
            }
        }

        return result;
    }
```

- [ ] **Step 9: Run tests + full build**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS — ConverterTests + AttributeContractTests green; all prior tests green (the snapshot test is unaffected — its fixture has no converter).
Run: `dotnet build DwarfMapper.NET.sln -c Debug` => 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```
git add src/DwarfMapper src/DwarfMapper.Generator tests/DwarfMapper.Generator.Tests/ConverterTests.cs tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs
git commit -m "feat(gen): explicit converters via [MapProperty(Use=...)] (DWARF014) + converter emit"
```

---

## Task 2: Auto-discovered nested mapping via declared mapper methods

Extends `TryResolveConversion` so that, when no `Use` and no implicit conversion exist, it finds a **unique declared partial mapping method** converting the member's source type to its destination type and calls it. This is nested object mapping.

**Files:**
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Create: `tests/DwarfMapper.Generator.Tests/NestedMappingTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/DwarfMapper.Generator.Tests/NestedMappingTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class NestedMappingTests
{
    [Fact]
    public void Nested_member_uses_declared_mapper_method()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);
                public partial AddrDto ToDto(Addr a);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Home = ToDto(p.Home)", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Missing_nested_mapper_reports_DWARF005()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);   // no Addr->AddrDto method declared
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void Ambiguous_nested_mappers_report_DWARF013()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);
                public partial AddrDto ToDto(Addr a);
                public partial AddrDto ToDtoAlt(Addr a);   // second Addr->AddrDto candidate
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF013" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Home", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~NestedMappingTests"`
Expected: FAIL — nested mapper not auto-discovered (`Nested_member_uses_declared_mapper_method` reports DWARF005 instead of emitting `ToDto(p.Home)`); DWARF013 doesn't exist.

- [ ] **Step 3: Add DWARF013 descriptor**

In `DiagnosticDescriptors.cs`:
```csharp
    public static readonly DiagnosticDescriptor AmbiguousConversion = new(
        "DWARF013",
        "Ambiguous conversion method",
        "Cannot map to '{0}': more than one mapping method converts these types; disambiguate with [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
```

- [ ] **Step 4: Register DWARF013 in release tracking**

Add to `AnalyzerReleases.Unshipped.md` (after DWARF012, keeping ascending order — i.e. 013 then the existing 014):
```
DWARF013 | DwarfMapper | Error | Ambiguous conversion method
```

- [ ] **Step 5: Collect the declared mapping methods as auto-discovery candidates**

In `Extract`, the partial mapping methods are the auto-discovery candidates. Build that list once. After `var allMethods = CollectMethods(classSymbol);` add:
```csharp
        var mapperMethods = CollectMapperMethods(classSymbol);
```
Add this helper:
```csharp
    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMapperMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition
                && !m.ReturnsVoid && m.Parameters.Length == 1 && m.ReturnType is INamedTypeSymbol)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }
```
Pass `mapperMethods` to `ResolveMembers` (update the call):
```csharp
            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods);
```

- [ ] **Step 6: Extend `TryResolveConversion` with auto-discovery and thread the candidates through**

Replace `TryResolveConversion` with this version (adds `autoCandidates` parameter and the auto-discovery + DWARF013 branch):
```csharp
    private static bool TryResolveConversion(
        Compilation compilation, ITypeSymbol srcType, ITypeSymbol tgtType, string? useMethod,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics,
        out string? converterMethod)
    {
        converterMethod = null;

        if (useMethod is not null)
        {
            foreach (var m in allMethods)
            {
                if (string.Equals(m.Name, useMethod, System.StringComparison.Ordinal)
                    && HasImplicitConversion(compilation, srcType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                {
                    converterMethod = m.Name;
                    return true;
                }
            }
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
            return false;
        }

        if (HasImplicitConversion(compilation, srcType, tgtType))
        {
            return true; // direct assignment
        }

        // AUTO-DISCOVER: a unique declared mapping method that converts srcType -> tgtType.
        string? found = null;
        foreach (var c in autoCandidates)
        {
            if (HasImplicitConversion(compilation, srcType, c.ParamType)
                && HasImplicitConversion(compilation, c.ReturnType, tgtType))
            {
                if (found is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion, location, targetName));
                    return false;
                }
                found = c.Name;
            }
        }

        if (found is not null)
        {
            converterMethod = found;
            return true;
        }

        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
        return false;
    }
```
Update `ResolveMembers` to accept `IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates` as a new final parameter, and pass it through in BOTH `TryResolveConversion(...)` calls (explicit loop and auto loop). The two calls become:
```csharp
            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, autoCandidates, location, tgtName, diagnostics, out var conv))
```
and
```csharp
            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, autoCandidates, location, target.Name, diagnostics, out var conv))
```
The `ResolveMembers` signature becomes:
```csharp
    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates)
```

- [ ] **Step 7: Run tests + full build**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS — NestedMappingTests green; all prior green. (Note: a self-referential or mutually-referential set of declared mappers is allowed; auto-discovery matches by signature regardless of method name.)
Run: `dotnet build DwarfMapper.NET.sln -c Debug` => 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```
git add src/DwarfMapper.Generator tests/DwarfMapper.Generator.Tests/NestedMappingTests.cs
git commit -m "feat(gen): auto-discover nested mapping via declared mapper methods + DWARF013"
```

---

## Task 3: Consolidate analyzer release tracking

**Files:**
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`

- [ ] **Step 1: Confirm the New Rules table lists DWARF001–014 (004 reserved) in ascending order, no duplicates**

The table must contain exactly these rows in order: DWARF001, 002, 003, 005, 006, 007, 008, 009, 010, 011, 012, 013, 014.

- [ ] **Step 2: Build warning-clean**

Run: `dotnet build DwarfMapper.NET.sln -c Debug`
Expected: 0 warnings, 0 errors (no RS2000-series complaints).

- [ ] **Step 3: Commit (if changed)**

```
git add src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md
git commit -m "chore(gen): consolidate analyzer release tracking (DWARF013/014)"
```

---

## Task 4: Runtime integration tests

**Files:**
- Create: `tests/DwarfMapper.IntegrationTests/ConversionRuntimeTests.cs`

- [ ] **Step 1: Write runtime tests that EXECUTE converters and nested mapping**

`tests/DwarfMapper.IntegrationTests/ConversionRuntimeTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class MoneySource { public string Amount { get; set; } = ""; }
public class MoneyTarget { public int Amount { get; set; } }

[DwarfMapper]
public partial class MoneyMapper
{
    [MapProperty("Amount", "Amount", Use = nameof(Parse))]
    public partial MoneyTarget Map(MoneySource s);
    private static int Parse(string v) => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
}

public class Addr { public string City { get; set; } = ""; }
public class AddrDto { public string City { get; set; } = ""; }
public class Person2 { public Addr Home { get; set; } = new(); }
public class Person2Dto { public AddrDto Home { get; set; } = new(); }

[DwarfMapper]
public partial class NestedMapper
{
    public partial Person2Dto ToDto(Person2 p);
    public partial AddrDto ToDto(Addr a);
}

public class ConversionRuntimeTests
{
    [Fact]
    public void Explicit_converter_runs()
    {
        var t = new MoneyMapper().Map(new MoneySource { Amount = "42" });
        Assert.Equal(42, t.Amount);
    }

    [Fact]
    public void Nested_mapping_runs()
    {
        var t = new NestedMapper().ToDto(new Person2 { Home = new Addr { City = "Moria" } });
        Assert.Equal("Moria", t.Home.City);
    }
}
```

- [ ] **Step 2: Build + run**

Run: `dotnet test tests/DwarfMapper.IntegrationTests -c Debug`
Expected: PASS (existing 5 + new 2 = 7). A missing generated body (CS8795) means the generator didn't emit — investigate; do not hand-write bodies.

- [ ] **Step 3: Full-solution sanity**

Run: `$env:DiffEngine_Disabled="true"; dotnet test DwarfMapper.NET.sln -c Debug`
Expected: ALL pass. Report the total count.

- [ ] **Step 4: Commit**

```
git add tests/DwarfMapper.IntegrationTests/ConversionRuntimeTests.cs
git commit -m "test: runtime integration for explicit converters and nested mapping"
```

---

## Task 5: Documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Extend the "Configuring mapping" section**

Add to the `### Configuring mapping` section (created in Plan 2) a converters/nested block:
```markdown
**Converting between types.** When a source and destination member have different types, DwarfMapper bridges them in one of two ways:

```csharp
[DwarfMapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order o);
    public partial AddressDto ToDto(Address a);   // nested: auto-wired by type signature

    // custom scalar conversion: name a method with Use
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(FormatMoney))]
    public partial OrderDto ToDto2(Order o);

    private static string FormatMoney(decimal d) => d.ToString("C");
}
```

- **Nested objects** map automatically when you declare a mapping method for the nested type pair; it is found by signature and called (`Home = ToDto(o.Home)`).
- **Custom conversions** use `[MapProperty(src, tgt, Use = nameof(Method))]`, where `Method` takes the source member type and returns the destination type.
- An ambiguous auto-conversion is `DWARF013`; an invalid `Use` method is `DWARF014`. If no conversion is possible, you still get the completeness error `DWARF005`.
```

- [ ] **Step 2: Update roadmap mention** if present (mark converters + nested as shipped; leave enums/null-strategies/collections/testing as upcoming).

- [ ] **Step 3: Commit**

```
git add README.md
git commit -m "docs: document custom converters (Use) and nested mapping"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Explicit converter via `[MapProperty(Use=...)]` → Task 1 (attr `Use`, `MemberMap.ConverterMethod`, emitter branch, `TryResolveConversion` Use path, DWARF014). ✅
- Auto-discovered nested mapping via declared mapper methods → Task 2 (candidate collection, auto branch, DWARF013). ✅
- DWARF005 still fires when nothing applies (message updated) → Tasks 1/2. ✅
- Release tracking for DWARF013/014 → Tasks 1/2 inline + Task 3 consolidation. ✅
- Runtime proof + docs → Tasks 4/5. ✅

**Placeholder scan:** none; complete code in every step.

**Type consistency:** `TryResolveConversion` evolves Task 1 → Task 2 (adds `autoCandidates`); both `ResolveMembers` call sites updated together. Candidate tuple shape `(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)` is identical for `allMethods` (Use lookup, all single-param non-void methods via `CollectMethods`) and `autoCandidates`/`mapperMethods` (declared partial mappers via `CollectMapperMethods`). `MemberMap`'s third positional param defaults to `null`, so existing two-arg construction stays valid. Generated converter calls are unqualified — valid for instance and static methods inside the partial class, and inside the object initializer (which executes in the mapper instance method body).

**Ambiguity / decisions:** auto-discovery is limited to **declared partial mapping methods** (no accidental use of unrelated helpers); arbitrary methods are reached only via `Use`. Resolution order is **Use → implicit → auto-discovered → DWARF005**, so a directly-assignable pair never invokes a converter. `Use` and auto-discovery accept implicit conversions at the parameter/return boundaries (identity included), which is the common exact-match case. Incrementality is preserved: `MemberMap.ConverterMethod` is a `string?`; the new candidate collections are consumed inside `Extract` and never stored in `MapperClassModel`.

**Deferred:** enums + null strategies → Plan 4; collections + SIMD/blit → Plan 5; `DwarfMapper.Testing` → Plan 6; auto-synthesis of undeclared nested mappers → later.
