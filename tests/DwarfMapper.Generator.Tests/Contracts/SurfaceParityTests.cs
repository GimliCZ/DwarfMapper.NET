// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     The executed cross-product: every surface element, at every legal declaration site, in every case its
///     declaration admits, at every endpoint.
///     <para>
///         Verified in BOTH directions against the element's own <c>AppliesTo</c> claim. A claimed endpoint
///         where the element does nothing observable fails (the claim over-reaches); an unclaimed endpoint
///         where it changes the output fails too (the claim under-reaches and the matrix would otherwise skip
///         a live cell). There is therefore no value of <c>AppliesTo</c> that passes vacuously.
///     </para>
///     <para>
///         Traited so it can run as its own CI leg: this is roughly seven times the work of the option matrix.
///     </para>
/// </summary>
[Trait("Category", "SurfaceMatrix")]
public sealed class SurfaceParityTests
{
    /// <summary>
    ///     One row per cell, keyed by (usage name, generic ARITY, axis, site, endpoint).
    ///     <para>
    ///         Arity is part of the key because five elements share a usage name with a generic twin —
    ///         <c>MapProperty</c>/<c>MapProperty&lt;S,T&gt;</c>, <c>MapIgnore</c>/<c>MapIgnore&lt;T&gt;</c>,
    ///         <c>MapValue</c>/<c>MapValue&lt;T&gt;</c>, <c>MapNullSkip</c>/<c>MapNullSkip&lt;S,T&gt;</c> and
    ///         the two <c>MapDerivedType</c> forms — and <see cref="SurfaceElement.UsageName" /> strips the
    ///         arity by design. Keyed on the name alone, the two <c>MapDerivedType</c> forms (same sites, same
    ///         axes) produce identical rows, and resolving them back would hand both to whichever came first:
    ///         the generic form would never be measured while appearing in the matrix as though it had been.
    ///     </para>
    /// </summary>
    public static TheoryData<string, int, string, string, Endpoint> Cells()
    {
        var data = new TheoryData<string, int, string, string, Endpoint>();
        foreach (var element in SurfaceCatalog.CrossProductElements)
        foreach (var c in SurfaceCatalog.CasesFor(element))
        foreach (var endpoint in EndpointSources.All)
            data.Add(element.UsageName, Arity(element), c.Axis, c.Site.ToString(), endpoint);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Every_cell_matches_the_elements_own_AppliesTo_claim(
        string usageName, int arity, string axis, string site, Endpoint endpoint)
    {
        var (element, c) = Resolve(usageName, arity, axis, site);
        var claimed = (element.AppliesTo & ToFlag(endpoint)) != 0;
        var (effect, detail) = SurfaceProbe.Classify(c, endpoint);

        // No cell to judge: the endpoint has no such site, or AttributeUsage forbids it and the compiler
        // agrees. Both are the declaration telling the truth.
        if (effect is SurfaceEffect.NoSuchSite or SurfaceEffect.NotCompilable) return;

        if (claimed)
        {
            if (effect is SurfaceEffect.Honoured or SurfaceEffect.Refused
                or SurfaceEffect.UnhonouredButLoud) return;

            if (MemberSiteIsNotADeclarationSite(element, c.Site, endpoint)) return;

            // Task 7 renames this to DeclaredDivergences.Reasons. Use the current name here so this task
            // compiles on its own; update the reference as part of that rename, not before it.
            if (OptionGaps.KnownSilent.TryGetValue($"{usageName}@{endpoint}", out var why))
            {
                Assert.False(string.IsNullOrWhiteSpace(why));
                return;
            }

            Assert.Fail(
                $"{c.Rendered} on a {site} CLAIMS {endpoint} (AppliesTo) but is SILENT there: no diagnostic, "
                + $"and output byte-identical to the same source without it. ({detail})\n\n"
                + "The caller wrote something, the generator accepted it, changed nothing, and said nothing. "
                + "Three ways out, in order of preference:\n"
                + $"  1. honour it at {endpoint};\n"
                + $"  2. refuse it there with a diagnostic;\n"
                + $"  3. drop {endpoint} from this element's AppliesTo — but only if it STRUCTURALLY cannot "
                + "apply, not because it currently does not.");
        }
        else
        {
            if (effect is SurfaceEffect.Silent or SurfaceEffect.UnhonouredButLoud) return;

            Assert.Fail(
                $"{c.Rendered} on a {site} does NOT claim {endpoint} (AppliesTo) but is {effect} there "
                + $"({detail}). The claim under-reaches: this cell is live and the matrix was told to skip "
                + $"it. Add {endpoint} to the element's AppliesTo.");
        }
    }

    /// <summary>
    ///     Counts an element as acting at <see cref="Endpoint.CreateMap" /> if ANY of its cases does, not just
    ///     the first. The brief specified the first case; measured, that reads 7 elements rather than the 12
    ///     that genuinely act, because <c>CasesFor</c> orders the zero-argument constructor first and a bare
    ///     <c>[DwarfMapper]</c> or <c>[MapIgnore]</c> configures nothing — the weakest case in the element's
    ///     whole domain stood in for the element. Widening it makes the guard stronger, not looser: it now
    ///     fails if an element loses its LAST acting case rather than only its first-listed one.
    /// </summary>
    [Fact]
    public void The_matrix_is_not_vacuous()
    {
        // If Classify() broke so every cell read NoSuchSite, the theory would return at the first guard and
        // pass without comparing anything.
        var acting = SurfaceCatalog.CrossProductElements
            .Count(e => SurfaceCatalog.CasesFor(e)
                .Any(c => SurfaceProbe.Classify(c, Endpoint.CreateMap).Effect
                    is SurfaceEffect.Honoured or SurfaceEffect.Refused));

        // Measured at 12 (see Issues/round20/SURFACE-MATRIX-FINDINGS.md); the gate sits below that so an
        // ordinary change does not trip it, and a collapse of the instrument does.
        Assert.True(acting >= 10,
            $"Only {acting} elements visibly act at CreateMap. Either the fixtures stopped triggering their "
            + "elements or Classify() is not observing correctly — in both cases the matrix is passing "
            + "without testing anything.");
    }

    /// <summary>
    ///     Every theory row must name exactly one case. A row that names two is worse than a missing row: the
    ///     duplicate resolves to whichever element the catalogue happened to order first, so the other one is
    ///     never measured while the matrix reports a cell for it. <see cref="Resolve" /> uses
    ///     <c>Single</c> rather than <c>First</c> for the same reason; this states the invariant up front
    ///     instead of leaving it to whichever cell happens to trip over it after several minutes of compiling.
    /// </summary>
    [Fact]
    public void The_theory_key_names_exactly_one_case()
    {
        var duplicates = SurfaceCatalog.CrossProductElements
            .SelectMany(e => SurfaceCatalog.CasesFor(e)
                .Select(c => $"{e.UsageName}`{Arity(e)}/{c.Axis}/{c.Site}"))
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ×{g.Count()}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "The (name, arity, axis, site) key is not unique: " + string.Join(", ", duplicates)
            + ". Add a discriminator to Cells() rather than letting one row stand in for two cases.");
    }

