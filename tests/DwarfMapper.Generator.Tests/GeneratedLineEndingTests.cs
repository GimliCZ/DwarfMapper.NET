// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-024 — generated output must use ONE line-ending convention.
///     <para>
///     The emitters build files with <c>StringBuilder.AppendLine</c> (which writes
///     <c>Environment.NewLine</c> — CRLF on Windows, LF elsewhere) and then splice in synthesized converter
///     bodies built with hard <c>"\n"</c> literals. A single file thus mixed both, and the mixture depended on
///     the build machine's OS, so the same input produced byte-different output on Windows and Linux —
///     defeating reproducible builds and making any byte-level comparison platform-dependent.
///     Everything now goes through AddNormalizedSource.
///     </para>
/// </summary>
public class GeneratedLineEndingTests
{
    // A mapper that forces BOTH construction paths into one file: plain member assignments come from the
    // AppendLine emitter, while the enum + collection members pull in synthesized helper bodies written with
    // hard "\n".
    private const string MixedSource = """
                                       using DwarfMapper;
                                       using System.Collections.Generic;
                                       namespace Demo;
                                       public enum SrcColor { Red = 1, Green = 2 }
                                       public enum DstColor { Red = 1, Green = 2 }
                                       public class Src { public int Id { get; set; } public SrcColor C { get; set; } public List<int> Xs { get; set; } = new(); }
                                       public class Dst { public long Id { get; set; } public DstColor C { get; set; } public List<long> Xs { get; set; } = new(); }
                                       [DwarfMapper(EnumStrategy = EnumStrategy.ByName)]
                                       public partial class M { public partial Dst Map(Src s); }
                                       """;

    [Fact]
    public void Generated_source_contains_no_CRLF()
    {
        var (_, generated) = GeneratorTestHarness.Run(MixedSource);

        Assert.NotEmpty(generated);
        Assert.DoesNotContain("\r", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_generated_source_contains_no_CRLF()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } public List<int> Xs { get; set; } = new(); }
                         public class Dto { public long Id { get; set; } public List<long> Xs { get; set; } = new(); }
                         """;
        var (_, generated) = GeneratorTestHarness.RunMapToWithSource(s);

        Assert.NotEmpty(generated);
        Assert.DoesNotContain("\r", generated, StringComparison.Ordinal);
    }
}
