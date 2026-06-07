# DwarfMapper.NET — Plan 7: DwarfMapper.Testing

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Ship the resilience toolkit that motivated the project: a **`DwarfMapper.Testing`** package with seeded fixture/fuzz instance generation, a mapping-aware **informed dump** (member-path diff), and a one-line **round-trip verifier** that proves `Back(Forward(x)) ≡ x` over fuzzed inputs and explains failures.

**Architecture:** `DwarfMapper.Testing` is a **test-time, reflection-based** library (`net10.0`). Reflection is appropriate here — the package is referenced by test projects, never AOT-published, and is *not* referenced by the AOT/trim gate sample, so the core library's reflection-free/AOT guarantees are untouched. Components: `ObjectFactory` (deterministic seeded instance builder), `Fuzzer` (seeded instance sequences), `StructuralComparer` + `MemberDiff` (deep, path-aware diff), `RoundTrip` + `RoundTripException` (verify + informed dump).

**Tech Stack:** `net10.0`, reflection (BCL only), xUnit for the package's own tests.

**Builds on Plans 1–6.** The mapper supports flat/fields/renames/case-insensitive/converters/nested/enums/nullable/collections (DWARF001–015). 75 tests pass. This plan adds NO generator changes.

**Out of scope:** `[RoundTrip]` attribute *auto-emitting* a test (deferred — the runtime `RoundTrip.Verify` delivers the value); NUnit/MSTest adapters (xUnit only here); custom comparers / float tolerance configuration beyond a default epsilon; generating instances for interfaces/abstract types (skipped gracefully).

**Conventions:** SPDX header on new files; CPM; warnings-as-errors + analyzers; warning-clean. The package does NOT set `IsAotCompatible`/`IsTrimmable` (it is reflection-based and test-only).

## File structure

```
src/DwarfMapper.Testing/DwarfMapper.Testing.csproj           # create
src/DwarfMapper.Testing/ObjectFactory.cs                     # create
src/DwarfMapper.Testing/Fuzzer.cs                            # create
src/DwarfMapper.Testing/MemberDiff.cs                        # create
src/DwarfMapper.Testing/StructuralComparer.cs               # create
src/DwarfMapper.Testing/RoundTripException.cs               # create
src/DwarfMapper.Testing/RoundTrip.cs                        # create
tests/DwarfMapper.Testing.Tests/DwarfMapper.Testing.Tests.csproj   # create
tests/DwarfMapper.Testing.Tests/ObjectFactoryTests.cs       # create
tests/DwarfMapper.Testing.Tests/StructuralComparerTests.cs # create
tests/DwarfMapper.Testing.Tests/RoundTripTests.cs          # create
README.md                                                    # modify
```

---

## Task 1: Project + `ObjectFactory` (seeded instance builder)

**Files:** `src/DwarfMapper.Testing/DwarfMapper.Testing.csproj`, `src/DwarfMapper.Testing/ObjectFactory.cs`, `tests/DwarfMapper.Testing.Tests/DwarfMapper.Testing.Tests.csproj`, `tests/DwarfMapper.Testing.Tests/ObjectFactoryTests.cs`.

- [ ] **Step 1: Create the package project** `src/DwarfMapper.Testing/DwarfMapper.Testing.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the test project** `tests/DwarfMapper.Testing.Tests/DwarfMapper.Testing.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <!-- CA1707: underscore test names. CA1515/CA1051/CA1819/CA2227/CA1002: public test-helper DTOs by design. -->
    <NoWarn>$(NoWarn);CA1707;CA1515;CA1051;CA1819;CA2227;CA1002</NoWarn>
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
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\..\src\DwarfMapper.Testing\DwarfMapper.Testing.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Failing tests** — `tests/DwarfMapper.Testing.Tests/ObjectFactoryTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper.Testing;

namespace DwarfMapper.Testing.Tests;

public class Sample
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? Maybe { get; set; }
    public List<int> Nums { get; set; } = new();
    public Nested Child { get; set; } = new();
}

public class Nested { public string City { get; set; } = ""; }

public class ObjectFactoryTests
{
    [Fact]
    public void Populates_all_members()
    {
        var s = ObjectFactory.Create<Sample>(seed: 1);
        Assert.NotEqual(0, s.Id);
        Assert.NotEqual("", s.Name);
        Assert.NotNull(s.Maybe);
        Assert.NotEmpty(s.Nums);
        Assert.NotEqual("", s.Child.City);
    }

    [Fact]
    public void Same_seed_is_deterministic()
    {
        var a = ObjectFactory.Create<Sample>(seed: 42);
        var b = ObjectFactory.Create<Sample>(seed: 42);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Child.City, b.Child.City);
    }

    [Fact]
    public void Different_seeds_differ()
    {
        var a = ObjectFactory.Create<Sample>(seed: 1);
        var b = ObjectFactory.Create<Sample>(seed: 2);
        Assert.NotEqual(a.Id, b.Id);
    }
}
```

