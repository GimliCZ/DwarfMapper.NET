# DwarfMapper.NET — Plan 5: Null Handling (nullable value types)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Map a nullable value-type source (`int?`, `MyEnum?`, …) to a non-nullable destination of a compatible type. Today this hits `DWARF005`. Plan 5 unwraps it according to a configurable strategy: **throw on null** (default) or **use default**.

**Architecture:** `MemberMap` gains a `NullHandling` enum (`None`/`ThrowIfNull`/`ValueOrDefault`); `MapEmitter` applies it on the direct-assignment path (`x ?? throw …` or `x.GetValueOrDefault()`). `MapperExtractor.TryResolveConversion` gets a `NullStrategy` and, after the implicit-conversion check fails, detects `Nullable<T>` sources whose `T` implicitly converts to the target, and emits the chosen null handling. Threaded like the enum strategy.

**Tech Stack:** As Plans 1–4.

**Builds on Plan 4.** `MemberMap(TargetName, SourceName, ConverterMethod?)`; `MapEmitter` member loop branches on `ConverterMethod`; `TryResolveConversion(compilation, srcType, tgtType, useMethod, allMethods, autoCandidates, enumStrategy, synthesized, location, targetName, diagnostics, out converterMethod)` does Use → implicit → auto-discover → enum → DWARF005. 60 tests pass.

**Out of scope:** reference-type NRT null guards (depend on consumer nullable context; messier `SetDefault` semantics) → future plan; collections + SIMD → Plan 6; `DwarfMapper.Testing` → Plan 7. Null handling combined with a converter/enum on the SAME member is out of scope (Plan 5 handles the direct nullable-value→value path only).

**Decision (flagged):** default `NullStrategy.Throw` (defensive/on-brand). `SetDefault` opt-in.

**Conventions:** SPDX header on new files; CPM; warnings-as-errors; warning-clean. No new diagnostic in this plan.

## File structure

```
src/DwarfMapper/NullStrategy.cs                                  # create (runtime enum)
src/DwarfMapper/DwarfMapperAttribute.cs                          # modify: add NullStrategy
src/DwarfMapper.Generator/Model/NullHandling.cs                 # create
src/DwarfMapper.Generator/Model/MemberMap.cs                    # modify: add NullHandling
src/DwarfMapper.Generator/Pipeline/MapEmitter.cs               # modify: apply null handling
src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs          # modify: NullStrategy + nullable-value branch
tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs    # modify
tests/DwarfMapper.Generator.Tests/NullHandlingTests.cs         # create
tests/DwarfMapper.IntegrationTests/NullHandlingRuntimeTests.cs # create
README.md                                                       # modify
```

---

## Task 1: `NullStrategy` runtime enum + attribute option

**Files:** `src/DwarfMapper/NullStrategy.cs` (create), `src/DwarfMapper/DwarfMapperAttribute.cs` (modify), `tests/DwarfMapper.Generator.Tests/AttributeContractTests.cs` (modify).

- [ ] **Step 1: Failing test** — append to `AttributeContractTests`:
```csharp
    [Fact]
    public void DwarfMapperAttribute_nullStrategy_defaults_Throw()
    {
        Assert.Equal(NullStrategy.Throw, new DwarfMapperAttribute().NullStrategy);
        Assert.Equal(NullStrategy.SetDefault, new DwarfMapperAttribute { NullStrategy = NullStrategy.SetDefault }.NullStrategy);
    }
```
- [ ] **Step 2: Run → FAIL** (`--filter "FullyQualifiedName~AttributeContractTests"`).
- [ ] **Step 3: Create `src/DwarfMapper/NullStrategy.cs`:**
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper;

/// <summary>How a nullable source value mapped to a non-nullable destination is handled when null.</summary>
public enum NullStrategy
{
    /// <summary>Throw at runtime if the source value is null (default).</summary>
    Throw = 0,

    /// <summary>Use the destination type's default value when the source is null.</summary>
    SetDefault = 1,
}
```
- [ ] **Step 4: Add property** to `DwarfMapperAttribute` (after `EnumStrategy`):
```csharp
    /// <summary>
    /// How a nullable value-type source mapped to a non-nullable destination is
    /// handled when null. Defaults to <see cref="NullStrategy.Throw"/>.
    /// </summary>
    public NullStrategy NullStrategy { get; set; } = NullStrategy.Throw;
