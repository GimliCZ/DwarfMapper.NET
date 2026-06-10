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

        // ── Policy 0 (pre-filter): accessible parameterless ctor exists → object-initializer path.
        // Must be checked BEFORE filtering by IsImplicitlyDeclared so that plain classes with
        // implicitly-generated parameterless ctors (IsImplicitlyDeclared=true) are treated as
        // "has parameterless ctor" and continue to use the existing object-initializer path.
        // Record positional types do NOT have an implicit parameterless ctor, so they fall through.
        var anyParameterless = target.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && !c.IsStatic
            && c.Parameters.Length == 0
            && !IsObsolete(c));

        if (anyParameterless is not null)
        {
            // Check whether any annotated constructor wins over the parameterless one.
            // If so, fall through to explicit-annotation handling below.
            var annotatedOverride = target.InstanceConstructors.FirstOrDefault(c =>
                c.DeclaredAccessibility == Accessibility.Public
                && !c.IsStatic
                && !c.IsImplicitlyDeclared
                && !IsObsolete(c)
                && c.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == DwarfMapperConstructorAttribute));

            if (annotatedOverride is null)
            {
                // Parameterless ctor wins → object-initializer path.
                useObjectInitializerOnly = true;
                return anyParameterless;
            }

            // Annotated ctor overrides parameterless preference.
            return annotatedOverride;
        }

        // Build the candidate list: public, non-static, non-implicitly-declared, non-copy, non-obsolete.
        // At this point we know there is no accessible parameterless ctor.
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

        // ── Policy 2 (already handled above — no parameterless ctor at this point) ─

        // ── Policy 3: no candidates at all → DWARF026 ────────────────────────
        if (candidates.Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoMappableConstructor, location, target.Name));
            return null;
        }

        // ── Policy 4: exactly one non-parameterless candidate ─────────────────
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        // ── Policy 5: multiple candidates → most params; tie → DWARF025 ───────
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
