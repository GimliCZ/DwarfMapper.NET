// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Deep source paths in [MapProperty] (Phase 3): a dotted source like "Customer.Name" reads through the
///     object graph (member names never contain dots, so it is unambiguous). The leaf type drives the
///     conversion; the path is emitted verbatim (s.Customer.Name). An unknown segment is DWARF043; a nullable
///     interior hop (can NRE at runtime) is the DWARF044 suggestion.
/// </summary>
public class DeepSourcePathGeneratorTests
{
    private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
    {
        return diags.FirstOrDefault(d => d.Id == id);
    }

    [Fact]
    public void Two_hop_path_reads_through_and_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Customer { public string Name { get; set; } = ""; }
                           public class S { public Customer Customer { get; set; } = new(); }
                           public class D { public string CustomerName { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Customer.Name", nameof(D.CustomerName))]
                               public partial D Map(S s);
                           }
                           """;
        var gen = GeneratorAssert.CompilesClean(src);
        Assert.Contains("s.Customer.Name", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Three_hop_path_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class City { public string Name { get; set; } = ""; }
                           public class Addr { public City City { get; set; } = new(); }
                           public class S { public Addr Address { get; set; } = new(); }
                           public class D { public string Town { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Address.City.Name", nameof(D.Town))]
                               public partial D Map(S s);
                           }
                           """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("s.Address.City.Name", gen, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void Path_with_leaf_conversion_uses_converter()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Customer { public int Age { get; set; } }
                           public class S { public Customer Customer { get; set; } = new(); }
                           public class D { public string CustomerAge { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Customer.Age", nameof(D.CustomerAge))]
                               public partial D Map(S s);
                           }
                           """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("s.Customer.Age", gen, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void Path_with_explicit_Use_converter_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Customer { public string Name { get; set; } = ""; }
                           public class S { public Customer Customer { get; set; } = new(); }
                           public class D { public int NameLen { get; set; } }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Customer.Name", nameof(D.NameLen), Use = nameof(Len))]
                               public partial D Map(S s);
                               private static int Len(string v) => v.Length;
                           }
                           """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("Len(s.Customer.Name)", gen, StringComparison.Ordinal);
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void Unknown_segment_reports_DWARF043()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Customer { public string Name { get; set; } = ""; }
                           public class S { public Customer Customer { get; set; } = new(); }
                           public class D { public string CustomerCity { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Customer.City", nameof(D.CustomerCity))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF043"));
    }

    [Fact]
    public void Unknown_root_segment_reports_DWARF043()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public string X { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Nope.Name", nameof(D.X))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF043"));
    }

    [Fact]
    public void Nullable_interior_hop_reports_DWARF044_warning()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class Customer { public string Name { get; set; } = ""; }
                           public class S { public Customer? Customer { get; set; } }
                           public class D { public string CustomerName { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Customer.Name", nameof(D.CustomerName))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        var d = Find(diags, "DWARF044");
        Assert.NotNull(d);
        // Item 8: a nullable interior hop can NRE at runtime → Warning, not a mere suggestion.
        Assert.Equal(DiagnosticSeverity.Warning, d!.Severity);
    }

    [Fact]
    public void Non_nullable_path_does_not_report_DWARF044()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class Customer { public string Name { get; set; } = ""; }
                           public class S { public Customer Customer { get; set; } = new(); }
                           public class D { public string CustomerName { get; set; } = ""; }
                           [DwarfMapper] public partial class M
                           {
                               [MapProperty("Customer.Name", nameof(D.CustomerName))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.Null(Find(diags, "DWARF044"));
    }

    // Fuzz: paths of increasing depth all resolve and compile.
    // CA1305: this method only assembles C# source text (ASCII identifiers / fixed literals) — there is
    // no locale-sensitive formatting, so the invariant-culture overloads add only noise here.
#pragma warning disable CA1305
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Paths_of_varying_depth_compile(int depth)
    {
        // Build a chain N0 -> N1 -> ... where each Ni has a `Next` of type N(i+1), leaf has `Value`.
        var sb = new StringBuilder();
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine("namespace Demo;");
        for (var i = 0; i < depth; i++)
        {
            var next = i < depth - 1
                ? $"public N{i + 1} Next {{ get; set; }} = new();"
                : "public string Value { get; set; } = \"\";";
            sb.AppendLine($"public class N{i} {{ {next} }}");
        }

        sb.AppendLine("public class S { public N0 Root { get; set; } = new(); }");
        sb.AppendLine("public class D { public string Leaf { get; set; } = \"\"; }");
        var path = "Root." + string.Join(".", Enumerable.Repeat("Next", depth - 1)) + (depth > 1 ? ".Value" : "");
        // depth>=2 guarantees at least Root.Value or Root.Next...Value
        if (depth == 1) path = "Root.Value";
        sb.AppendLine(
            $"[DwarfMapper] public partial class M {{ [MapProperty(\"{path}\", nameof(D.Leaf))] public partial D Map(S s); }}");
        var src = sb.ToString();

        GeneratorAssert.CompilesClean(src);
    }
#pragma warning restore CA1305
}
