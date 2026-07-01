# MapConfig<S,T> Member-Selector Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a compile-time-analyzed fluent config (`MapConfig<S,T>`) that lets users author every string-bearing member mapping with type-safe, refactor-safe lambda selectors and method groups instead of `nameof`/string literals.

**Architecture:** A new runtime type `DwarfMapper.MapConfig<TSource,TTarget>` whose fluent methods exist only so the selector lambdas type-check. A new generator front-end reads a convention method (matched by its `MapConfig<S,T>` parameter type) **syntactically** — never executing it — and emits the *same* IR structures (`PairProp`/`PairIgnore`/`PairValue`/`PairConstructor` + source-ignores) the attributes already produce, so everything downstream is unchanged.

**Tech Stack:** C# / .NET 10 runtime; `DwarfMapper.Generator` is a netstandard2.0 Roslyn incremental source generator; xUnit tests; `GeneratorTestHarness` value oracle; Verify snapshots.

## Global Constraints

- Generator target framework stays **netstandard2.0**; runtime library stays **net10.0**. Do NOT change either.
- Generator MUST NOT reference the runtime `DwarfMapper` assembly — all names are matched via `KnownNames`/metadata-name lookups.
- License header on every new file: `// SPDX-License-Identifier: GPL-2.0-only`.
- "Never silent": any selector/reference the generator cannot understand is a diagnostic, never an ignored mapping.
- The full self-test suite (currently **3,589** tests across `DwarfMapper.NET.sln`) MUST be green at the end of every task. Run: `dotnet test C:\Users\Jouda\RiderProjects\DwarfMapper.NET\DwarfMapper.NET.sln -c Debug --nologo`.
- Next free diagnostic id is **DWARF068**, then **DWARF069** (DWARF001–067 are taken).
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Do NOT `git push`.
- Build shell: PowerShell. Test-failure grep pattern: `Select-String -Pattern "\[FAIL\]"`. Success/fail summary lines are Czech: `Úspěšné!` / `Neúspěšné!`.

## File Structure

- **Create** `src/DwarfMapper/MapConfig.cs` — runtime `MapConfig<TSource,TTarget>` fluent type (compile-time-only surface).
- **Create** `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs` — `partial class MapperExtractor` holding the config collector (shares the private `PairProp`/`PairIgnore`/`PairValue`/`PairConstructor` nested types).
- **Modify** `src/DwarfMapper.Generator/Pipeline/KnownNames.cs` — add `MapConfig` type name + metadata name.
- **Modify** `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` — invoke the collector and merge its output into `pairProps`/`pairIgnores`/`pairValues`/`pairConstructors`/`classIgnoreSources`; refactor `TryFormatConstant` to add a `(value,type)` overload; wire DWARF068/069.
- **Modify** `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs` + `AnalyzerReleases.Unshipped.md` — DWARF068/069.
- **Create** `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs` — runtime + differential-parity tests (config vs attribute produce identical output).
- **Create** `tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs` — selector parsing, diagnostics, mixed-combination matrix.
- **Modify** self-validation: `FeatureInteractionCompileMatrixTests.cs` (or `MatrixExemptAttributes`) + `docs/diagnostics.md`.

**Precondition:** `MapperExtractor` is a non-generic `internal` class with `private static` helpers, so a `partial` file can host the collector and see the private nested IR types. Verify with `Grep` for `class MapperExtractor` before Task 3; if it is declared `static`, declare the partial `static` too.

---

### Task 1: Runtime `MapConfig<TSource,TTarget>` type

**Files:**
- Create: `src/DwarfMapper/MapConfig.cs`
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Produces: `public sealed class DwarfMapper.MapConfig<TSource,TTarget>` with fluent methods `Map`, `MapWhen`, `MapOr` (two overloads: `where TMember:struct` and `where TMember:class`), `Ignore`, `IgnoreSource`, `Value` (constant + `Func<TMember>` compute), `Construct`. Every method returns `MapConfig<TSource,TTarget>`.

- [ ] **Step 1: Write the failing test** (`tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`)

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

public class MapConfigRuntimeTests
{
    private sealed class S { public string? Name { get; set; } public int Count { get; set; } }
    private sealed class T { public string? Full { get; set; } public int Total { get; set; } }

