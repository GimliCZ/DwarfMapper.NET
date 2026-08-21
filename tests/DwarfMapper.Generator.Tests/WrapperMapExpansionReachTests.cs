// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     What <c>[GenerateWrapperMap]</c> does on a class with no <c>[GenerateMap]</c> pair to expand —
///     <c>DWARF093</c>.
/// </summary>
/// <remarks>
///     <para>
///         The surface matrix measured the attribute producing NOTHING and saying nothing on a
///         <c>[DwarfMapper]</c> class at all five mapper endpoints (finding <c>D15</c>), while the co-located
///         host refused it as <c>DWARF067</c>. The entry read that asymmetry as the generator "having an
///         opinion about where the attribute is valid". It is not: <c>DWARF067</c> is an opinion about the
///         WRAPPER TYPE, and it fired at the co-located host only because that template declares a
///         <c>[GenerateMap]</c> pair while the probe's sampled <c>typeof(Dst)</c> is not a single-parameter
///         generic. The silence was <c>ExpandWrapperMaps</c> returning early on an empty pair list.
///     </para>
///     <para>
///         The fork was <b>emit</b> versus <b>refuse</b>, and the answer is refuse, argued from what the
///         attribute means rather than from what is easier: it is defined relative to <c>[GenerateMap]</c>,
///         and four of the five endpoints have no <c>W&lt;A&gt; -&gt; W&lt;B&gt;</c> create-map shape to
///         synthesize at all. The remedy — and its collision edge (<c>DWARF094</c>) — is measured below,
///         not asserted.
///     </para>
/// </remarks>
public class WrapperMapExpansionReachTests
{
    private const string Types = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Envelope<T> { public T Payload { get; set; } = default!; public int Status { get; set; } }
        public class Src { public int Id { get; set; } }
        public class Dst { public int Id { get; set; } }
        """;

    // ── All five mapper endpoints now say so ─────────────────────────────────

    [Theory]
    [InlineData("public partial Dst Map(Src s);")]
    [InlineData("public partial void Update(Src s, Dst d);")]
    [InlineData("public partial IQueryable<Dst> Project(IQueryable<Src> q);")]
    [InlineData("public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);")]
    [InlineData("public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);")]
    public void A_wrapper_family_on_a_class_with_no_declared_pair_is_refused(string signature)
    {
        var message = GeneratorAssert.Reports(Types + $$"""

            [DwarfMapper]
            [GenerateWrapperMap(typeof(Envelope<>))]
            public partial class M
            {
                {{signature}}
            }
            """, "DWARF093")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("[GenerateWrapperMap(typeof(Envelope<>))] on 'M' expands nothing", message,
            StringComparison.Ordinal);
        Assert.Contains("Declare the payload pair as [GenerateMap<A, B>] on this class", message,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     The ORDERING, which is the part a reviewer should be able to falsify: the wrapper below is
    ///     perfectly well-formed, so a <c>DWARF067</c> here would mean the shape check ran first and sent the
    ///     caller to fix something that changes no output. It is also an Error, which would strand the class's
    ///     partial method behind <c>CS8795</c>.
    /// </summary>
    [Fact]
    public void A_well_formed_wrapper_with_no_pair_is_reported_as_the_empty_expansion_and_not_as_a_bad_shape()
    {
        const string source = Types + """

            [DwarfMapper]
            [GenerateWrapperMap(typeof(Envelope<>))]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """;

        GeneratorAssert.Reports(source, "DWARF093");
        GeneratorAssert.DoesNotReport(source, "DWARF067");
        Assert.Contains("public partial global::Demo.Dst Map(", GeneratorAssert.EmitsCompilableCode(source),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     And the same check must not swallow the shape complaint where there IS a pair: with the list
    ///     non-empty the expansion is attempted for real, and a wrapper that cannot be expanded is still
    ///     <c>DWARF067</c>. Pinned in both directions, because a gate that fires everywhere and a gate that
    ///     fires nowhere look the same from one side.
    /// </summary>
    [Fact]
    public void A_declared_pair_restores_the_wrapper_shape_check()
    {
        const string source = Types + """

