// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
///     Deterministic policy for choosing a destination constructor to use for mapping.
/// </summary>
internal static class ConstructorSelector
{
    private const string DwarfMapperConstructorAttribute = KnownNames.DwarfMapperConstructorFqn;
    private const string ObsoleteAttribute = "System.ObsoleteAttribute";

    /// <summary>
    ///     Select the best constructor for <paramref name="target" /> to use for mapping.
    /// </summary>
    /// <param name="target">The destination type.</param>
    /// <param name="diagnostics">Diagnostic sink; DWARF025/026 appended on failure.</param>
    /// <param name="location">Location for diagnostic reporting.</param>
    /// <param name="useObjectInitializerOnly">
    ///     Set to <see langword="true" /> when a parameterless constructor was chosen
    ///     (i.e. the existing object-initializer path — no ctor args needed).
    /// </param>
    /// <returns>
    ///     The selected <see cref="IMethodSymbol" />, or <see langword="null" /> when a blocking
    ///     diagnostic was emitted and the method should be skipped.
    /// </returns>
    public static IMethodSymbol? Select(
        Compilation compilation,
        INamedTypeSymbol target,
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        out bool useObjectInitializerOnly,
        bool allowNonPublicConstructors = false)
    {
        useObjectInitializerOnly = false;

        // ── Policy 0 (pre-filter): accessible parameterless ctor exists → object-initializer path.
        // We include implicitly-declared parameterless ctors here so that plain classes (which have
        // an implicit public parameterless ctor) continue to use the existing object-initializer path.
        // EXCEPTION: structs with explicit non-parameterless ctors — the implicit struct parameterless ctor
        // is a no-op (zero-init) and we should prefer the explicit ctor for proper initialization.
        // Detect this case: target is a value type AND has at least one explicit (non-implicitly-declared)
        // non-parameterless ctor AND there is no explicit parameterless ctor.
        // NOTE: this deliberately does NOT use IsUsableCandidate — it asks "does the struct declare an
        // explicit non-parameterless ctor at all?" so we can avoid silently using the implicit zero-init
        // parameterless ctor. An explicit ctor that is itself unusable (ref/out) must still suppress the
        // implicit fallback, so the unusable case surfaces DWARF026 rather than a wrong zero-init result.
        var hasExplicitNonParameterlessCtor = target.InstanceConstructors.Any(c =>
            !c.IsImplicitlyDeclared
            && IsAccessible(c, compilation, allowNonPublicConstructors)
            && !c.IsStatic
            && c.Parameters.Length > 0
            && !IsObsolete(c));

        // For value types (structs) with explicit non-parameterless ctors, don't use the implicit
        // parameterless ctor — prefer the explicit ctor instead.
        var skipImplicitParameterlessCtor = target.IsValueType && hasExplicitNonParameterlessCtor;

        var anyParameterless = target.InstanceConstructors.FirstOrDefault(c =>
            IsAccessible(c, compilation, allowNonPublicConstructors)
            && !c.IsStatic
            && c.Parameters.Length == 0
            && !IsObsolete(c)
            && (!c.IsImplicitlyDeclared || !skipImplicitParameterlessCtor));

        if (anyParameterless is not null)
        {
            // Check whether any annotated constructor wins over the parameterless one.
            // If so, fall through to explicit-annotation handling below.
            // Count all annotated candidates so that >1 → DWARF025 (same policy as the no-parameterless path).
            // An annotated ctor must also be a USABLE candidate (no ref/out, not a copy ctor, etc.) — otherwise
            // selecting it would emit CS1620/CS-invalid code. An unusable annotated ctor falls back to the
            // (safe) parameterless object-initializer path rather than producing broken output.
            var annotatedOverrides = target.InstanceConstructors
                .Where(c =>
                    IsUsableCandidate(c, target, compilation, allowNonPublicConstructors)
                    && c.GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == DwarfMapperConstructorAttribute))
                .ToList();

            if (annotatedOverrides.Count == 0)
            {
                // Parameterless ctor wins → object-initializer path.
                useObjectInitializerOnly = true;
                return anyParameterless;
            }

            if (annotatedOverrides.Count > 1)
            {
                // Multiple [DwarfMapperConstructor] annotations — ambiguous regardless of parameterless ctor.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConstructor, location, target.Name));
                return null;
            }

            // Exactly one annotated ctor overrides parameterless preference.
            return annotatedOverrides[0];
        }

        // Build the candidate list: public, non-static, non-implicitly-declared, non-copy, non-obsolete,
        // and no ref/out parameters (ref/out args cannot be emitted with plain named-argument syntax).
        // `in` parameters (RefKind.In / ref-readonly) are fine — callable with plain named args.
        var candidates = new List<IMethodSymbol>();
        foreach (var ctor in target.InstanceConstructors)
            if (IsUsableCandidate(ctor, target, compilation, allowNonPublicConstructors))
                candidates.Add(ctor);

        // ── Policy 1: [DwarfMapperConstructor] annotation ────────────────────
        var annotated = candidates
            .Where(c => c.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == DwarfMapperConstructorAttribute))
            .ToList();

        if (annotated.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConstructor, location, target.Name));
            return null;
        }

        if (annotated.Count == 1) return annotated[0];

        // ── Policy 2 (already handled above — no parameterless ctor at this point) ─

        // ── Policy 3: no candidates at all → DWARF026 ────────────────────────
        if (candidates.Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoMappableConstructor, location, target.Name));
            return null;
        }

        // ── Policy 4: exactly one non-parameterless candidate ─────────────────
        if (candidates.Count == 1) return candidates[0];

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

    // Public ctors are always usable. A non-public ctor is usable only when the mapper opted in via
    // [DwarfMapper(AllowNonPublicConstructors = true)] AND code in the mapper's own assembly can reach
    // it — i.e. an internal/protected-internal ctor in the same assembly or one exposed via
    // [InternalsVisibleTo]. private / protected ctors (no derivation here) are never reachable, so
    // IsSymbolAccessibleWithin filters them out even when the flag is set.
    private static bool IsAccessible(IMethodSymbol ctor, Compilation compilation, bool allowNonPublic)
    {
        return ctor.DeclaredAccessibility == Accessibility.Public
               || (allowNonPublic && compilation.IsSymbolAccessibleWithin(ctor, compilation.Assembly));
    }

    /// <summary>
    ///     A constructor the generator can actually emit a call to: accessible, instance, explicitly declared,
    ///     not the record copy ctor, not <c>[Obsolete]</c>, and free of <c>ref</c>/<c>out</c> parameters (which
    ///     cannot be passed as named args — CS1620). Shared by every selection path so an annotated or
    ///     most-params pick can never resolve to a ctor that would emit broken code.
    /// </summary>
    private static bool IsUsableCandidate(IMethodSymbol ctor, INamedTypeSymbol target, Compilation compilation,
        bool allowNonPublic)
    {
        if (!IsAccessible(ctor, compilation, allowNonPublic)) return false;
        if (ctor.IsStatic) return false;
        if (ctor.IsImplicitlyDeclared) return false;
        // Record copy constructor: single parameter whose type == target.
        if (ctor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, target))
            return false;
        if (IsObsolete(ctor)) return false;
        // ref / out parameters cannot be emitted as plain named args (CS1620). `in` (RefKind.In) is fine.
        if (ctor.Parameters.Any(p => p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)) return false;
        return true;
    }

    private static bool IsObsolete(IMethodSymbol method)
    {
        return method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ObsoleteAttribute);
    }
}
