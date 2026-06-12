# Golden Snapshot Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ~35–40 Verify golden-snapshot tests that pin the exact generated C# source for every major DwarfMapper feature so that codegen drift is immediately caught.

**Architecture:** All snapshot tests live in `tests/DwarfMapper.Generator.Tests/Snapshots/` as several partial-class files. Each test calls `GeneratorTestHarness.Run(src)`, then `return Verifier.Verify(generated)` — identical to the existing `SnapshotTests.Flat_mapper_output_is_stable` pattern. After the first run produces `.received.txt` files, each is renamed to `.verified.txt` (accepted as baseline) and committed.

**Tech Stack:** xunit, Verify.Xunit, Verify.SourceGenerators, DwarfMapper source generator, .NET 10, PowerShell, `$env:DiffEngine_Disabled='true'`

---

## File Structure

| File | Responsibility |
|------|---------------|
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Basic.cs` | Flat mapping, public fields, CaseInsensitive, MapProperty rename, MapProperty Use= converter, MapIgnore |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Nested.cs` | AutoNest 2-level, AutoNest 3-level, AutoNest with conversion at depth |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Collections.cs` | T[], List<T>, HashSet<T>, IReadOnlyList→List, ImmutableArray, Dictionary, IReadOnlyDictionary→Dictionary, nested List<List<int>>, List<RecordDto> auto-nest element |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Blit.cs` | Blit/reinterpret unmanaged struct array via MemoryMarshal.Cast |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Enums.cs` | Enum by-name (switch), enum by-value (CreateChecked), enum↔string, enum↔numeric |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Nullable.cs` | int?→int throw, int?→int default, T?→U? NullableProject ternary |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Conversions.cs` | long→int CreateChecked, string→int IParsable, string→Guid IParsable, int→string IFormattable, DateTime→string "o" |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Constructor.cs` | Positional record, record with extra init, required members, mixed ctor+init, DwarfMapperConstructor |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Advanced.cs` | Flatten, BeforeMap/AfterMap hooks, RoundTrip emission |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Graph.cs` | Preserve self-ref node (TryGetReference/SetReference), depth-guarded recursive (None mode) |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Projection.cs` | Flat projection Select, nested member-init projection, collection Select().ToList(), constructor projection |
| `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Diagnostics.cs` | Snapshot diagnostics strings for DWARF001, DWARF005, DWARF024, DWARF028 |

All snapshot files use `namespace DwarfMapper.Generator.Tests;` and `using VerifyXunit;`.

---

## Task 1: Create `SnapshotTests.Basic.cs` — flat / field / CaseInsensitive / MapProperty / MapIgnore

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Basic.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Basic: public properties (flat) ──────────────────────────────────────
    [Fact]
    public Task Snap_Flat_Properties()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial PersonDto Map(Person p); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Public fields ─────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Public_Fields()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int X; public string Y = ""; }
            public class Dst { public int X; public string Y = ""; }
            [DwarfMapper]
            public partial class M { public partial Dst Map(Src s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── CaseInsensitive = true ────────────────────────────────────────────────
    [Fact]
    public Task Snap_CaseInsensitive()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Value { get; set; } }
            public class Dst { public int value { get; set; } }
            [DwarfMapper(CaseInsensitive = true)]
            public partial class M { public partial Dst Map(Src s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── MapProperty rename ────────────────────────────────────────────────────
    [Fact]
    public Task Snap_MapProperty_Rename()
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
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── MapProperty Use= converter ────────────────────────────────────────────
    [Fact]
    public Task Snap_MapProperty_Use_Converter()
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
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── MapIgnore ─────────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_MapIgnore()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapIgnore("Name")]
                public partial PersonDto Map(Person p);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

- [ ] **Step 2: Run just this file to get the first failure (expected)**

```powershell
$env:DiffEngine_Disabled='true'; dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~SnapshotSuite" 2>&1 | Select-String -Pattern "passed|failed|error|FAILED|PASSED|verified|received" | Select-Object -First 30
```

Expected: tests FAIL with "received" files created (no verified yet).

---

## Task 2: Create `SnapshotTests.Nested.cs` — AutoNest 2-level, 3-level, conversion at depth

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Nested.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── AutoNest: 2-level nested class→record ─────────────────────────────────
    [Fact]
    public Task Snap_AutoNest_TwoLevel()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr   { public string City { get; set; } = ""; public int Zip { get; set; } }
            public class Person { public Addr Home { get; set; } = new(); public string Name { get; set; } = ""; }
            public record AddrDto(string City, int Zip);
            public record PersonDto(AddrDto Home, string Name);
            [DwarfMapper]
            public partial class M { public partial PersonDto Map(Person p); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── AutoNest: 3-level graph ───────────────────────────────────────────────
    [Fact]
    public Task Snap_AutoNest_ThreeLevel()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class City   { public string Name { get; set; } = ""; }
            public class Addr   { public City Location { get; set; } = new(); public int Zip { get; set; } }
            public class Person { public Addr Home { get; set; } = new(); public string FullName { get; set; } = ""; }
            public record CityDto(string Name);
            public record AddrDto(CityDto Location, int Zip);
            public record PersonDto(AddrDto Home, string FullName);
            [DwarfMapper]
            public partial class M { public partial PersonDto Map(Person p); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── AutoNest: conversion at depth (long→int, string→Guid) ─────────────────
    [Fact]
    public Task Snap_AutoNest_ConversionAtDepth()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class Inner { public long Count { get; set; } public string Id { get; set; } = ""; }
            public class Outer { public Inner Sub { get; set; } = new(); }
            public record InnerDto(int Count, Guid Id);
            public record OuterDto(InnerDto Sub);
            [DwarfMapper]
            public partial class M { public partial OuterDto Map(Outer o); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 3: Create `SnapshotTests.Collections.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Collections.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── T[] ───────────────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_Array()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            public class B { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── List<T> ───────────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_List()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public List<int> Xs { get; set; } = new(); }
            public class B { public List<int> Xs { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── HashSet<T> ────────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_HashSet()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public HashSet<int> Xs { get; set; } = new(); }
            public class B { public HashSet<int> Xs { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── IReadOnlyList<T> → List<T> ────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_IReadOnlyList_To_List()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public IReadOnlyList<int> Xs { get; set; } = System.Array.Empty<int>(); }
            public class B { public List<int> Xs { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── ImmutableArray<T> ─────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_ImmutableArray()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Immutable;
            namespace Demo;
            public class A { public ImmutableArray<int> Xs { get; set; } = ImmutableArray<int>.Empty; }
            public class B { public ImmutableArray<int> Xs { get; set; } = ImmutableArray<int>.Empty; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Dictionary<K,V> ───────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_Dictionary()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public Dictionary<string,int> Map { get; set; } = new(); }
            public class B { public Dictionary<string,int> Map { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Copy(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── IReadOnlyDictionary<K,V> → Dictionary<K,V> ───────────────────────────
    [Fact]
    public Task Snap_Collection_IReadOnlyDictionary_To_Dictionary()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public IReadOnlyDictionary<string,int> Data { get; set; } = new Dictionary<string,int>(); }
            public class B { public Dictionary<string,int> Data { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Nested List<List<int>> ────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_NestedListOfList()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public List<List<int>> Matrix { get; set; } = new(); }
            public class B { public List<List<int>> Matrix { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── List<RecordDto> — auto-nest element ───────────────────────────────────
    [Fact]
    public Task Snap_Collection_List_AutoNest_Element()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Item    { public int Id { get; set; } public string Name { get; set; } = ""; }
            public record ItemDto(int Id, string Name);
            public class Container    { public List<Item> Items { get; set; } = new(); }
            public class ContainerDto { public List<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial ContainerDto Map(Container c); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 4: Create `SnapshotTests.Blit.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Blit.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Blit: layout-identical unmanaged structs → MemoryMarshal.Cast ─────────
    [Fact]
    public Task Snap_Blit_LayoutIdenticalStructArray()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public struct SrcV { public float X; public float Y; public float Z; }
            public struct DstV { public float X; public float Y; public float Z; }
            public class A { public SrcV[] Verts { get; set; } = System.Array.Empty<SrcV>(); }
            public class B { public DstV[] Verts { get; set; } = System.Array.Empty<DstV>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 5: Create `SnapshotTests.Enums.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Enums.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Enum by-name (switch expression) ─────────────────────────────────────
    [Fact]
    public Task Snap_Enum_ByName_Switch()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green, Blue }
            public enum ColorDto { Red, Green, Blue }
            public class A { public Color C { get; set; } }
            public class B { public ColorDto C { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Enum by-value (CreateChecked cast) ────────────────────────────────────
    [Fact]
    public Task Snap_Enum_ByValue_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Status { Active = 1, Inactive = 2 }
            public enum StatusCode { Active = 1, Suspended = 3 }
            public class A { public Status S { get; set; } }
            public class B { public StatusCode S { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Enum → string ─────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Enum_To_String()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green }
            public class A { public Color V { get; set; } }
            public class B { public string V { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── String → enum ─────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_String_To_Enum()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green }
            public class A { public string V { get; set; } = ""; }
            public class B { public Color V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Enum ↔ numeric (int→enum underlying) ──────────────────────────────────
    [Fact]
    public Task Snap_Enum_To_Numeric()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Priority { Low = 0, High = 1 }
            public class A { public Priority P { get; set; } }
            public class B { public int P { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 6: Create `SnapshotTests.Nullable.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Nullable.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── int? → int — throw on null (default) ─────────────────────────────────
    [Fact]
    public Task Snap_Nullable_IntToInt_Throw()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── int? → int — SetDefault strategy ─────────────────────────────────────
    [Fact]
    public Task Snap_Nullable_IntToInt_SetDefault()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
            public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── T? → U? NullableProject ternary ──────────────────────────────────────
    [Fact]
    public Task Snap_Nullable_NullableToNullable_Ternary()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public long? N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 7: Create `SnapshotTests.Conversions.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Conversions.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── long → int CreateChecked numeric narrowing ────────────────────────────
    [Fact]
    public Task Snap_Conversion_LongToInt_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public long V { get; set; } }
            public class B { public int V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── string → int via IParsable ────────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_StringToInt_IParsable()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string V { get; set; } = ""; }
            public class B { public int V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── string → Guid via IParsable ───────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_StringToGuid_IParsable()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class A { public string V { get; set; } = ""; }
            public class B { public Guid V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── int → string via IFormattable ─────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_IntToString_IFormattable()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int V { get; set; } }
            public class B { public string V { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── DateTime → string "o" format ─────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_DateTimeToString_Iso8601()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class A { public DateTime V { get; set; } }
            public class B { public string V { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 8: Create `SnapshotTests.Constructor.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Constructor.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Positional record target ───────────────────────────────────────────────
    [Fact]
    public Task Snap_Constructor_PositionalRecord()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public record R(int X, string Y);
            [DwarfMapper]
            public partial class M { public partial R Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Record with extra init-only property ──────────────────────────────────
    [Fact]
    public Task Snap_Constructor_RecordWithExtraInit()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; public int Z { get; set; } }
            public record R(int X, string Y) { public int Z { get; init; } }
            [DwarfMapper]
            public partial class M { public partial R Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Required members ──────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Constructor_RequiredMembers()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public class D { public required int X { get; set; } public required string Y { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── DwarfMapperConstructor attribute ─────────────────────────────────────
    [Fact]
    public Task Snap_Constructor_DwarfMapperConstructor_Attribute()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public class D
            {
                public D() { }
                [DwarfMapperConstructor]
                public D(int x, string y) { X = x; Y = y; }
                public int X { get; set; }
                public string Y { get; set; } = "";
            }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 9: Create `SnapshotTests.Advanced.cs` — Flatten, Hooks, RoundTrip

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Advanced.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Flatten dotted path ───────────────────────────────────────────────────
    [Fact]
    public Task Snap_Flatten_DottedPath()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Address  { public string City { get; set; } = ""; }
            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public string City { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [Flatten("Address")]
                public partial CustomerDto ToDto(Customer c);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── BeforeMap / AfterMap hooks ────────────────────────────────────────────
    [Fact]
    public Task Snap_Hooks_BeforeAndAfter()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [BeforeMap] private static void OnBefore(Src s) { }
                [AfterMap]  private static void OnAfter(Src s, Dst d) { }
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── [RoundTrip] emission ──────────────────────────────────────────────────
    [Fact]
    public Task Snap_RoundTrip_Emission()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Order    { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class OrderDto { public int Id { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [RoundTrip] public partial OrderDto ToDto(Order o);
                public partial Order FromDto(OrderDto d);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 10: Create `SnapshotTests.Graph.cs` — Preserve references and depth-guarded recursion

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Graph.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Preserve: self-referential node → TryGetReference/SetReference ────────
    [Fact]
    public Task Snap_Graph_Preserve_SelfRef_Node()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Node    { public int V { get; set; } public Node? Next { get; set; } }
            public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            public partial class M { public partial NodeDto Map(Node n); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Depth-guarded recursive type (None mode) ──────────────────────────────
    [Fact]
    public Task Snap_Graph_DepthGuarded_Recursive_None()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Tree    { public int Val { get; set; } public Tree? Left { get; set; } public Tree? Right { get; set; } }
            public class TreeDto { public int Val { get; set; } public TreeDto? Left { get; set; } public TreeDto? Right { get; set; } }
            [DwarfMapper]
            public partial class M { public partial TreeDto Map(Tree t); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 11: Create `SnapshotTests.Projection.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Projection.cs`

- [ ] **Step 1: Write the test file**

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Flat projection via Queryable.Select ──────────────────────────────────
    [Fact]
    public Task Snap_Projection_Flat()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person    { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Projection with MapProperty rename ────────────────────────────────────
    [Fact]
    public Task Snap_Projection_Rename()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person    { public string FullName { get; set; } = ""; }
            public class PersonDto { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName","Name")]
                public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Nested member-init projection ─────────────────────────────────────────
    [Fact]
    public Task Snap_Projection_Nested_MemberInit()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Addr   { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); public string Name { get; set; } = ""; }
            public class AddrDto   { public string City { get; set; } = ""; }
            public class PersonDto { public AddrDto Home { get; set; } = new(); public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Constructor projection (record target) ────────────────────────────────
    [Fact]
    public Task Snap_Projection_Constructor()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            public record PersonDto(int Age, string Name);
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
```

---

## Task 12: Create `SnapshotTests.Diagnostics.cs`

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Diagnostics.cs`

- [ ] **Step 1: Write the test file**

The diagnostic snapshots format each diagnostic deterministically as `"ID|Severity|Message"` (culture-invariant, sorted by ID).

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── DWARF001: unmapped destination member ─────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF001_UnmappedMember()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF001")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }

    // ── DWARF005: no implicit conversion ──────────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF005_NoImplicitConversion()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Widget    { public int X { get; set; } }
            public class PersonDto { public Widget Age { get; set; } = new(); }
            public class Person    { public int   Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF005")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }

    // ── DWARF024: ctor param unmapped ─────────────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF024_CtorParamUnmapped()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public PersonDto(int x) { Age = x; } public int Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF024")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }

    // ── DWARF028: projection not translatable ─────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF028_ProjectionNotTranslatable()
    {
        // Use a converter (Use=) which can't be translated to an expression tree
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person    { public string Amount { get; set; } = ""; }
            public class PersonDto { public int Amount { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Amount","Amount", Use = nameof(ParseInt))]
                public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
                private static int ParseInt(string v) => int.Parse(v);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF028")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }
}
```

---

## Task 13: Run ALL snapshot tests — collect `.received.txt` baseline files

- [ ] **Step 1: Run the full snapshot suite**

```powershell
$env:DiffEngine_Disabled='true'; dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~SnapshotSuite" 2>&1 | Tee-Object -Variable testOut; $testOut | Select-String -Pattern "passed|failed|received|verified|error" | Select-Object -First 40
```

Expected: ALL tests FAIL (no `.verified` files yet). `.received.txt` files created alongside each test file.

- [ ] **Step 2: Count received files**

```powershell
Get-ChildItem -Recurse "tests\DwarfMapper.Generator.Tests" -Filter "*.received.txt" | Measure-Object | Select-Object Count
```

Expected: count matching number of snapshot tests.

---

## Task 14: Accept baselines — rename `.received.txt` → `.verified.txt`

- [ ] **Step 1: Rename all received files to verified**

```powershell
Get-ChildItem -Recurse "tests\DwarfMapper.Generator.Tests" -Filter "*.received.txt" | ForEach-Object {
    $dest = $_.FullName -replace '\.received\.txt$', '.verified.txt'
    Rename-Item -Path $_.FullName -NewName $dest
}
```

Expected: no output = success; all `.received.txt` replaced by `.verified.txt`.

- [ ] **Step 2: Verify count**

```powershell
Get-ChildItem -Recurse "tests\DwarfMapper.Generator.Tests" -Filter "*.verified.txt" | Measure-Object | Select-Object Count
```

Expected: same count as before (one more than original, since the existing `Flat_mapper_output_is_stable.verified.txt` is already there).

---

## Task 15: Re-run snapshot suite — confirm all green

- [ ] **Step 1: Run snapshot tests**

```powershell
$env:DiffEngine_Disabled='true'; dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~SnapshotSuite" 2>&1 | Select-String -Pattern "passed|failed|error"
```

Expected: ALL passed, 0 failed.

- [ ] **Step 2: Re-run for determinism (2nd pass)**

```powershell
$env:DiffEngine_Disabled='true'; dotnet test tests/DwarfMapper.Generator.Tests -c Debug --filter "FullyQualifiedName~SnapshotSuite" 2>&1 | Select-String -Pattern "passed|failed|error"
```

Expected: same result — confirms snapshots are deterministic.

---

## Task 16: Run the full solution test suite

- [ ] **Step 1: Full solution test run**

```powershell
$env:DiffEngine_Disabled='true'; dotnet test DwarfMapper.NET.sln -c Debug 2>&1 | Select-String -Pattern "passed|failed|error" | Select-Object -Last 10
```

Expected: all prior tests + new snapshots = green.

---

## Task 17: Release build validation

- [ ] **Step 1: Release build**

```powershell
dotnet build DwarfMapper.NET.sln -c Release 2>&1 | Select-String -Pattern "error|warning|Build succeeded|Sestavení proběhlo" | Select-Object -Last 10; Write-Host "ExitCode=$LASTEXITCODE"
```

Expected: `ExitCode=0`, Czech success text, 0 errors.

---

## Task 18: Commit baselines and test files

- [ ] **Step 1: Check git status**

```powershell
git status --short
```

Expected: new `Snapshots/*.cs` test files + new `*.verified.txt` files (untracked/modified).

- [ ] **Step 2: Stage and commit**

```powershell
git add tests/DwarfMapper.Generator.Tests/Snapshots/
git add "tests/DwarfMapper.Generator.Tests/SnapshotTests.*.verified.txt"
git add tests/DwarfMapper.Generator.Tests/
git status --short
```

- [ ] **Step 3: Create commit**

```powershell
git commit -m @'
test(snapshots): add golden-snapshot suite — 35+ Verify baselines

Pins exact generated source for: flat props, public fields, CaseInsensitive,
MapProperty rename/Use, MapIgnore, AutoNest 2/3-level, conversion at depth,
T[]/List/HashSet/IReadOnlyList/ImmutableArray/Dictionary/IReadOnlyDictionary/
nested-list/List<RecordDto>, blit, enum by-name/by-value/↔string/↔numeric,
int?→int throw/default, T?→U? ternary, numeric narrowing, IParsable,
IFormattable, positional record, record+extra-init, required members,
DwarfMapperConstructor, Flatten, BeforeMap+AfterMap hooks, RoundTrip,
Preserve graph (TryGetReference/SetReference), depth-guarded recursive,
flat/rename/nested/constructor projections, DWARF001/005/024/028 diagnostics.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

- [ ] **Step 4: Verify git log**

```powershell
git log --oneline -3
```

Expected: new commit at top, clean status.

---

## Self-Review Checklist

**Spec coverage:**
- [x] Flat properties, public fields — Task 1
- [x] CaseInsensitive — Task 1
- [x] MapProperty rename + Use= converter — Task 1
- [x] MapIgnore — Task 1
- [x] AutoNest 2/3-level + conversion at depth — Task 2
- [x] T[], List<T>, HashSet<T>, IReadOnlyList→List, ImmutableArray, Dictionary, IReadOnlyDictionary→Dictionary, List<List<int>>, List<RecordDto> — Task 3
- [x] Blit/reinterpret MemoryMarshal.Cast — Task 4
- [x] Enum by-name, by-value, ↔string, ↔numeric — Task 5
- [x] int?→int throw/default, T?→U? ternary — Task 6
- [x] long→int CreateChecked, string→int/Guid IParsable, int→string, DateTime→string "o" — Task 7
- [x] Positional record, record+extra-init, required members, DwarfMapperConstructor — Task 8
- [x] Flatten, BeforeMap/AfterMap, RoundTrip — Task 9
- [x] Preserve self-ref (TryGetReference/SetReference), depth-guarded recursive — Task 10
- [x] Flat/rename/nested/constructor projections — Task 11
- [x] DWARF001/005/024/028 diagnostic snapshots — Task 12
- [x] Existing `Flat_mapper_output_is_stable` kept intact — partial class pattern, no conflict

**Placeholder scan:** None — all code blocks are complete, no "TBD" or "similar to".

**Type consistency:** All tests use `GeneratorTestHarness.Run(src)` → `(_, generated)` and `Verifier.Verify(...)`. Diagnostic tests use `(diagnostics, _)` and culture-invariant snapshot string.

**Verify pattern:** Every snapshot test matches existing `SnapshotTests.Flat_mapper_output_is_stable` — no extra `UseDirectory`/`UseFileName` (defaults, same as existing test).
