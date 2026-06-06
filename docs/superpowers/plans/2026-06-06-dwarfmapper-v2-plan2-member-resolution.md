# DwarfMapper.NET — Plan 2: Flexible Member Resolution

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make member pairing real-world usable: map **public fields** (not just properties), support **opt-in case-insensitive** name matching, and allow **explicit `[MapProperty(source, target)]` renames** — all while keeping completeness-as-a-build-error intact.

**Architecture:** Pure extraction-layer change. All new behavior lives in `MapperExtractor`, two new attributes, and three new diagnostics. **The emitter (`MapEmitter`) and the pipeline models (`MemberMap`/`MapMethodModel`/`MapperClassModel`) do NOT change** — fields and properties are both accessed as `instance.Name`, and the per-mapper/per-method configuration is consumed inside `Extract` before the model is built. This keeps the value-equatable incremental model and the codegen identical, so Plan 2 is low-risk.

**Tech Stack:** Same as Plan 1 — .NET 10, Roslyn (`Microsoft.CodeAnalysis.CSharp` 4.14.0), xUnit, Verify, Central Package Management.

**Builds on Plan 1.** Current state: `MapperExtractor` resolves settable **properties** by exact ordinal name (`ResolveMembers`, `MapperExtractor.cs:94-146`), with diagnostics DWARF001/002/003/005/006/007. Attributes: `DwarfMapperAttribute` (class marker), `MapIgnoreAttribute`. 18 tests pass.

**Out of scope (Plan 3 and later):** custom converters / `[MapWith]`, user-defined conversion methods, nested-object auto-mapping, enums, null-handling strategies (Plan 3); collections + SIMD/blit (Plan 4); `DwarfMapper.Testing` (Plan 5). Do not implement these here.

**Conventions:** every new `.cs` file starts with `// SPDX-License-Identifier: GPL-2.0-only`. Central Package Management — `PackageReference` with no `Version`. Build is warnings-as-errors + all analyzers + `EnforceExtendedAnalyzerRules`; every task must end warning-clean. New diagnostics MUST be added to `AnalyzerReleases.Unshipped.md` (Task 5) or the build fails (RS2008).

---

## New diagnostics introduced in this plan

| ID | Severity | Meaning |
|----|----------|---------|
| `DWARF008` | Error | `[MapProperty]` references a destination member that does not exist (or is not writable) |
| `DWARF009` | Error | `[MapProperty]` references a source member that does not exist (or is not readable) |
| `DWARF010` | Error | Case-insensitive matching produced an ambiguous source member for a destination |

(`DWARF004` remains intentionally reserved.)

## File structure (this plan)

```
src/DwarfMapper/MapPropertyAttribute.cs                      # create
src/DwarfMapper/DwarfMapperAttribute.cs                      # modify: add CaseInsensitive
src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs   # modify: +DWARF008/009/010
src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md          # modify: +DWARF008/009/010
src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs            # modify: fields, case-insensitivity, [MapProperty]
tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs     # modify: new attribute contracts
tests/DwarfMapper.Generator.Tests/FieldMappingTests.cs          # create
tests/DwarfMapper.Generator.Tests/CaseInsensitiveMappingTests.cs # create
tests/DwarfMapper.Generator.Tests/MapPropertyTests.cs           # create
tests/DwarfMapper.IntegrationTests/MemberResolutionRuntimeTests.cs # create
README.md                                                        # modify: examples + roadmap
```

---

## Task 1: New attributes — `[MapProperty]` and `[DwarfMapper(CaseInsensitive=…)]`

**Files:**
- Create: `src/DwarfMapper/MapPropertyAttribute.cs`
- Modify: `src/DwarfMapper/DwarfMapperAttribute.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs`

- [ ] **Step 1: Write the failing contract tests**

