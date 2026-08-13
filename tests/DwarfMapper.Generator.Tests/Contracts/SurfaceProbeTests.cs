// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

public sealed class SurfaceProbeTests
{
    [Fact]
    public void A_MapIgnore_on_a_create_map_is_observed_as_honoured()
    {
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "MapIgnore", StringComparison.Ordinal)
            && e.Type.GetGenericArguments().Length == 0);
        var c = new SurfaceCase(element, AttributeTargets.Method, "[MapIgnore(\"Name\")]", "ctor(1)");

        var (effect, detail) = SurfaceProbe.Classify(c, Endpoint.CreateMap);

        Assert.True(effect is SurfaceEffect.Honoured or SurfaceEffect.Refused,
            $"[MapIgnore] at CreateMap classified as {effect} ({detail}). If this reads Silent, the probe is "
            + "not observing correctly and every cell built on it is vacuous.");
    }

    [Fact]
    public void A_class_only_attribute_on_a_method_is_NotCompilable()
    {
        // Pins AttributeUsage to reality. Nothing else in the repository does: the declaration says
        // AttributeTargets.Class and no test ever tries the illegal site to confirm the compiler agrees.
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "DwarfMapper", StringComparison.Ordinal));
        var c = new SurfaceCase(element, AttributeTargets.Method, "[DwarfMapper]", "illegal-site");

        var (effect, _) = SurfaceProbe.Classify(c, Endpoint.CreateMap);

        Assert.Equal(SurfaceEffect.NotCompilable, effect);
    }

    /// <summary>
    ///     A non-null <see cref="EndpointSources.BuildAt" /> result claims a cell exists. If the rendered
    ///     attribute text is missing from that source, the case was never actually placed — the cell reads
    ///     <see cref="SurfaceEffect.Silent" /> for a reason that has nothing to do with the element, and every
    ///     classification built on it is a measurement bug wearing the coat of a real finding. This is what
    ///     let <c>(CoLocatedHost, Property/Field)</c> hide: the template consumed <c>classAttribute</c> and
    ///     silently dropped <c>memberAttribute</c> on the floor.
    /// </summary>
    [Fact]
    public void BuildAt_never_returns_a_source_that_omits_the_rendered_attribute_text()
    {
        const string sentinel = "ZzzBuildAtCoverageSentinel";
        var rendered = $"[{sentinel}]";

        var sites = Enum.GetValues<AttributeTargets>()
            .Where(t => t != AttributeTargets.All && int.PopCount((int)t) == 1);

        var offenders = new List<string>();
        foreach (var endpoint in EndpointSources.All)
        foreach (var site in sites)
        {
            var source = EndpointSources.BuildAt(endpoint, site, rendered);
            if (source is null) continue; // NoSuchSite: no cell claimed, nothing to check.
            if (!source.Contains(sentinel, StringComparison.Ordinal))
                offenders.Add($"{endpoint}/{site}");
        }

        Assert.True(offenders.Count == 0,
            "BuildAt returned a non-null source that omits the rendered attribute text for: "
            + string.Join(", ", offenders) + ". A non-null result claims a cell exists; add the missing slot "
            + "to the endpoint's template in Endpoints.cs, or return null (NoSuchSite) if there genuinely is "
            + "no such site.");
    }

    /// <summary>
    ///     Regression guard for the assembly-site placement bug: an <c>[assembly: ...]</c> attribute must
    ///     follow every <c>using</c> directive (CS1529 otherwise), and a constructor argument like
    ///     <c>typeof(Dst)</c> must still resolve even though the attribute necessarily sits above
    ///     <c>namespace Demo;</c> (CS0246 otherwise). <see cref="BuildAt_never_returns_a_source_that_omits_the_rendered_attribute_text" />
    ///     would not have caught either — the rendered text survives intact in a syntactically broken source.
    /// </summary>
    [Fact]
    public void BuildAt_at_the_assembly_site_produces_compilable_source()
    {
        var element = SurfaceCatalog.Elements.Single(e =>
            string.Equals(e.UsageName, "DwarfProvidesMap", StringComparison.Ordinal));
        var rendered = "[DwarfProvidesMap(typeof(Dst), typeof(Src))]\n[DwarfProvidesMap(typeof(Src), typeof(Dst))]";
        var source = EndpointSources.BuildAt(Endpoint.CreateMap, element.ValidOn, rendered);
        Assert.NotNull(source);

        GeneratorAssert.EmitsCompilableCode(source!);
    }
}
