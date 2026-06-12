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

    // ── Fix 1: Top-level collection/dictionary-returning partial methods ──────────

    [Fact]
    public void TopLevel_List_long_to_List_int_no_DWARF007()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            [DwarfMapper]
            public partial class M
            {
                public partial List<int> Map(List<long> xs);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void TopLevel_List_Src_to_List_Dto_auto_nest_no_DWARF007()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Src { public int Value { get; set; } }
            public class Dto { public int Value { get; set; } }
            [DwarfMapper(AutoNest = true)]
            public partial class M
            {
                public partial List<Dto> MapAll(List<Src> xs);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void TopLevel_Dictionary_string_long_to_Dictionary_string_int_no_DWARF007()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            [DwarfMapper]
            public partial class M
            {
                public partial Dictionary<string, int> Map(Dictionary<string, long> d);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void TopLevel_intArray_to_List_int_no_DWARF007()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            [DwarfMapper]
            public partial class M
            {
                public partial List<int> Map(int[] xs);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void TopLevel_polymorphic_List_Animal_to_List_AnimalDto_no_DWARF007()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public abstract class Animal { public string Name { get; set; } = ""; }
            public class Dog : Animal { public string Breed { get; set; } = ""; }
            public class AnimalDto { public string Name { get; set; } = ""; }
            public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                public partial List<AnimalDto> MapAll(List<Animal> animals);
                [MapDerivedType<Dog, DogDto>]
                public partial AnimalDto Map(Animal a);
                public partial DogDto Map(Dog d);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }
}