Append these `[Fact]` methods inside the existing `public class AttributeContractTests` in `tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs`:
```csharp
    [Fact]
    public void MapPropertyAttribute_exposes_source_and_target()
    {
        var attr = new MapPropertyAttribute("Src", "Dst");
        Assert.Equal("Src", attr.Source);
        Assert.Equal("Dst", attr.Target);
    }

    [Fact]
    public void MapPropertyAttribute_allows_multiple_on_methods()
    {
        var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(MapPropertyAttribute), typeof(System.AttributeUsageAttribute))!;
        Assert.True(usage.AllowMultiple);
        Assert.Equal(System.AttributeTargets.Method, usage.ValidOn);
    }

    [Fact]
    public void DwarfMapperAttribute_caseInsensitive_defaults_false()
    {
        Assert.False(new DwarfMapperAttribute().CaseInsensitive);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~AttributeContractTests"`
Expected: FAIL — `MapPropertyAttribute` and `CaseInsensitive` do not exist (compile error).

- [ ] **Step 3: Create `MapPropertyAttribute`**

`src/DwarfMapper/MapPropertyAttribute.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Explicitly maps a source member to a differently-named destination member,
/// overriding name-based matching for that destination. Apply to a mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <param name="source">Name of the source member to read from.</param>
    /// <param name="target">Name of the destination member to assign.</param>
    public MapPropertyAttribute(string source, string target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>Name of the source member to read from.</summary>
    public string Source { get; }

    /// <summary>Name of the destination member to assign.</summary>
    public string Target { get; }
}
```

- [ ] **Step 4: Add `CaseInsensitive` to `DwarfMapperAttribute`**

Modify `src/DwarfMapper/DwarfMapperAttribute.cs` so the class body contains the new property (keep the existing SPDX header, usings, and `[AttributeUsage]`):
```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, source and destination member names are matched
    /// case-insensitively (ordinal-ignore-case). Defaults to <c>false</c>
    /// (exact, case-sensitive matching).
    /// </summary>
    public bool CaseInsensitive { get; set; }
}
```
(Use `get; set;` — attribute named arguments require a settable property.)

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~AttributeContractTests"`
Expected: PASS (existing 2 + new 3 = 5 attribute tests).

- [ ] **Step 6: Commit**

```
git add src/DwarfMapper tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs
git commit -m "feat(runtime): MapProperty attribute + DwarfMapper.CaseInsensitive option"
```

---

## Task 2: Map public fields (not just properties)

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Create: `tests/DwarfMapper.Generator.Tests/FieldMappingTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/DwarfMapper.Generator.Tests/FieldMappingTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;

namespace DwarfMapper.Generator.Tests;

public class FieldMappingTests
{
    [Fact]
    public void Public_field_is_mapped()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int Count; }
            public class Target { public int Count; }
            [DwarfMapper]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Count = s.Count", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Field_maps_to_property_of_same_name()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int Count; }
            public class Target { public int Count { get; set; } }
            [DwarfMapper]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Readonly_target_field_with_source_reports_DWARF007()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int Count { get; set; } }
            public class Target { public readonly int Count; }
            [DwarfMapper]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF007");
    }

    [Fact]
    public void Const_field_is_ignored()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int Count { get; set; } }
            public class Target { public const int Tag = 5; public int Count { get; set; } }
            [DwarfMapper]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        // 'Tag' is const (static) -> not a writable instance member -> no DWARF001, not assigned.
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.DoesNotContain("Tag =", generated, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~FieldMappingTests"`
Expected: FAIL — fields are not yet considered (e.g., `Public_field_is_mapped` emits nothing / reports DWARF001).

- [ ] **Step 3: Replace the member-enumeration helpers in `MapperExtractor.cs`**

In `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`, REPLACE the four helper methods `ReadableProperties`, `SettableProperties`, `ReadOnlyProperties`, and `EnumerateProperties` (lines ~154-181) with these member-aware versions that include fields. Each returns `(string Name, ITypeSymbol Type)` tuples:
```csharp
    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.SetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && !f.IsReadOnly && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadOnlyMembers(ITypeSymbol type)
    {
        var writable = new HashSet<string>(WritableMembers(type).Select(m => m.Name), System.StringComparer.Ordinal);
        return ReadableMembers(type).Where(m => !writable.Contains(m.Name));
    }
```
NOTE: `IsImplicitlyDeclared` excludes property backing fields (which are private anyway). `IsStatic` excludes `const` fields (consts are static). Init-only property setters have a public `SetMethod`, so they remain writable (correctly mappable) and are NOT classified read-only.