- [ ] **Step 4: Run → FAIL** (`ObjectFactory` missing).

- [ ] **Step 5: Implement** `src/DwarfMapper.Testing/ObjectFactory.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Testing;

/// <summary>Builds deterministically-seeded, fully-populated instances for fixtures and fuzzing.</summary>
public static class ObjectFactory
{
    private const int MaxDepth = 6;

    /// <summary>Create a populated instance of <typeparamref name="T"/> for the given seed.</summary>
    public static T Create<T>(int seed = 0) => (T)Create(typeof(T), new Random(seed), 0)!;

    /// <summary>Create a populated instance of <paramref name="type"/> using <paramref name="rng"/>.</summary>
    public static object? Create(Type type, Random rng, int depth)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return Create(underlying, rng, depth);
        }

        if (type == typeof(string))
        {
            return "s" + rng.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(bool))
        {
            return rng.Next(0, 2) == 1;
        }
        if (type == typeof(byte))
        {
            return (byte)rng.Next(1, 256);
        }
        if (type == typeof(sbyte))
        {
            return (sbyte)rng.Next(1, 127);
        }
        if (type == typeof(short))
        {
            return (short)rng.Next(1, short.MaxValue);
        }
        if (type == typeof(ushort))
        {
            return (ushort)rng.Next(1, ushort.MaxValue);
        }
        if (type == typeof(int))
        {
            return rng.Next(1, int.MaxValue);
        }
        if (type == typeof(uint))
        {
            return (uint)rng.Next(1, int.MaxValue);
        }
        if (type == typeof(long))
        {
            return (long)rng.Next(1, int.MaxValue);
        }
        if (type == typeof(ulong))
        {
            return (ulong)rng.Next(1, int.MaxValue);
        }
        if (type == typeof(float))
        {
            return (float)(rng.NextDouble() * 1000);
        }
        if (type == typeof(double))
        {
            return rng.NextDouble() * 1000;
        }
        if (type == typeof(decimal))
        {
            return (decimal)(rng.NextDouble() * 1000);
        }
        if (type == typeof(char))
        {
            return (char)rng.Next('A', 'Z' + 1);
        }
        if (type == typeof(Guid))
        {
            var b = new byte[16];
            rng.NextBytes(b);
            return new Guid(b);
        }
        if (type == typeof(DateTime))
        {
            return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 5_000_000));
        }
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(rng.Next(values.Length));
        }
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var n = depth >= MaxDepth ? 0 : rng.Next(1, 4);
            var array = Array.CreateInstance(elementType, n);
            for (var i = 0; i < n; i++)
            {
                array.SetValue(Create(elementType, rng, depth + 1), i);
            }
            return array;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(type)!;
            var n = depth >= MaxDepth ? 0 : rng.Next(1, 4);
            for (var i = 0; i < n; i++)
            {
                list.Add(Create(elementType, rng, depth + 1));
            }
            return list;
        }

        if (type.IsInterface || type.IsAbstract || depth >= MaxDepth)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor is null)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        var instance = ctor.Invoke(null);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanWrite && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0)
            {
                p.SetValue(instance, Create(p.PropertyType, rng, depth + 1));
            }
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.IsInitOnly)
            {
                f.SetValue(instance, Create(f.FieldType, rng, depth + 1));
            }
        }
        return instance;
    }
}
```

- [ ] **Step 6: Run → PASS.** Add both projects to the solution: `dotnet sln add src\DwarfMapper.Testing\DwarfMapper.Testing.csproj` and `dotnet sln add tests\DwarfMapper.Testing.Tests\DwarfMapper.Testing.Tests.csproj`. `dotnet build DwarfMapper.NET.sln -c Debug` → 0/0.

- [ ] **Step 7: Commit** `feat(testing): DwarfMapper.Testing project + seeded ObjectFactory`.

---

## Task 2: `StructuralComparer` + informed dump

**Files:** `src/DwarfMapper.Testing/MemberDiff.cs`, `src/DwarfMapper.Testing/StructuralComparer.cs`, `tests/DwarfMapper.Testing.Tests/StructuralComparerTests.cs`.

