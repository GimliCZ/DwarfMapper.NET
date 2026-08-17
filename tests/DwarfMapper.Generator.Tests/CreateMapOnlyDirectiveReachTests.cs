// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The directives the generator reads only where the destination is CONSTRUCTED and RETURNED, and what
///     the other four endpoints now say about them — <c>DWARF092</c>.
/// </summary>
/// <remarks>
///     <para>
///         The surface matrix recorded the identical shape three times: a directive honoured at
///         <c>CreateMap</c> and SILENT at <c>UpdateInto</c>, <c>Projection</c>, <c>SpanMap</c> and
///         <c>AsyncStream</c> — accepted, no diagnostic, output byte-identical to the same mapper without it
///         (<c>D11</c>, and its two siblings <c>D8</c> and <c>D13</c>). One declaration on one mapper class
///         meant one thing on one overload and nothing on the next four.
///     </para>
///     <para>
///         The remedy the message names was MEASURED before it was prescribed, which on this branch is not a
///         formality: a <c>DWARF090</c> tail asserting an endpoint behaviour that was false has already
///         shipped here once and been reverted. At the two element-wise endpoints the claim is specific —
///         the emitted loop calls a declared create map for the element pair — and it is pinned below by
///         reading the emitted loop, not by trusting the sentence.
///     </para>
/// </remarks>
public class CreateMapOnlyDirectiveReachTests
{
    /// <summary>
    ///     A recursive source navigation and a flat destination collection: the two halves a graph flatten
    ///     needs. <c>Src.Flat</c> exists so the BASELINE compiles — without it the directive is the only thing
    ///     supplying <c>Dst.Flat</c> and a fixture that cannot compile without the element under test can
    ///     never show that element doing nothing.
    /// </summary>
    private const string Types = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Node { public int Id { get; set; } public List<Node> Children { get; set; } = new(); }
        public class NodeDto { public int Id { get; set; } }
        public class Src { public int Id { get; set; } public Node Root { get; set; } public List<Node> Flat { get; set; } = new(); }
        public class Dst { public int Id { get; set; } public List<NodeDto> Flat { get; set; } = new(); }
        """;

    // ── The four endpoints that do not read it now say so ────────────────────

    [Theory]
    [InlineData("public partial void Update(Src s, Dst d);", "Update", "update-into")]
    [InlineData("public partial IQueryable<Dst> Project(IQueryable<Src> q);", "Project", "projection")]
    [InlineData("public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);", "MapSpan", "span-map")]
    [InlineData("public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);", "MapStream",
        "async-stream")]
    public void A_graph_flatten_written_anywhere_but_a_create_map_is_refused_and_names_the_endpoint(
        string signature, string methodName, string endpointName)
    {
        var reported = GeneratorAssert.Reports(Types + $$"""

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                {{signature}}
            }
            """, "DWARF092");

        var message = reported[0].GetMessage(CultureInfo.InvariantCulture);

        // The written form is echoed rather than described, so a reader can find the line it is about.
        Assert.Contains("[FlattenGraph(\"Root\", \"Flat\")]", message, StringComparison.Ordinal);
        Assert.Contains($"on '{methodName}'", message, StringComparison.Ordinal);
        Assert.Contains($"the {endpointName} endpoint", message, StringComparison.Ordinal);
        Assert.Contains("Declare it on a create map over the same pair", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_element_wise_endpoints_alone_claim_the_create_map_is_reached_from_here()
    {
        const string claim = "so that create map is what this method's loop calls";

        foreach (var signature in new[]
                 {
                     "public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);",
                     "public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);"
                 })
            Assert.Contains(claim, Message(signature), StringComparison.Ordinal);

        // Pinned in BOTH directions. An update-into and a projection resolve their own members and never call
        // a sibling create map, so the transfer claim is FALSE there — and a message that tells a caller an
        // endpoint is handled when it is not is the defect this whole matrix exists to find.
        foreach (var signature in new[]
                 {
                     "public partial void Update(Src s, Dst d);",
                     "public partial IQueryable<Dst> Project(IQueryable<Src> q);"
                 })
            Assert.DoesNotContain(claim, Message(signature), StringComparison.Ordinal);

        string Message(string signature) => GeneratorAssert.Reports(Types + $$"""

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                {{signature}}
            }
            """, "DWARF092")[0].GetMessage(CultureInfo.InvariantCulture);
    }

    // ── The remedy, measured rather than asserted ────────────────────────────

    [Theory]
    [InlineData("public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);", "d[__i] = Map(s[__i]);")]
    [InlineData("public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);",
        "yield return Map(")]
    public void The_prescribed_remedy_really_does_carry_the_flatten_to_an_element_wise_endpoint(
        string signature, string expectedCall)
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Types + $$"""

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                public partial Dst Map(Src s);

                {{signature}}
            }
            """);

        // The element pair resolves to the DECLARED create map, so the walk runs per element through the
        // method that carries the directive. Without this the message would be prescribing a form nobody ran.
        Assert.Contains(expectedCall, generated, StringComparison.Ordinal);
        Assert.Contains("__DwarfMap_FlattenGraph_", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_remedy_is_not_reported_as_a_gap_on_the_create_map_that_carries_it()
    {
        // The gate is called from four branches and deliberately not from the fifth. A copy of it in the
        // create-map branch would refuse the very shape the message tells the caller to write.
        GeneratorAssert.DoesNotReport(Types + """

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                public partial Dst Map(Src s);
            }
            """, "DWARF092");
    }

    // ── Malformed input, through the create map's own reader ─────────────────

    [Fact]
    public void A_null_directive_argument_produces_no_report_and_no_crash()
    {
        // ReadFlattenGraphAttributes — the reader the create-map branch resolves with — requires both
        // constructor arguments to be strings, so an application it never resolved is never reported as one
        // it dropped. The gate calls that reader rather than re-parsing the attributes, which is what stops
        // the word "null" appearing in a remedy. Both of this branch's generator crashes came from a second
        // reader that did re-parse.
        GeneratorAssert.DoesNotReport(Types + """

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph(null, null)]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092");
    }

    [Fact]
    public void A_directive_naming_members_that_do_not_exist_is_still_reported_and_echoed_verbatim()
    {
        // Reported per APPLICATION, malformed included: at a create map this is DWARF034, and here it was
        // refused as nothing at all — so "it does not reach this endpoint" is the true statement either way.
        // Echoed as written, not normalized, so the caller can find the line.
        var message = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("NoSuchNavigation", "NoSuchCollection")]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("[FlattenGraph(\"NoSuchNavigation\", \"NoSuchCollection\")]", message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("null", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_directive_written_twice_is_reported_twice()
    {
        // Two wrong applications are two mistakes to fix, the rule DWARF088 and DWARF090 already follow.
        var reported = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                [FlattenGraph("Root", "Flat")]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092");

        Assert.Equal(2, reported.Count);
    }

    // ── [MapDerivedType], both of its forms ──────────────────────────────────

    /// <summary>
    ///     A real base/derived hierarchy on both sides, which is the only shape a dispatch arm has anything
    ///     to say about. The derived types carry an extra member each, so the arm is observable.
    /// </summary>
    private const string Hierarchy = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Id { get; set; } }
        public sealed class SrcDerived : Src { public string Extra { get; set; } }
        public class Dst { public int Id { get; set; } }
        public sealed class DstDerived : Dst { public string Extra { get; set; } }
        """;

    [Theory]
    [InlineData("[MapDerivedType(typeof(SrcDerived), typeof(DstDerived))]",
        "[MapDerivedType(typeof(SrcDerived), typeof(DstDerived))]")]
    [InlineData("[MapDerivedType<SrcDerived, DstDerived>]", "[MapDerivedType<SrcDerived, DstDerived>]")]
    public void A_dispatch_arm_outside_a_create_map_is_refused_and_quoted_in_the_form_it_was_written(
        string written, string expectedInMessage)
    {
        // The two forms mean the same thing to resolution, and the reader carries which one was TYPED for
        // exactly this: a caller handed back a syntax they did not write has to translate the remedy before
        // they can apply it.
        var message = GeneratorAssert.Reports(Hierarchy + $$"""

            [DwarfMapper]
            public partial class M
            {
                {{written}}
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains(expectedInMessage, message, StringComparison.Ordinal);

        // What is actually lost, named: the members the derived DTO declares beyond the base one.
        Assert.Contains("dropped", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_open_form_is_refused_at_all_four_endpoints_that_are_not_a_create_map()
    {
        foreach (var signature in new[]
                 {
                     "public partial void Update(Src s, Dst d);",
                     "public partial IQueryable<Dst> Project(IQueryable<Src> q);",
                     "public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);",
                     "public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);"
                 })
            GeneratorAssert.Reports(Hierarchy + $$"""

                [DwarfMapper]
                public partial class M
                {
                    [MapDerivedType(typeof(SrcDerived), typeof(DstDerived))]
                    {{signature}}
                }
                """, "DWARF092");
    }

    [Fact]
    public void The_prescribed_remedy_really_does_carry_the_dispatch_to_an_element_wise_endpoint()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Hierarchy + """

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType(typeof(SrcDerived), typeof(DstDerived))]
                public partial Dst Map(Src s);

                public partial DstDerived MapDerived(SrcDerived s);

                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """);

        // The span loop calls the DECLARED create map, and that create map is the runtime-type switch.
        Assert.Contains("d[__i] = Map(s[__i]);", generated, StringComparison.Ordinal);
        Assert.Contains("global::Demo.SrcDerived __s => MapDerived(__s)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dispatch_arm_naming_a_type_that_is_not_assignable_is_still_reported_and_never_crashes()
    {
        // At a create map this is DWARF035, an Error; here it was refused as nothing at all. Reported per
        // application, malformed included, because "it does not reach this endpoint" is true either way —
        // and the arm reads through ReadDerivedTypeAttributes, whose `is INamedTypeSymbol` patterns are what
        // keep a half-typed application from reaching a message or a crash.
        var message = GeneratorAssert.Reports(Hierarchy + """

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType(typeof(string), typeof(DstDerived))]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("[MapDerivedType(typeof(string), typeof(DstDerived))]", message,
            StringComparison.Ordinal);
    }

    // ── [ReverseMap] ─────────────────────────────────────────────────────────

    /// <summary>A pair with a RENAME, which is the only thing <c>[ReverseMap]</c> inherits.</summary>
    private const string Renamed = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Id { get; set; } public string A { get; set; } }
        public class Dst { public int Id { get; set; } public string B { get; set; } }
        """;

    [Theory]
    [InlineData("public partial void Update(Src s, Dst d);", "update-into")]
    [InlineData("public partial IQueryable<Dst> Project(IQueryable<Src> q);", "projection")]
    [InlineData("public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);", "span-map")]
    [InlineData("public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);", "async-stream")]
    public void A_ReverseMap_outside_a_create_map_is_refused_and_never_claims_a_transfer(
        string signature, string endpointName)
    {
        var message = GeneratorAssert.Reports(Renamed + $$"""

            [DwarfMapper]
            public partial class M
            {
                [ReverseMap]
                [MapProperty("A", "B")]
                {{signature}}
            }
            """, "DWARF092")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("[ReverseMap]", message, StringComparison.Ordinal);
        Assert.Contains($"the {endpointName} endpoint", message, StringComparison.Ordinal);

        // NO transfer claim, at ANY of the four — including the two element-wise ones, where the other two
        // directives do carry one. [ReverseMap] does not change what the create map emits; it makes a
        // separately-declared inverse inherit renames, and "your inverse reaches the span map" is not a claim
        // about anything. This is the assertion that would have caught the false-tail defect this branch
        // already shipped once.
        Assert.DoesNotContain("so that create map is what this method's loop calls", message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_does_not_say_the_inverse_is_generated_because_it_is_not()
    {
        // The finding's own wording said [ReverseMap] "asks for the inverse mapping to be GENERATED", and the
        // caller "discovers the absence at the call site of a method that was never generated". Measured: the
        // caller DECLARES the inverse partial themselves, [ReverseMap] only makes it inherit the forward
        // renames inverted, and a missing inverse is DWARF052 rather than a silent absence. A message that
        // repeated the entry's mechanism would have taught a reader the wrong model of the feature.
        var message = GeneratorAssert.Reports(Renamed + """

            [DwarfMapper]
            public partial class M
            {
                [ReverseMap]
                [MapProperty("A", "B")]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("separately-declared inverse", message, StringComparison.Ordinal);
        Assert.DoesNotContain("generated", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prescribed_remedy_really_does_invert_the_rename_on_a_create_map_pair()
    {
        // Measured before it was prescribed. The inverse inherits `A <- B`; without [ReverseMap] the same two
        // methods are DWARF001, because Src.A has no source on the way back.
        var generated = GeneratorAssert.EmitsCompilableCode(Renamed + """

            [DwarfMapper]
            public partial class M
            {
                [ReverseMap]
                [MapProperty("A", "B")]
                public partial Dst Map(Src s);

                public partial Src Back(Dst d);
            }
            """);

        Assert.Contains("A = d.B", generated, StringComparison.Ordinal);

        GeneratorAssert.Reports(Renamed + """

            [DwarfMapper]
            public partial class M
            {
                [MapProperty("A", "B")]
                public partial Dst Map(Src s);

                public partial Src Back(Dst d);
            }
            """, "DWARF001");
    }

    [Fact]
    public void An_inverse_UPDATE_does_not_inherit_the_renames_which_is_the_silence_being_refused()
    {
        // The shape a caller who wrote [ReverseMap] on an update-into actually has: an inverse update
        // declared, and the renames not inherited — DWARF001, from the same pair that compiles clean when
        // both halves are create maps. The refusal now sits beside it and says why.
        const string source = Renamed + """

            [DwarfMapper]
            public partial class M
            {
                [ReverseMap]
                [MapProperty("A", "B")]
                public partial void Update(Src s, Dst d);

                public partial void Back(Dst d, Src s);
            }
            """;

        GeneratorAssert.Reports(source, "DWARF001");
        GeneratorAssert.Reports(source, "DWARF092");
    }

    [Fact]
    public void The_refusal_is_a_warning_so_the_rest_of_the_mapper_is_still_emitted()
    {
        // A blocking error suppresses the class's emission, and every partial mapping method on it then
        // arrives as CS8795 with this refusal buried underneath — which would also move the cells into the
        // "judged by nothing" population rather than out of it.
        var reported = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF092");

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, reported[0].Severity);

        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            public partial class M
            {
                [FlattenGraph("Root", "Flat")]
                public partial void Update(Src s, Dst d);
            }
            """);
        Assert.Contains("public partial void Update(", generated, StringComparison.Ordinal);
    }
}
