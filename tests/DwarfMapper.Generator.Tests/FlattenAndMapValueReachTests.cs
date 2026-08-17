// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Which endpoints <c>[Flatten]</c> and <c>[MapValue]</c> reach, and what they say where they cannot.
/// </summary>
/// <remarks>
///     <para>
///         Both directives were measured SILENT at projection and at the two element-wise endpoints — accepted,
///         no diagnostic, output byte-identical to the same mapper without them (recorded as <c>D9</c> and
///         <c>D10</c>). The two have different answers and the difference is not a preference: a projection
///         becomes an expression tree, and <c>__s.Child.X</c> is the navigation access every query provider
///         translates, so a flatten there is HONOURED; an element-wise map resolves no members of its own and
///         takes its configuration from pair-scoped directives only, so a method-scoped directive there is
///         REFUSED, with the pair-scoped form that does work named in the message.
///     </para>
///     <para>
///         The remedies below are pinned because they were measured before they were prescribed. This
///         repository has already shipped a <c>DWARF090</c> tail asserting an endpoint behaviour that was
///         false, and reverted it; a diagnostic that sends the reader to a form nobody ran is the same defect.
///     </para>
/// </remarks>
public class FlattenAndMapValueReachTests
{
    private const string Types = """
        using System.Linq;
        using DwarfMapper;
        namespace Demo;
        public struct Inner { public int X { get; set; } }
        public class Src { public int Id { get; set; } public Inner Child { get; set; } }
        public class Dst { public int Id { get; set; } public int X { get; set; } }
        """;

    private const string Flat = """
        using System.Linq;
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Id { get; set; } public string Name { get; set; } }
        public class Dst { public int Id { get; set; } public string Name { get; set; } }
        """;

    // ── Projection now resolves the directive ────────────────────────────────