```
- [ ] **Step 5: Run → PASS; build solution → 0/0.**
- [ ] **Step 6: Commit** `feat(runtime): NullStrategy + DwarfMapper.NullStrategy option`.

---

## Task 2: Nullable value-type unwrapping

**Files:** `Model/NullHandling.cs` (create), `Model/MemberMap.cs` (modify), `Pipeline/MapEmitter.cs` (modify), `Pipeline/MapperExtractor.cs` (modify), `tests/DwarfMapper.Generator.Tests/NullHandlingTests.cs` (create).

- [ ] **Step 1: Failing tests** — `tests/DwarfMapper.Generator.Tests/NullHandlingTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class NullHandlingTests
{
    [Fact]
    public void NullableValue_to_value_throws_by_default()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("?? throw", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableValue_to_value_uses_default_when_configured()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
            public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("GetValueOrDefault()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_to_nullable_assigns_directly()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int? N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.DoesNotContain("?? throw", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValueOrDefault", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableValue_to_widening_value_works()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public long N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
```
- [ ] **Step 2: Run → FAIL** (`int?`→`int` currently DWARF005; no `?? throw`).
- [ ] **Step 3: Create `src/DwarfMapper.Generator/Model/NullHandling.cs`:**
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>How the emitter unwraps a nullable value-type source for a non-nullable destination.</summary>
public enum NullHandling
{
    /// <summary>Assign directly.</summary>
    None = 0,

    /// <summary>Emit <c>x ?? throw</c>.</summary>
    ThrowIfNull = 1,

    /// <summary>Emit <c>x.GetValueOrDefault()</c>.</summary>
    ValueOrDefault = 2,
}
```
- [ ] **Step 4: Add `NullHandling` to `MemberMap`** (`Model/MemberMap.cs`):
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// One resolved destination&lt;-source member assignment. <see cref="ConverterMethod"/>,
/// when set, transforms the value; otherwise <see cref="NullHandling"/> controls
/// how a nullable value-type source is unwrapped.
/// </summary>
public sealed record MemberMap(
    string TargetName,
    string SourceName,
    string? ConverterMethod = null,
    NullHandling NullHandling = NullHandling.None) : System.IEquatable<MemberMap>;
```
- [ ] **Step 5: Apply null handling in the emitter.** In `MapEmitter.cs`, REPLACE the member loop (lines ~57-70) with:
```csharp
        foreach (var member in method.Members)
        {
            sb.Append(indent).Append("        ").Append(member.TargetName).Append(" = ");
            if (member.ConverterMethod is not null)
            {
                sb.Append(member.ConverterMethod).Append('(')
                  .Append(method.ParameterName).Append('.').Append(member.SourceName).Append(')');
            }
            else
            {
                switch (member.NullHandling)
                {
                    case Model.NullHandling.ThrowIfNull:
                        sb.Append(method.ParameterName).Append('.').Append(member.SourceName)
                          .Append(" ?? throw new global::System.InvalidOperationException(\"Source member '")
                          .Append(member.SourceName).Append("' was null\")");
                        break;
                    case Model.NullHandling.ValueOrDefault:
                        sb.Append(method.ParameterName).Append('.').Append(member.SourceName).Append(".GetValueOrDefault()");
                        break;
                    default:
                        sb.Append(method.ParameterName).Append('.').Append(member.SourceName);
                        break;
                }
            }
            sb.AppendLine(",");
        }
```
(`MapEmitter` is in `DwarfMapper.Generator.Pipeline`; reference the enum as `Model.NullHandling`. Add `using DwarfMapper.Generator.Model;` if not present — it is.)

- [ ] **Step 6: Detect nullable value sources in `MapperExtractor`.**

6a. Add a generator-internal strategy enum to `MapperExtractor.cs` (or a small file). Place at top of the file's namespace, e.g. just below the `using`s:
```csharp
internal enum NullStrategy
{
    Throw = 0,
    SetDefault = 1,
}
```
6b. In `Extract`, after `var enumStrategy = ReadEnumStrategy(ctx.Attributes);` add:
```csharp
        var nullStrategy = ReadNullStrategy(ctx.Attributes);
```
Add the reader near `ReadEnumStrategy`:
```csharp
    private static NullStrategy ReadNullStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "NullStrategy" && named.Value.Value is int i)
                {
                    return (NullStrategy)i;
                }
            }
        }
        return NullStrategy.Throw;
    }
```
6c. Pass `nullStrategy` into the `ResolveMembers` call (append after `synthesized`):
```csharp
            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                enumStrategy, synthesized, nullStrategy);