    [Fact]
    public void MapConfig_surface_compiles_and_chains()
    {
        // The type exists purely so selector lambdas type-check. It is never used by the generator at runtime;
        // this test only proves the fluent surface compiles and chains.
        var c = System.Activator.CreateInstance(typeof(MapConfig<S, T>), nonPublic: true) as MapConfig<S, T>;
        Assert.NotNull(c);
        var back = c!
            .Map(t => t.Total, s => s.Count)
            .Map(t => t.Full, s => s.Name)
            .Ignore(t => t.Full)
            .IgnoreSource(s => s.Name)
            .Value(t => t.Total, 7)
            .MapOr(t => t.Total, s => s.Count, 0);
        Assert.Same(c, back);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test C:\Users\Jouda\RiderProjects\DwarfMapper.NET\tests\DwarfMapper.IntegrationTests\DwarfMapper.IntegrationTests.csproj -c Debug --nologo`
Expected: FAIL — `MapConfig<,>` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation** (`src/DwarfMapper/MapConfig.cs`)

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// A compile-time-only configuration surface for the <c>(TSource → TTarget)</c> map. Declare a method on a
/// <c>[DwarfMapper]</c> class taking a single <see cref="MapConfig{TSource,TTarget}"/> parameter and configure
/// members with type-safe selector lambdas; the DwarfMapper generator reads the method's body <b>syntactically
/// and never executes it</b>, so this stays reflection-free and AOT-safe. Method arguments must be member-access
/// selector lambdas (<c>t =&gt; t.A.B</c>) or method groups (<c>Convert</c>); inline converter/factory lambda
/// bodies are not supported in this version.
/// </summary>
/// <typeparam name="TSource">Source type of the configured pair.</typeparam>
/// <typeparam name="TTarget">Destination type of the configured pair.</typeparam>
public sealed class MapConfig<TSource, TTarget>
{
    private MapConfig() { }

    /// <summary>Maps <paramref name="source"/> (a member or dotted flatten path) to <paramref name="target"/>.</summary>
    public MapConfig<TSource, TTarget> Map<TMember>(Func<TTarget, TMember> target, Func<TSource, TMember> source) => this;

    /// <summary>Maps with a converter method group: source member type → target member type.</summary>
    public MapConfig<TSource, TTarget> Map<TSrcMember, TTgtMember>(
        Func<TTarget, TTgtMember> target, Func<TSource, TSrcMember> source, Func<TSrcMember, TTgtMember> convert) => this;

    /// <summary>Assigns only when <paramref name="when"/> (a predicate method group over the source) is true.</summary>
    public MapConfig<TSource, TTarget> MapWhen<TMember>(
        Func<TTarget, TMember> target, Func<TSource, TMember> source, Func<TSource, bool> when) => this;

    /// <summary>Null-substitute for a value-type member: emitted as <c>source ?? fallback</c>.</summary>
    public MapConfig<TSource, TTarget> MapOr<TMember>(
        Func<TTarget, TMember> target, Func<TSource, TMember> source, TMember fallback) where TMember : struct => this;

    /// <summary>Null-substitute for a reference-type member: emitted as <c>source ?? fallback</c>.</summary>
    public MapConfig<TSource, TTarget> MapOr<TMember>(
        Func<TTarget, TMember?> target, Func<TSource, TMember?> source, TMember fallback) where TMember : class => this;

    /// <summary>Suppresses completeness (DWARF001) for a destination member.</summary>
    public MapConfig<TSource, TTarget> Ignore<TMember>(Func<TTarget, TMember> target) => this;

    /// <summary>Suppresses source-coverage suggestion (DWARF039) for a source member.</summary>
    public MapConfig<TSource, TTarget> IgnoreSource<TMember>(Func<TSource, TMember> source) => this;

    /// <summary>Assigns a constant to a source-less destination member.</summary>
    public MapConfig<TSource, TTarget> Value<TMember>(Func<TTarget, TMember> target, TMember value) => this;

    /// <summary>Assigns a destination member from a parameterless method group.</summary>
    public MapConfig<TSource, TTarget> Value<TMember>(Func<TTarget, TMember> target, Func<TMember> compute) => this;

    /// <summary>Constructs the destination via a factory method group: <c>TSource → TTarget</c>.</summary>
    public MapConfig<TSource, TTarget> Construct(Func<TSource, TTarget> factory) => this;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test C:\Users\Jouda\RiderProjects\DwarfMapper.NET\tests\DwarfMapper.IntegrationTests\DwarfMapper.IntegrationTests.csproj -c Debug --nologo --filter MapConfigRuntimeTests`
Expected: PASS.

Note: `MapOr(t => t.Total, s => s.Count, 0)` in Step 1 binds the `struct` overload (`int`). If overload resolution complains about the `class` overload's `TMember?` in `Value`/`MapOr` under nullable context, add `where TMember : notnull` is NOT needed — keep the two constrained overloads; they are unambiguous by the `struct`/`class` constraint.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper/MapConfig.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig<S,T> compile-time-only fluent configuration surface

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Register `MapConfig` in KnownNames + DWARF068/069 descriptors

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/KnownNames.cs`
- Modify: `src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs`
- Modify: `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`

**Interfaces:**
- Produces: `KnownNames.MapConfig = "MapConfig"`, `KnownNames.MapConfigMetadata = "DwarfMapper.MapConfig`2"`; `DiagnosticDescriptors.MapConfigUnsupportedExpression` (DWARF068, Error), `DiagnosticDescriptors.MapConfigConflict` (DWARF069, Error).

- [ ] **Step 1: Add the KnownNames constants**

In `KnownNames.cs`, after the `DwarfRequiresMap` simple-name line, add:

```csharp
    public const string MapConfig = "MapConfig";
    // Generic arity-2 metadata name for compilation.GetTypeByMetadataName lookups.
    public const string MapConfigMetadata = "DwarfMapper.MapConfig`2";
```

- [ ] **Step 2: Add the two descriptors**

In `DiagnosticDescriptors.cs`, mirror the existing descriptor style (id, title, message format, category `"DwarfMapper"`, `DiagnosticSeverity.Error`, `isEnabledByDefault: true`). Add:

```csharp
public static readonly DiagnosticDescriptor MapConfigUnsupportedExpression = new(
    id: "DWARF068",
    title: "Unsupported MapConfig expression",
    messageFormat: "MapConfig {0}: expected a member-access selector (t => t.A.B) or a method group, but found '{1}'",
    category: "DwarfMapper",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor MapConfigConflict = new(
    id: "DWARF069",
    title: "Conflicting member configuration",
    messageFormat: "Destination member '{0}' of {1} is configured more than once (MapConfig and/or attribute); remove one",
    category: "DwarfMapper",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

- [ ] **Step 3: Add the AnalyzerReleases rows**

In `AnalyzerReleases.Unshipped.md`, under the existing table (the one listing DWARF064–067), add two rows with the exact column format used there (`Rule ID | Category | Severity | Notes`):

```
DWARF068 | DwarfMapper | Error | MapConfigUnsupportedExpression
DWARF069 | DwarfMapper | Error | MapConfigConflict
```

- [ ] **Step 4: Build the generator to verify it compiles**

Run: `dotnet build C:\Users\Jouda\RiderProjects\DwarfMapper.NET\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj -c Debug --nologo -v quiet`
Expected: 0 errors. (Descriptors are not yet referenced in the pipeline; Scan2 self-validation is checked later in Task 15, so do not run the full suite yet.)

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/KnownNames.cs src/DwarfMapper.Generator/Diagnostics/DiagnosticDescriptors.cs src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md
git commit -m "feat: register MapConfig name + DWARF068/069 descriptors

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Collector skeleton + `.Map` rename → PairProp (differential parity)

**Files:**
- Create: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs:133-136` (merge collector output)
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Consumes: private nested types `PairProp`, `PairIgnore`, `PairValue`, `PairConstructor` from `MapperExtractor.cs`; `KnownNames.MapConfigMetadata`.
- Produces: `private static MapConfigResult ReadMapConfig(INamedTypeSymbol classSymbol, Compilation compilation, SemanticModelCache models, List<Diagnostic> diagnostics)` where `MapConfigResult` bundles `List<PairProp> Props`, `List<PairIgnore> Ignores`, `List<PairValue> Values`, `List<PairConstructor> Constructors`, `List<string> IgnoreSources`. Also the syntactic helpers `TryReadMemberPath`, `TryReadMethodGroup`.

- [ ] **Step 1: Write the failing test**

Add to `MapConfigRuntimeTests.cs`. This uses the same `GeneratorTestHarness` differential pattern used elsewhere (compile a mapper authored with `MapConfig`, compile an equivalent authored with the attribute, run both over the same object, assert equal). Follow the existing harness usage in `tests/DwarfMapper.IntegrationTests` — read one sibling test (e.g. `WrapperMapRuntimeTests.cs`) for the exact `GeneratorTestHarness.EmitAssembly`/invoke idiom, then:

```csharp
[Fact]
public void MapConfig_Map_rename_matches_attribute_form()
{
    const string configSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public int MessagesCount { get; set; } }
          public class T { public int NumberOfMessages { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.NumberOfMessages, s => s.MessagesCount);
          }
        }
        """;
    const string attrSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public int MessagesCount { get; set; } }
          public class T { public int NumberOfMessages { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          [MapProperty<S, T>("MessagesCount", "NumberOfMessages")]
          public partial class M { }
        }
        """;
    // Helper (add to this test file): compiles src, maps a S{MessagesCount=42}, returns T.NumberOfMessages.
    Assert.Equal(MapAndReadInt(attrSrc, 42), MapAndReadInt(configSrc, 42));
    Assert.Equal(42, MapAndReadInt(configSrc, 42));
}
```

Write `MapAndReadInt(string src, int input)` in the test file using `GeneratorTestHarness.EmitAssembly(src)` to build the assembly, reflectively construct `X.M`, invoke its generated `Map(S)` (or the pair entry point the harness exposes), set `S.MessagesCount = input`, and return `T.NumberOfMessages`. Model it exactly on the reflection helper already present in a sibling IntegrationTests file.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ...\DwarfMapper.IntegrationTests.csproj -c Debug --nologo --filter MapConfig_Map_rename_matches_attribute_form`
Expected: FAIL — the config mapper leaves `NumberOfMessages` at 0 (config not yet read) while the attribute mapper yields 42, so the equality assertion fails.

- [ ] **Step 3: Write the collector + syntactic helpers** (`MapperExtractor.MapConfig.cs`)

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal partial class MapperExtractor
{
    internal sealed class MapConfigResult
    {
        public readonly List<PairProp> Props = new();
        public readonly List<PairIgnore> Ignores = new();
        public readonly List<PairValue> Values = new();
        public readonly List<PairConstructor> Constructors = new();
        public readonly List<string> IgnoreSources = new();
    }

    /// <summary>Dotted member path from a selector lambda `p => p.A.B`, or null when it is not a pure
    /// member-access chain rooted at the lambda's single parameter.</summary>
    private static string? TryReadMemberPath(ExpressionSyntax arg)
    {
        if (arg is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } lambda)
            return null;
        var param = lambda.Parameter.Identifier.ValueText;
        var parts = new List<string>();
        var cur = body;
        while (cur is MemberAccessExpressionSyntax ma)
        {
            parts.Add(ma.Name.Identifier.ValueText);
            cur = ma.Expression;
        }
        if (cur is IdentifierNameSyntax id && id.Identifier.ValueText == param)
        {
            parts.Reverse();
            return string.Join(".", parts);
        }
        return null;
    }

    /// <summary>Simple method name from a method-group argument (`Convert` or `Type.Convert`/`this.Convert`),
    /// or null when the argument is not a method group (e.g. an inline lambda).</summary>
    private static string? TryReadMethodGroup(ExpressionSyntax arg) => arg switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => null,
    };