- [ ] **Step 1: Failing tests** — `StructuralComparerTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Testing;

namespace DwarfMapper.Testing.Tests;

public class Box { public int X { get; set; } public string S { get; set; } = ""; public List<int> Xs { get; set; } = new(); }

public class StructuralComparerTests
{
    [Fact]
    public void Equal_objects_have_no_diffs()
    {
        var a = new Box { X = 1, S = "a", Xs = new() { 1, 2 } };
        var b = new Box { X = 1, S = "a", Xs = new() { 1, 2 } };
        Assert.Empty(StructuralComparer.Diff(a, b));
    }

    [Fact]
    public void Scalar_difference_is_reported_with_path()
    {
        var a = new Box { X = 1, S = "a" };
        var b = new Box { X = 2, S = "a" };
        var diffs = StructuralComparer.Diff(a, b);
        Assert.Contains(diffs, d => d.Path.Contains("X") && d.Expected == "1" && d.Actual == "2");
    }

    [Fact]
    public void Collection_element_difference_is_reported_with_index()
    {
        var a = new Box { Xs = new() { 1, 2, 3 } };
        var b = new Box { Xs = new() { 1, 9, 3 } };
        var diffs = StructuralComparer.Diff(a, b);
        Assert.Contains(diffs, d => d.Path.Contains("Xs[1]"));
    }

    [Fact]
    public void Render_produces_readable_lines()
    {
        var diffs = StructuralComparer.Diff(new Box { X = 1 }, new Box { X = 2 });
        var text = StructuralComparer.Render(diffs);
        Assert.Contains("X", text, System.StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement** `src/DwarfMapper.Testing/MemberDiff.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Testing;

/// <summary>A single difference found by <see cref="StructuralComparer"/>.</summary>
public sealed class MemberDiff
{
    /// <summary>Creates a member difference.</summary>
    public MemberDiff(string path, string? expected, string? actual)
    {
        Path = path;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>Member path, e.g. <c>Order.Lines[2].Price</c>.</summary>
    public string Path { get; }

    /// <summary>Rendered expected value.</summary>
    public string? Expected { get; }

    /// <summary>Rendered actual value.</summary>
    public string? Actual { get; }
}
```

- [ ] **Step 4: Implement** `src/DwarfMapper.Testing/StructuralComparer.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DwarfMapper.Testing;

/// <summary>Deep, path-aware structural comparison producing an "informed dump" of differences.</summary>
public static class StructuralComparer
{
    private const int MaxDepth = 12;
    private const double FloatEpsilon = 1e-9;

    /// <summary>Compare two object graphs and return every difference with its member path.</summary>
    public static IReadOnlyList<MemberDiff> Diff(object? expected, object? actual)
    {
        var diffs = new List<MemberDiff>();
        Compare("root", expected, actual, diffs, 0);
        return diffs;
    }

    /// <summary>Render diffs as readable lines.</summary>
    public static string Render(IReadOnlyList<MemberDiff> diffs)
    {
        if (diffs is null)
        {
            throw new ArgumentNullException(nameof(diffs));
        }
        var sb = new StringBuilder();
        foreach (var d in diffs)
        {
            sb.Append("  ").Append(d.Path).Append(": expected ").Append(d.Expected ?? "<null>")
              .Append(", actual ").Append(d.Actual ?? "<null>").Append('\n');
        }
        return sb.ToString();
    }

    private static void Compare(string path, object? expected, object? actual, List<MemberDiff> diffs, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }
        if (expected is null || actual is null)
        {
            if (!(expected is null && actual is null))
            {
                diffs.Add(new MemberDiff(path, Fmt(expected), Fmt(actual)));
            }
            return;
        }

        var type = expected.GetType();
        if (IsScalar(type))
        {
            if (!ScalarEquals(expected, actual))
            {
                diffs.Add(new MemberDiff(path, Fmt(expected), Fmt(actual)));
            }
            return;
        }

        if (expected is IEnumerable le && actual is IEnumerable re)
        {
            var el = ToList(le);
            var rl = ToList(re);
            if (el.Count != rl.Count)
            {
                diffs.Add(new MemberDiff(path + ".Count", Fmt(el.Count), Fmt(rl.Count)));
            }
            var n = Math.Min(el.Count, rl.Count);
            for (var i = 0; i < n; i++)
            {
                Compare(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", el[i], rl[i], diffs, depth + 1);
            }
            return;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanRead && p.GetIndexParameters().Length == 0)
            {
                Compare(path + "." + p.Name, p.GetValue(expected), p.GetValue(actual), diffs, depth + 1);
            }
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            Compare(path + "." + f.Name, f.GetValue(expected), f.GetValue(actual), diffs, depth + 1);
        }
    }

