// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// <c>.IgnoreSource</c> on a <c>MapConfig&lt;S,T&gt;</c> convention method must suppress the DWARF039
/// source-coverage suggestion (under <c>RequiredMapping = RequiredMappingStrategy.Both</c>) for that source
/// member, the same way <c>[MapIgnoreSource]</c> does for the pair-scoped attribute front-end.
/// </summary>
public class MapConfigGeneratorTests
{
    private static Diagnostic? D039(System.Collections.Generic.IEnumerable<Diagnostic> diags)
        => diags.FirstOrDefault(d => d.Id == "DWARF039");

    private static Diagnostic? D068(System.Collections.Generic.IEnumerable<Diagnostic> diags)
        => diags.FirstOrDefault(d => d.Id == "DWARF068");

    private static Diagnostic? D069(System.Collections.Generic.IEnumerable<Diagnostic> diags)
        => diags.FirstOrDefault(d => d.Id == "DWARF069");

    [Fact]
    public void IgnoreSource_on_config_method_silences_DWARF039()
    {
        // NOTE: the DWARF039 source-coverage check only runs for the explicit-partial-method pair path
        // (see MapperExtractor.cs's `requiredMapping == 1` block), not for the separate [GenerateMap<S,T>]
        // synthesis path — so the pair here is declared the same way the sibling SourceCoverageGeneratorTests
        // do it, via an explicit `public partial D Map(S s);`, with a MapConfig<S,D> convention method
        // alongside it purely to supply `.IgnoreSource`.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } public int Unused { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
            public partial class M
            {
                private static void Cfg(MapConfig<S, D> c) => c.Map(t => t.A, s => s.A).IgnoreSource(s => s.Unused);
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// F3 catch-all ("never silent" for an unrecognized op) defensive net: the receiver-gate (F1) plus the
    /// typed <see cref="DwarfMapper.MapConfig{TSource,TTarget}"/> surface make it impossible to construct a
    /// genuine unrecognized-op call through valid C# — there is no method to call that doesn't exist. So this
    /// test instead proves the catch-all's NEGATIVE space: a config method chaining `.MapOr` and `.Value`
    /// (Batch B's two new recognized ops) must emit NO DWARF068, confirming the new `else if` branches are
    /// reached before the final catch-all `else`.
    /// </summary>
    [Fact]
    public void MapOr_and_Value_on_config_method_emit_no_DWARF068()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int? V { get; set; } public int A { get; set; } }
            public class D { public int V { get; set; } public int Tag { get; set; } }
            [DwarfMapper]
            [GenerateMap<S, D>]
            public partial class M
            {
                private static void Cfg(MapConfig<S, D> c) => c.MapOr(t => t.V, s => s.V, 5).Value(t => t.Tag, 99);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D068(diags));
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>DWARF068: a non-member-access selector (a method CALL, not a member selector) must be
    /// rejected by <c>TryReadMemberPath</c> and reported, not silently mis-parsed.</summary>
    [Fact]
    public void Map_with_method_call_selector_reports_DWARF068()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper]
            [GenerateMap<S, D>]
            public partial class M
            {
                private static int Identity(int x) => x;
                private static void Cfg(MapConfig<S, D> c) => c.Map(t => t.A, s => Identity(s.A));
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Equal("DWARF068", D068(diags)?.Id);
    }

    /// <summary>DWARF068: an inline converter lambda (not a method group) in the 3-arg <c>.Map</c> overload
    /// must be rejected by <c>TryReadMethodGroup</c> and reported.</summary>
    [Fact]
    public void Map_with_inline_converter_lambda_reports_DWARF068()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper]
            [GenerateMap<S, D>]
            public partial class M
            {
                private static void Cfg(MapConfig<S, D> c) => c.Map(t => t.A, s => s.A, v => v);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Equal("DWARF068", D068(diags)?.Id);
    }

    /// <summary>DWARF069: the same destination member ('A' on D) is configured once by
    /// <c>[MapProperty&lt;S,T&gt;]</c> AND once by a <c>MapConfig&lt;S,T&gt;</c> <c>.Map</c> call — a genuine
    /// attribute-vs-config collision, caught by <c>ReportMapConfigConflicts</c> at the merge point.</summary>
    [Fact]
    public void Attribute_and_config_both_targeting_same_member_reports_DWARF069()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } public int B { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper]
            [GenerateMap<S, D>]
            [MapProperty<S, D>("A", "A")]
            public partial class M
            {
                private static void Cfg(MapConfig<S, D> c) => c.Map(t => t.A, s => s.B);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Equal("DWARF069", D069(diags)?.Id);
    }

    /// <summary>DWARF069: two <c>.Map</c> calls within the SAME config method targeting the same destination
    /// member ('A' on D) — a config-vs-config collision.</summary>
    [Fact]
    public void Config_with_two_Map_calls_on_same_member_reports_DWARF069()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } public int B { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper]
            [GenerateMap<S, D>]
            public partial class M
            {
                private static void Cfg(MapConfig<S, D> c) => c.Map(t => t.A, s => s.A).Map(t => t.A, s => s.B);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Equal("DWARF069", D069(diags)?.Id);
    }

    /// <summary>DWARF056 reuse: a <c>MapConfig&lt;S,Other&gt;</c> convention method targets a type pair
    /// (S,Other) that has no <c>[GenerateMap]</c> (the class's only generated map is &lt;S,T&gt;) — the
    /// generator's "matched nothing" sweep must flag it for a config-origin entry the same way it does for
    /// an attribute-origin one, since config entries carry the same Consumed flag through pairProps.</summary>
    [Fact]
    public void MapConfig_targeting_a_never_mapped_pair_reports_DWARF056()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } }
            public class T { public int A { get; set; } }
            public class Other { public int A { get; set; } }
            [DwarfMapper]
            [GenerateMap<S, T>]
            public partial class M
            {
                private static void Cfg(MapConfig<S, Other> c) => c.Map(t => t.A, s => s.A);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF056");
    }
}
