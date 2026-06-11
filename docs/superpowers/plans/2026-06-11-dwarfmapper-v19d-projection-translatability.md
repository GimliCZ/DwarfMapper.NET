# DwarfMapper Plan 19D — Provably-Translatable IQueryable Projection

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend IQueryable projection to support inlined nested objects, collections, and constructor projections, while making any non-translatable construct a compile error (DWARF028) instead of a runtime throw.

**Architecture:** A new recursive `ResolveProjectionMembers` replaces the flat version. It walks the target type tree and for each member either emits an inline expression fragment (SAFE: direct assign, nested `new`, collection `.Select().ToList()/.ToArray()`, ctor `new`) or records DWARF028 with a named reason. Inline fragments are stored as a string in a new `ProjectionMemberMap` record carried through the model. `MapEmitter` splats those fragments verbatim into the lambda body. The `CollectionConverter.IsTargetKindTranslatable` flag from Part B is consumed here for collection targets.

**Tech Stack:** Roslyn netstandard2.0 incremental source generator; test harness (`GeneratorTestHarness.Run`/`EmitAssembly`); xUnit; .NET 10 generated code; GPL-2.0.

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `src/DwarfMapper.Generator/Model/ProjectionMemberMap.cs` | **Create** | New value record: `TargetName` + `InlineExpr` (string fragment) |
| `src/DwarfMapper.Generator/Model/MapMethodModel.cs` | **Modify** | Add `ProjectionMembers` field (EquatableArray of ProjectionMemberMap) |
| `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` | **Modify** | Replace flat `ResolveProjectionMembers` with recursive version; add DWARF028; pass `referenceHandling` check |
| `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs` | **Modify** | Update projection emit branch to use `ProjectionMembers` (inline exprs) instead of `Members` |
| `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs` | **Modify** | Add DWARF028 descriptor |
| `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md` | **Modify** | Register DWARF028 |
| `tests/DwarfMapper.Generator.Tests/ProjectionDeepTests.cs` | **Create** | All new SAFE + UNSAFE projection tests |
| `tests/DwarfMapper.IntegrationTests/ProjectionDeepRuntimeTests.cs` | **Create** | Runtime SAFE projection tests over in-memory IQueryable |

---

## Task 1: DWARF028 diagnostic + AnalyzerReleases

**Files:**
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`

- [ ] **Step 1: Write failing test** — DWARF028 descriptor should exist and have id "DWARF028"

Add to `tests/DwarfMapper.Generator.Tests/ProjectionTests.cs`:
```csharp
[Fact]
public void DWARF028_descriptor_exists_and_is_error()
{
    var d = DwarfMapper.Generator.Diagnostics.DiagnosticDescriptors.ProjectionNotTranslatable;
    Assert.Equal("DWARF028", d.Id);
    Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, d.DefaultSeverity);
}
```

- [ ] **Step 2: Run test — confirm FAIL**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "DWARF028_descriptor_exists" -v minimal`
Expected: FAIL (ProjectionNotTranslatable does not exist yet)

- [ ] **Step 3: Add DWARF028 to DiagnosticDescriptors.cs**

In `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`, replace the comment `// DWARF028–029 reserved for Plan 19 Parts D (projection translatability).` with:
```csharp
public static readonly DiagnosticDescriptor ProjectionNotTranslatable = new(
    "DWARF028",
    "Projection mapping not translatable",
    "Projection member '{0}' cannot be translated to SQL: {1}. Map it at runtime or use a type that is EF-Core-translatable.",
    Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
```

- [ ] **Step 4: Register DWARF028 in AnalyzerReleases.Unshipped.md**

Add row to the table:
```
DWARF028 | DwarfMapper | Error | Projection mapping not translatable
```

- [ ] **Step 5: Run test — confirm PASS**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "DWARF028_descriptor_exists" -v minimal`
Expected: PASS

- [ ] **Step 6: Commit**
```
git add src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md tests/DwarfMapper.Generator.Tests/ProjectionTests.cs
git commit -m "feat(diag): add DWARF028 ProjectionNotTranslatable descriptor"
```

---

## Task 2: ProjectionMemberMap model + MapMethodModel extension

**Files:**
- Create: `src/DwarfMapper.Generator/Model/ProjectionMemberMap.cs`
- Modify: `src/DwarfMapper.Generator/Model/MapMethodModel.cs`

- [ ] **Step 1: Write failing test** — projection method model should carry ProjectionMembers

Add to `tests/DwarfMapper.Generator.Tests/ProjectionTests.cs`:
```csharp
[Fact]
public void MapMethodModel_has_ProjectionMembers_field()
{
    // Just a compile-time check: the record must have the field.
    var m = new DwarfMapper.Generator.Model.MapMethodModel(
        "Test", "public", "T", "S", "s", false,
        DwarfMapper.Generator.Collections.EquatableArray<DwarfMapper.Generator.Model.MemberMap>.Empty,
        DwarfMapper.Generator.Collections.EquatableArray<string>.Empty,
        DwarfMapper.Generator.Collections.EquatableArray<DwarfMapper.Generator.Model.HookCall>.Empty,
        false, "");
    Assert.NotNull(m.ProjectionMembers);
}
```

- [ ] **Step 2: Run test — confirm FAIL**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "MapMethodModel_has_ProjectionMembers_field" -v minimal`
Expected: FAIL (field does not exist)

