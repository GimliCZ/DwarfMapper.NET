// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// The "never silent" promise has a corollary: when a mapper has SEVERAL independent problems, the generator
/// must report ALL of them, not bail on the first. A generator that stops at the first error forces the
/// developer through a fix-one-rebuild-find-the-next loop and, worse, hides how much is actually wrong — a
/// second unmapped member is exactly as silent as if no diagnostic existed until the first is resolved.
/// <para>
/// The generator already behaves correctly; these lock it against a future early-return regression. Each case
/// is a distinct masking axis: same diagnostic repeated, different diagnostic kinds, and different methods.
/// </para>
/// </summary>
public class DiagnosticsDoNotMaskEachOtherTests
{
    private static string[] Ids(string source) =>
        GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();

    [Fact]
    public void Multiple_unmapped_members_all_report()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; set; } }
            public class Dst { public int A { get; set; } public int OrphanB { get; set; } public int OrphanC { get; set; } }
            [DwarfMapper]
            public partial class M { public partial Dst Map(Src s); }
            """;

        var messages = GeneratorTestHarness.Run(source).Diagnostics
            .Where(d => d.Id == "DWARF001")
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();

        // Both orphans must be named — not just OrphanB, which is what a first-error-wins generator would show.
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Contains("OrphanB", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("OrphanC", StringComparison.Ordinal));
    }

    [Fact]
    public void Different_diagnostic_kinds_in_one_method_all_report()
    {
        // A bad [MapProperty] target (DWARF008) and an unrelated unmapped member (DWARF001) are independent
        // problems; resolving either does not resolve the other, so both must surface at once.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; set; } }
            public class Dst { public int A { get; set; } public int Orphan { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("A", "NoSuchTarget")]
                public partial Dst Map(Src s);
            }
            """;

        var ids = Ids(source);
        Assert.Contains("DWARF008", ids);
        Assert.Contains("DWARF001", ids);
    }

    [Fact]
    public void A_broken_method_does_not_suppress_another_methods_diagnostic()
    {
        // Two independent mapping methods, each broken. The generator must report BOTH — a failure extracting
        // one method must not abort the whole class and swallow the other's diagnostic.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class SrcA { public int X { get; set; } }
            public class DstA { public int X { get; set; } public int OrphanA { get; set; } }
            public class SrcB { public int Y { get; set; } }
            public class DstB { public int Y { get; set; } public int OrphanB { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial DstA MapA(SrcA s);
                public partial DstB MapB(SrcB s);
            }
            """;

        var byLine = GeneratorTestHarness.Run(source).Diagnostics
            .Where(d => d.Id == "DWARF001")
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToList();

        Assert.Contains(byLine, m => m.Contains("OrphanA", StringComparison.Ordinal));
        Assert.Contains(byLine, m => m.Contains("OrphanB", StringComparison.Ordinal));
    }
}