    /// <summary>
    ///     The endpoints whose mapping is declared on a MAPPER, separately from the DTO pair it maps.
    /// </summary>
    private static readonly Endpoint[] MapperDeclaredEndpoints =
        [Endpoint.CreateMap, Endpoint.UpdateInto, Endpoint.Projection, Endpoint.SpanMap, Endpoint.AsyncStream];

    /// <summary>
    ///     Elements whose Property/Field placement is, by their own documentation, the <c>[MapTo]</c> REGISTRY
    ///     form — not a second way to configure a mapper.
    /// </summary>
    private static readonly HashSet<string> MemberPlacementIsTheRegistryForm =
        new(StringComparer.Ordinal) { "MapProperty", "MapIgnore" };

    /// <summary>
    ///     Whether this cell is a member-site placement at an endpoint whose mapping is declared on a mapper
    ///     rather than on the DTO — in which case the DTO's members are not part of that mapper's declaration
    ///     and there is nothing here for the element to configure.
    ///     <para>
    ///         This is a claim about the SHAPE of the endpoint, not about what the generator currently reads.
    ///         At <see cref="Endpoint.Registry" /> the directive lives on the source TYPE and at
    ///         <see cref="Endpoint.CoLocatedHost" /> on the target type, so in both the annotated DTO IS the
    ///         declaration; at the five endpoints listed above the mapping is declared by a partial method on a
    ///         separate <c>[DwarfMapper]</c> class, and the DTOs are ordinary types the consumer may not even
    ///         own. <see cref="MapPropertyAttribute" /> and <see cref="MapIgnoreAttribute" /> say exactly this
    ///         in their own summaries: the member-placement form is "(the <c>[MapTo]</c> registry)". Verified
    ///         at source level as well — <c>MapperExtractor</c> reads these attributes off the class symbol or
    ///         the method symbol, and only <c>Registry/MapToGenerator.cs</c> reads them off member symbols.
    ///     </para>
    ///     <para>
    ///         It lives here, as a per-(element, site, endpoint) predicate, rather than in the element's own
    ///         <c>AppliesTo</c>, because <c>AppliesTo</c> is per-ENDPOINT and cannot express it: at CreateMap
    ///         <c>[MapProperty("Id", "Name")]</c> on the mapping METHOD is refused with DWARF038 while the same
    ///         text on a DTO member is silent. Dropping CreateMap from the claim to satisfy the member cell
    ///         would break the method cell in the other direction, so no value of the flags satisfies both.
    ///         Making <c>[DwarfSurface]</c> site-aware is the real fix and is a maintainer decision.
    ///     </para>
    /// </summary>
    private static bool MemberSiteIsNotADeclarationSite(SurfaceElement element, AttributeTargets site,
        Endpoint endpoint) =>
        site is AttributeTargets.Property or AttributeTargets.Field
        && MemberPlacementIsTheRegistryForm.Contains(element.UsageName)
        && element.Type.GetGenericArguments().Length == 0
        && Array.IndexOf(MapperDeclaredEndpoints, endpoint) >= 0;

    private static (SurfaceElement, SurfaceCase) Resolve(string usageName, int arity, string axis, string site)
    {
        var element = SurfaceCatalog.CrossProductElements
            .Single(e => string.Equals(e.UsageName, usageName, StringComparison.Ordinal)
                         && Arity(e) == arity);
        var c = SurfaceCatalog.CasesFor(element)
            .Single(x => string.Equals(x.Axis, axis, StringComparison.Ordinal)
                         && string.Equals(x.Site.ToString(), site, StringComparison.Ordinal));
        return (element, c);
    }

    private static int Arity(SurfaceElement element) => element.Type.GetGenericArguments().Length;

    private static SurfaceEndpoints ToFlag(Endpoint e) =>
        Enum.Parse<SurfaceEndpoints>(e.ToString());
}
