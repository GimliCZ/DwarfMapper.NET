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
        var c = new SurfaceCase(element, AttributeTargets.Method, "[MapIgnore(\"Name\")]", "ctor(1)", element.ProbeKey, null);

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
        var c = new SurfaceCase(element, AttributeTargets.Method, "[DwarfMapper]", "illegal-site", element.ProbeKey, null);

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

    /// <summary>
    ///     Regression guard for the Method/Property-Field routing bug: for every endpoint that HAS a mapping
    ///     method (i.e. not <see cref="Endpoint.Registry" /> or <see cref="Endpoint.CoLocatedHost" />, where a
    ///     Property/Field site already lands on a real member), the Method-site and Property-site sources for
    ///     the same rendered text must differ. They were byte-identical before this fix — both routed through
    ///     <c>memberAttribute</c> onto the mapping method — so a Property/Field cell silently measured
    ///     METHOD-placement semantics. That is not <see cref="SurfaceEffect.NotCompilable" /> (the site is
    ///     syntactically legal) and the containment guard above cannot see it (the attribute text IS present)
    ///     — a plausible, wrong classification is exactly what this test exists to catch.
    /// </summary>
    [Fact]
    public void Method_and_Property_sites_are_not_measured_as_the_same_source()
    {
        const string rendered = "[MapIgnore(\"Name\")]";
        var methodBased = EndpointSources.All
            .Where(e => e is not (Endpoint.Registry or Endpoint.CoLocatedHost));

        foreach (var endpoint in methodBased)
        {
            var methodSource = EndpointSources.BuildAt(endpoint, AttributeTargets.Method, rendered);
            var propertySource = EndpointSources.BuildAt(endpoint, AttributeTargets.Property, rendered);

            Assert.NotEqual(methodSource, propertySource);
        }
    }

    /// <summary>
    ///     Regression guard for the Property/Field routing bug, and the twin of
    ///     <see cref="Method_and_Property_sites_are_not_measured_as_the_same_source" /> one level down. A
    ///     single <c>BuildAt</c> arm handled <c>AttributeTargets.Property or AttributeTargets.Field</c> and
    ///     DISCARDED the site, so for the two elements legal on both — <c>MapProperty</c> and
    ///     <c>MapIgnore</c> — every Field cell produced byte-identical source to its Property cell at all
    ///     seven endpoints. The matrix read as fully measured while a field-only divergence was invisible.
    ///     <para>
    ///         Neither of the guards next door can see this: the placement is legal, so it is not
    ///         <see cref="SurfaceEffect.NotCompilable" />, and the rendered attribute text IS present, so the
    ///         containment guard passes. Only comparing the two sites' sources exposes it — which is also why
    ///         "both sites honestly decline" is accepted here: a cell that does not exist is not a cell
    ///         measured under the wrong label.
    ///     </para>
    ///     <para>
    ///         The element set is DERIVED from <c>AttributeUsage.ValidOn</c> rather than listed, so a third
    ///         element becoming legal on both sites acquires this guard with no edit here.
    ///     </para>
    /// </summary>
    [Fact]
    public void Property_and_Field_sites_are_not_measured_as_the_same_source()
    {
        var legalOnBoth = SurfaceCatalog.Elements
            .Where(e => (e.ValidOn & AttributeTargets.Property) == AttributeTargets.Property
                        && (e.ValidOn & AttributeTargets.Field) == AttributeTargets.Field)
            .ToList();

        // Non-vacuity: an empty element set, or a set whose cases stopped producing comparable pairs, would
        // pass this test without comparing anything at all.
        Assert.NotEmpty(legalOnBoth);

        var offenders = new List<string>();
        var compared = 0;
        foreach (var element in legalOnBoth)
        foreach (var c in SurfaceCatalog.CasesFor(element).Where(x => x.Site == AttributeTargets.Property))
        foreach (var endpoint in EndpointSources.All)
        {
            var types = SurfaceFixtures.Get(c.ProbeKey);
            var atProperty = EndpointSources.BuildAt(
                endpoint, AttributeTargets.Property, c.Rendered, types, c.MapperOptions);
            var atField = EndpointSources.BuildAt(
                endpoint, AttributeTargets.Field, c.Rendered, types, c.MapperOptions);

            if (atProperty is null && atField is null) continue; // Both decline: no cell claimed either way.
            compared++;
            if (string.Equals(atProperty, atField, StringComparison.Ordinal))
                offenders.Add($"{element.UsageName}({c.Axis}) @ {endpoint}");
        }

        Assert.True(compared > 0, "No (Property, Field) pair was compared at all — the case space or the "
                                  + "site enumeration collapsed and this guard is measuring nothing.");
        Assert.True(offenders.Count == 0,
            "Property-site and Field-site sources are byte-identical for: " + string.Join(", ", offenders)
            + ". A Field cell answered with the property slot measures the property code path under a field "
            + $"label. Give the endpoint's DTO pair a {nameof(EndpointSources.FieldSlotMarker)} ahead of a "
            + "real field, or return null (NoSuchSite) for the Field site — never fall through to the other "
            + "site's slot.");
    }

    /// <summary>
    ///     Declares, rather than silently absorbs, the fixtures that have no member slot — neither
    ///     <see cref="EndpointSources.PropertySlotMarker" /> nor <see cref="EndpointSources.FieldSlotMarker" />
    ///     — for the method-based endpoints' Property/Field sites. Each one means a whole run of
    ///     Property/Field-site cells for that fixture reads <see cref="SurfaceEffect.NoSuchSite" /> instead of
    ///     being measured — an honest "no cell" rather than a wrong classification, but still unmeasured
    ///     coverage. If this count changes, it was a deliberate choice (a fixture gained or lost a marker),
    ///     not a drift nobody noticed.
    /// </summary>
    [Fact]
    public void Fixtures_without_a_member_slot_are_counted_not_silently_absent()
    {
        var missing = SurfaceFixtures.All
            .Where(kv => !kv.Value.Contains(EndpointSources.PropertySlotMarker, StringComparison.Ordinal)
                         && !kv.Value.Contains(EndpointSources.FieldSlotMarker, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        // 17 → 18 with `flattenable-nested-member`, raised deliberately as this assertion's own message
        // instructs. It is not a coverage regression: [Flatten] is AttributeTargets.Method by AttributeUsage,
        // so no Property- or Field-site case demands that fixture and none reads NoSuchSite for want of a slot
        // there. Giving it a marker anyway would have kept the number at 17 while declaring a slot nothing
        // splices into — a count that no longer describes anything, which is the opposite of what this
        // assertion is for.
        //
        // 18 → 19 with `polymorphic-hierarchy`, raised deliberately for exactly the same reason:
        // [MapDerivedType] is AttributeTargets.Method in both of its forms, so no Property- or Field-site case
        // demands that fixture either.
        const int baseline = 19;
        Assert.True(missing.Count == baseline,
            $"{missing.Count} of {SurfaceFixtures.All.Count} fixtures carry neither "
            + $"{nameof(EndpointSources.PropertySlotMarker)} nor {nameof(EndpointSources.FieldSlotMarker)}: "
            + string.Join(", ", missing) + ". Each one means every Property/Field-site case that needs that "
            + "fixture reads NoSuchSite for the method-based endpoints instead of being measured — declared "
            + $"and counted, not silently dropped. Baseline is {baseline}; if you added a marker to a fixture, "
            + "lower it deliberately, and if you added a fixture without one, raise it deliberately.");
    }

    /// <summary>
    ///     The over-subtraction direction: a case that legitimately re-triggers a CS id the baseline already
    ///     carries once must still read <see cref="SurfaceEffect.NotCompilable" />, not be masked because the
    ///     id is already "known." Keying the baseline subtraction on id MEMBERSHIP (an earlier version of
    ///     <see cref="SurfaceProbe.Classify" /> did exactly this) would treat "the id showed up before" and
    ///     "the id showed up again, for an unrelated reason" as the same fact.
    ///     <para>
    ///         This cannot currently be constructed end-to-end through an actual compile: every class-model
    ///         endpoint in <see cref="EndpointSources" /> declares exactly ONE partial mapping method, so
    ///         <c>CS8795</c> — the id a blocking DWARF error produces, by leaving that one method unimplemented
    ///         — tops out at one occurrence per compilation regardless of what the case under test does, and
    ///         nothing in <see cref="EndpointSources.BuildAt" /> lets a case add a second partial method to
    ///         fail independently. So <see cref="SurfaceProbe.NewOccurrences" /> — the pure counting
    ///         function <see cref="SurfaceProbe.Classify" /> delegates to — is verified directly instead.
    ///     </para>
    /// </summary>
    [Fact]
    public void NewOccurrences_flags_an_id_the_baseline_already_carries_once_when_the_case_doubles_it()
    {
        var baselineCounts = new Dictionary<string, int> { ["CS8795"] = 1 };
        var withCounts = new Dictionary<string, int> { ["CS8795"] = 2 };

        var result = SurfaceProbe.NewOccurrences(withCounts, baselineCounts);

        Assert.Equal(["CS8795"], result);
    }

    /// <summary>The correct-suppression direction, for contrast: an id whose count is unchanged from the
    /// baseline is not new, however many times it already occurs.</summary>
    [Fact]
    public void NewOccurrences_does_not_flag_an_id_whose_count_is_unchanged()
    {
        var baselineCounts = new Dictionary<string, int> { ["CS8795"] = 1 };
        var withCounts = new Dictionary<string, int> { ["CS8795"] = 1 };

        var result = SurfaceProbe.NewOccurrences(withCounts, baselineCounts);

        Assert.Empty(result);
    }

    /// <summary>
    ///     ALL new ids, not just the first — the property <see cref="SurfaceProbe.Classify" />'s refusal rule
    ///     depends on. That rule reads "every CS id the case introduced is an absent-emission id", and a
    ///     counting function returning only the first would answer that question about one id while the case
    ///     introduced two: a <c>CS8795</c> ordered ahead of a genuine <c>CS0111</c> would let a real placement
    ///     defect be reclassified as a clean refusal and disappear from the counted population.
    /// </summary>
    [Fact]
    public void NewOccurrences_returns_every_new_id_not_only_the_first()
    {
        var baselineCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var withCounts = new Dictionary<string, int>(StringComparer.Ordinal)
            { ["CS8795"] = 1, ["CS0111"] = 1 };

        var result = SurfaceProbe.NewOccurrences(withCounts, baselineCounts);

        Assert.Equal(["CS0111", "CS8795"], result);
    }
}
