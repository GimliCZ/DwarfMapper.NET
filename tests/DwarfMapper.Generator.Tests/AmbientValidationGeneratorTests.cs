// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Phase-5 tests for the ambient root validation (DWARF061): in the assembly marked
/// <c>[assembly: DwarfMapperValidationRoot]</c>, every consumed ambient map must be provided by some
/// assembly in the graph, else a compile-time error. Mid-tier assemblies (no root marker) do not validate.
/// </summary>
public sealed class AmbientValidationGeneratorTests
{
    private const string Consumer = """
        public class Doc { public int V { get; set; } }
        public class Model { public int V { get; set; } }
        public class Consumer
        {
            public Model Convert(global::DwarfMapper.IDwarfMapper m, Doc d) => m.Map<Model>(d);
        }
        """;

    [Fact]
    public void Root_with_unprovided_consumed_map_reports_DWARF061()
    {
        const string s = "[assembly: global::DwarfMapper.DwarfMapperValidationRoot]\nnamespace Demo;\n" + Consumer;

        var (diags, _) = GeneratorTestHarness.Run(s);

        Assert.Contains(diags, d => d.Id == "DWARF061" && d.Severity == DiagnosticSeverity.Error
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Demo.Doc", System.StringComparison.Ordinal)
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Demo.Model", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Root_with_provided_map_does_not_report_DWARF061()
    {
        // The same consumed pair is now provided by a [GenerateMap] in this (root) assembly.
        const string s = "[assembly: global::DwarfMapper.DwarfMapperValidationRoot]\nnamespace Demo;\n" + Consumer + """

            [global::DwarfMapper.DwarfMapper]
            [global::DwarfMapper.GenerateMap<Doc, Model>]
            public partial class M { }
            """;

        var (diags, _) = GeneratorTestHarness.Run(s);

        Assert.DoesNotContain(diags, d => d.Id == "DWARF061");
    }

    [Fact]
    public void Root_emits_runtime_Validate_method_over_consumed_pairs()
    {
        const string s = "[assembly: global::DwarfMapper.DwarfMapperValidationRoot]\nnamespace Demo;\n" + Consumer + """

            [global::DwarfMapper.DwarfMapper]
            [global::DwarfMapper.GenerateMap<Doc, Model>]
            public partial class M { }
            """;

        var validate = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.Validate.g.cs");

        Assert.Contains("public static void Validate()", validate, System.StringComparison.Ordinal);
        Assert.Contains("IsProvided(typeof(global::Demo.Doc), typeof(global::Demo.Model))", validate, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Non_root_does_not_emit_Validate_method()
    {
        const string s = "namespace Demo;\n" + Consumer;
        Assert.Equal(string.Empty, GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.Validate.g.cs"));
    }

    [Fact]
    public void Non_root_assembly_does_not_validate()
    {
        // No [DwarfMapperValidationRoot]: a mid-tier assembly only emits manifests, never DWARF061
        // (it sees a partial graph).
        const string s = "namespace Demo;\n" + Consumer;

        var (diags, _) = GeneratorTestHarness.Run(s);

        Assert.DoesNotContain(diags, d => d.Id == "DWARF061");
    }
}