    [Fact]
    public void Projection_pulls_a_flattened_leaf_up_exactly_as_the_create_map_does()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("Child")]
                public partial Dst Map(Src s);

                [Flatten("Child")]
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """);

        // The runtime map has always done this; the projection did nothing at all.
        Assert.Contains("s.Child.X", generated, StringComparison.Ordinal);
        Assert.Contains("__s.Child.X", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_projection_flatten_root_that_names_nothing_is_refused_by_the_create_maps_own_guard()
    {
        // DWARF016 is not a new check written for this endpoint — ResolveFlattenInfos is the one walk both
        // resolvers call, so a root the runtime path refuses cannot be quietly accepted by the other.
        GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("NoSuchMember")]
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """, "DWARF016");
    }

    [Fact]
    public void A_projection_flatten_root_that_names_a_scalar_is_refused_too()
    {
        GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("Id")]
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """, "DWARF016");
    }

    [Fact]
    public void Two_flatten_roots_supplying_one_destination_member_are_ambiguous_at_projection_as_well()
    {
        GeneratorAssert.Reports("""
            using System.Linq;
            using DwarfMapper;
            namespace Demo;
            public struct Inner { public int X { get; set; } }
            public struct Other { public int X { get; set; } }
            public class Src { public int Id { get; set; } public Inner A { get; set; } public Other B { get; set; } }
            public class Dst { public int Id { get; set; } public int X { get; set; } }

            [DwarfMapper]
            public partial class M
            {
                [Flatten("A")]
                [Flatten("B")]
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """, "DWARF017");
    }

    [Fact]
    public void A_nullable_flatten_root_warns_at_the_create_map_and_deliberately_not_at_projection()
    {
        // DWARF044 says a null interior throws when dereferenced. True of emitted C#; false of a translated
        // path, which the provider turns into a join yielding null. The dotted [MapProperty] source already
        // makes that call at this endpoint, and ResolveFlattenInfos takes it as a parameter so the two cannot
        // drift apart. Asserted in BOTH directions: a check that only proved the silence would pass equally
        // if the whole flatten had stopped resolving.
        const string nullableRoot = """
            using System.Linq;
            using DwarfMapper;
            namespace Demo;
            public class Inner { public int X { get; set; } }
            public class Src { public int Id { get; set; } public Inner? Child { get; set; } }
            public class Dst { public int Id { get; set; } public int X { get; set; } }
            """;

        GeneratorAssert.Reports(nullableRoot + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("Child")]
                public partial Dst Map(Src s);
            }
            """, "DWARF044", Microsoft.CodeAnalysis.NullableContextOptions.Enable);

        GeneratorAssert.DoesNotReport(nullableRoot + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("Child")]
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """, "DWARF044", Microsoft.CodeAnalysis.NullableContextOptions.Enable);
    }

    // ── The element-wise endpoints refuse, and name a remedy that works ──────

    [Fact]
    public void A_method_scoped_Flatten_is_refused_element_wise_and_the_dotted_remedy_is_the_one_that_works()
    {
        var diagnostic = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("Child")]
                public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
            }
            """, "DWARF090")[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("[Flatten(\"Child\")] on this mapping method", diagnostic, StringComparison.Ordinal);
        Assert.Contains("[MapProperty<Src, Dst>(\"Child.<leaf>\", \"<leaf>\")]", diagnostic,
            StringComparison.Ordinal);

        // And the prescribed form is not merely plausible: written pair-scoped it maps the element pair.
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            [MapProperty<Src, Dst>("Child.X", "X")]
            public partial class M
            {
                public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
            }
            """);
        Assert.Contains("Child.X", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_scoped_MapValue_is_refused_element_wise_and_the_pair_scoped_remedy_assigns_the_constant()
    {
        var diagnostic = GeneratorAssert.Reports(Flat + """

            [DwarfMapper]
            public partial class M
            {
                [MapValue("Name", "probe")]
                public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
            }
            """, "DWARF090")[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("[MapValue(\"Name\", \"probe\")] on this mapping method", diagnostic,
            StringComparison.Ordinal);
        Assert.Contains("[MapValue<Dst>(\"Name\", \"probe\")]", diagnostic, StringComparison.Ordinal);

        var generated = GeneratorAssert.EmitsCompilableCode(Flat + """

            [DwarfMapper]
            [MapValue<Dst>("Name", "probe")]
            public partial class M
            {
                public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
            }
            """);
        Assert.Contains("Name = \"probe\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_element_wise_MapValue_message_echoes_what_was_written_and_never_the_wrong_endpoint()
    {
        // Three written forms, three quoted remedies. A message that rendered one shape for all three would
        // hand a caller who wrote Use= a constant they never asked for.
        foreach (var (written, expected) in new[]
                 {
                     ("[MapValue(\"Name\")]", "[MapValue<Dst>(\"Name\")]"),
                     ("[MapValue(\"Name\", Use = \"Now\")]", "[MapValue<Dst>(\"Name\", Use = \"Now\")]"),
                     ("[MapValue(\"Name\", \"probe\")]", "[MapValue<Dst>(\"Name\", \"probe\")]")
                 })
        {
            var diagnostic = GeneratorAssert.Reports(Flat + $$"""

                [DwarfMapper]
                public partial class M
                {
                    {{written}}
                    public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                }
                """, "DWARF090")[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture);

            Assert.Contains(written + " on this mapping method", diagnostic, StringComparison.Ordinal);
            Assert.Contains(expected, diagnostic, StringComparison.Ordinal);

            // Projection is SILENT for this directive (D9), not refused. The one thing this tail must never
            // do is tell a reader an endpoint is handled when it is not — that exact sentence shipped once
            // for [MapNullSkip] and was reverted.
            Assert.Contains("silent at projection", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("refused at projection", diagnostic, StringComparison.Ordinal);
        }
    }

    // ── Malformed input reaches neither the model nor a message ──────────────

    [Fact]
    public void A_null_directive_argument_produces_no_element_wise_report_and_no_crash()
    {
        // ReadFlattenRoots and ReadMapValues each drop an argument that is absent or not a string, and the
        // gate calls those readers rather than re-parsing — so a directive resolution never saw cannot be
        // reported as one resolution dropped. The alternative is a message with the word "null" in it.
        const string source = """

            [DwarfMapper]
            public partial class M
            {
                [Flatten(null)]
                [MapValue(null)]
                public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
            }
            """;

        GeneratorAssert.DoesNotReport(Flat + source, "DWARF090");
        GeneratorAssert.CompilesClean(Flat + source);
    }

    [Fact]
    public void A_MapValue_whose_constant_does_not_fit_the_destination_is_still_refused_at_the_create_map()
    {
        // The type check is DWARF040 and it lives on the create-map path, which is where the [MapValue] that
        // reaches an endpoint is resolved. Pinned here because A8 added a second reader of these attributes
        // (the element-wise gate) and a guard that moved rather than being shared is this round's recurring
        // defect.
        GeneratorAssert.Reports(Flat + """

            [DwarfMapper]
            public partial class M
            {
                [MapValue("Id", "not-an-int")]
                public partial Dst Map(Src s);
            }
            """, "DWARF040");
    }

    [Fact]
    public void A_flatten_root_naming_a_member_that_does_not_exist_is_refused_at_the_create_map_too()
    {
        GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Flatten("NoSuchMember")]
                public partial Dst Map(Src s);
            }
            """, "DWARF016");
    }
}
