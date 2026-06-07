// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class CollectionTests
{
    private static void NoErrors(string src)
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Array_to_array_identity_uses_clone()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            public class B { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("Clone()", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void List_to_list_identity_uses_bulk_ctor()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public List<int> Xs { get; set; } = new(); }
            public class B { public List<int> Xs { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("new global::System.Collections.Generic.List<int>(src)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Array_to_list_maps()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            public class B { public List<int> Xs { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        NoErrors(s);
    }

    [Fact]
    public void Enumerable_source_to_array_maps()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public IEnumerable<int> Xs { get; set; } = System.Array.Empty<int>(); }
            public class B { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        NoErrors(s);
    }

    [Fact]
    public void Nested_element_uses_declared_mapper()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class A { public List<Addr> Items { get; set; } = new(); }
            public class B { public List<AddrDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                public partial B Map(A a);
                public partial AddrDto ToDto(Addr x);
            }
            """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("ToDto(", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_element_maps()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public enum C { Red, Green }
            public class A { public C[] Xs { get; set; } = System.Array.Empty<C>(); }
            public class B { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        NoErrors(s);
    }

    [Fact]
    public void String_member_is_not_treated_as_char_collection()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string Name { get; set; } = ""; }
            public class B { public string Name { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("Name = a.Name", gen, StringComparison.Ordinal);
    }
}