- [ ] **Step 4: Update `ResolveMembers` to use the new helpers**

REPLACE the body of `ResolveMembers` (lines ~94-146) with this version (same signature, now member-based):
```csharp
    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        var sources = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, System.StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), System.StringComparer.Ordinal);

        // SORT: canonical, declaration-order-independent ordering by member name.
        var targets = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
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

            // PROVE (safety): an implicit, non-user-defined conversion must exist.
            if (!HasImplicitConversion(compilation, source.Type, target.Type))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, target.Name));
                continue;
            }

            result.Add(new MemberMap(target.Name, source.Name));
        }

        foreach (var readOnly in ReadOnlyMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (ignores.Contains(readOnly.Name))
            {
                continue;
            }
            if (sources.ContainsKey(readOnly.Name))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReadOnlyDestinationMember, location, readOnly.Name));
            }
        }

        return result;
    }
```
NOTE: `using Microsoft.CodeAnalysis;` is already imported (`IFieldSymbol`/`IPropertySymbol` resolve). Remove any now-unused `IPropertySymbol`-only helpers you replaced. Confirm there are no leftover references to `ReadableProperties`/`SettableProperties`/`ReadOnlyProperties`.

- [ ] **Step 5: Run to verify pass + full project**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS — new FieldMappingTests green, all previously-passing tests still green.

- [ ] **Step 6: Commit**

```
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs tests/DwarfMapper.Generator.Tests/FieldMappingTests.cs
git commit -m "feat(gen): map public fields as members (read/write/readonly-aware)"
```

---

## Task 3: Case-insensitive matching (opt-in)

**Files:**
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Create: `tests/DwarfMapper.Generator.Tests/CaseInsensitiveMappingTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/DwarfMapper.Generator.Tests/CaseInsensitiveMappingTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class CaseInsensitiveMappingTests
{
    [Fact]
    public void CaseInsensitive_matches_differently_cased_names()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int count { get; set; } }
            public class Target { public int Count { get; set; } }
            [DwarfMapper(CaseInsensitive = true)]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Count = s.count", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Default_is_case_sensitive_and_reports_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int count { get; set; } }
            public class Target { public int Count { get; set; } }
            [DwarfMapper]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF001"); // 'Count' unmatched (case-sensitive)
    }

    [Fact]
    public void CaseInsensitive_ambiguous_source_reports_DWARF010()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public int Count { get; set; } public int count { get; set; } }
            public class Target { public int Count { get; set; } }
            [DwarfMapper(CaseInsensitive = true)]
            public partial class M { public partial Target Map(Source s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF010" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Count", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~CaseInsensitiveMappingTests"`
Expected: FAIL — `CaseInsensitive_matches_differently_cased_names` fails (still case-sensitive), and DWARF010 does not exist yet.

- [ ] **Step 3: Add the `DWARF010` descriptor**

In `DiagnosticDescriptors.cs`, add:
```csharp
    public static readonly DiagnosticDescriptor AmbiguousMatch = new(
        "DWARF010",
        "Ambiguous source member",
        "Destination member '{0}' matches more than one source member under case-insensitive matching; rename or use [MapProperty]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
```

- [ ] **Step 4: Read `CaseInsensitive` in `Extract` and thread it into `ResolveMembers`**

In `MapperExtractor.Extract`, after computing `classIgnores` (around line 31), read the attribute option:
```csharp
        var caseInsensitive = ReadCaseInsensitive(ctx.Attributes);
```
Add this helper (place near `ReadIgnores`):
```csharp
    private static bool ReadCaseInsensitive(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "CaseInsensitive" && named.Value.Value is bool b)
                {
                    return b;
                }
            }
        }
        return false;
    }
```
NOTE: `ctx.Attributes` is the `ImmutableArray<AttributeData>` of the `[DwarfMapper]` attributes on the class (provided by `GeneratorAttributeSyntaxContext`). The `CaseInsensitive` value is a named argument.

Change the `ResolveMembers` call (around line 72) to pass the flag:
```csharp
            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive);
```

