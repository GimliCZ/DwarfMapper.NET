// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Unflattening (Phase 4): a dotted TARGET path in [MapProperty] (single level, e.g. "Address.City")
/// assigns the leaf through a synthesized intermediate, instantiated once post-construction. The
/// intermediate must be a writable class with a public parameterless constructor (else DWARF045); a
/// direct mapping of the same root conflicts (DWARF046).
/// </summary>
public class UnflattenGeneratorTests
{
    private static Diagnostic? Find(System.Collections.Generic.IEnumerable<Diagnostic> diags, string id)
        => diags.FirstOrDefault(d => d.Id == id);

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    [Fact]
    public void Single_leaf_unflatten_instantiates_and_assigns()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class S { public string City { get; set; } = ""; }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.City), "Address.City")]
                public partial D Map(S s);
            }
            """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("__dwarf_target.Address.City = s.City", gen, StringComparison.Ordinal);
        Assert.Contains("is null) __dwarf_target.Address = new global::Demo.Addr()", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_leaves_same_intermediate_instantiate_once()
    {
        // Regression: City + Street into one Address must produce ONE instantiation, not a DWARF046 conflict.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; public string Street { get; set; } = ""; }
            public class S { public string City { get; set; } = ""; public string Street { get; set; } = ""; }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.City), "Address.City")]
                [MapProperty(nameof(S.Street), "Address.Street")]
                public partial D Map(S s);
            }
            """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Equal(1, Count(gen, "__dwarf_target.Address = new global::Demo.Addr()"));
        Assert.Contains("__dwarf_target.Address.City = s.City", gen, StringComparison.Ordinal);
        Assert.Contains("__dwarf_target.Address.Street = s.Street", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Unflatten_with_leaf_conversion_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string Zip { get; set; } = ""; }
            public class S { public int Zip { get; set; } }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.Zip), "Address.Zip")]
                public partial D Map(S s);
            }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Dotted_source_into_unflatten_target_compiles()
    {
        // Phase 3 (deep source) + Phase 4 (unflatten target) combined.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string Name { get; set; } = ""; }
            public class Addr { public string City { get; set; } = ""; }
            public class S { public Inner Inner { get; set; } = new(); }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty("Inner.Name", "Address.City")]
                public partial D Map(S s);
            }
            """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("__dwarf_target.Address.City = s.Inner.Name", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Multi_level_target_reports_DWARF045()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Geo { public double Lat { get; set; } }
            public class Addr { public Geo Geo { get; set; } = new(); }
            public class S { public double Lat { get; set; } }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.Lat), "Address.Geo.Lat")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF045"));
    }

    [Fact]
    public void Struct_intermediate_reports_DWARF045()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public struct Addr { public string City { get; set; } }
            public class S { public string City { get; set; } = ""; }
            public class D { public Addr Address { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.City), "Address.City")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF045"));
    }

    [Fact]
    public void Intermediate_without_parameterless_ctor_reports_DWARF045()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public Addr(int x) { City = x.ToString(); } public string City { get; set; } }
            public class S { public string City { get; set; } = ""; }
            public class D { public Addr Address { get; set; } = new(0); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.City), "Address.City")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF045"));
    }

    [Fact]
    public void Unknown_leaf_reports_DWARF045()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class S { public string Zip { get; set; } = ""; }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.Zip), "Address.Zip")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF045"));
    }

    [Fact]
    public void Unknown_root_reports_DWARF045()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public string City { get; set; } = ""; }
            public class D { public string Other { get; set; } = ""; }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.City), "Nope.City")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF045"));
    }

    [Fact]
    public void Direct_mapping_of_root_then_unflatten_reports_DWARF046()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class S { public Addr Address { get; set; } = new(); public string City { get; set; } = ""; }
            public class D { public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.Address), nameof(D.Address))]
                [MapProperty(nameof(S.City), "Address.City")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF046"));
    }
}
