// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Deterministic policy for choosing a destination constructor to use for mapping.
/// </summary>
internal static class ConstructorSelector
{
    private const string DwarfMapperConstructorAttribute = "DwarfMapper.DwarfMapperConstructorAttribute";
    private const string ObsoleteAttribute = "System.ObsoleteAttribute";

    /// <summary>
    /// Select the best constructor for <paramref name="target"/> to use for mapping.
    /// </summary>
    /// <param name="target">The destination type.</param>
    /// <param name="diagnostics">Diagnostic sink; DWARF025/026 appended on failure.</param>
    /// <param name="location">Location for diagnostic reporting.</param>
    /// <param name="useObjectInitializerOnly">
    /// Set to <see langword="true"/> when a parameterless constructor was chosen
    /// (i.e. the existing object-initializer path — no ctor args needed).
    /// </param>
    /// <returns>
    /// The selected <see cref="IMethodSymbol"/>, or <see langword="null"/> when a blocking
    /// diagnostic was emitted and the method should be skipped.
    /// </returns>
    public static IMethodSymbol? Select(
        INamedTypeSymbol target,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        out bool useObjectInitializerOnly)
    {
        useObjectInitializerOnly = false;

        // Build the candidate list: public, non-static, non-implicitly-declared, non-copy, non-obsolete.
        var candidates = new List<IMethodSymbol>();
        foreach (var ctor in target.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
            if (ctor.IsStatic) continue;
            if (ctor.IsImplicitlyDeclared) continue;
            // Exclude the record copy constructor: single parameter whose type == target.
            if (ctor.Parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, target))
                continue;
            if (IsObsolete(ctor)) continue;

            candidates.Add(ctor);
        }

        // ── Policy 1: [DwarfMapperConstructor] annotation ────────────────────
        var annotated = candidates
            .Where(c => c.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == DwarfMapperConstructorAttribute))
            .ToList();

        if (annotated.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConstructor, location, target.Name));
            return null;
        }

        if (annotated.Count == 1)
        {
            return annotated[0];
        }

        // ── Policy 2: parameterless ctor → object-initializer path ────────────
        var parameterless = candidates.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterless is not null)
        {
            useObjectInitializerOnly = true;
            return parameterless;
        }

        // ── Policy 3: exactly one non-parameterless candidate ─────────────────
        if (candidates.Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoMappableConstructor, location, target.Name));
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // ── Policy 4: multiple candidates → most params; tie → DWARF025 ───────
        var maxParams = candidates.Max(c => c.Parameters.Length);
        var withMax = candidates.Where(c => c.Parameters.Length == maxParams).ToList();

        if (withMax.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConstructor, location, target.Name));
            return null;
        }

        return withMax[0];
    }

    private static bool IsObsolete(IMethodSymbol method) =>
        method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ObsoleteAttribute);
}
