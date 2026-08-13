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
}
