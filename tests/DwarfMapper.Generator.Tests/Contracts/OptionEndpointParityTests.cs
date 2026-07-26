// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     What an option DOES at one endpoint, observed rather than declared.
/// </summary>
public enum OptionEffect
{
    /// <summary>The option changed the emitted output.</summary>
    Honoured,

    /// <summary>The option produced a diagnostic — a refusal the caller can see.</summary>
    Refused,

    /// <summary>Neither. The option was accepted and had no effect anyone can observe.</summary>
    Silent,

    /// <summary>
    ///     The option changed nothing, but the build fails at this endpoint anyway. Distinguished from
    ///     <see cref="Silent" /> deliberately: silence is only dangerous when the code COMPILES. An option
    ///     that goes unhonoured while the compilation errors cannot ship wrong data — the developer is
    ///     stopped and told something, even if not the thing they configured. That is a missing feature,
    ///     which is a different and much smaller problem than a trust boundary evaporating.
    /// </summary>
    UnhonouredButLoud
}

/// <summary>
///     Endpoint parity for the class-level <c>[DwarfMapper]</c> options, as a PROPERTY rather than a table.
///     <para>
///         <see cref="OptionContractTests" /> hand-declares sixteen cells for one endpoint. Extending that by
///         hand to every endpoint means 112 declarations, and the declarations are the part most likely to be
///         written to match whatever the generator currently does — which is precisely how a silent divergence
///         gets ratified as intended behaviour.
///     </para>
///     <para>
///         So this asserts a relationship instead: <c>CreateMap</c> is the reference endpoint, and an option
///         that visibly does SOMETHING there (honoured or refused) must not go <see cref="OptionEffect.Silent" />
///         at another endpoint. Silence is the failure mode that matters — the option was accepted, no
///         diagnostic was raised, and the caller's configuration evaporated. This is the exact shape of all
///         seven divergences found so far, and unlike a table it needs no update when an option is added.
///     </para>
/// </summary>
public class OptionEndpointParityTests
{
    /// <summary>
    ///     Endpoints that take a <c>[DwarfMapper]</c>-annotated mapper class, so a class-level option can
    ///     reach them at all. The registry front door and the co-located host are excluded structurally, not
    ///     by preference: neither has a mapper class to annotate, so there is no cell to compare.
    /// </summary>
    public static readonly Endpoint[] ComparableEndpoints =
        [Endpoint.UpdateInto, Endpoint.Projection, Endpoint.SpanMap, Endpoint.AsyncStream];

    /// <summary>
    ///     Known-and-accepted silences, each with the reason it is not a divergence. An entry here is a claim
    ///     that the option CANNOT apply at that endpoint — not that it currently does not.
    /// </summary>
    private static readonly Dictionary<(string Option, Endpoint Endpoint), string> Exempt = new()
    {
        // A span map fills a caller-owned buffer element-wise and an async stream maps elements through the
        // element mapper. Neither creates the target graph, so options about HOW the target is constructed
        // have nothing to act on at that endpoint.
        [("GenerateExtensions", Endpoint.SpanMap)] =
            "the convenience extension is generated per mapper, not per span overload",
        [("GenerateExtensions", Endpoint.AsyncStream)] =
            "the convenience extension is generated per mapper, not per stream overload",
        [("GenerateExtensions", Endpoint.UpdateInto)] =
            "the convenience extension is a create-shaped source.ToTarget(); an update has no such form"
    };

