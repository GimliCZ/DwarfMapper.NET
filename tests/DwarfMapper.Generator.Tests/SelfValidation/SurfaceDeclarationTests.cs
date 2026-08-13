// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;

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
