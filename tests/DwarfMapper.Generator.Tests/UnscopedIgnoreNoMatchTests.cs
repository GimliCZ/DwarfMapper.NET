// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>DWARF095</c> — an unscoped <c>[MapIgnore("Name")]</c> whose name matches nothing (B20), and the
///     <c>B21</c> comparer ruling it makes loud: directive names bind ORDINALLY even under
///     <c>[DwarfMapper(CaseInsensitive = true)]</c>, and a name that therefore matches nothing is reported
///     rather than silently excluding nothing. The registry mirror (<c>DWARFR12</c>, B15) is pinned in
///     <see cref="RegistryDiagnosticsGenTests" />, where the family's completeness gate demands its trigger.
/// </summary>
public sealed class UnscopedIgnoreNoMatchTests
{
    // ── Method site ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_method_MapIgnore_naming_no_destination_member_reports_DWARF095()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper]
                           public partial class M
                           {
                               [MapIgnore("Typo")]
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF095"
                                          && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                                              .Contains("\"Typo\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_method_MapIgnore_naming_a_real_member_is_honoured_and_reports_nothing()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } public string? Extra { get; set; } }
                           public class Target { public int Id { get; set; } public string? Extra { get; set; } }

                           [DwarfMapper]
                           public partial class M
                           {
                               [MapIgnore("Extra")]
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
        Assert.DoesNotContain("Extra =", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_update_into_method_MapIgnore_naming_nothing_reports_DWARF095()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper]
                           public partial class M
                           {
                               [MapIgnore("Typo")]
                               public partial void Map(Source s, Target d);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF095");
    }

    [Fact]
    public void A_projection_method_MapIgnore_naming_nothing_reports_DWARF095()
    {
        const string src = """
                           using System.Linq;
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper]
                           public partial class M
                           {
                               [MapIgnore("Typo")]
                               public partial IQueryable<Target> Project(IQueryable<Source> q);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF095");
    }

    /// <summary>
    ///     A span map's method-site directives are <c>DWARF090</c>'s to report — matched or not — so the
    ///     dead-name check stands down there entirely: one attribute must not earn two ids pointing in two
    ///     directions.
    /// </summary>
    [Fact]
    public void A_span_method_MapIgnore_reports_DWARF090_only_never_DWARF095()
    {
        const string src = """
                           using System;
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper]
                           public partial class M
                           {
                               [MapIgnore("Typo")]
                               public partial void Map(ReadOnlySpan<Source> src, Span<Target> dst);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF090");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
    }

    // ── Class site ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_class_MapIgnore_matching_no_pair_at_all_reports_DWARF095()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper]
                           [MapIgnore("Typo")]
                           public partial class M
                           {
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF095"
                                          && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                                              .Contains("any pair this mapper maps", StringComparison.Ordinal));
    }

    /// <summary>
    ///     The tolerance the element-wise gate documents, pinned as a legal neighbour (the B4 lesson): a
    ///     class-wide ignore that is about ONE of the class's pairs matches nothing on the other, and that is
    ///     how it is meant to work — no <c>DWARF095</c>.
    /// </summary>
    [Fact]
    public void A_class_MapIgnore_matching_one_of_two_pairs_reports_nothing()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class SourceA { public int Id { get; set; } public string? OnlyOnA { get; set; } }
                           public class TargetA { public int Id { get; set; } public string? OnlyOnA { get; set; } }
                           public class SourceB { public int Id { get; set; } }
                           public class TargetB { public int Id { get; set; } }

                           [DwarfMapper]
                           [MapIgnore("OnlyOnA")]
                           public partial class M
                           {
                               public partial TargetA Map(SourceA s);
                               public partial TargetB Map(SourceB s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
    }

    /// <summary>
    ///     The liveness set is resolution's own, not "writables": an ignore naming a READ-ONLY destination
    ///     member legitimately suppresses the silent-loss warning, so it must never read as dead.
    /// </summary>
    [Fact]
    public void A_class_MapIgnore_naming_a_read_only_member_is_live_not_dead()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } public string? Computed { get; set; } }
                           public class Target { public int Id { get; set; } public string? Computed => null; }

                           [DwarfMapper]
                           [MapIgnore("Computed")]
                           public partial class M
                           {
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
        // And the suppression it protects still works: DWARF007's own remedy prescribes exactly this
        // [MapIgnore], so reading it as dead would have broken what the diagnostic itself recommends.
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
    }

