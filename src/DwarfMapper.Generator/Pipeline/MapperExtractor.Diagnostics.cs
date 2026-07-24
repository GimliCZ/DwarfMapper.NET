// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    /// <summary>
    /// Item 20: for each <c>[GenerateWrapperMap(typeof(W&lt;&gt;))]</c>, append the closed wrapper instantiation
    /// <c>W&lt;A&gt; -&gt; W&lt;B&gt;</c> to <paramref name="genPairs"/> for every already-declared
    /// <c>[GenerateMap&lt;A, B&gt;]</c> pair. The wrapper must be a single-payload generic (one type parameter,
    /// one member of that parameter's type) or a DWARF067 is reported and the attribute is skipped. Only closed
    /// instantiations are produced — open generics are never emitted (AOT-safe).
    /// </summary>
    private static void ExpandWrapperMaps(
        INamedTypeSymbol classSymbol, Compilation comp,
        List<(ITypeSymbol Src, INamedTypeSymbol Tgt)> genPairs,
        List<DiagnosticInfo> diagnostics, LocationInfo? loc)
    {
        // Snapshot the explicitly-declared pairs; expansion applies only to those, so a wrapper is never
        // wrapped around another wrapper's synthesized pair (no W<W<A>>).
        var declared = genPairs.ToArray();
        if (declared.Length == 0) return;

        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not { Name: KnownNames.GenerateWrapperMap }
                || attr.AttributeClass.ContainingNamespace?.ToDisplayString() != KnownNames.Ns
                || attr.ConstructorArguments.Length != 1
                || attr.ConstructorArguments[0].Value is not INamedTypeSymbol wrapperArg)
                continue;

            var wrapper = wrapperArg.OriginalDefinition;
            if (wrapper.TypeParameters.Length != 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.WrapperMapInvalid, loc,
                    $"[GenerateWrapperMap(typeof({wrapper.Name}<>))]: the wrapper must be a generic type with exactly one type parameter."));
                continue;
            }

            var typeParam = wrapper.TypeParameters[0];
            // Single (non-collection) payload: exactly one instance property/field whose type IS the type
            // parameter. A List<T> payload is excluded (its type is List<T>, not T).
            var payloadCount = wrapper.GetMembers().Count(m =>
                (m is IPropertySymbol p && !p.IsStatic && SymbolEqualityComparer.Default.Equals(p.Type, typeParam))
                // Exclude compiler-generated auto-property backing fields (also typed T) to avoid double-counting.
                || (m is IFieldSymbol f && !f.IsStatic && !f.IsImplicitlyDeclared && SymbolEqualityComparer.Default.Equals(f.Type, typeParam)));
            if (payloadCount != 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.WrapperMapInvalid, loc,
                    $"[GenerateWrapperMap(typeof({wrapper.Name}<>))]: the wrapper must have exactly one single (non-collection) payload member of type '{typeParam.Name}', but found {payloadCount}."));
                continue;
            }

            foreach (var (src, tgt) in declared)
            {
                var closedSrc = wrapper.Construct(src);
                var closedTgt = wrapper.Construct(tgt);
                if (genPairs.Any(pp => SymbolEqualityComparer.Default.Equals(pp.Src, closedSrc)
                                    && SymbolEqualityComparer.Default.Equals(pp.Tgt, closedTgt)))
                    continue; // user declared this closed wrapper pair explicitly
                genPairs.Add((closedSrc, closedTgt));
            }
        }
    }

    private static void EmitImplicitConversionDiag(
        List<DiagnosticInfo> diagnostics, LocationInfo? location, string targetName,
        ITypeSymbol srcType, ITypeSymbol tgtType, string kind, bool implicitConversions, bool lossy = false)
    {
        var src = srcType.ToDisplayString();
        var tgt = tgtType.ToDisplayString();
        // Item 15: name the runtime exceptions for a parse/format conversion so the risk is concrete.
        var risk = kind.StartsWith("parse/format", System.StringComparison.Ordinal)
            ? " (a malformed or out-of-range value throws FormatException / OverflowException at runtime)"
            : "";
        var msg = implicitConversions
            ? $"Member '{targetName}': implicit {kind} conversion {src} → {tgt} is applied{risk}. Make it explicit with [MapProperty(Use = nameof(...))], or set [DwarfMapper(ImplicitConversions = false)] to require explicit conversions."
            : $"Member '{targetName}': implicit {kind} conversion {src} → {tgt} is disallowed ([DwarfMapper(ImplicitConversions = false)]). Map it explicitly with [MapProperty(Use = nameof(...))].";
        // Item 8: lossy sub-cases (numeric narrowing/sign-change, parse/format, cross-category numeric) describe
        // data-losing / runtime-throwing behaviour, so they warn by default; widening stays unflagged and a
        // user-defined explicit operator stays Info (the user opted into it). Disallowed remains Error.
        var severity = !implicitConversions ? DiagnosticSeverity.Error
                     : lossy ? DiagnosticSeverity.Warning
                     : DiagnosticSeverity.Info;
        diagnostics.Add(new DiagnosticInfo(
            DiagnosticDescriptors.ImplicitConversionApplied, location, msg,
            SeverityOverride: severity));
    }

    private static string AccessibilityText(Accessibility a)
    {
        return a switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "public"
        };
    }

    private static List<RoundTripPair> CollectRoundTrips(INamedTypeSymbol classSymbol, Compilation compilation,
        List<DiagnosticInfo> diagnostics)
    {
        var pairs = new List<RoundTripPair>();
        // Only emit a verifier when DwarfMapper.Testing is referenced — never force the test package into production.
        if (compilation.GetTypeByMetadataName("DwarfMapper.Testing.RoundTrip") is null) return pairs;

        var partials = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition && !m.ReturnsVoid &&
                        m.Parameters.Length == 1)
            .ToList();

        foreach (var fwd in partials)
        {
            if (!fwd.GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == KnownNames.RoundTripFqn))
                continue;
            var loc = LocationInfo.From(fwd.Locations.FirstOrDefault() ?? Location.None);
            var src = fwd.Parameters[0].Type;
            var dto = fwd.ReturnType;

            var inverses = partials.Where(m =>
                !SymbolEqualityComparer.Default.Equals(m, fwd)
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, dto)
                && SymbolEqualityComparer.Default.Equals(m.ReturnType, src)).ToList();

            if (inverses.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RoundTripNoInverse, loc, fwd.Name));
                continue;
            }

            if (inverses.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RoundTripAmbiguousInverse, loc, fwd.Name));
                continue;
            }

            pairs.Add(new RoundTripPair(
                fwd.Name,
                inverses[0].Name,
                src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        return pairs;
    }
}