- [ ] **Step 3: Create ProjectionMemberMap.cs**

Create `src/DwarfMapper.Generator/Model/ProjectionMemberMap.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// A single projection member binding for IQueryable.Select — carries the
/// target name and a complete inline expression fragment (no helper calls).
/// Value-equatable; safe for incremental-generator model caching.
/// </summary>
public sealed record ProjectionMemberMap(
    /// <summary>The destination member name (used as the LHS of member-init).</summary>
    string TargetName,
    /// <summary>
    /// The complete RHS inline expression, e.g.:
    ///   "__s.Age"
    ///   "(long)__s.X"
    ///   "(Status2)__s.Status"
    ///   "__s.Inner == null ? null : new InnerDto { A = __s.Inner.A }"
    ///   "__s.Items.Select(__i0 => new ItemDto { V = __i0.V }).ToList()"
    ///   "new PointDto(__s.Point.X, __s.Point.Y)"
    /// Never contains a synthesized helper call (__DwarfMap_*).
    /// </summary>
    string InlineExpr) : System.IEquatable<ProjectionMemberMap>;
```

- [ ] **Step 4: Add ProjectionMembers to MapMethodModel**

In `src/DwarfMapper.Generator/Model/MapMethodModel.cs`, add a new parameter after `IsPreserveMode`:
```csharp
    /// <summary>
    /// For projection methods (<see cref="IsProjection"/> = true), the inline expression
    /// fragments for each member (nested new / Select / ctor / direct assign).
    /// Empty for non-projection methods.
    /// </summary>
    EquatableArray<ProjectionMemberMap> ProjectionMembers = default) : IEquatable<MapMethodModel>;
```

The full record now ends with `IsPreserveMode = false, EquatableArray<ProjectionMemberMap> ProjectionMembers = default)`.

- [ ] **Step 5: Run test — confirm PASS**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "MapMethodModel_has_ProjectionMembers_field" -v minimal`
Expected: PASS

- [ ] **Step 6: Run full suite to confirm no regression**

Run: `dotnet test -c Release --logger "console;verbosity=minimal" 2>&1 | tail -8`
Expected: all green (781 total)

- [ ] **Step 7: Commit**
```
git add src/DwarfMapper.Generator/Model/ProjectionMemberMap.cs src/DwarfMapper.Generator/Model/MapMethodModel.cs tests/DwarfMapper.Generator.Tests/ProjectionTests.cs
git commit -m "feat(model): ProjectionMemberMap record + ProjectionMembers field on MapMethodModel"
```

---

## Task 3: Recursive projection resolver (SAFE constructs) — TDD

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`
- Create: `tests/DwarfMapper.Generator.Tests/ProjectionDeepTests.cs`

The key algorithm: `ResolveProjectionExpr(srcType, tgtType, srcPathExpr, depth, compilation, location, diagnostics, enumStrategy, referenceHandling, caseInsensitive, explicitMaps)` → returns `string?` (inline expression or null = DWARF028 emitted). Called recursively for nested objects/collection elements.

**SAFE classification:**
1. Direct-assignable (implicit conversion): emit `srcPathExpr.MemberName`
2. Widening cast (built-in numeric widening that is implicit): same as #1 (already covered by `HasImplicitConversion`)
3. Enum by-value cast (`(TargetEnum)src.E` or `(int)src.E`): emit `(TgtType)srcPathExpr.E` when both are enum and same underlying, or numeric widening
4. Nested named object (class/struct/record, not scalar/collection): recursively inline `new TgtType { ... }` with null-nav ternary for reference types
5. Collection (projection-translatable target only): inline `srcPathExpr.Items.Select(__iN => new ItemDto { ... }).ToList()` / `.ToArray()` / no-terminal for IEnumerable

**UNSAFE → DWARF028:**
- `use` method specified → "custom converter not translatable in projection"
- NumericConverter would fire (narrowing: `long`→`int`, `int`→`byte`, etc.) → "narrowing numeric conversion is not SQL-translatable; map at runtime or use a widening type"
- ParsableConverter would fire (string↔T) → "string parse/format is not translatable in projection"
- EnumConverter by-name → "enum by-name mapping is not translatable; use EnumStrategy.ByValue or map at runtime"
- Collection target where `IsTargetKindTranslatable` is false → "collection type '{typeName}' is not translatable in projection (HashSet/ISet/immutable/Dictionary targets are not supported by EF Core)"
- ReferenceHandling != None on the mapper class → "reference handling is not supported in projection (stateful identity map cannot live in an expression tree)"
- Depth > 32 → "projection nesting depth exceeded 32; split into a runtime mapper"
- No source member → DWARF001 (existing, unchanged)