    private static MapConfigResult ReadMapConfig(
        INamedTypeSymbol classSymbol, Compilation compilation, List<Diagnostic> diagnostics)
    {
        var result = new MapConfigResult();
        var mapConfigDef = compilation.GetTypeByMetadataName(KnownNames.MapConfigMetadata);
        if (mapConfigDef is null)
            return result; // runtime library not referenced; nothing to do

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1) continue;
            if (method.Parameters[0].Type is not INamedTypeSymbol pt) continue;
            if (!SymbolEqualityComparer.Default.Equals(pt.OriginalDefinition, mapConfigDef)) continue;
            if (pt.TypeArguments.Length != 2) continue;

            var src = pt.TypeArguments[0];
            var tgt = pt.TypeArguments[1];
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is not MethodDeclarationSyntax decl) continue;

            foreach (var inv in decl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
                var op = ma.Name.Identifier.ValueText;
                var args = inv.ArgumentList.Arguments;
                var loc = LocationInfo.From(inv.GetLocation());
                if (op == "Map" && args.Count >= 2)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    var srcPath = TryReadMemberPath(args[1].Expression);
                    if (tgtPath is null || srcPath is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "Map", inv.ToString()));
                        continue;
                    }
                    string? use = null;
                    if (args.Count == 3)
                    {
                        use = TryReadMethodGroup(args[2].Expression);
                        if (use is null)
                        {
                            diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                                inv.GetLocation(), "Map converter", args[2].Expression.ToString()));
                            continue;
                        }
                    }
                    result.Props.Add(new PairProp
                    {
                        Source = src, Target = tgt, SrcMember = srcPath, TgtMember = tgtPath, Use = use, Loc = loc,
                    });
                }
            }
        }
        return result;
    }
}
```

If `MapperExtractor.cs` declares `class MapperExtractor` (not `partial`), add the `partial` keyword to its declaration in this step (one-word edit). If it is `static`, make both declarations `static partial`.

- [ ] **Step 4: Merge collector output in `MapperExtractor.cs`**

At `MapperExtractor.cs:133-136`, immediately after `var pairConstructors = ReadPairConstructors(classSymbol);`, add:

```csharp
        // Type-safe alternative front-end: MapConfig<S,T> convention methods, read syntactically (never executed).
        var mapConfig = ReadMapConfig(classSymbol, compilation, diagnostics);
        pairProps.AddRange(mapConfig.Props);
        pairIgnores.AddRange(mapConfig.Ignores);
        pairValues.AddRange(mapConfig.Values);
        pairConstructors.AddRange(mapConfig.Constructors);
        classIgnoreSources.AddRange(mapConfig.IgnoreSources);
```

Confirm `compilation` and `diagnostics` are in scope at this point (they are used a few lines above/below); if the local list is named differently (e.g. `diags`), use that name.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test ...\DwarfMapper.IntegrationTests.csproj -c Debug --nologo --filter MapConfig_Map_rename_matches_attribute_form`
Expected: PASS.

- [ ] **Step 6: Run the full suite (guard against regressions)**

Run: `dotnet test C:\Users\Jouda\RiderProjects\DwarfMapper.NET\DwarfMapper.NET.sln -c Debug --nologo`
Expected: 0 failures.

- [ ] **Step 7: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig collector + .Map rename → PairProp (parity with attribute)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `.Map` flatten (dotted source path)

**Files:**
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Consumes: `ReadMapConfig` from Task 3 (already emits the dotted source path via `TryReadMemberPath`).

