// SPDX-License-Identifier: GPL-2.0-only

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
        return Verify(generated);
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
        return Verify(generated);
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
        return Verify(generated);
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
        return Verify(generated);
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
        return Verify(generated);
    }

    // ── Dictionary<K,V> ───────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Collection_Dictionary()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public class A { public Dictionary<string,int> Data { get; set; } = new(); }
                           public class B { public Dictionary<string,int> Data { get; set; } = new(); }
                           [DwarfMapper] public partial class M { public partial B Copy(A a); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
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
        return Verify(generated);
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
        return Verify(generated);
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
        return Verify(generated);
    }
}