            public class TwoParam<T, U> { public T Payload { get; set; } = default!; public U Meta { get; set; } = default!; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [GenerateWrapperMap(typeof(TwoParam<,>))]
            public partial class M
            {
            }
            """;

        GeneratorAssert.Reports(source, "DWARF067");
        GeneratorAssert.DoesNotReport(source, "DWARF093");
    }

    // ── The remedy, measured rather than asserted ────────────────────────────

    /// <summary>
    ///     The form the message names really does emit the wrapper map. Measured against an update-into so the
    ///     class carries a mapping method as well as the pair — the shape a caller migrating from the refused
    ///     source most plausibly lands on.
    /// </summary>
    [Fact]
    public void The_prescribed_remedy_really_does_emit_the_closed_wrapper_map()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [GenerateWrapperMap(typeof(Envelope<>))]
            public partial class M
            {
                public partial void Update(Src s, Dst d);
            }
            """);

        Assert.Contains("Envelope<global::Demo.Dst> Map(global::Demo.Envelope<global::Demo.Src> src)",
            generated, StringComparison.Ordinal);
        Assert.Contains("Payload = Map(src.Payload)", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The remedy's SHARP EDGE, measured rather than discovered by the reader. <c>[GenerateMap&lt;A, B&gt;]</c>
    ///     emits its own <c>B Map(A)</c>, so adding it to a class that already declares a
    ///     <c>partial B Map(A)</c> over the same pair collides — originally as a raw generated-code
    ///     <c>CS0111</c>, refused as <c>DWARF094</c> since B27 closed. The message names that case
    ///     explicitly; without this test the sentence would be a claim nobody ran.
    /// </summary>
    [Fact]
    public void The_message_names_the_DWARF094_edge_and_that_edge_is_real()
    {
        var message = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            [GenerateWrapperMap(typeof(Envelope<>))]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """, "DWARF093")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("it is refused as DWARF094", message, StringComparison.Ordinal);

        // The edge itself: the naive remedy on a create-map class really does collide — and since B27
        // closed, the collision arrives as the generator's own refusal, never as CS0111 in a file the
        // caller cannot edit.
        const string naiveRemedy = Types + """

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [GenerateWrapperMap(typeof(Envelope<>))]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """;

        GeneratorAssert.Reports(naiveRemedy, "DWARF094");

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(naiveRemedy);
        Assert.DoesNotContain(errors, d => d.Id == "CS0111");
    }

    // ── Malformed input, through the guard the expansion path already had ────

    /// <summary>
    ///     The refusal sits AFTER the same argument guard the expansion uses, so an argument that is not a
    ///     named type yields nothing on either path — one rule, not two. <c>[GenerateWrapperMap(null)]</c> is
    ///     legal C# (the parameter is <c>Type</c>), and it must reach neither the pair list nor a message.
    /// </summary>
    [Fact]
    public void A_null_wrapper_argument_produces_no_report_and_no_crash()
    {
        const string source = Types + """

            [DwarfMapper]
            [GenerateWrapperMap(null)]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """;

        GeneratorAssert.DoesNotReport(source, "DWARF093");
        GeneratorAssert.DoesNotReport(source, "DWARF067");
        GeneratorAssert.EmitsCompilableCode(source);
    }

    /// <summary>
    ///     A <c>typeof</c> naming a type that does not exist. MEASURED rather than reasoned about: an
    ///     <c>IErrorTypeSymbol</c> <i>implements</i> <c>INamedTypeSymbol</c>, so the guard's pattern matches
    ///     and the application IS reported — the same trap a review caught in <c>DWARF092</c>'s report, where
    ///     the opposite was asserted from the pattern alone.
    /// </summary>
    [Fact]
    public void A_typeof_naming_a_type_that_does_not_exist_is_reported_without_crashing()
    {
        var message = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            [GenerateWrapperMap(typeof(NoSuchWrapper<>))]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """, "DWARF093")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("NoSuchWrapper", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_wrapper_family_written_twice_is_reported_twice()
    {
        var reported = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            [GenerateWrapperMap(typeof(Envelope<>))]
            [GenerateWrapperMap(typeof(Envelope<>))]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """, "DWARF093");

        Assert.Equal(2, reported.Count);
    }

    /// <summary>
    ///     A Warning, so the mapper is still emitted. An Error would strand every partial mapping method on
    ///     the class behind <c>CS8795</c> with this refusal buried under it — and would move the cells into
    ///     the population the surface parity theory judges by nothing rather than out of it.
    /// </summary>
    [Fact]
    public void The_refusal_is_a_warning()
    {
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            GeneratorAssert.Reports(Types + """

                [DwarfMapper]
                [GenerateWrapperMap(typeof(Envelope<>))]
                public partial class M
                {
                    public partial Dst Map(Src s);
                }
                """, "DWARF093")[0].Severity);
    }
}