**DWARF019 decision:** Keep DWARF019 for the *existing* case (non-assignable flat member with no other SAFE path — i.e. a member that has no source at all or is not directly assignable and the new resolver can't produce an inline expr via the SAFE list). DWARF028 is used for members that the resolver *recognises* as a known-unsafe construct. DWARF019 remains for the "I couldn't even classify what to do here" fallback. Documented with a comment in the source.

- [ ] **Step 1: Write failing tests — SAFE matrix**

Create `tests/DwarfMapper.Generator.Tests/ProjectionDeepTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Plan 19D — TDD tests for the recursive, translatability-classifying projection resolver.
/// SAFE: nested object inline, collection inline, ctor inline, widening numeric, enum by-value.
/// UNSAFE: each → DWARF028 with appropriate reason; NO __DwarfMap_ helper calls in output.
/// </summary>
public class ProjectionDeepTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // SAFE MATRIX
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Projection_flat_regression_still_works_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class Dst { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Contains("Age = __s.Age", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_nested_object_2level_inlines_new_inner_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Inner { public int X { get; set; } }
            public class InnerDto { public int X { get; set; } }
            public class Outer { public int Id { get; set; } public Inner? Inner { get; set; } }
            public class OuterDto { public int Id { get; set; } public InnerDto? Inner { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new D.InnerDto", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_nested_object_null_nav_ternary_emitted_for_nullable_ref()
    {
        // Nullable reference src.Inner → ternary: __s.Inner == null ? null : new InnerDto{...}
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Inner { public int X { get; set; } }
            public class InnerDto { public int X { get; set; } }
            public class Outer { public int Id { get; set; } public Inner? Inner { get; set; } }
            public class OuterDto { public int Id { get; set; } public InnerDto? Inner { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // Null-nav ternary must appear for the nullable inner
        Assert.Contains("== null ? null :", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_nested_object_3level_inlines_all_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class L3 { public int Z { get; set; } }
            public class L3Dto { public int Z { get; set; } }
            public class L2 { public int Y { get; set; } public L3? Deep { get; set; } }
            public class L2Dto { public int Y { get; set; } public L3Dto? Deep { get; set; } }
            public class L1 { public int X { get; set; } public L2? Mid { get; set; } }
            public class L1Dto { public int X { get; set; } public L2Dto? Mid { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<L1Dto> Prj(IQueryable<L1> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new D.L2Dto", gen, StringComparison.Ordinal);
        Assert.Contains("new D.L3Dto", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_collection_list_to_list_inlines_Select_ToList_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public List<Item> Items { get; set; } = new(); }
            public class OuterDto { public List<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".Select(", gen, StringComparison.Ordinal);
        Assert.Contains(".ToList()", gen, StringComparison.Ordinal);
        Assert.Contains("new D.ItemDto", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_collection_array_to_array_inlines_Select_ToArray_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public Item[] Items { get; set; } = []; }
            public class OuterDto { public ItemDto[] Items { get; set; } = []; }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".Select(", gen, StringComparison.Ordinal);
        Assert.Contains(".ToArray()", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_collection_IEnumerable_target_lazy_no_ToList_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public IEnumerable<Item> Items { get; set; } = []; }
            public class OuterDto { public IEnumerable<ItemDto> Items { get; set; } = []; }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".Select(", gen, StringComparison.Ordinal);
        // IEnumerable target → no ToList/ToArray terminal
        Assert.DoesNotContain(".ToList()", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToArray()", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_ctor_record_positional_inlines_new_ctor_no_DwarfMap_helper()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
            public record DstRec(int X, string Y);
            [DwarfMapper] public partial class M { public partial IQueryable<DstRec> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // Constructor projection → new DstRec(...)
        Assert.Contains("new D.DstRec(", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_widening_numeric_int_to_long_inline()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } }
            public class Dst { public long X { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // int→long is an implicit widening — direct assignment, no cast needed
        Assert.Contains("X = __s.X", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_enum_by_value_same_type_direct_assign()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum Status { A, B }
            public class Src { public Status S { get; set; } }
            public class Dst { public Status S { get; set; } }
            [DwarfMapper(EnumStrategy = DwarfMapper.EnumStrategy.ByValue)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Projection_enum_by_value_different_enum_type_cast_inline()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum E1 { A = 0, B = 1 }
            public enum E2 { A = 0, B = 1 }
            public class Src { public E1 E { get; set; } }
            public class Dst { public E2 E { get; set; } }
            [DwarfMapper(EnumStrategy = DwarfMapper.EnumStrategy.ByValue)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        // By-value enum: cast inline (E2)__s.E
        Assert.Contains("(D.E2)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UNSAFE MATRIX — each → DWARF028 with right reason
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Projection_narrowing_numeric_long_to_int_reports_DWARF028_narrowing_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public long X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.Contains("narrowing", d028.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_string_to_int_parsable_reports_DWARF028_parse_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public string X { get; set; } = ""; }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.Contains("parse", d028.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_enum_by_name_reports_DWARF028_byname_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public enum E1 { A, B }
            public enum E2 { A, B }
            public class Src { public E1 E { get; set; } }
            public class Dst { public E2 E { get; set; } }
            [DwarfMapper(EnumStrategy = DwarfMapper.EnumStrategy.ByName)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.Contains("by-name", d028.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_custom_Use_converter_reports_DWARF028_converter_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } }
            public class Dst { public string X { get; set; } = ""; }
            [DwarfMapper] public partial class M
            {
                [MapProperty("X", "X", Use = "Conv")]
                public partial IQueryable<Dst> Prj(IQueryable<Src> q);
                private static string Conv(int x) => x.ToString();
            }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.Contains("converter", d028.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_HashSet_collection_target_reports_DWARF028_collection_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public HashSet<Item> Items { get; set; } = new(); }
            public class OuterDto { public HashSet<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.Contains("translatable", d028.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_ImmutableArray_collection_target_reports_DWARF028()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Immutable;
            namespace D;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Outer { public ImmutableArray<Item> Items { get; set; } }
            public class OuterDto { public ImmutableArray<ItemDto> Items { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diag, d => d.Id == "DWARF028");
    }

    [Fact]
    public void Projection_Dictionary_collection_target_reports_DWARF028()
    {
        const string s = """
            using DwarfMapper; using System.Linq; using System.Collections.Generic;
            namespace D;
            public class Outer { public Dictionary<string, int> Tags { get; set; } = new(); }
            public class OuterDto { public Dictionary<string, int> Tags { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diag, d => d.Id == "DWARF028");
    }

    [Fact]
    public void Projection_reference_handling_preserve_reports_DWARF028_refhandling_reason()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public int X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper(ReferenceHandling = DwarfMapper.ReferenceHandlingStrategy.Preserve)]
            public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        var d028 = Assert.Single(diag.Where(d => d.Id == "DWARF028"));
        Assert.Contains("reference handling", d028.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    // ── Regression: old DWARF019 flat case still fires ──
    [Fact]
    public void Projection_non_assignable_flat_member_still_reports_DWARF019()
    {
        const string s = """
            using DwarfMapper; using System.Linq;
            namespace D;
            public class Src { public string Age { get; set; } = ""; }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<Dst> Prj(IQueryable<Src> q); }
            """;
        var (diag, _) = GeneratorTestHarness.Run(s);
        // string→int: ParsableConverter fires first → should now be DWARF028 with parse reason
        // (we fold it into DWARF028; DWARF019 is for truly unclassifiable cases)
        // Accept either DWARF019 or DWARF028 — the exact routing is impl detail, but there MUST be an error
        Assert.Contains(diag, d => d.Id == "DWARF028" || d.Id == "DWARF019");
    }
}
```

- [ ] **Step 2: Run tests — confirm ALL FAIL**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "ProjectionDeepTests" -v minimal`
Expected: all FAIL (generator not updated yet)

- [ ] **Step 3: Implement recursive projection resolver in MapperExtractor.cs**

In `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs`:

**3a.** Add a constant for max projection depth (after the class declaration):
```csharp
private const int ProjectionMaxDepth = 32;
```

**3b.** Replace the current `ResolveProjectionMembers` (line ~1923–1994) with the new recursive version. The new method signature:
```csharp
private static List<ProjectionMemberMap> ResolveProjectionMembers(
    ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
    Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
    bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
    EnumStrategy enumStrategy, int referenceHandling, string paramExpr = "__s")
```

Add a private recursive helper:
```csharp
/// <summary>
/// Resolve a single inline projection expression for source type srcType → tgt type tgtType,
/// where the source is accessed via <paramref name="srcExpr"/> in the lambda.
/// Returns the inline expression string (no helper call), or null and emits DWARF028.
/// </summary>
private static string? ResolveProjectionExpr(
    ITypeSymbol srcType, ITypeSymbol tgtType,
    string srcExpr,   // expression to read the source value
    int depth,
    Compilation compilation,
    LocationInfo? location,
    List<DiagnosticInfo> diagnostics,
    string targetMemberName,
    EnumStrategy enumStrategy)
```

**3c.** Logic for `ResolveProjectionExpr`:

```
if depth > ProjectionMaxDepth:
    emit DWARF028: "projection nesting depth exceeded {ProjectionMaxDepth}; split into a runtime mapper"
    return null

// 1. Direct-assignable (includes widening numeric)
if HasImplicitConversion(srcType, tgtType):
    return srcExpr

// 2. Enum by-value (numeric cast)
if srcType.TypeKind==Enum && tgtType.TypeKind==Enum && enumStrategy==ByValue:
    return $"({tgtFqn}){srcExpr}"

// 3. UNSAFE: numeric narrowing (NumericConverter would fire)
if NumericConverter.WouldFire(srcType, tgtType):
    emit DWARF028 reason "narrowing numeric conversion is not SQL-translatable; map at runtime or use a widening type"
    return null

// 4. UNSAFE: parsable (ParsableConverter would fire)
if ParsableConverter.WouldFire(srcType, tgtType, compilation):
    emit DWARF028 reason "string parse/format is not translatable in projection"
    return null

// 5. UNSAFE: enum by-name (EnumConverter would fire AND enumStrategy==ByName)
if (srcType.TypeKind==Enum || tgtType.TypeKind==Enum) && enumStrategy==ByName:
    emit DWARF028 reason "enum by-name mapping is not translatable in projection; use EnumStrategy.ByValue or map at runtime"
    return null

// 6. Collection target
if CollectionConverter.TryResolve(srcType, tgtType, out srcElem, out tgtElem, out shape):
    if !IsTargetKindTranslatable(shape.Target):
        emit DWARF028 reason "collection type '{tgtTypeName}' is not translatable in projection (HashSet/ISet/immutable/Dictionary targets are not supported by EF Core)"
        return null
    // recursively get element inline expr
    var elemParam = $"__i{depth}"
    var elemExpr = ResolveProjectionExpr(srcElem, tgtElem, elemParam, depth+1, ...)
    if elemExpr is null: return null (DWARF028 already emitted by nested call)
    var select = $"{srcExpr}.Select({elemParam} => {elemExpr})"
    var terminal = shape.Target switch {
        Array => ".ToArray()",
        IEnumerable => "",   // lazy
        _ => ".ToList()"
    }
    return select + terminal

// 7. DictionaryConverter target (always UNSAFE in projection)
if DictionaryConverter.TryResolve(srcType, tgtType, ...):
    emit DWARF028 reason "Dictionary targets are not translatable in projection"
    return null

// 8. Nested object (class/struct/record, not scalar)
if IsMappableObjectPair(compilation, srcType, tgtType as INamedTypeSymbol):
    return ResolveNestedProjectionObject(srcType, tgtType as INamedTypeSymbol, srcExpr, depth, ...)

// 9. No match
// Note: we emit DWARF028 here for the "truly unclassifiable" case to avoid DWARF019.
// DWARF019 (NotProjectable) is now only emitted from the ResolveProjectionMembers method
// for use= conflicts and other attribute conflicts. For type-mismatch we use DWARF028
// to be consistent with the "compile error for non-translatable" thesis.
emit DWARF028 reason "no translatable conversion found; map at runtime"
return null
```

**3d.** Add `ResolveNestedProjectionObject` that:
- Iterates writable members of tgtType
- For each, matches source member
- Calls `ResolveProjectionExpr` recursively to get the inline expr for `srcExpr.MemberName`
- If target has no parameterless ctor, tries constructor projection: for each ctor param, resolve `srcExpr.ParamName` inline; produce `new TgtType(param1: expr1, param2: expr2)`
- Emits null-navigation ternary for reference source types: `{srcExpr} == null ? null : new TgtType { ... }`

**3e.** Update the caller in `Extract()` (the `IsQueryable` branch) to:
- Check `referenceHandling != 0` BEFORE calling `ResolveProjectionMembers` → emit DWARF028 with "reference handling is not supported in projection" and skip resolve; the method still needs to be added to `methods` with empty members to avoid further errors
- Call new `ResolveProjectionMembers` returning `List<ProjectionMemberMap>`
- Build `MapMethodModel` with `ProjectionMembers = EquatableArray.From(projMembers.ToArray())`

**3f.** Keep existing DWARF019 for the `use is not null` check in explicit maps (that's an attribute conflict, not a type conversion, so DWARF019 is appropriate).

Also: update the `HasAccessibleParameterlessCtor` gate in the projection branch — for ctor-projection targets (no parameterless ctor but has a suitable ctor) we now DON'T skip. Remove the `HasAccessibleParameterlessCtor` check from the projection path (or make it conditional: only required when NO ctor can be used).

- [ ] **Step 4: Run tests — confirm significant progress**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "ProjectionDeepTests" -v minimal`
Expected: most SAFE tests pass; UNSAFE tests may still fail if DWARF028 emit missing

- [ ] **Step 5: Fix remaining failures**

Iterate on the implementation until all `ProjectionDeepTests` pass.

- [ ] **Step 6: Run full suite — confirm no regression**

Run: `dotnet test -c Release --logger "console;verbosity=minimal" 2>&1 | tail -8`
Expected: 781+ green

- [ ] **Step 7: Commit**
```
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs tests/DwarfMapper.Generator.Tests/ProjectionDeepTests.cs
git commit -m "feat(proj): recursive projection resolver with SAFE inline + DWARF028 UNSAFE (Plan 19D)"
```

---

## Task 4: Update MapEmitter to use ProjectionMembers inline expressions

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`

- [ ] **Step 1: Write failing test** — emitted code must use inline fragments, not `__s.Member`

From the existing tests, the flat projection test already checks `Age = __s.Age`. The new test checking for nested `new InnerDto` already covers this indirectly. But we need to confirm the emitter actually reads `ProjectionMembers`.

Add to `ProjectionDeepTests.cs`:
```csharp
[Fact]
public void Projection_emitter_uses_ProjectionMembers_inline_expr_not_raw_member()
{
    // The nested object test implicitly validates this, but let's be explicit:
    // the generated code must NOT contain "Inner = __s.Inner" (raw assign for class member)
    // but must contain "new D.InnerDto" (inline new).
    const string s = """
        using DwarfMapper; using System.Linq;
        namespace D;
        public class Inner { public int X { get; set; } }
        public class InnerDto { public int X { get; set; } }
        public class Outer { public Inner? Inner { get; set; } }
        public class OuterDto { public InnerDto? Inner { get; set; } }
        [DwarfMapper] public partial class M { public partial IQueryable<OuterDto> Prj(IQueryable<Outer> q); }
        """;
    var (diag, gen) = GeneratorTestHarness.Run(s);
    Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
    Assert.Contains("new D.InnerDto", gen, StringComparison.Ordinal);
    // Must not be a raw assignment of the object reference
    Assert.DoesNotContain("Inner = __s.Inner,", gen, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run test — confirm FAIL**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "Projection_emitter_uses_ProjectionMembers" -v minimal`
Expected: FAIL (emitter still uses `Members` not `ProjectionMembers`)

- [ ] **Step 3: Update MapEmitter.cs projection branch**

In `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`, find the `if (method.IsProjection)` block (lines ~141–153):

Replace:
```csharp
if (method.IsProjection)
{
    sb.Append(indent).Append("    return global::System.Linq.Queryable.Select(")
      .Append(method.ParameterName).AppendLine(", __s => new " + method.ElementTargetTypeFullName);
    sb.Append(indent).AppendLine("    {");
    foreach (var member in method.Members)
    {
        sb.Append(indent).Append("        ").Append(member.TargetName)
          .Append(" = __s.").Append(member.SourceName).AppendLine(",");
    }
    sb.Append(indent).AppendLine("    });");
    sb.Append(indent).AppendLine("}");
    return;
}
```

With:
```csharp
if (method.IsProjection)
{
    sb.Append(indent).Append("    return global::System.Linq.Queryable.Select(")
      .Append(method.ParameterName).Append(", __s =>");

    // If we have inline ProjectionMembers (new recursive resolver), emit them.
    // Otherwise fall back to the flat Members (legacy flat projection, backward compat).
    if (method.ProjectionMembers.Count > 0)
    {
        sb.AppendLine(" new " + method.ElementTargetTypeFullName);
        sb.Append(indent).AppendLine("    {");
        foreach (var pm in method.ProjectionMembers)
        {
            sb.Append(indent).Append("        ").Append(pm.TargetName)
              .Append(" = ").Append(pm.InlineExpr).AppendLine(",");
        }
        sb.Append(indent).AppendLine("    });");
    }
    else if (method.Members.Count > 0)
    {
        // Legacy flat path (should only trigger for backward-compat edge cases)
        sb.AppendLine(" new " + method.ElementTargetTypeFullName);
        sb.Append(indent).AppendLine("    {");
        foreach (var member in method.Members)
        {
            sb.Append(indent).Append("        ").Append(member.TargetName)
              .Append(" = __s.").Append(member.SourceName).AppendLine(",");
        }
        sb.Append(indent).AppendLine("    });");
    }
    else
    {
        // Ctor-projection: the InlineExpr on the first (and only) ProjectionMemberMap IS the whole lambda body
        // (e.g. "new DstRec(x: __s.X, y: __s.Y)"). Emit directly.
        sb.Append(" "); // handled below by CtorProjection path
        // (This branch hit only when there are 0 members AND 0 ProjectionMembers — shouldn't happen)
        sb.AppendLine("new " + method.ElementTargetTypeFullName + "());");
    }

    sb.Append(indent).AppendLine("}");
    return;
}
```

**Note on ctor-projection emission:** For records/classes with only ctor args (no writable members), the recursive resolver produces a *single* `ProjectionMemberMap` with `TargetName = ""` and `InlineExpr = "new DstRec(x: __s.X)"` — OR we can store it differently. Actually, the cleaner approach: for ctor-only projection, the resolver wraps the whole `new DstRec(x: ..., y: ...)` expression as the lambda body. We can use the `ElementTargetTypeFullName` path and emit `__s => <inline>` directly.

Revise: the emitter should check if `ProjectionMembers.Count == 1 && ProjectionMembers[0].TargetName == ""` as a signal for "whole-lambda ctor expression":

```csharp
if (method.ProjectionMembers.Count == 1 && method.ProjectionMembers[0].TargetName == "")
{
    // Ctor-only projection: the entire lambda body is the inline expr
    sb.Append(" ").Append(method.ProjectionMembers[0].InlineExpr).AppendLine(");");
}
else if (method.ProjectionMembers.Count > 0)
{
    // Member-init projection
    sb.AppendLine(" new " + method.ElementTargetTypeFullName);
    ...member init...
}
else
{
    // Flat legacy Members path
    ...
}
```

The resolver must be adjusted to produce `ProjectionMemberMap("", "new D.DstRec(x: __s.X, y: __s.Y)")` for ctor-only targets.

- [ ] **Step 4: Run test — confirm PASS**

Run: `dotnet test tests/DwarfMapper.Generator.Tests -c Release --filter "Projection_emitter_uses_ProjectionMembers" -v minimal`

- [ ] **Step 5: Run full suite**

Run: `dotnet test -c Release --logger "console;verbosity=minimal" 2>&1 | tail -8`
Expected: all green

- [ ] **Step 6: Commit**
```
git add src/DwarfMapper.Generator/Pipeline/MapEmitter.cs tests/DwarfMapper.Generator.Tests/ProjectionDeepTests.cs
git commit -m "feat(emit): projection emitter reads inline ProjectionMembers fragments"
```

---

## Task 5: Runtime integration tests over in-memory IQueryable

**Files:**
- Create: `tests/DwarfMapper.IntegrationTests/ProjectionDeepRuntimeTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/DwarfMapper.IntegrationTests/ProjectionDeepRuntimeTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
// Plan 19D — runtime projection tests over in-memory IQueryable (System.Linq.AsQueryable)
// Confirms the generated expression trees are valid LINQ-to-objects (proxy for EF translatability shape).
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Types ─────────────────────────────────────────────────────────────────────

public class PrjInner { public int X { get; set; } }
public class PrjInnerDto { public int X { get; set; } }
public class PrjOuter { public int Id { get; set; } public PrjInner? Inner { get; set; } }
public class PrjOuterDto { public int Id { get; set; } public PrjInnerDto? Inner { get; set; } }

public class PrjOuter3L2 { public int Y { get; set; } public PrjInner? Deep { get; set; } }
public class PrjOuter3L2Dto { public int Y { get; set; } public PrjInnerDto? Deep { get; set; } }
public class PrjOuter3 { public int X { get; set; } public PrjOuter3L2? Mid { get; set; } }
public class PrjOuter3Dto { public int X { get; set; } public PrjOuter3L2Dto? Mid { get; set; } }

public class PrjItem { public int V { get; set; } }
public class PrjItemDto { public int V { get; set; } }
public class PrjCollOuter { public List<PrjItem> Items { get; set; } = new(); }
public class PrjCollOuterDto { public List<PrjItemDto> Items { get; set; } = new(); }

public class PrjCtorSrc { public int X { get; set; } public string Y { get; set; } = ""; }
public record PrjCtorRec(int X, string Y);

// ── Mappers ───────────────────────────────────────────────────────────────────

[DwarfMapper]
public partial class NestedProjMapper
{
    public partial IQueryable<PrjOuterDto> Project(IQueryable<PrjOuter> q);
}

[DwarfMapper]
public partial class Nested3LProjMapper
{
    public partial IQueryable<PrjOuter3Dto> Project(IQueryable<PrjOuter3> q);
}

[DwarfMapper]
public partial class CollProjMapper
{
    public partial IQueryable<PrjCollOuterDto> Project(IQueryable<PrjCollOuter> q);
}

[DwarfMapper]
public partial class CtorProjMapper
{
    public partial IQueryable<PrjCtorRec> Project(IQueryable<PrjCtorSrc> q);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class ProjectionDeepRuntimeTests
{
    [Fact]
    public void Nested_object_projection_executes_and_shapes_correctly()
    {
        var source = new[]
        {
            new PrjOuter { Id = 1, Inner = new PrjInner { X = 10 } },
            new PrjOuter { Id = 2, Inner = null },
        }.AsQueryable();

        var dtos = new NestedProjMapper().Project(source).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(1, dtos[0].Id);
        Assert.NotNull(dtos[0].Inner);
        Assert.Equal(10, dtos[0].Inner!.X);
        Assert.Equal(2, dtos[1].Id);
        Assert.Null(dtos[1].Inner);
    }

    [Fact]
    public void Three_level_nested_projection_executes_and_shapes_correctly()
    {
        var source = new[]
        {
            new PrjOuter3 { X = 1, Mid = new PrjOuter3L2 { Y = 2, Deep = new PrjInner { X = 3 } } },
            new PrjOuter3 { X = 4, Mid = null },
        }.AsQueryable();

        var dtos = new Nested3LProjMapper().Project(source).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(1, dtos[0].X);
        Assert.NotNull(dtos[0].Mid);
        Assert.Equal(2, dtos[0].Mid!.Y);
        Assert.NotNull(dtos[0].Mid.Deep);
        Assert.Equal(3, dtos[0].Mid.Deep!.X);
        Assert.Null(dtos[1].Mid);
    }

    [Fact]
    public void Collection_list_projection_executes_and_shapes_correctly()
    {
        var source = new[]
        {
            new PrjCollOuter { Items = new List<PrjItem> { new() { V = 1 }, new() { V = 2 } } },
            new PrjCollOuter { Items = new List<PrjItem>() },
        }.AsQueryable();

        var dtos = new CollProjMapper().Project(source).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(2, dtos[0].Items.Count);
        Assert.Equal(1, dtos[0].Items[0].V);
        Assert.Equal(2, dtos[0].Items[1].V);
        Assert.Empty(dtos[1].Items);
    }

    [Fact]
    public void Constructor_record_projection_executes_and_shapes_correctly()
    {
        var source = new[]
        {
            new PrjCtorSrc { X = 42, Y = "hello" },
        }.AsQueryable();

        var dtos = new CtorProjMapper().Project(source).ToList();

        Assert.Single(dtos);
        Assert.Equal(42, dtos[0].X);
        Assert.Equal("hello", dtos[0].Y);
    }
}
```

- [ ] **Step 2: Run tests — confirm FAIL** (generator not producing inline exprs yet; will fail once resolver is added)

Run: `dotnet test tests/DwarfMapper.IntegrationTests -c Release --filter "ProjectionDeepRuntime" -v minimal`
Expected: FAIL or compile error until generator is updated

- [ ] **Step 3: Verify after Task 3+4 — all runtime tests PASS**

After Tasks 3 and 4 are complete, run:
`dotnet test tests/DwarfMapper.IntegrationTests -c Release -v minimal`
Expected: all green (includes existing `ProjectionRuntimeTests`)

- [ ] **Step 4: Commit**
```
git add tests/DwarfMapper.IntegrationTests/ProjectionDeepRuntimeTests.cs
git commit -m "test(proj): runtime integration tests for nested/collection/ctor projection (Plan 19D)"
```

---

## Task 6: Final validation + AOT concern documentation

**Files:**
- None (just run commands and commit)

- [ ] **Step 1: Full solution test run**

Run: `$env:DiffEngine_Disabled='true'; dotnet test -c Release --logger "console;verbosity=minimal" 2>&1 | tail -12`
Expected: all green; no failures

- [ ] **Step 2: Release build check**

Run: `dotnet build -c Release 2>&1 | tail -5`
Expected: 0 errors, 0 warnings (warnings-as-errors) — `$LASTEXITCODE == 0`

- [ ] **Step 3: Confirm git log**

Run: `git log --oneline -6`

- [ ] **Step 4: Defensive assertion — no `__DwarfMap_` in any projection output**

Add this final test to `ProjectionDeepTests.cs`:
```csharp
[Fact]
public void All_safe_projection_outputs_contain_no_DwarfMap_helper_calls()
{
    // Enumerate all SAFE cases and assert __DwarfMap_ never appears.
    var cases = new[]
    {
        // flat
        "using DwarfMapper; using System.Linq; namespace D; public class S{public int A{get;set;}} public class T{public int A{get;set;}} [DwarfMapper] public partial class M{public partial IQueryable<T> P(IQueryable<S> q);}",
        // nested
        "using DwarfMapper; using System.Linq; namespace D; public class I{public int X{get;set;}} public class ID{public int X{get;set;}} public class S{public I? N{get;set;}} public class T{public ID? N{get;set;}} [DwarfMapper] public partial class M{public partial IQueryable<T> P(IQueryable<S> q);}",
    };
    foreach (var s in cases)
    {
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(gen, "__DwarfMap_", StringComparison.Ordinal);
    }
}
```

- [ ] **Step 5: Commit final validation test**
```
git add tests/DwarfMapper.Generator.Tests/ProjectionDeepTests.cs
git commit -m "test(proj): defensive no-__DwarfMap_ assertion for all SAFE projections"
```

---

## Design Notes (for implementer)

### DWARF019 vs DWARF028 Decision
- **DWARF028** (ProjectionNotTranslatable) is used when the resolver *identifies* a known-unsafe construct: narrowing numeric, parsable, enum-by-name, custom Use= converter, non-translatable collection target, reference handling.
- **DWARF019** (NotProjectable) is kept ONLY for attribute-conflict cases in explicit maps (e.g. `[MapProperty]` + `use=` on a projection method — that's an attribute usage error, not a type translatability error). This preserves the existing DWARF019 test regression.
- For the "no conversion found" fallback (truly unclassifiable), we emit DWARF028 with reason "no translatable conversion found; map at runtime". This is consistent with the thesis and more informative than DWARF019.
- **Document this with a code comment** in `ResolveProjectionMembers`.

### Inline Expression Format
The resolver produces inline C# strings. Examples:
- Flat int member: `__s.Age`
- Widening (int→long implicit): `__s.X` (implicit widening needs no cast)
- Enum by-value cast: `(global::D.E2)__s.E`
- Nested object (nullable ref src): `__s.Inner == null ? null : new global::D.InnerDto { X = __s.Inner.X }`
- Nested object (non-nullable/value src): `new global::D.InnerDto { X = __s.Inner.X }`
- Collection List→List: `__s.Items.Select(__i0 => new global::D.ItemDto { V = __i0.V }).ToList()`
- Array→Array: `__s.Items.Select(__i0 => new global::D.ItemDto { V = __i0.V }).ToArray()`
- IEnumerable lazy: `__s.Items.Select(__i0 => new global::D.ItemDto { V = __i0.V })`
- Ctor record (whole-lambda): stored as `ProjectionMemberMap("", "new global::D.DstRec(x: __s.X, y: __s.Y)")`

### Collection null-navigation
For collection members where source is nullable (`List<Item>?`), the inline expr should include null-nav:
`__s.Items == null ? null : __s.Items.Select(...).ToList()`

### Ctor-only target (record) detection
A positional record (or any type with only init/ctor parameters and no writable members) should be detected by `WritableMembers(targetType).Count == 0 && ctor.Parameters.Length > 0`. In this case, produce the whole-lambda ctor expression and store as `ProjectionMemberMap("", expr)`.

### Mixed ctor+init
When the target has both ctor parameters AND writable members (e.g. `new Dto(x) { B = __s.B }`), produce a member-init expression where the ctor-param members are expressed positionally/by-name in the ctor, and remaining init members are in the `{}` block. The inline expression is still a single C# expression: `new Dto(x: __s.X) { B = __s.B }`.

### Depth counter for nested lambdas
Use `depth` to generate unique lambda parameter names (`__i0`, `__i1`, etc.) to avoid shadowing in nested collections.

### AOT concern
Projection is expression-tree only (no reflection, no helper calls). The generated lambda `__s => new InnerDto { ... }` is a pure expression tree — fully AOT-safe. No concern.
