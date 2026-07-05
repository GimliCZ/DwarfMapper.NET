// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Source-member coverage (Phase 1). Under <c>[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]</c>
///     every readable source member must be read by some destination (member OR constructor argument); a
///     source consumed by nothing surfaces <c>DWARF039</c> (Info), unless suppressed by <c>[MapIgnoreSource]</c>.
///     The default (<c>Target</c>) preserves today's destination-only gate.
/// </summary>
public class SourceCoverageGeneratorTests
{
    private static Diagnostic? D039(IEnumerable<Diagnostic> diags)
    {
        return diags.FirstOrDefault(d => d.Id == "DWARF039");
    }

    [Fact]
    public void Default_strategy_does_not_flag_unconsumed_source()
    {
        // RequiredMappingStrategy.Target is the default — an extra source member is fine.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Extra { get; set; } = ""; }
                           public class D { public int Id { get; set; } }
                           [DwarfMapper] public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Both_flags_unconsumed_source_as_info()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Extra { get; set; } = ""; }
                           public class D { public int Id { get; set; } }
                           [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = D039(diags);
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Info, d!.Severity);
        Assert.Contains("Extra", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MapIgnoreSource_on_method_silences_it()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Extra { get; set; } = ""; }
                           public class D { public int Id { get; set; } }
                           [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                           public partial class M
                           {
                               [MapIgnoreSource(nameof(S.Extra))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
    }

    [Fact]
    public void MapIgnoreSource_on_class_silences_it()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Extra { get; set; } = ""; }
                           public class D { public int Id { get; set; } }
                           [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                           [MapIgnoreSource(nameof(S.Extra))]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
    }

    [Fact]
    public void All_source_consumed_is_silent_under_Both()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Name { get; set; } = ""; }
                           public class D { public int Id { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Flattened_root_counts_as_consumed()
    {
        // [Flatten("Address")] consumes Address.City / Address.Street → the Address root must NOT be flagged.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Addr { public string City { get; set; } = ""; public string Street { get; set; } = ""; }
                           public class S { public int Id { get; set; } public Addr Address { get; set; } = new(); }
                           public class D { public int Id { get; set; } public string City { get; set; } = ""; public string Street { get; set; } = ""; }
                           [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                           public partial class M
                           {
                               [Flatten(nameof(S.Address))]
                               public partial D Map(S s);
                           }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Constructor_consumed_source_counts()
    {
        // A record target consumes Id/Name via constructor args — neither should be flagged under Both.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public string Name { get; set; } = ""; }
                           public record D(int Id, string Name);
                           [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                           public partial class M { public partial D Map(S s); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D039(diags));
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Coverage_check_does_not_change_emitted_code()
    {
        // DWARF039 is diagnostics-only: generated code must be byte-identical with/without Both.
        const string baseSrc = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int Id { get; set; } public string Extra { get; set; } = ""; }
                               public class D { public int Id { get; set; } }
                               """;
        var (_, genTarget) = GeneratorTestHarness.Run(baseSrc
                                                      + "[DwarfMapper] public partial class M { public partial D Map(S s); }");
        var (_, genBoth) = GeneratorTestHarness.Run(baseSrc
                                                    + "[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)] public partial class M { public partial D Map(S s); }");
        Assert.Equal(genTarget, genBoth);
    }
}
