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
}