    private static bool IsScalar(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
        || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);

    private static bool ScalarEquals(object a, object b)
    {
        if (a is double da && b is double db)
        {
            return Math.Abs(da - db) < FloatEpsilon;
        }
        if (a is float fa && b is float fb)
        {
            return Math.Abs(fa - fb) < FloatEpsilon;
        }
        return Equals(a, b);
    }

    private static List<object?> ToList(IEnumerable e)
    {
        var list = new List<object?>();
        foreach (var item in e)
        {
            list.Add(item);
        }
        return list;
    }

    private static string? Fmt(object? o) => o switch
    {
        null => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString(),
    };
}
```

- [ ] **Step 5: Run → PASS; build → 0/0.**
- [ ] **Step 6: Commit** `feat(testing): structural comparer + informed dump`.

---

## Task 3: `Fuzzer` + `RoundTrip.Verify`

**Files:** `src/DwarfMapper.Testing/Fuzzer.cs`, `src/DwarfMapper.Testing/RoundTripException.cs`, `src/DwarfMapper.Testing/RoundTrip.cs`, `tests/DwarfMapper.Testing.Tests/RoundTripTests.cs`.

- [ ] **Step 1: Failing tests** — `RoundTripTests.cs` (uses a REAL generated mapper):
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;
using DwarfMapper.Testing;

namespace DwarfMapper.Testing.Tests;

public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class PersonRoundTripMapper
{
    public partial PersonDto ToDto(Person p);
    public partial Person FromDto(PersonDto d);
}

// A deliberately lossy mapper to prove failures are caught.
public class LossyDto { public int Age { get; set; } public string Name { get; set; } = ""; }

public class RoundTripTests
{
    [Fact]
    public void Fuzzer_yields_requested_count()
    {
        var items = new System.Collections.Generic.List<Person>(Fuzzer.Generate<Person>(5, seed: 3));
        Assert.Equal(5, items.Count);
    }

    [Fact]
    public void Lossless_roundtrip_passes()
    {
        var m = new PersonRoundTripMapper();
        RoundTrip.Verify<Person, PersonDto>(m.ToDto, m.FromDto, seed: 7, iterations: 50);
    }

    [Fact]
    public void Lossy_roundtrip_throws_with_informed_dump()
    {
        // forward drops Name; backward cannot restore it -> round-trip mismatch.
        Func<Person, LossyDto> forward = p => new LossyDto { Age = p.Age, Name = "" };
        Func<LossyDto, Person> backward = d => new Person { Age = d.Age, Name = d.Name };
        var ex = Assert.Throws<RoundTripException>(() =>
            RoundTrip.Verify<Person, LossyDto>(forward, backward, seed: 1, iterations: 20));
        Assert.Contains("Name", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement** `src/DwarfMapper.Testing/Fuzzer.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;

namespace DwarfMapper.Testing;

/// <summary>Generates deterministic sequences of populated instances for property-based tests.</summary>
public static class Fuzzer
{
    /// <summary>Yield <paramref name="count"/> seeded instances of <typeparamref name="T"/>.</summary>
    public static IEnumerable<T> Generate<T>(int count, int seed = 0)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            yield return (T)ObjectFactory.Create(typeof(T), rng, 0)!;
        }
    }
}
```

- [ ] **Step 4: Implement** `src/DwarfMapper.Testing/RoundTripException.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DwarfMapper.Testing;

/// <summary>Thrown when a round-trip mapping fails to reproduce the original, with an informed dump.</summary>
public sealed class RoundTripException : Exception
{
    /// <summary>Creates a round-trip failure with the offending seed, iteration, and diffs.</summary>
    public RoundTripException(int seed, int iteration, IReadOnlyList<MemberDiff> diffs)
        : base(Build(seed, iteration, diffs))
    {
        Seed = seed;
        Iteration = iteration;
        Diffs = diffs;
    }

    /// <summary>The fuzz seed that produced the failure (replay with this seed).</summary>
    public int Seed { get; }

    /// <summary>The iteration index within the run.</summary>
    public int Iteration { get; }

    /// <summary>The structural differences found.</summary>
    public IReadOnlyList<MemberDiff> Diffs { get; }

    private static string Build(int seed, int iteration, IReadOnlyList<MemberDiff> diffs)
    {
        var header = string.Format(
            CultureInfo.InvariantCulture,
            "Round-trip mismatch [seed: {0}, iteration: {1}]\n", seed, iteration);
        return header + StructuralComparer.Render(diffs);
    }
}
```

- [ ] **Step 5: Implement** `src/DwarfMapper.Testing/RoundTrip.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Testing;