- [ ] **Step 5: Update `ResolveMembers` to honor the comparer + ambiguity**

REPLACE `ResolveMembers` with this version (adds the `bool caseInsensitive` parameter, a comparer, and the DWARF010 ambiguity check):
```csharp
    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        var sourceGroups = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        // SORT: canonical, declaration-order-independent ordering by member name.
        var targets = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();

        var result = new List<MemberMap>();
        foreach (var target in targets)
        {
            if (ignores.Contains(target.Name))
            {
                continue;
            }

            // PAIR: resolved name under the configured comparer.
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
            if (!HasImplicitConversion(compilation, source.Type, target.Type))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, target.Name));
                continue;
            }

            result.Add(new MemberMap(target.Name, source.Name));
        }

        foreach (var readOnly in ReadOnlyMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (ignores.Contains(readOnly.Name))
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

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS — CaseInsensitiveMappingTests green, all prior tests green. (Build will warn RS2008 only if you forgot the descriptor; the release-tracking file is updated in Task 5 — if the build fails on RS2008 now, jump to Task 5 Step 1 to add DWARF010, then return. To keep each task green, you MAY add the DWARF010 row to `AnalyzerReleases.Unshipped.md` now.)

> To keep this task's build green, also add the `DWARF010` row to `AnalyzerReleases.Unshipped.md` now (full table is consolidated in Task 5):
> `DWARF010 | DwarfMapper | Error | Ambiguous source member`

- [ ] **Step 7: Commit**

```
git add src/DwarfMapper.Generator tests/DwarfMapper.Generator.Tests/CaseInsensitiveMappingTests.cs
git commit -m "feat(gen): opt-in case-insensitive matching (DwarfMapper.CaseInsensitive) + DWARF010 ambiguity"
```

---

## Task 4: `[MapProperty(source, target)]` explicit renames

**Files:**
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Create: `tests/DwarfMapper.Generator.Tests/MapPropertyTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/DwarfMapper.Generator.Tests/MapPropertyTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class MapPropertyTests
{
    [Fact]
    public void Rename_maps_differently_named_members()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Name = s.FullName", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Rename_suppresses_DWARF001_for_the_target()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
    }

    [Fact]
    public void Unknown_target_reports_DWARF008()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Nope")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF008" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_source_reports_DWARF009()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Source { public string FullName { get; set; } = ""; }
            public class Target { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Ghost", "Name")]
                public partial Target Map(Source s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF009" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Ghost", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~MapPropertyTests"`
Expected: FAIL — `[MapProperty]` is not yet honored; `Name` is reported as DWARF001 (unmapped) and the explicit pair is not emitted; DWARF008/009 do not exist.

- [ ] **Step 3: Add `DWARF008`/`DWARF009` descriptors**

In `DiagnosticDescriptors.cs`, add:
```csharp
    public static readonly DiagnosticDescriptor MapPropertyUnknownTarget = new(
        "DWARF008",
        "MapProperty target not found",
        "[MapProperty] destination member '{0}' does not exist or is not writable",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MapPropertyUnknownSource = new(
        "DWARF009",
        "MapProperty source not found",
        "[MapProperty] source member '{0}' does not exist or is not readable",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
```

- [ ] **Step 4: Read `[MapProperty]` pairs in `Extract` and pass them in**

Add this helper near `ReadIgnores`:
```csharp
    private static List<(string Source, string Target)> ReadExplicitMaps(ISymbol method)
    {
        var maps = new List<(string Source, string Target)>();
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
                maps.Add((s, t));
            }
        }
        return maps;
    }
```
In the method loop in `Extract`, before the `ResolveMembers` call, read them and pass them in:
```csharp
            var explicitMaps = ReadExplicitMaps(method);

            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps);
```

- [ ] **Step 5: Update `ResolveMembers` to apply explicit pairs first**

REPLACE `ResolveMembers` with this final version (adds `IReadOnlyList<(string Source, string Target)> explicitMaps`, processes them before auto-matching, and excludes explicitly-handled targets from completeness):
```csharp
    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target)> explicitMaps)
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
        foreach (var (srcName, tgtName) in explicitMaps)
        {
            handledTargets.Add(tgtName);

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

            if (!HasImplicitConversion(compilation, srcMatch, tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, tgtName));
                continue;
            }

            result.Add(new MemberMap(tgtName, srcName));
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
            if (!HasImplicitConversion(compilation, source.Type, target.Type))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, target.Name));
                continue;
            }

            result.Add(new MemberMap(target.Name, source.Name));
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

- [ ] **Step 6: Run to verify pass + add release-tracking rows to keep build green**

Add to `AnalyzerReleases.Unshipped.md` (if not already present from Task 5/earlier) the rows for DWARF008 and DWARF009:
```
DWARF008 | DwarfMapper | Error | MapProperty target not found
DWARF009 | DwarfMapper | Error | MapProperty source not found
```
Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Debug`
Expected: PASS — MapPropertyTests green, all prior tests green, warning-clean.

- [ ] **Step 7: Commit**

```
git add src/DwarfMapper.Generator tests/DwarfMapper.Generator.Tests/MapPropertyTests.cs
git commit -m "feat(gen): [MapProperty] explicit renames + DWARF008/009"
```

---

## Task 5: Consolidate analyzer release tracking

**Files:**
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`

- [ ] **Step 1: Ensure the New Rules table lists all current rules in ID order**

Confirm `AnalyzerReleases.Unshipped.md`'s New Rules table contains exactly these rows (in this order); add any missing, deduplicate any you added ad-hoc in Tasks 3/4:
```
DWARF001 | DwarfMapper | Error | Destination member is not mapped
DWARF002 | DwarfMapper | Error | Mapper type must be partial
DWARF003 | DwarfMapper | Error | Invalid mapping method signature
DWARF005 | DwarfMapper | Error | No implicit conversion between mapped members
DWARF006 | DwarfMapper | Error | Destination type is not constructible
DWARF007 | DwarfMapper | Error | Destination member is read-only
DWARF008 | DwarfMapper | Error | MapProperty target not found
DWARF009 | DwarfMapper | Error | MapProperty source not found
DWARF010 | DwarfMapper | Error | Ambiguous source member
```

- [ ] **Step 2: Build warning-clean**

Run: `dotnet build DwarfMapper.NET.sln -c Debug`
Expected: 0 warnings, 0 errors (no RS2008/RS2000-series complaints).

- [ ] **Step 3: Commit (only if changed)**

```
git add src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md
git commit -m "chore(gen): consolidate analyzer release tracking (DWARF008/009/010)"
```

---

## Task 6: Runtime integration tests + snapshot

**Files:**
- Create: `tests/DwarfMapper.IntegrationTests/MemberResolutionRuntimeTests.cs`

- [ ] **Step 1: Write runtime tests that execute the new behaviors**

`tests/DwarfMapper.IntegrationTests/MemberResolutionRuntimeTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class FieldSource { public int Count; public string Tag = ""; }
public class FieldTarget { public int Count; public string Tag = ""; }

[DwarfMapper]
public partial class FieldMapper
{
    public partial FieldTarget Map(FieldSource s);
}

public class RenameSource { public string FullName { get; set; } = ""; }
public class RenameTarget { public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class RenameMapper
{
    [MapProperty("FullName", "Name")]
    public partial RenameTarget Map(RenameSource s);
}

public class CiSource { public int count { get; set; } }
public class CiTarget { public int Count { get; set; } }

[DwarfMapper(CaseInsensitive = true)]
public partial class CiMapper
{
    public partial CiTarget Map(CiSource s);
}

public class MemberResolutionRuntimeTests
{
    [Fact]
    public void Maps_public_fields()
    {
        var t = new FieldMapper().Map(new FieldSource { Count = 3, Tag = "ore" });
        Assert.Equal(3, t.Count);
        Assert.Equal("ore", t.Tag);
    }

    [Fact]
    public void Maps_renamed_member()
    {
        var t = new RenameMapper().Map(new RenameSource { FullName = "Gimli" });
        Assert.Equal("Gimli", t.Name);
    }

    [Fact]
    public void Maps_case_insensitively()
    {
        var t = new CiMapper().Map(new CiSource { count = 9 });
        Assert.Equal(9, t.Count);
    }
}
```

- [ ] **Step 2: Run the integration tests**

Run: `dotnet test tests/DwarfMapper.IntegrationTests -c Debug`
Expected: PASS (existing 2 + new 3 = 5). If any fails to COMPILE (partial method body missing), the generator did not emit for that case — investigate; do not hand-write bodies.

- [ ] **Step 3: Full-solution sanity**

Run: `$env:DiffEngine_Disabled="true"; dotnet test DwarfMapper.NET.sln -c Debug`
Expected: ALL tests pass across both test projects. Report the total count.

- [ ] **Step 4: Commit**

```
git add tests/DwarfMapper.IntegrationTests/MemberResolutionRuntimeTests.cs
git commit -m "test: runtime integration for fields, renames, case-insensitive"
```

---

## Task 7: Documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add a short "Configuring mapping" subsection after the Quick start**

Insert after the Quick start section a concise block showing the now-supported features (keep prose tight, match existing README tone):
```markdown
### Configuring mapping

```csharp
[DwarfMapper(CaseInsensitive = true)]      // opt-in: match 'name' to 'Name'
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]  // explicit rename
    public partial CustomerDto ToDto(Customer src);
}
```

- **Fields and properties** are both mapped (public, instance, writable on the destination).
- **Matching is exact/case-sensitive by default**; set `CaseInsensitive = true` to relax it (ambiguous matches become `DWARF010`).
- **`[MapProperty(source, target)]`** maps differently-named members; an unknown source/target is `DWARF009`/`DWARF008`.
```

- [ ] **Step 2: Update the diagnostics/roadmap mention**

If the README's roadmap lists "case-insensitive matching + [MapProperty] + fields" under future work, move those items to the implemented set (or add a line noting they shipped in the member-resolution milestone). Leave converters/enums/nested/null-strategies/collections in the roadmap.

- [ ] **Step 3: Commit**

```
git add README.md
git commit -m "docs: document fields, case-insensitive matching, and [MapProperty]"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Fields as mappable members → Task 2 (read/write/readonly-aware; const & backing fields excluded). ✅
- Case-insensitive opt-in + ambiguity diagnostic → Task 3 (DWARF010). ✅
- `[MapProperty]` renames + unknown-member diagnostics → Task 4 (DWARF008/009). ✅
- Completeness intact: explicit-handled and ignored targets excluded from DWARF001; read-only-with-source still DWARF007 → Tasks 2/4. ✅
- Emitter & models unchanged (verified by design — `MemberMap`/`MapEmitter` untouched). ✅
- Release tracking for new rules → Tasks 3/4 inline + Task 5 consolidation. ✅
- Runtime proof + docs → Tasks 6/7. ✅

**Placeholder scan:** no TBD/TODO; every code step has complete code.

**Type consistency:** `ResolveMembers` evolves across Tasks 2→3→4 (each task gives the complete replacement body; final signature is `(sourceType, targetType, ignores, compilation, location, diagnostics, bool caseInsensitive, IReadOnlyList<(string,string)> explicitMaps)`). Member helpers return `(string Name, ITypeSymbol Type)` consistently. New descriptors `AmbiguousMatch`/`MapPropertyUnknownTarget`/`MapPropertyUnknownSource` referenced after definition. `ReadCaseInsensitive`/`ReadExplicitMaps` defined before use.

**Ambiguity check:** explicit `[MapProperty]` source/target names are matched **case-sensitively (Ordinal)** regardless of the `CaseInsensitive` option (the user typed exact names). Auto-matching uses the configured comparer. A target named by `[MapProperty]` is removed from auto-matching and completeness even if the explicit pair errors (prevents duplicate diagnostics). Read-only DWARF007 is suppressed for explicitly-handled or ignored targets.

**Decisions deferred to Plan 3 (value transformation):** custom converters / user-defined conversion methods / `[MapWith]`, nested-object auto-mapping, enums, null-handling strategies. **Plan 4:** collections + SIMD/blit. **Plan 5:** `DwarfMapper.Testing`.
