// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The forcing function for the whole surface-coverage architecture: a public attribute that carries no
///     <c>[DwarfSurface]</c> has no category, therefore no obligation, therefore no proof. Before this gate,
///     thirteen public attributes had zero consumer-shaped presence and nothing in the repository failed.
/// </summary>
public sealed class SurfaceDeclarationTests
{
    /// <summary>Every public attribute type shipped by the runtime package.</summary>
    public static IReadOnlyList<Type> PublicAttributeTypes { get; } =
        typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false } && typeof(Attribute).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void The_public_attribute_scan_is_not_vacuous()
    {
        Assert.True(PublicAttributeTypes.Count >= 30,
            $"Only {PublicAttributeTypes.Count} public attribute types reflected; the surface has ~32. "
            + "Either the package genuinely shrank (lower this floor deliberately) or the reflection stopped "
            + "seeing the surface and every gate built on it has gone vacuous.");
    }

    [Fact]
    public void Every_public_attribute_declares_a_DwarfSurface_category()
    {
        var undeclared = PublicAttributeTypes
            .Where(t => t.GetCustomAttribute<DwarfSurfaceAttribute>(inherit: false) is null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(undeclared.Count == 0,
            "Public attribute type(s) with no [DwarfSurface] declaration:\n  "
            + string.Join("\n  ", undeclared)
            + "\n\nAdd [DwarfSurface(SurfaceCategory.X, ...)] to the declaration. There is deliberately no "
            + "'exempt' category: every category carries a proof obligation, and choosing one is how the "
            + "obligation gets assigned. See docs/superpowers/specs/"
            + "2026-08-13-surface-coverage-architecture-design.md for the category table.");
    }

    [Fact]
    public void SurfaceEndpoints_and_Endpoint_name_the_same_seven_endpoints()
    {
        // Two enums that drift apart would silently repoint every AppliesTo claim at the wrong endpoint,
        // and the matrix would keep passing while measuring the wrong cell.
        var flags = Enum.GetNames<SurfaceEndpoints>()
            .Where(n => n is not ("None" or "All"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var endpoints = Enum.GetNames<Contracts.Endpoint>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(endpoints, flags);
    }

    [Fact]
    public void Every_catalog_element_yields_at_least_one_case()
    {
        var barren = Contracts.SurfaceCatalog.Elements
            .Where(e => Contracts.SurfaceCatalog.CasesFor(e).Count == 0)
            .Select(e => e.UsageName)
            .ToList();

        Assert.True(barren.Count == 0,
            "Surface element(s) that produce NO probe case, so the matrix silently skips them entirely:\n  "
            + string.Join("\n  ", barren)
            + "\n\nThis is the vacuity failure the whole arrangement exists to prevent: an element with no "
            + "cases passes every cell it has, which is none.");
    }

    [Fact]
    public void The_catalog_produces_a_case_count_in_the_expected_order_of_magnitude()
    {
        var total = Contracts.SurfaceCatalog.Elements.Sum(e => Contracts.SurfaceCatalog.CasesFor(e).Count);
        Assert.InRange(total, 60, 4000);
    }

    /// <summary>
    ///     Every <c>[DwarfSurfaceSite]</c> narrowing is well-formed: it names only sites the element's
    ///     <c>AttributeUsage</c> permits, no two claims cover one site, it states a reason, and it actually
    ///     narrows something.
    ///     <para>
    ///         A malformed override is worse than a missing one. It reads as a reviewed, structural decision
    ///         while the cells it was meant to govern are decided by the element default — a stale site (one
    ///         removed from <c>AttributeUsage</c> later) is exactly how a claim quietly stops applying, and a
    ///         second claim on the same site hands the site to whichever the reflection ordered first.
    ///     </para>
    /// </summary>
    [Fact]
    public void Every_declared_site_override_is_well_formed()
    {
        var problems = Contracts.SurfaceCatalog.Elements
            .SelectMany(e => Contracts.SurfaceCatalog.ValidateSiteClaims(
                e.UsageName, e.ValidOn, e.AppliesTo, e.SiteClaims))
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    ///     Feeds <c>ValidateSiteClaims</c> each malformed shape directly. Against the two well-formed
    ///     declarations the repository actually has, every branch of that validator passes vacuously, and a
    ///     gate that has never fired is unverified code — the same reason
    ///     <c>SurfaceProbe.FirstNewOccurrence</c> is a separated pure function with its own test.
    /// </summary>
    [Fact]
    public void The_site_override_validator_rejects_every_malformed_shape()
    {
        const AttributeTargets validOn = AttributeTargets.Method | AttributeTargets.Property;
        const SurfaceEndpoints def = SurfaceEndpoints.All;

        static string[] Check(AttributeTargets on, SurfaceEndpoints fallback, params SurfaceSiteClaim[] cs) =>
            Contracts.SurfaceCatalog.ValidateSiteClaims("Probe", on, fallback, cs).ToArray();

        // A site AttributeUsage does not permit: the narrowing applies to nothing.
        Assert.Contains("Field", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Field, SurfaceEndpoints.Registry, "shape"))),
            StringComparison.Ordinal);

        // Partially illegal: Property is fine, Field is not, and the legal half must not excuse the other.
        Assert.Contains("Field", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Property | AttributeTargets.Field,
                SurfaceEndpoints.Registry, "shape"))),
            StringComparison.Ordinal);

        // Two claims covering one site.
        Assert.Contains("both cover", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "shape"),
            new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.CreateMap, "shape"))),
            StringComparison.Ordinal);

        // Overlapping rather than identical: Property is claimed twice, Method only once.
        Assert.Contains("both cover", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "shape"),
            new SurfaceSiteClaim(AttributeTargets.Property | AttributeTargets.Method,
                SurfaceEndpoints.CreateMap, "shape"))),
            StringComparison.Ordinal);

        // No reason stated.
        Assert.Contains("states no reason", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "   "))),
            StringComparison.Ordinal);

        // Restates the default: narrows nothing.
        Assert.Contains("restates", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Property, def, "shape"))),
            StringComparison.Ordinal);

        // Names no site at all.
        Assert.Contains("no site at all", Assert.Single(Check(validOn, def,
            new SurfaceSiteClaim(default, SurfaceEndpoints.Registry, "shape"))),
            StringComparison.Ordinal);

        // The well-formed shape the repository actually uses reports nothing.
        Assert.Empty(Check(validOn, def,
            new SurfaceSiteClaim(AttributeTargets.Property, SurfaceEndpoints.Registry, "shape")));
    }

    /// <summary>
    ///     A <c>[DwarfSurfaceSite]</c> on a type with no <c>[DwarfSurface]</c> has no default to narrow, and
    ///     <c>SurfaceCatalog</c> filters the type out of the element set entirely — so the override, and every
    ///     cell it was written to govern, would vanish without a word.
    /// </summary>
    [Fact]
    public void No_site_override_sits_on_a_type_that_declares_no_category()
    {
        var orphans = typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
            .Where(t => t.GetCustomAttributes<DwarfSurfaceSiteAttribute>(inherit: false).Any()
                        && t.GetCustomAttribute<DwarfSurfaceAttribute>(inherit: false) is null)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "Type(s) carrying [DwarfSurfaceSite] but no [DwarfSurface]: " + string.Join(", ", orphans)
            + ". The override narrows a default that does not exist, and the type is not in the catalogue at "
            + "all, so neither the claim nor its cells are ever looked at.");
    }

    /// <summary>
    ///     The site overrides are load-bearing, not decoration: at least one element must resolve DIFFERENT
    ///     claims at two of its own sites. If every override were deleted — or <c>ClaimFor</c> stopped
    ///     consulting them — this fails, rather than the matrix quietly going back to one claim per element
    ///     and ~100 cells changing verdict with nothing to say so.
    /// </summary>
    [Fact]
    public void At_least_one_element_claims_differently_at_two_of_its_sites()
    {
        var siteAware = Contracts.SurfaceCatalog.CrossProductElements
            .Where(e => Contracts.SurfaceCatalog.SitesOf(e)
                .Select(s => Contracts.SurfaceCatalog.ClaimFor(e, s))
                .Distinct()
                .Count() > 1)
            .Select(e => e.UsageName)
            .ToList();

        Assert.True(siteAware.Count >= 2,
            $"Only {siteAware.Count} element(s) resolve a different claim at different sites "
            + $"({string.Join(", ", siteAware)}). Two do: MapProperty and MapIgnore, whose member placement "
            + "is the [MapTo] registry form rather than a second way to configure a mapper. Fewer means "
            + "either an override was deleted or ClaimFor stopped reading them, and ~100 member-site cells "
            + "are back to being judged by the element-wide default.");
    }

    /// <summary>
    ///     There are two demand sources, not one. Element-level: <c>[DwarfSurface(ProbeKey = "…")]</c> on an
    ///     attribute TYPE. Property-level: <c>OptionCatalog</c>'s option → key map, because the class-level
    ///     options are PROPERTIES of a single type (<c>DwarfMapperAttribute</c>) and a type-level attribute
    ///     cannot express a per-property fixture. Supply is <see cref="Contracts.SurfaceFixtures" />; the
    ///     bijection holds against the UNION of both demand sources, not either alone.
    /// </summary>
    [Fact]
    public void Every_declared_ProbeKey_binds_to_exactly_one_fixture()
    {
        var elementDemand = Contracts.SurfaceCatalog.Elements
            .Where(e => e.ProbeKey is not null)
            .Select(e => (Key: e.ProbeKey!, Source: "element " + e.UsageName));

        var optionDemand = Contracts.OptionCatalog.ProbeKeys
            .Select(kv => (Key: kv.Value, Source: "option " + kv.Key));

        var demandSources = elementDemand.Concat(optionDemand)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Join(" & ", g.Select(x => x.Source)), StringComparer.Ordinal);

        var demanded = demandSources.Keys.ToHashSet(StringComparer.Ordinal);
        var supplied = Contracts.SurfaceFixtures.All.Keys.ToHashSet(StringComparer.Ordinal);

        var unbound = demanded.Except(supplied).OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => $"{k} (demanded by {demandSources[k]})")
            .ToList();
        Assert.True(unbound.Count == 0,
            "ProbeKey(s) declared with no fixture in SurfaceFixtures: " + string.Join(", ", unbound)
            + ". The declaration states a demand — either an element's [DwarfSurface(ProbeKey = ...)] or an "
            + "entry in OptionCatalog.ProbeKeys — and the fixture is the supply. An unbound key means the "
            + "demanding element or option's probe silently falls back to the flat DTO pair, which cannot "
            + "trigger it, and the cell reads 'no effect' while the feature works perfectly.");

        var orphaned = supplied.Except(demanded).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(orphaned.Count == 0,
            "Fixture(s) in SurfaceFixtures claimed by no [DwarfSurface(ProbeKey = ...)] and no "
            + "OptionCatalog.ProbeKeys entry: " + string.Join(", ", orphaned)
            + ". An orphaned fixture is dead weight that reads as coverage.");
    }
}