Flatten needs no new collector code — `TryReadMemberPath` already returns `"Author.Name"` for `s => s.Author.Name`, and the downstream flatten pipeline consumes a dotted `SrcMember` exactly as `[MapProperty("Author.Name","X")]` does. This task only proves it.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MapConfig_Map_flatten_dotted_source_matches_attribute()
{
    const string configSrc = """
        using DwarfMapper;
        namespace X {
          public class Author { public string? Name { get; set; } }
          public class S { public Author Author { get; set; } = new(); }
          public class T { public string? AuthorName { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.AuthorName, s => s.Author.Name);
          }
        }
        """;
    // MapAndReadString: set S.Author.Name = "dwarf", return T.AuthorName.
    Assert.Equal("dwarf", MapAndReadString(configSrc, "dwarf"));
}
```

Write `MapAndReadString(string src, string input)` in the test file mirroring `MapAndReadInt`, driving the nested `Author.Name`.

- [ ] **Step 2: Run to verify it fails, then passes**

Run the `--filter MapConfig_Map_flatten_dotted_source_matches_attribute` test. Expected: it PASSES immediately (the mechanism already supports it). If it fails, `TryReadMemberPath` is mishandling the nested chain — fix the chain-walk order in `MapperExtractor.MapConfig.cs`.

- [ ] **Step 3: Commit**

```bash
git add tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "test: MapConfig .Map flatten (dotted source path) parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: `.Map(target, source, convert)` converter method group → Use

**Files:**
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Consumes: the 3-arg `Map` branch already emitted in Task 3 (`use = TryReadMethodGroup(args[2])`).

- [ ] **Step 1: Write the failing/passing test**

```csharp
[Fact]
public void MapConfig_Map_with_converter_method_group_matches_attribute()
{
    const string configSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public string? Raw { get; set; } }
          public class T { public string? Clean { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.Clean, s => s.Raw, Norm);
            private static string? Norm(string? v) => v?.Trim();
          }
        }
        """;
    Assert.Equal("dwarf", MapAndReadString(configSrc, "  dwarf  "));
}
```

- [ ] **Step 2: Run to verify PASS** (the converter path was implemented in Task 3). If the generated code fails to resolve `Norm`, confirm the existing `Use` resolution (DWARF041) treats a class-declared static method as valid — it does for `[MapProperty(Use=)]`, and the config path produces the same `PairProp.Use`.

Run: `--filter MapConfig_Map_with_converter_method_group_matches_attribute`. Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "test: MapConfig .Map converter method group → Use parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: `.MapWhen` predicate method group → When

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs` (add the `MapWhen` op)
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Produces: `PairProp.When` set from the 3rd arg method group.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MapConfig_MapWhen_predicate_matches_attribute()
{
    const string configSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public int V { get; set; } public bool Ok { get; set; } }
          public class T { public int V { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.MapWhen(t => t.V, s => s.V, Gate);
            private static bool Gate(S s) => s.Ok;
          }
        }
        """;
    Assert.Equal(0, MapAndReadIntGated(configSrc, value: 9, ok: false));  // guarded out → default 0
    Assert.Equal(9, MapAndReadIntGated(configSrc, value: 9, ok: true));
}
```

Add `MapAndReadIntGated(string src, int value, bool ok)` mirroring `MapAndReadInt` but also setting `S.Ok`.

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `MapWhen` op not handled, so the assignment is unconditional (returns 9 even when `ok:false`).

- [ ] **Step 3: Add the `MapWhen` branch**

In `ReadMapConfig`, after the `if (op == "Map" …)` block, add:

```csharp
                else if (op == "MapWhen" && args.Count == 3)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    var srcPath = TryReadMemberPath(args[1].Expression);
                    var when = TryReadMethodGroup(args[2].Expression);
                    if (tgtPath is null || srcPath is null || when is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "MapWhen", inv.ToString()));
                        continue;
                    }
                    result.Props.Add(new PairProp
                    {
                        Source = src, Target = tgt, SrcMember = srcPath, TgtMember = tgtPath, When = when, Loc = loc,
                    });
                }
```

- [ ] **Step 4: Run to verify PASS + full suite**