    public static TheoryData<string, Endpoint> Cells()
    {
        var data = new TheoryData<string, Endpoint>();
        foreach (var cell in OptionContractTests.ProjectionCells)
        foreach (var endpoint in ComparableEndpoints)
            data.Add(cell.Option, endpoint);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void An_option_that_acts_at_CreateMap_does_not_go_silent_at_another_endpoint(
        string option, Endpoint endpoint)
    {
        var cell = OptionContractTests.ProjectionCells
            .Single(c => string.Equals(c.Option, option, StringComparison.Ordinal));

        var reference = OptionProbe.Classify(Endpoint.CreateMap, cell.NonDefault, cell.Types);

        // An option the REFERENCE endpoint ignores says nothing about the others — there is no parity to
        // break. Those cells are carried in the theory anyway so the count stays honest.
        if (reference.Effect == OptionEffect.Silent) return;

        var actual = OptionProbe.Classify(endpoint, cell.NonDefault, cell.Types);
        if (actual.Effect != OptionEffect.Silent) return;

        if (Exempt.TryGetValue((option, endpoint), out var why))
        {
            Assert.False(string.IsNullOrWhiteSpace(why));
            return;
        }

        // Known and recorded, but not yet fixed. Failing here would mean either hiding the gap or blocking
        // every future change on fixing it; OptionGaps names it instead, and the ratchet there stops it
        // spreading and forces removal once it is fixed.
        if (OptionGaps.KnownSilent.TryGetValue(option, out var gap))
        {
            Assert.False(string.IsNullOrWhiteSpace(gap));
            return;
        }

        Assert.Fail(
            $"[DwarfMapper({cell.NonDefault})] is {reference.Effect} at CreateMap "
            + $"({reference.Detail}) but SILENT at {endpoint}: no diagnostic, and output byte-identical to "
            + "the same source without the option.\n\n"
            + "The caller configured something and the generator accepted it, changed nothing, and said "
            + "nothing. Either honour it at this endpoint, refuse it with a diagnostic, or add an Exempt "
            + "entry saying why it structurally cannot apply here.");
    }

    [Fact]
    public void The_parity_property_is_not_vacuous()
    {
        // If Classify() were broken so that everything read Silent at CreateMap, every cell would return at
        // the first guard and the whole theory would pass without comparing anything.
        var acting = OptionContractTests.ProjectionCells
            .Count(c => OptionProbe.Classify(Endpoint.CreateMap, c.NonDefault, c.Types).Effect != OptionEffect.Silent);

        Assert.True(acting >= 6,
            $"Expected several options to visibly act at CreateMap, but only {acting} did. Either the "
            + "fixtures stopped triggering their options or Classify() is not observing correctly — in "
            + "both cases the parity theory is passing without testing anything.");
    }

    [Fact]
    public void Every_exemption_names_a_real_option_and_endpoint()
    {
        // A stale exemption silently re-permits the divergence it was written to excuse.
        var known = OptionContractTests.ProjectionCells.Select(c => c.Option).ToHashSet(StringComparer.Ordinal);
        foreach (var ((opt, ep), why) in Exempt)
        {
            Assert.True(known.Contains(opt), $"Exemption names unknown option '{opt}'.");
            Assert.Contains(ep, ComparableEndpoints);
            Assert.False(string.IsNullOrWhiteSpace(why), $"Exemption for {opt}/{ep} states no reason.");
        }
    }

    [Theory]
    [InlineData(Endpoint.SpanMap)]
    [InlineData(Endpoint.AsyncStream)]
    public void An_explicit_only_mapper_refuses_element_wise_endpoints_with_DWARF077(Endpoint endpoint)
    {
        // The trust boundary the parity property found missing. [DwarfMapper(AutoMatchMembers = false)]
        // guarded .Map and silently did not guard .MapSpan on the SAME mapper, because span and async-stream
        // map the element pair through an auto-synthesized mapper that explicit-only does not propagate into.
        // Half a trust boundary is worse than none, because the developer believes they have one.
        var src = EndpointSources.Build(endpoint, options: "AutoMatchMembers = false");
        var (diagnostics, _) = GeneratorTestHarness.Run(src);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "DWARF077", StringComparison.Ordinal));
    }

    [Fact]
    public void DWARF077_does_not_fire_when_the_mapper_is_not_explicit_only()
    {
        // The negative control. A refusal that fires unconditionally would break every span mapper and would
        // still satisfy the test above.
        foreach (var endpoint in new[] { Endpoint.SpanMap, Endpoint.AsyncStream })
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(EndpointSources.Build(endpoint));
            Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "DWARF077", StringComparison.Ordinal));
        }
    }

}
