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
}