/// <summary>Verifies that mapping a value forward and back reproduces the original, over fuzzed inputs.</summary>
public static class RoundTrip
{
    /// <summary>
    /// Generate <paramref name="iterations"/> seeded <typeparamref name="TSource"/> instances and assert
    /// <c>backward(forward(x))</c> structurally equals <c>x</c>. Throws <see cref="RoundTripException"/> on
    /// the first mismatch, with a mapping-aware diff.
    /// </summary>
    public static void Verify<TSource, TDto>(
        Func<TSource, TDto> forward, Func<TDto, TSource> backward, int seed = 12345, int iterations = 100)
    {
        if (forward is null)
        {
            throw new ArgumentNullException(nameof(forward));
        }
        if (backward is null)
        {
            throw new ArgumentNullException(nameof(backward));
        }

        var rng = new Random(seed);
        for (var i = 0; i < iterations; i++)
        {
            var itemSeed = rng.Next();
            var original = ObjectFactory.Create<TSource>(itemSeed);
            var roundTripped = backward(forward(original));
            var diffs = StructuralComparer.Diff(original, roundTripped);
            if (diffs.Count > 0)
            {
                throw new RoundTripException(itemSeed, i, diffs);
            }
        }
    }
}
```

- [ ] **Step 6: Run → PASS; build → 0/0.** Confirm `Lossless_roundtrip_passes` (real generated mapper) and `Lossy_roundtrip_throws_with_informed_dump` (message names `Name`).
- [ ] **Step 7: Commit** `feat(testing): Fuzzer + RoundTrip verifier with informed dumps`.

---

## Task 4: Full-solution check + docs

**Files:** `README.md`.

- [ ] **Step 1: Full-solution test** — `$env:DiffEngine_Disabled="true"; dotnet test DwarfMapper.NET.sln -c Debug` → ALL pass; report total count (generator + integration + testing-tests).

- [ ] **Step 2: Docs.** In `README.md`, the Resilience section already describes `[RoundTrip]`/informed dumps as the vision. Add a concrete, working subsection after "Informed dumps" (or near it):
```markdown
### Verifying maps today

`DwarfMapper.Testing` ships the round-trip verifier you can call now:

```csharp
var m = new OrderMapper();
RoundTrip.Verify<Order, OrderDto>(m.ToDto, m.FromDto);   // fuzzes inputs, asserts Back(Forward(x)) ≡ x
```

On mismatch it throws with a mapping-aware dump (member path, expected vs. actual, and the replay seed). `ObjectFactory.Create<T>(seed)` and `Fuzzer.Generate<T>(count, seed)` build seeded fixtures for your own tests. The package is reflection-based and test-only — it is never AOT-published and does not affect the core library's reflection-free guarantees.
```
(If the README's roadmap lists the testing toolkit as upcoming, mark the verifier/fixtures/fuzzer/informed-dumps as shipped; the `[RoundTrip]` *attribute auto-emission* remains a future enhancement.)

- [ ] **Step 3: Commit** `docs: document DwarfMapper.Testing (RoundTrip.Verify, fixtures, fuzzer)`.

---

## Self-Review

- `DwarfMapper.Testing` project (reflection, test-time, not AOT) → Task 1. ✅
- `ObjectFactory` seeded/deterministic, recursive, handles primitives/string/Guid/DateTime/enum/nullable/array/List/POCO with depth guard → Task 1. ✅
- `StructuralComparer` + `MemberDiff` informed dump (path + index, float epsilon) → Task 2. ✅
- `Fuzzer` + `RoundTrip.Verify` + `RoundTripException` (informed dump + replay seed); verified against a REAL generated mapper (lossless passes, lossy throws naming the lost member) → Task 3. ✅
- Docs → Task 4. ✅

**Notes:** reflection is intentional and confined to this test-time package — the AOT/trim gate sample does not reference it, so the core library's guarantees are intact. `ObjectFactory` skips interfaces/abstracts/ctor-less reference types gracefully (returns null). `StructuralComparer` treats `string` as scalar (not a char sequence) before the `IEnumerable` branch. Determinism: `Random(seed)`; `RoundTrip` derives a per-iteration seed from the run seed and reports it for replay.

**Deferred:** `[RoundTrip]` attribute auto-emitting tests; NUnit/MSTest adapters; configurable comparers/tolerance; cyclic-graph detection beyond depth guard.