    /// <summary>
    ///     A class-wide ignore naming a member of a span map's ELEMENT pair is <c>DWARF090</c>'s report
    ///     (dropped, with the pair-scoped remedy) — it is not DEAD, so <c>DWARF095</c> stays silent.
    /// </summary>
    [Fact]
    public void A_class_MapIgnore_matching_a_span_element_member_is_DWARF090_not_DWARF095()
    {
        const string src = """
                           using System;
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } public string? Extra { get; set; } }
                           public class Target { public int Id { get; set; } public string? Extra { get; set; } }

                           [DwarfMapper]
                           [MapIgnore("Extra")]
                           public partial class M
                           {
                               public partial void Map(ReadOnlySpan<Source> src, Span<Target> dst);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF090");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
    }

    /// <summary>
    ///     The stand-down clause: a class carrying an endpoint whose unscoped-ignore consumption the walk
    ///     cannot see (here a top-level collection map) must not guess a class-site "names nothing" — a false
    ///     positive would break a working suppression, which is B19's exact genre.
    /// </summary>
    [Fact]
    public void A_class_with_a_top_level_collection_map_never_gets_a_class_site_DWARF095()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper]
                           [MapIgnore("Typo")]
                           public partial class M
                           {
                               public partial List<Target> Map(List<Source> s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
    }

    /// <summary>
    ///     The boundary the liveness walk relies on, pinned so it cannot move in silence: an auto-synthesized
    ///     NESTED pair is resolved with pair-scoped <c>[MapIgnore&lt;T&gt;]</c> only (<c>nestedIgnores</c>) —
    ///     class-level unscoped ignores never reach it — so a class ignore naming a member that exists only on
    ///     a nested target really is dead and is reported. If that wiring ever changes, this test goes red and
    ///     forces the class-site judge to walk nested targets too rather than emit a false "names nothing".
    /// </summary>
    [Fact]
    public void A_class_MapIgnore_naming_only_a_NESTED_targets_member_is_dead_and_reported()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Leaf { public int Id { get; set; } public string? OnlyOnLeaf { get; set; } }
                           public class LeafDto { public int Id { get; set; } public string? OnlyOnLeaf { get; set; } }
                           public class Source { public int Id { get; set; } public Leaf? Child { get; set; } }
                           public class Target { public int Id { get; set; } public LeafDto? Child { get; set; } }

                           [DwarfMapper]
                           [MapIgnore("OnlyOnLeaf")]
                           public partial class M
                           {
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF095");
        // And the reason it is dead: the nested pair maps the member the class-level ignore names.
        Assert.Contains("OnlyOnLeaf = ", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The stand-down clause's SECOND site (the sibling of the top-level collection map above): a
    ///     <c>[MapDerivedType]</c> dispatch resolves its arms through <c>TryResolveConversion</c>, whose
    ///     synthesis this walk does not follow, so the class-site verdict stands down for the whole class.
    ///     Pinned because the flag has two set-sites and a guard that reaches only one of them is round 20's
    ///     "the guard did not propagate" defect.
    /// </summary>
    [Fact]
    public void A_class_with_a_MapDerivedType_dispatch_never_gets_a_class_site_DWARF095()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public sealed class SourceDerived : Source { public string? Extra { get; set; } }
                           public class Target { public int Id { get; set; } }
                           public sealed class TargetDerived : Target { public string? Extra { get; set; } }

                           [DwarfMapper]
                           [MapIgnore("Typo")]
                           public partial class M
                           {
                               [MapDerivedType(typeof(SourceDerived), typeof(TargetDerived))]
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
    }

    // ── B21: the CaseInsensitive × ignore ruling, pinned in both directions ──────

    /// <summary>
    ///     B21 direction one: under <c>CaseInsensitive = true</c>, <c>[MapIgnore("id")]</c> does NOT exclude
    ///     <c>Id</c> — directive names bind ordinally under every option — and since B20 that mismatch is
    ///     loud (<c>DWARF095</c>) instead of a silent no-op.
    /// </summary>
    [Fact]
    public void Under_CaseInsensitive_a_case_mismatched_ignore_excludes_nothing_and_reports_DWARF095()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } }
                           public class Target { public int Id { get; set; } }

                           [DwarfMapper(CaseInsensitive = true)]
                           public partial class M
                           {
                               [MapIgnore("id")]
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF095");
        Assert.Contains("Id =", generated, StringComparison.Ordinal); // Id is still mapped.
    }

    /// <summary>B21 direction two: the exactly-cased name excludes the member, with no diagnostic.</summary>
    [Fact]
    public void Under_CaseInsensitive_an_exactly_cased_ignore_excludes_the_member_silently()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public int Id { get; set; } public string? Extra { get; set; } }
                           public class Target { public int Id { get; set; } public string? Extra { get; set; } }

                           [DwarfMapper(CaseInsensitive = true)]
                           public partial class M
                           {
                               [MapIgnore("Extra")]
                               public partial Target Map(Source s);
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF095");
        Assert.DoesNotContain("Extra =", generated, StringComparison.Ordinal);
    }
}
