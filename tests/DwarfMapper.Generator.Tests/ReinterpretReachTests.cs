// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Which endpoints <c>[Reinterpret]</c> reaches, and what the two that cannot now say — <c>DWARF090</c>.
/// </summary>
/// <remarks>
///     <para>
///         A forced blit is what a caller reaches for when they are moving elements in bulk, and the two
///         endpoints that dropped it were the span map and the async-stream map: the two whose whole purpose
///         is bulk element throughput (surface-matrix finding <c>D12</c>). Honoured on the same mapper's
///         create map and update-into, silent on the next two, and the array copied element by element
///         through an ordinary conversion helper instead.
///     </para>
///     <para>
///         It is the first arm of the element-wise gate with NO pair-scoped twin, so its remedy is a DECLARED
///         create map rather than a re-scoped attribute. That remedy is measured here rather than asserted —
///         this repository has already shipped a <c>DWARF090</c> tail claiming an endpoint behaviour that was
///         false, and reverted it.
///     </para>
///     <para>
///         The fixture is two UNMANAGED arrays of the SAME WIDTH and DIFFERENT element types. That is the only
///         shape the directive has anything to say about: with one element type the automatic layout proof
///         blits already and <c>[Reinterpret]</c> changes nothing, and with differing widths or a managed
///         element it is refused as <c>DWARF022</c>.
///     </para>
/// </remarks>
public class ReinterpretReachTests
{
    private const string Types = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Id { get; set; } public int[] Data { get; set; } = Array.Empty<int>(); }
        public class Dst { public int Id { get; set; } public uint[] Data { get; set; } = Array.Empty<uint>(); }
        """;

    // ── The two element-wise endpoints now say so ────────────────────────────

    [Theory]
    [InlineData("public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);", "MapSpan")]
    [InlineData("public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);", "MapStream")]
    public void A_forced_blit_written_on_an_element_wise_map_is_refused_and_names_the_member(
        string signature, string methodName)
    {
        var message = GeneratorAssert.Reports(Types + $$"""

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                {{signature}}
            }
            """, "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("[Reinterpret(\"Data\")] on this mapping method", message, StringComparison.Ordinal);
        Assert.Contains($"does not reach '{methodName}'", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The message must NOT prescribe a pair-scoped form, because there is none: no
    ///     <c>[Reinterpret&lt;Src, Dst&gt;]</c> exists, and telling a caller to write one would be a remedy
    ///     that does not compile. Pinned negatively for the reason the transfer-claim suppressions on
    ///     <c>DWARF092</c> are: a sentence that is only ever absent is verified by nothing unless something
    ///     asserts its absence.
    /// </summary>
    [Fact]
    public void The_message_names_a_declared_create_map_and_never_a_pair_scoped_form_that_does_not_exist()
    {
        var message = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """, "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("has no pair-scoped form", message, StringComparison.Ordinal);
        Assert.Contains("`partial Dst <Name>(Src s)`", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Write it PAIR-SCOPED", message, StringComparison.Ordinal);
        Assert.DoesNotContain("[Reinterpret<", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The tail states where the directive DOES act, and stops at the two endpoints that were measured.
    ///     Projection is deliberately absent: only the create-map and update-into branches read
    ///     <c>[Reinterpret]</c>, so a projection honours nothing here either, and <c>D12</c>'s claim that it
    ///     acted there was false.
    /// </summary>
    [Fact]
    public void The_tail_claims_the_create_map_and_the_update_into_and_not_projection()
    {
        var message = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """, "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("honoured at the create-map and update-into endpoints", message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("projection", message, StringComparison.Ordinal);
    }

    // ── The remedy, measured rather than asserted ────────────────────────────

    [Theory]
    [InlineData("public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);", "d[__i] = Map(s[__i]);")]
    [InlineData("public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);",
        "yield return Map(")]
    public void The_prescribed_remedy_really_does_carry_the_blit_to_an_element_wise_endpoint(
        string signature, string expectedCall)
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Types + $$"""

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial Dst Map(Src s);

                {{signature}}
            }
            """);

        // The element pair resolves to the DECLARED create map, so the blit runs per element through the
        // method that carries the directive rather than through a freshly synthesized element mapper.
        Assert.Contains(expectedCall, generated, StringComparison.Ordinal);
        Assert.Contains("__DwarfBlit_", generated, StringComparison.Ordinal);
        Assert.Contains("MemoryMarshal.Cast<int, uint>", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The other half of the same measurement: WITHOUT the remedy the element pair is copied element by
    ///     element, which is what makes the silence a data-shape change and not merely a missed optimization
    ///     hint. Asserted so the remedy test above cannot pass for a reason that was already true.
    /// </summary>
    [Fact]
    public void Without_the_remedy_the_element_pair_converts_element_by_element_rather_than_blitting()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """);

        Assert.DoesNotContain("__DwarfBlit_", generated, StringComparison.Ordinal);
        Assert.Contains("__DwarfMap_Num_int__uint", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The gate must not fire on the very shape the message prescribes. A create map declared beside the
    ///     span method IS the remedy, and reporting it would send the caller in a circle.
    /// </summary>
    [Fact]
    public void The_create_map_that_carries_the_remedy_is_not_itself_reported()
    {
        GeneratorAssert.DoesNotReport(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial Dst Map(Src s);
            }
            """, "DWARF090");

        GeneratorAssert.DoesNotReport(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial void Update(Src s, Dst d);
            }
            """, "DWARF090");
    }

    // ── Malformed input, through the reader that already guards it ───────────

    /// <summary>
    ///     <c>ReadReinterpretMembers</c> requires the single argument to be a string, so a null yields no
    ///     directive and reaches neither the model nor a message. The guard that already existed on the
    ///     create-map path, inherited rather than rewritten.
    /// </summary>
    [Fact]
    public void A_null_reinterpret_argument_produces_no_report_and_no_crash()
    {
        GeneratorAssert.DoesNotReport(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret(null)]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """, "DWARF090");
    }

    /// <summary>
    ///     A member the destination does not have is still reported, and echoed VERBATIM. At a create map the
    ///     same text is <c>DWARF022</c> ("names no writable destination member"); here it was validated by
    ///     nothing at all, so "it does not reach the element pair" is the true statement in both cases — and
    ///     that asymmetry is half of what makes the silence worth a diagnostic.
    /// </summary>
    [Theory]
    [InlineData("NoSuchMember")]
    [InlineData("")]
    public void A_reinterpret_naming_nothing_real_is_still_reported_and_echoed_verbatim(string member)
    {
        var message = GeneratorAssert.Reports(Types + $$"""

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("{{member}}")]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """, "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains($"[Reinterpret(\"{member}\")]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("null", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_reinterpret_written_twice_is_reported_twice()
    {
        var reported = GeneratorAssert.Reports(Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                [Reinterpret("Data")]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """, "DWARF090");

        Assert.Equal(2, reported.Count);
    }

    /// <summary>
    ///     The refusal is a Warning, so the rest of the mapper is still emitted. An Error would strand every
    ///     partial mapping method on the class behind <c>CS8795</c> with this refusal buried under it — and
    ///     would move the cells into the population the parity theory judges by nothing rather than out of it.
    /// </summary>
    [Fact]
    public void The_refusal_is_a_warning_so_the_span_map_is_still_emitted()
    {
        const string source = Types + """

            [DwarfMapper]
            public partial class M
            {
                [Reinterpret("Data")]
                public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);
            }
            """;

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning,
            GeneratorAssert.Reports(source, "DWARF090")[0].Severity);
        Assert.Contains("public partial void MapSpan(", GeneratorAssert.EmitsCompilableCode(source),
            StringComparison.Ordinal);
    }
}