Run: `--filter MapConfig_MapWhen_predicate_matches_attribute`, then the full solution suite. Expected: PASS / 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig .MapWhen predicate → When parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: `.MapOr` null-substitute → PairProp.NullSub (constant bridge)

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` (refactor `TryFormatConstant` to add a `(object? value, ITypeSymbol valueType)` overload; add `string? NullSubLiteral` to `PairProp`; branch at the NullSub format site `:2337`)
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs` (add the `MapOr` op)
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Produces: `PairProp.NullSubLiteral` (rendered C# literal, non-null when a config `MapOr` supplied a constant). Consumes: the existing NullSub emit path at `MapperExtractor.cs:2337`.

Rationale: the attribute path stores a Roslyn `TypedConstant`; the config path only has a syntax expression. `TypedConstant` has no public constructor, so the config path pre-renders the literal string and stores it on the new `PairProp.NullSubLiteral`. Assignability is already guaranteed by the compiler (`MapOr<TMember>` forces the fallback to the member type), so no DWARF049 re-check is needed for the config path.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MapConfig_MapOr_null_substitute_matches_attribute()
{
    const string configSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public int? V { get; set; } }
          public class T { public int V { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.MapOr(t => t.V, s => s.V, 5);
          }
        }
        """;
    Assert.Equal(5, MapAndReadNullableInt(configSrc, input: null));  // null → fallback 5
    Assert.Equal(9, MapAndReadNullableInt(configSrc, input: 9));
}
```

Note: the `struct` `MapOr` overload requires `Func<TTarget,TMember>` / `Func<TSource,TMember>` with `TMember = int`; the source is `int?`. Because the `struct` overload's `source` is `Func<TSource,TMember>` (non-nullable `int`), and `s.V` is `int?`, the selector `s => s.V` will not bind. Adjust the `struct` overload in `MapConfig.cs` to `Func<TSource, TMember?> source` (i.e. `Func<TSource, int?>`), keeping `TMember fallback` non-null. Make this one-line signature change in this step and re-run Task 1's compile test to confirm it still passes.

- [ ] **Step 2: Run to verify it fails**

Expected: FAIL — `MapOr` op not handled; `T.V` stays 0 for null input.

- [ ] **Step 3: Add the `NullSubLiteral` field + format overload**

In `MapperExtractor.cs`, add to `PairProp` (after `public TypedConstant NullSub;`):

```csharp
        public string? NullSubLiteral;   // pre-rendered literal when the null-substitute came from MapConfig
```

Refactor `TryFormatConstant` so the primitive/enum/null rendering is reusable from a `(value,type)` pair. Extract the body after the `TypedConstant` is decomposed into a private helper, and add:

```csharp
    private static bool TryFormatConstantValue(
        object? value, ITypeSymbol? valueType, ITypeSymbol targetType, Compilation compilation,
        out string literal, out string why)
    {
        literal = ""; why = "";
        if (value is null)
        {
            literal = "null";
            return true;
        }
        if (valueType is { TypeKind: TypeKind.Enum })
        {
            var enumFqn = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            literal = $"({enumFqn})({SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false)})";
            return true;
        }
        literal = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
        return true;
    }
```

(The existing `TryFormatConstant(TypedConstant,…)` may delegate to this for the non-error, non-array cases; a minimal approach leaves it as-is and only adds the new method for the config path. Assignability is compiler-guaranteed for the config path, so `why` is unused there.)

At the NullSub emit site `MapperExtractor.cs:2337`, branch so a pre-rendered literal wins:

```csharp
                        string lit; string why;
                        bool ok = ex.NullSubLiteral is not null
                            ? (lit = ex.NullSubLiteral!) is { } && (why = "") is { }
                            : TryFormatConstant(ex.NullSub, tgtType, compilation, out lit, out why);
                        if (!ok) { /* existing DWARF049 diagnostic path unchanged */ }
```

Where `ex` exposes `NullSubLiteral`: extend the `mapPropertyExtras` tuple (`MapperExtractor.cs:2161,2165-2168`) to carry `string? NullSubLiteral` alongside `HasNullSub`/`NullSub`/`When`, and populate it from `PairProp.NullSubLiteral` wherever that tuple is built from a `PairProp`. Grep for `HasNullSub, NullSub, When` to find the 1–2 construction sites and thread the extra field through.

- [ ] **Step 4: Add the `MapOr` collector branch**

In `ReadMapConfig`, add after the `MapWhen` branch:

```csharp
                else if (op == "MapOr" && args.Count == 3)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    var srcPath = TryReadMemberPath(args[1].Expression);
                    if (tgtPath is null || srcPath is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "MapOr", inv.ToString()));
                        continue;
                    }
                    var model = compilation.GetSemanticModel(inv.SyntaxTree);
                    var cv = model.GetConstantValue(args[2].Expression);
                    if (!cv.HasValue)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "MapOr fallback", args[2].Expression.ToString()));
                        continue;
                    }
                    var vt = model.GetTypeInfo(args[2].Expression).Type;
                    TryFormatConstantValue(cv.Value, vt, tgt, compilation, out var litText, out _);
                    result.Props.Add(new PairProp
                    {
                        Source = src, Target = tgt, SrcMember = srcPath, TgtMember = tgtPath,
                        HasNullSub = true, NullSubLiteral = litText, Loc = loc,
                    });
                }
```

- [ ] **Step 5: Run to verify PASS + full suite**

Run: `--filter MapConfig_MapOr_null_substitute_matches_attribute`, then full solution suite. Expected: PASS / 0 failures.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig .MapOr null-substitute via pre-rendered literal bridge

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: `.Ignore` → PairIgnore (completeness)

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs`
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Produces: `PairIgnore { Target, Member, Loc }` entries.

- [ ] **Step 1: Write the failing test** — a target with an extra member that has no source; without ignore it is DWARF001 (compile error), with `.Ignore` it compiles and the member stays default.

```csharp
[Fact]
public void MapConfig_Ignore_suppresses_completeness()
{
    const string src = """
        using DwarfMapper;
        namespace X {
          public class S { public int A { get; set; } }
          public class T { public int A { get; set; } public int Extra { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Ignore(t => t.Extra);
          }
        }
        """;
    // AssertCompilesNoError: emits and asserts no DWARF001; then map S{A=3} → T.A==3, T.Extra==0.
    var (a, extra) = MapAndReadTwoInts(src, aInput: 3);
    Assert.Equal(3, a);
    Assert.Equal(0, extra);
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL with DWARF001 (Extra unmapped) because `.Ignore` is not yet handled.

- [ ] **Step 3: Add the `Ignore` branch**

```csharp
                else if (op == "Ignore" && args.Count == 1)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    if (tgtPath is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "Ignore", inv.ToString()));
                        continue;
                    }
                    result.Ignores.Add(new PairIgnore { Target = tgt, Member = tgtPath, Loc = loc });
                }
```

- [ ] **Step 4: Run to verify PASS + full suite.** Expected: PASS / 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig .Ignore → PairIgnore parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: `.IgnoreSource` → classIgnoreSources

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs`
- Test: `tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs`

**Interfaces:**
- Produces: source member names appended to `MapConfigResult.IgnoreSources` → merged into `classIgnoreSources`.

- [ ] **Step 1: Write the failing test** (a generator test asserting no DWARF039 when `RequiredMapping = Both` and the unread source member is ignored via config). Create `MapConfigGeneratorTests.cs`, modeled on an existing diagnostics generator test (`GenericAndFlattenDiagnosticsGeneratorTests.cs`) for the compile-and-collect-diagnostics idiom.

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using Xunit;
namespace DwarfMapper.Generator.Tests;

public class MapConfigGeneratorTests
{
    [Fact]
    public void IgnoreSource_via_config_suppresses_DWARF039()
    {
        const string src = """
            using DwarfMapper;
            namespace X {
              public class S { public int A { get; set; } public int Unused { get; set; } }
              public class T { public int A { get; set; } }
              [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
              [GenerateMap<S, T>]
              public partial class M {
                private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.A, s => s.A).IgnoreSource(s => s.Unused);
              }
            }
            """;
        var diags = TestHelper.GetDiagnostics(src);   // use the project's existing diagnostic-collecting helper
        Assert.DoesNotContain(diags, d => d.Id == "DWARF039");
    }
}
```

Replace `TestHelper.GetDiagnostics` with the actual helper name used in sibling generator tests.

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL — DWARF039 present because `.IgnoreSource` is not yet collected.

- [ ] **Step 3: Add the `IgnoreSource` branch**

```csharp
                else if (op == "IgnoreSource" && args.Count == 1)
                {
                    var srcPath = TryReadMemberPath(args[0].Expression);
                    if (srcPath is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "IgnoreSource", inv.ToString()));
                        continue;
                    }
                    result.IgnoreSources.Add(srcPath);
                }
```

- [ ] **Step 4: Run to verify PASS + full suite.** Expected: PASS / 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs
git commit -m "feat: MapConfig .IgnoreSource → source-coverage ignore parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: `.Value` constant + `.Value` compute (method group) → PairValue

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` (add `string? ConstLiteral` to `PairValue`; branch at value format site `:2432`)
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs`
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Produces: `PairValue` with either `Use` (compute method group) or `ConstLiteral` (pre-rendered constant).

- [ ] **Step 1: Write the failing tests** (both overloads, plus parity vs `[MapValue]`).

```csharp
[Fact]
public void MapConfig_Value_constant_and_compute_match_attribute()
{
    const string constSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public int A { get; set; } }
          public class T { public int A { get; set; } public int Tag { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Value(t => t.Tag, 99);
          }
        }
        """;
    var (_, tag) = MapAndReadTwoInts(constSrc, aInput: 1);
    Assert.Equal(99, tag);

    const string computeSrc = """
        using DwarfMapper;
        namespace X {
          public class S { public int A { get; set; } }
          public class T { public int A { get; set; } public int Tag { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Value(t => t.Tag, Make);
            private static int Make() => 7;
          }
        }
        """;
    var (_, tag2) = MapAndReadTwoInts(computeSrc, aInput: 1);
    Assert.Equal(7, tag2);
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL — `Value` op not handled; `Tag` stays 0.

- [ ] **Step 3: Add `ConstLiteral` to `PairValue` + format branch**

In `PairValue` (`MapperExtractor.cs:5607`), add:

```csharp
        public string? ConstLiteral;   // pre-rendered constant literal when the value came from MapConfig
```

At the `MapValue` format site `MapperExtractor.cs:2432`, branch so a pre-rendered literal wins over `TryFormatConstant(mv.Value,…)`:

```csharp
                if (mv.ConstLiteral is not null) { literal = mv.ConstLiteral; }
                else if (!TryFormatConstant(mv.Value, mvTgtType, compilation, out literal, out var why)) { /* unchanged DWARF040 path */ }
```

Ensure `MatchPairValues` (`MapperExtractor.cs:5654`) carries `ConstLiteral` through its returned tuple — extend the tuple `(string Target, bool IsConstant, TypedConstant Value, string? Use)` to `(…, string? ConstLiteral)` and populate it, updating the one consumer at `:2432`.

- [ ] **Step 4: Add the `Value` collector branch**

```csharp
                else if (op == "Value" && args.Count == 2)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    if (tgtPath is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "Value", inv.ToString()));
                        continue;
                    }
                    var compute = TryReadMethodGroup(args[1].Expression);
                    if (compute is not null)
                    {
                        result.Values.Add(new PairValue { Target = tgt, Member = tgtPath, Use = compute, Loc = loc });
                    }
                    else
                    {
                        var model = compilation.GetSemanticModel(inv.SyntaxTree);
                        var cv = model.GetConstantValue(args[1].Expression);
                        if (!cv.HasValue)
                        {
                            diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                                inv.GetLocation(), "Value", args[1].Expression.ToString()));
                            continue;
                        }
                        var vt = model.GetTypeInfo(args[1].Expression).Type;
                        TryFormatConstantValue(cv.Value, vt, tgt, compilation, out var litText, out _);
                        result.Values.Add(new PairValue { Target = tgt, Member = tgtPath, IsConstant = true, ConstLiteral = litText, Loc = loc });
                    }
                }
```

Distinguishing overloads: a method group (`Make`) reads as `IdentifierNameSyntax` → `TryReadMethodGroup` non-null → compute; a literal (`99`) → `GetConstantValue` → constant. This matches the compiler's own overload choice.

- [ ] **Step 5: Run to verify PASS + full suite.** Expected: PASS / 0 failures.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig .Value constant + compute → PairValue parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: `.Construct` factory method group → PairConstructor

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs`
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`

**Interfaces:**
- Produces: `PairConstructor { Source, Target, Method, Loc }`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MapConfig_Construct_factory_matches_attribute()
{
    const string src = """
        using DwarfMapper;
        namespace X {
          public class S { public string Fmt { get; set; } = ""; public string Name { get; set; } = ""; }
          public class Alias { public Alias(string fmt, string name) { Fmt = fmt; Name = name; } public string Fmt { get; } public string Name { get; } }
          [DwarfMapper]
          [GenerateMap<S, Alias>]
          public partial class M {
            private static void Cfg(MapConfig<S, Alias> c) => c.Construct(Make);
            private static Alias Make(S s) => new(s.Fmt, s.Name);
          }
        }
        """;
    // MapAndReadAlias: S{Fmt="f",Name="n"} → returns (Fmt,Name) of the built Alias.
    var (fmt, name) = MapAndReadAlias(src, "f", "n");
    Assert.Equal(("f", "n"), (fmt, name));
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL — `Construct` not handled; the generator falls back to constructor selection and errors (Alias has no parameterless/settable path) or produces the wrong result.

- [ ] **Step 3: Add the `Construct` branch**

```csharp
                else if (op == "Construct" && args.Count == 1)
                {
                    var factory = TryReadMethodGroup(args[0].Expression);
                    if (factory is null)
                    {
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigUnsupportedExpression,
                            inv.GetLocation(), "Construct", args[0].Expression.ToString()));
                        continue;
                    }
                    result.Constructors.Add(new PairConstructor { Source = src, Target = tgt, Method = factory, Loc = loc });
                }
```

- [ ] **Step 4: Run to verify PASS + full suite.** Expected: PASS / 0 failures.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs
git commit -m "feat: MapConfig .Construct factory → PairConstructor parity

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: DWARF068 — unsupported selector / non-method-group (diagnostics)

**Files:**
- Test: `tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs`

The emit sites already raise DWARF068 (added incrementally in Tasks 3–11). This task locks the behavior with explicit diagnostic tests.

- [ ] **Step 1: Write the failing/passing tests**

```csharp
[Fact]
public void Selector_that_is_not_member_access_is_DWARF068()
{
    const string src = """
        using DwarfMapper;
        namespace X {
          public class S { public int A { get; set; } }
          public class T { public int A { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static int Id(int x) => x;
            private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.A, s => Id(s.A));
          }
        }
        """;
    Assert.Contains(TestHelper.GetDiagnostics(src), d => d.Id == "DWARF068");
}

[Fact]
public void Inline_converter_lambda_is_DWARF068()
{
    const string src = """
        using DwarfMapper;
        namespace X {
          public class S { public string? A { get; set; } }
          public class T { public string? A { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.A, s => s.A, v => v);
          }
        }
        """;
    Assert.Contains(TestHelper.GetDiagnostics(src), d => d.Id == "DWARF068");
}
```

- [ ] **Step 2: Run to verify PASS** (diagnostics already emitted). If either fails, ensure the corresponding op branch raises DWARF068 on the null-selector / null-method-group path (Tasks 3 & 5).

- [ ] **Step 3: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs
git commit -m "test: DWARF068 for unsupported MapConfig selectors / inline lambdas

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: DWARF069 — conflicting configuration (attribute↔config, and duplicate config)

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs` (conflict detection over merged config)
- Test: `tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs`

**Interfaces:**
- Consumes: the attribute-derived `pairProps`/`pairValues`/`pairIgnores` and the config-derived `MapConfigResult`. Detection runs after merge in `MapperExtractor.cs`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Same_target_member_in_attribute_and_config_is_DWARF069()
{
    const string src = """
        using DwarfMapper;
        namespace X {
          public class S { public int A { get; set; } public int B { get; set; } }
          public class T { public int A { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          [MapProperty<S, T>("A", "A")]
          public partial class M {
            private static void Cfg(MapConfig<S, T> c) => c.Map(t => t.A, s => s.B);  // conflict on target A
          }
        }
        """;
    Assert.Contains(TestHelper.GetDiagnostics(src), d => d.Id == "DWARF069");
}
```

- [ ] **Step 2: Run to verify it fails** — Expected: FAIL — no conflict detection yet (the two just both land in `pairProps`).

- [ ] **Step 3: Add conflict detection**

In `MapperExtractor.cs`, immediately after the merge block from Task 3, add a check over `pairProps`+`pairValues` grouping by `(Source?, Target, TgtMember/Member)` per pair; when a `(Target type, member)` appears from more than one origin (or twice), emit DWARF069 at the config `Loc`. Implement as a helper in `MapperExtractor.MapConfig.cs`:

```csharp
    private static void ReportMapConfigConflicts(
        List<PairProp> props, List<PairValue> values, List<Diagnostic> diagnostics)
    {
        var seen = new Dictionary<string, PairProp>();
        foreach (var p in props)
        {
            var key = p.Target.ToDisplayString() + "::" + p.TgtMember;
            if (seen.ContainsKey(key))
            {
                var loc = (p.Loc ?? seen[key].Loc)?.ToLocation() ?? Location.None;
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigConflict, loc,
                    p.TgtMember, p.Target.ToDisplayString()));
            }
            else { seen[key] = p; }
        }
        foreach (var v in values)
        {
            var key = v.Target.ToDisplayString() + "::" + v.Member;
            if (seen.ContainsKey(key))
            {
                var loc = v.Loc?.ToLocation() ?? Location.None;
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MapConfigConflict, loc,
                    v.Member, v.Target.ToDisplayString()));
            }
            else { seen[key] = new PairProp { Target = v.Target, TgtMember = v.Member }; }
        }
    }
```

Call `ReportMapConfigConflicts(pairProps, pairValues, diagnostics);` right after the merge. Use `LocationInfo.ToLocation()` if that is the project's accessor (confirm the method name on `LocationInfo`; grep `LocationInfo` for the syntax→Location round-trip; if it is `GetLocation()` use that).

- [ ] **Step 4: Run to verify PASS + full suite.** Expected: PASS / 0 failures. Also confirm no existing test that legitimately repeats a member across DIFFERENT pairs now falsely trips — the key includes the target type, so different pairs to different targets are safe; a pair mapped to the same target from two `[GenerateMap]` sources shares the target member key and would conflict only if both configure it (correct).

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/MapperExtractor.MapConfig.cs src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs
git commit -m "feat: DWARF069 conflicting attribute/config member configuration

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: Full combination matrix — every op × {attribute, config, mixed} + DWARF056 reuse

**Files:**
- Test: `tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs`
- Test: `tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs`

This task delivers the user's explicit requirement: **every combination of lambda-config and attribute**.

- [ ] **Step 1: Write the combination-matrix parity test**

Add one parameterized xUnit theory that, for each of the 9 operations, builds three mappers — attribute-only, config-only, and mixed (this op via config while another member uses the attribute) — and asserts all three produce identical output over the same input. Encode the matrix as `[Theory]` + `[InlineData]` rows (one per op), each row carrying the attribute snippet, the config snippet, and the mixed snippet, driven through a shared `AssertParity(string attrSrc, string configSrc, string mixedSrc, object input, string readMember)` helper that reflectively maps `input` and reads `readMember` from each result.

```csharp
[Theory]
[InlineData("rename")]
[InlineData("flatten")]
[InlineData("convert")]
[InlineData("when")]
[InlineData("nullsub")]
[InlineData("ignore")]
[InlineData("ignoresource")]
[InlineData("value")]
[InlineData("construct")]
public void Attribute_config_and_mixed_agree(string op)
{
    var (attrSrc, configSrc, mixedSrc, input, read) = MatrixCase(op);   // returns the three sources + fixture
    AssertParity(attrSrc, configSrc, mixedSrc, input, read);
}
```

Implement `MatrixCase(op)` returning the three equivalent sources per op (reuse the snippets from Tasks 3–11) and `AssertParity` (emit all three via `GeneratorTestHarness`, map the same input, compare the read-back member). For `ignore`/`ignoresource` the assertion is "compiles with no DWARF001/DWARF039 and the mapped members agree".

- [ ] **Step 2: Write the DWARF056 reuse test** (config for a pair that is never mapped)

```csharp
[Fact]
public void Config_for_unmapped_pair_is_DWARF056()
{
    const string src = """
        using DwarfMapper;
        namespace X {
          public class S { public int A { get; set; } }
          public class T { public int A { get; set; } }
          public class Other { public int A { get; set; } }
          [DwarfMapper]
          [GenerateMap<S, T>]
          public partial class M {
            private static void Cfg(MapConfig<S, Other> c) => c.Map(t => t.A, s => s.A);  // Other is never mapped
          }
        }
        """;
    Assert.Contains(TestHelper.GetDiagnostics(src), d => d.Id == "DWARF056");
}
```

DWARF056 fires from the existing `Consumed`-flag check because config-derived `PairProp`s carry the same `Consumed` flag and are matched via `MatchPairProps`. If it does not fire, ensure config-derived entries participate in the same end-of-pipeline "matched nothing" scan (they do, being appended to `pairProps`).

- [ ] **Step 3: Run to verify PASS + full suite.** Expected: PASS / 0 failures.

- [ ] **Step 4: Commit**

```bash
git add tests/DwarfMapper.IntegrationTests/MapConfigRuntimeTests.cs tests/DwarfMapper.Generator.Tests/MapConfigGeneratorTests.cs
git commit -m "test: full attribute×config×mixed combination matrix + DWARF056 reuse

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 15: Self-validation (T2/Scan) + diagnostics docs

**Files:**
- Modify: `tests/DwarfMapper.Generator.Tests/FeatureInteractionCompileMatrixTests.cs` (or the `MatrixExemptAttributes` list)
- Modify: `docs/diagnostics.md`

**Interfaces:** none (test/docs only).

- [ ] **Step 1: Run the self-validation tests to see what the new public type / diagnostics trip**

Run: `dotnet test ...\DwarfMapper.Generator.Tests\... --filter "FullyQualifiedName~SelfValidation|FullyQualifiedName~FeatureInteractionCompileMatrix"`
Expected: possible FAIL from T2 (`MapConfig` is a new public type) and/or Scan3 (DWARF068/069 need a test reference — satisfied by Tasks 12–14) and Scan1 (AnalyzerReleases — satisfied by Task 2).

- [ ] **Step 2: Exempt `MapConfig` from the attribute matrix**

`MapConfig` is a configuration *type*, not an attribute, so it does not belong in `FeatureInteractionCompileMatrix`. Add `"MapConfig"` to the `MatrixExemptAttributes` set (grep for `MatrixExemptAttributes` in the test project) with a comment: `// MapConfig is a fluent config surface, not an attribute — exercised by MapConfigGeneratorTests/MapConfigRuntimeTests.`

- [ ] **Step 3: Document DWARF068/069** in `docs/diagnostics.md`

Add two entries mirroring the existing format (id, severity, one-line description, and a short "how to fix"):

```markdown
### DWARF068 — Unsupported MapConfig expression
A `MapConfig<S,T>` argument was not a member-access selector (`t => t.A.B`) or a method group.
Inline converter/factory lambda bodies are not supported; extract a named method and pass it as a method group.

### DWARF069 — Conflicting member configuration
A destination member is configured more than once — by both an attribute and a `MapConfig`, or twice within a
`MapConfig`. Remove one so the mapping is unambiguous.
```

- [ ] **Step 4: Run the full suite.** Expected: 0 failures (all self-validation green).

Run: `dotnet test C:\Users\Jouda\RiderProjects\DwarfMapper.NET\DwarfMapper.NET.sln -c Debug --nologo`

- [ ] **Step 5: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/FeatureInteractionCompileMatrixTests.cs docs/diagnostics.md
git commit -m "test/docs: exempt MapConfig from attribute matrix; document DWARF068/069

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 16: Consumer proof — migrate one FusedChat mapper + verify

**Files:**
- Modify: one FusedChat mapper (a `[MapProperty<…>(nameof(...), nameof(...))]`-heavy one, e.g. the `ChatSessionDocument → ChatHistorySummary` mapper) — DO NOT commit FusedChat changes (under review).

**Interfaces:** none (integration verification).

- [ ] **Step 1: Build Release generator and sync the FusedChat lib**

```bash
dotnet build C:\Users\Jouda\RiderProjects\DwarfMapper.NET\src\DwarfMapper.Generator\DwarfMapper.Generator.csproj -c Release --nologo -v quiet
```
Then copy `src/DwarfMapper.Generator/bin/Release/netstandard2.0/DwarfMapper.Generator.dll` and the built `src/DwarfMapper/bin/Release/net10.0/DwarfMapper.dll` to `C:\Users\Jouda\RiderProjects\MedbotOmega\lib\DwarfMapper\`. (The new `MapConfig` type lives in the runtime `DwarfMapper.dll`, so that assembly MUST be synced too, not just the generator.)

- [ ] **Step 2: Rewrite one mapper to use `MapConfig`**

Pick the `ChatSessionDocument → ChatHistorySummary` mapper and replace its `[MapProperty<…>(nameof(...), nameof(...))]` attributes with a `private static void Cfg(MapConfig<ChatSessionDocument, ChatHistorySummary> c) => c.Map(...)...` method using selector lambdas.

- [ ] **Step 3: Build + test FusedChat**

```bash
dotnet build C:\Users\Jouda\RiderProjects\MedbotOmega\FusedChat.sln -c Debug --nologo -t:Rebuild
dotnet test  C:\Users\Jouda\RiderProjects\MedbotOmega\FusedChat.sln -c Debug --nologo --no-build
```
Expected: 0 build errors; all FusedChat test projects `Úspěšné!`. This proves the feature works end-to-end in a real consumer and that the migrated mapper behaves identically.

- [ ] **Step 4: Revert the FusedChat mapper edit** (the migration set is under review and must not be committed here). Keep only the DwarfMapper.NET repo changes.

```bash
cd C:\Users\Jouda\RiderProjects\MedbotOmega ; git checkout -- <the-mapper-file>
```

- [ ] **Step 5: Final full DwarfMapper suite green**

Run: `dotnet test C:\Users\Jouda\RiderProjects\DwarfMapper.NET\DwarfMapper.NET.sln -c Debug --nologo`
Expected: 0 failures. No commit in this task (verification only).

---

## Self-Review

**Spec coverage:**
- §3 convention method matched by `MapConfig<S,T>` param type → Task 3 (`ReadMapConfig`).
- §4 nine member ops → `.Map` rename (T3), flatten (T4), convert (T5), `.MapWhen` (T6), `.MapOr` (T7), `.Ignore` (T8), `.IgnoreSource` (T9), `.Value` const+compute (T10), `.Construct` (T11).
- §5 runtime API → Task 1 (incl. the `MapOr` struct/class overloads and the `struct` overload's `Func<TSource,TMember?>` source fixed in T7 Step 1).
- §6 same-IR front-end → Task 3 merge + Tasks 6–11 emit the same `PairProp`/`PairIgnore`/`PairValue`/`PairConstructor`/source-ignore structures.
- §6.1/6.2 grammar + validation → `TryReadMemberPath`/`TryReadMethodGroup` (T3), DWARF068 tests (T12).
- §7 diagnostics → DWARF068 (T2 descriptor, T3–T11 emit, T12 tests), DWARF069 (T13), DWARF056 reuse (T14), reused DWARF041/050/059 (covered by parity Tasks 5/6/11 exercising the same resolution).
- §8 coexistence/precedence → DWARF069 (T13), mixed matrix (T14); never-called `private static` documented in Task 15 docs + spec.
- §9 self-validation → Task 15.
- §10 testing (differential oracle, generator unit, combination matrix) → Tasks 3–14.
- §11 scope: inline lambdas rejected → DWARF068 (T12); method-group-only enforced throughout.

**Placeholder scan:** the only deliberately-parameterized bits are the *reflection test helpers* (`MapAndReadInt`, `MapAndReadString`, `MapAndReadNullableInt`, `MapAndReadTwoInts`, `MapAndReadAlias`, `MapAndReadIntGated`, `AssertParity`, `MatrixCase`, and the `TestHelper.GetDiagnostics` name), which must be written against the **actual** `GeneratorTestHarness`/diagnostic-helper idiom already in each test project — Task 3 Step 1 mandates reading a sibling test first to copy the exact idiom. These are test scaffolding, not production placeholders. All production code steps contain complete code.

**Type consistency:** `ReadMapConfig(INamedTypeSymbol, Compilation, List<Diagnostic>)` and `MapConfigResult` fields (`Props`/`Ignores`/`Values`/`Constructors`/`IgnoreSources`) are used consistently in Tasks 3–13. `PairProp`/`PairIgnore`/`PairValue`/`PairConstructor` field names match `MapperExtractor.cs` (`Source`,`Target`,`SrcMember`,`TgtMember`,`Use`,`When`,`HasNullSub`,`NullSub`, + new `NullSubLiteral`; `Member`; `IsConstant`,`Value`,`Use`, + new `ConstLiteral`; `Method`). New IR fields `PairProp.NullSubLiteral` (T7) and `PairValue.ConstLiteral` (T10) are threaded through their tuple/emit sites in the same task that introduces them.

**Open confirmations for the implementer (grep before use, called out inline):** `MapperExtractor` `partial`/`static` modifier (T3 precondition); the `LocationInfo` syntax→`Location` accessor name (T13); the exact `mapPropertyExtras` tuple construction sites (T7) and `MatchPairValues` tuple (T10); the sibling test-harness idiom and diagnostic-collecting helper name (T3/T9).