```
6d. Add `NullStrategy nullStrategy` as the final parameter of `ResolveMembers`. Both `TryResolveConversion` calls must now also produce a `NullHandling`; update them to capture it and pass it into `new MemberMap(...)`. Each call site becomes:
```csharp
            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, tgtName, diagnostics, out var conv, out var nullH))
            {
                result.Add(new MemberMap(tgtName, srcName, conv, nullH));
            }
```
and
```csharp
            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var conv, out var nullH))
            {
                result.Add(new MemberMap(target.Name, source.Name, conv, nullH));
            }
```
6e. Extend `TryResolveConversion`: add `NullStrategy nullStrategy` (after `synthesized`) and `out Model.NullHandling nullHandling`. Initialize `nullHandling = Model.NullHandling.None;` at the top. Insert the nullable-value branch immediately AFTER the `if (HasImplicitConversion(compilation, srcType, tgtType)) { return true; }` block:
```csharp
        if (IsNullableValue(srcType, out var underlying) && HasImplicitConversion(compilation, underlying, tgtType))
        {
            nullHandling = nullStrategy == NullStrategy.SetDefault ? Model.NullHandling.ValueOrDefault : Model.NullHandling.ThrowIfNull;
            return true;
        }
```
Add the helper:
```csharp
    private static bool IsNullableValue(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            underlying = named.TypeArguments[0];
            return true;
        }
        underlying = type;
        return false;
    }
```
Ensure every `return` path of `TryResolveConversion` leaves `nullHandling` assigned (it is initialized to `None` at the top, so all early returns are fine).

- [ ] **Step 7: Run tests + build.** `dotnet test tests/DwarfMapper.Generator.Tests -c Debug` → all pass (NullHandlingTests + prior; snapshot unchanged — flat fixture has no nullable members). `dotnet build DwarfMapper.NET.sln -c Debug` → 0/0.
- [ ] **Step 8: Commit** `feat(gen): nullable value-type unwrapping (Throw/SetDefault)`.

---

## Task 3: Runtime integration + docs

**Files:** `tests/DwarfMapper.IntegrationTests/NullHandlingRuntimeTests.cs` (create), `README.md` (modify).

- [ ] **Step 1: Runtime tests** — `tests/DwarfMapper.IntegrationTests/NullHandlingRuntimeTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class NSrc { public int? N { get; set; } }
public class NDst { public int N { get; set; } }

[DwarfMapper]
public partial class ThrowMapper
{
    public partial NDst Map(NSrc s);
}

[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
public partial class DefaultMapper
{
    public partial NDst Map(NSrc s);
}

public class NullHandlingRuntimeTests
{
    [Fact]
    public void Throw_strategy_maps_value_and_throws_on_null()
    {
        Assert.Equal(5, new ThrowMapper().Map(new NSrc { N = 5 }).N);
        Assert.Throws<System.InvalidOperationException>(() => new ThrowMapper().Map(new NSrc { N = null }));
    }

    [Fact]
    public void SetDefault_strategy_uses_zero_on_null()
    {
        Assert.Equal(0, new DefaultMapper().Map(new NSrc { N = null }).N);
        Assert.Equal(7, new DefaultMapper().Map(new NSrc { N = 7 }).N);
    }
}
```
- [ ] **Step 2: Build + run** integration tests → expected 10 pass (8 existing + 2 new). Full solution → all pass; report count.
- [ ] **Step 3: Docs.** In README "Configuring mapping", after the enums bullet add:
```markdown
**Nullable values.** A nullable value-type source mapped to a non-nullable destination (`int? → int`) is unwrapped per `NullStrategy`: **throw on null** (default) or `[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]` to use the destination default.
```
- [ ] **Step 4: Commit** `test+docs: null handling runtime coverage and documentation`.

---

## Self-Review

- `NullStrategy` (default Throw) → Task 1. ✅
- `NullHandling` + MemberMap + emitter + nullable-value detection → Task 2. ✅
- `int?`→`int` (throw/default), `int?`→`int?` (direct), `int?`→`long` (widening) → Task 2 tests. ✅
- Runtime + docs → Task 3. ✅
- Precedence: Use → implicit → **nullable-value** → auto-discover → enum → DWARF005 (nullable-value placed right after implicit; identity nullable→nullable handled by implicit, so only nullable→non-null reaches the branch). ✅
- Incrementality: `NullHandling` is an enum (value-equatable); `MemberMap` stays string/enum-only. ✅
- Deferred: reference NRT guards, null-handling combined with converters/enums, collections (Plan 6), testing (Plan 7).
