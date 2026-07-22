// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class HashSetTests
{
    private static void NoErrors(string s)
    {
        GeneratorAssert.CompilesClean(s);
    }

    [Fact]
    public void HashSet_same_type_uses_bulk_ctor()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class A { public HashSet<int> Xs { get; set; } = new(); }
                         public class B { public HashSet<int> Xs { get; set; } = new(); }
                         [DwarfMapper] public partial class M { public partial B Map(A a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("new global::System.Collections.Generic.HashSet<int>(src)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void HashSet_converts_elements()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public enum C { Red, Green }
                         public class A { public HashSet<C> Xs { get; set; } = new(); }
                         public class B { public HashSet<int> Xs { get; set; } = new(); }
                         [DwarfMapper] public partial class M { public partial B Map(A a); }
                         """;
        NoErrors(s);
    }

    [Fact]
    public void List_source_to_hashset_maps()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class A { public List<int> Xs { get; set; } = new(); }
                         public class B { public HashSet<int> Xs { get; set; } = new(); }
                         [DwarfMapper] public partial class M { public partial B Map(A a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains(".Add(", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlySet_source_maps()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class A { public IReadOnlySet<int> Xs { get; set; } = new HashSet<int>(); }
                         public class B { public HashSet<int> Xs { get; set; } = new(); }
                         [DwarfMapper] public partial class M { public partial B Map(A a); }
                         """;
        NoErrors(s);
    }
}
