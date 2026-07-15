// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Explicitly exercises the DEFAULT values of the option enums — <c>NameConvention.Exact</c>,
/// <c>OnCycleStrategy.Throw</c>, <c>ReferenceHandlingStrategy.None</c>. Their behaviour is implicitly hit by
/// every mapper that doesn't override them, but nothing named them, so the enum-value coverage gate
/// (AssemblyScanTests.Scan5) was passing vacuously on the common words "Exact"/"Throw"/"None". These give the
/// gate a real reference AND pin what each default actually does.
/// </summary>
public class ExplicitDefaultEnumValuesTests
{
    [Fact]
    public void NameConvention_Exact_is_case_sensitive()
    {
        // Under the default NameConvention.Exact, 'Name' does NOT pair with 'name' → the destination is unmapped.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Name { get; set; } }
            public class Dst { public int name { get; set; } }
            [DwarfMapper(NameConvention = NameConvention.Exact)]
            public partial class M { public partial Dst Map(Src s); }
            """;

        Assert.Contains(GeneratorTestHarness.Run(source).Diagnostics, d => d.Id == "DWARF001");
    }

    [Fact]
    public void OnCycleStrategy_Throw_and_ReferenceHandlingStrategy_None_compile_as_the_defaults()
    {
        // Explicitly stating the defaults must be a no-op relative to omitting them: it compiles cleanly.
        // (The cyclic-throw and no-identity-map runtime behaviour is covered by the depth/graph suites; here the
        // point is a genuine qualified reference to each default value.)
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Node { public int V { get; set; } public Node? Next { get; set; } }
            public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
            [DwarfMapper(OnCycle = OnCycleStrategy.Throw, ReferenceHandling = ReferenceHandlingStrategy.None)]
            public partial class M { public partial NodeDto Map(Node n); }
            """;

        Assert.DoesNotContain(GeneratorTestHarness.Run(source).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(source));
    }
}
