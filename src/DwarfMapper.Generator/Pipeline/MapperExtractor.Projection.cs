// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    private static bool IsQueryable(ITypeSymbol type, out ITypeSymbol element)
    {
        element = type;
        if (type is INamedTypeSymbol n && n.Name == "IQueryable" && n.TypeArguments.Length == 1
            && n.ContainingNamespace?.ToDisplayString() == "System.Linq")
        {
            element = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Emits DWARF028 (ProjectionNotTranslatable) with a fully-formatted single-arg message.
    ///     The descriptor uses "{0}" so both member name and reason are concatenated here.
    /// </summary>
    private static void EmitDWARF028(
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string memberName,
        string reason)
    {
        var msg = $"Projection member '{memberName}' cannot be translated to SQL: {reason}.";
        diagnostics.Add(new DiagnosticInfo(
            DiagnosticDescriptors.ProjectionNotTranslatable, location, msg));
    }

    /// <summary>
    ///     Source-member lookup shared by all three projection resolvers.
    ///     <para>
    ///     Was <c>GroupBy(name, comparer).ToDictionary(g =&gt; g.Key, g =&gt; g.First())</c>, which under
    ///     <c>CaseInsensitive = true</c> SILENTLY first-picked one of two members differing only in case
    ///     (<c>Foo</c> / <c>foo</c> are distinct symbols and <c>ReadableMembers</c> de-duplicates by Ordinal, so
    ///     both reach the group). Three defects in one: it was silent (the library's "never silent" tenet), it
    ///     disagreed with the runtime map path — which reports <see cref="DiagnosticDescriptors.AmbiguousMatch" />
    ///     for the very same input, so the answer depended on whether you called <c>.Map</c> or the projection —
    ///     and the winner was whichever member <c>GetMembers()</c> yielded first, which for a partial source type
    ///     split across files is not stable, so two builds could emit different expression trees (H1).
    ///     </para>
    ///     Reports DWARF010 and binds NOTHING for an ambiguous group, exactly like the runtime path.
    /// </summary>
    private static Dictionary<string, (string Name, ITypeSymbol Type)> BuildProjectionSourceLookup(
        ITypeSymbol sourceType, StringComparer comparer, LocationInfo? location,
        List<DiagnosticInfo> diagnostics)
    {
        var sources = new Dictionary<string, (string Name, ITypeSymbol Type)>(comparer);
        foreach (var group in ReadableMembers(sourceType).GroupBy(m => m.Name, comparer))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, group.Key));
                continue;
            }

            sources[group.Key] = members[0];
        }

        return sources;
    }

    /// <summary>
    ///     New (Plan 19D) recursive projection resolver. Produces a list of
    ///     <see cref="ProjectionMemberMap" /> with inline expression fragments (no helper calls).
    ///     Projection translatability: every non-translatable projection member is reported as
    ///     DWARF028 (ProjectionNotTranslatable) with a specific reason — including the
    ///     [MapProperty(Use=)] attribute-conflict case and all type-conversion unsafety
    ///     (narrowing numeric, parsable string↔T, enum by-name, non-translatable collections,
    ///     reference handling, and the "no translatable conversion found" fallback).
    ///     (DWARF019/NotProjectable was retired in favour of DWARF028's reason-carrying messages.)
    /// </summary>
    private static List<ProjectionMemberMap> ResolveProjectionMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        EnumStrategy enumStrategy, int referenceHandling, string paramExpr)
    {
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var sources = BuildProjectionSourceLookup(sourceType, comparer, location, diagnostics);
        // C4: pass comparer to nested resolvers so CaseInsensitive propagates into nested objects.
        var writableByName = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType)) writableByName[m.Name] = m.Type;

        var result = new List<ProjectionMemberMap>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var explicitSeen = new HashSet<string>(StringComparer.Ordinal);

        // ── Explicit maps ([MapProperty]) ────────────────────────────────────
        foreach (var (srcName, tgtName, use) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            handled.Add(tgtName);
            if (ignores.Contains(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }

            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }

            // A custom converter (Use=) cannot run inside a provider-translated projection.
            if (use is not null)
            {
                EmitDWARF028(diagnostics, location, tgtName,
                    "custom converter (Use=) is not translatable in projection; remove Use= or map at runtime");
                continue;
            }

            // Resolve the source, supporting a dotted path (e.g. "Colour.Code") for value-object /
            // nested-scalar flattening — matching the class-model [MapProperty] dotted-path feature.
            // The projection accessor "__s.Colour.Code" is built verbatim below; we walk the segments
            // here only to find the leaf type and validate each hop is a readable member.
            var sm = sourceType;
            foreach (var seg in srcName.Split('.'))
            {
                sm = sm is null
                    ? null
                    : ReadableMembers(sm)
                        .Where(m => StringComparer.Ordinal.Equals(m.Name, seg))
                        .Select(m => (ITypeSymbol?)m.Type).FirstOrDefault();
                if (sm is null) break;
            }

            if (sm is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            var srcExprForExplicit = paramExpr + "." + srcName;
            var inlineExpr = ResolveProjectionExpr(
                sm, tgtType, srcExprForExplicit, 0, compilation, location,
                diagnostics, tgtName, enumStrategy, comparer);
            if (inlineExpr is not null)
                result.Add(new ProjectionMemberMap(tgtName, inlineExpr));
        }

        // ── Auto-matched writable members ────────────────────────────────────

        // For IQueryable projection, member-init syntax is only SQL-translatable when the target
        // type has a public parameterless constructor (EF Core materialises via default ctor then
        // sets members). Positional records and other ctor-only types must use constructor projection.
        var hasParameterlessCtor = targetType.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && !c.IsStatic
            && c.Parameters.Length == 0);

        var writableMembers = WritableMembers(targetType)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        if (writableMembers.Count == 0 || !hasParameterlessCtor)
        {
            // Try constructor projection: select the ctor with the most params matching source.
            ConstructorSelector.Select(compilation, targetType, diagnostics, location, out var ctorOnly);
            var bestCtor = targetType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length > 0)
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();

            if (bestCtor is not null)
            {
                // C4: pass comparer (carries CaseInsensitive setting) to ctor projection resolver.
                var ctorExpr = ResolveProjectionCtorExpr(
                    bestCtor, sourceType, paramExpr, 0,
                    compilation, location, diagnostics, targetType, enumStrategy, comparer);
                if (ctorExpr is not null)
                    // Store as a whole-lambda body (TargetName = "")
                    result.Add(new ProjectionMemberMap("", ctorExpr));
            }

            return result;
        }

        foreach (var target in writableMembers)
        {
            if (handled.Contains(target.Name) || ignores.Contains(target.Name)) continue;
            if (!sources.TryGetValue(target.Name, out var src))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name,
                    MemberName: target.Name));
                continue;
            }

            var srcAccessExpr = paramExpr + "." + src.Name;
            // C4: pass comparer so nested objects respect CaseInsensitive setting.
            var inlineExpr = ResolveProjectionExpr(
                src.Type, target.Type, srcAccessExpr, 0,
                compilation, location, diagnostics, target.Name, enumStrategy, comparer);
            if (inlineExpr is not null)
                result.Add(new ProjectionMemberMap(target.Name, inlineExpr));
        }

        return result;
    }

    /// <summary>
    ///     Resolve a single inline projection expression for a source→target type pair.
    ///     Returns the inline C# expression string (pure, no helper calls), or null when
    ///     DWARF028 has been emitted (unsafe construct).
    ///     SAFE:
    ///     1. Direct-assignable (implicit conversion incl. widening numeric).
    ///     2. Enum by-value cast: (TgtEnum)srcExpr.
    ///     3. Nested named object: new TgtType { M1 = ..., M2 = ... } (recursive).
    ///     4. Collection (projection-translatable): .Select(...).ToList()/.ToArray()/lazy.
    ///     UNSAFE → DWARF028:
    ///     - Narrowing numeric (CreateChecked path).
    ///     - String↔T parsable (IParsable/IFormattable path).
    ///     - Enum by-name (switch path).
    ///     - Non-translatable collection target (HashSet/ISet/immutable/dict).
    ///     - Depth > ProjectionMaxDepth.
    ///     - No translatable conversion found.
    /// </summary>
    /// <param name="comparer">
    ///     C4: the case-sensitivity comparer for member name matching; passed recursively into
    ///     nested object and ctor resolvers so CaseInsensitive propagates to all depths.
    /// </param>
    private static string? ResolveProjectionExpr(
        ITypeSymbol srcType, ITypeSymbol tgtType,
        string srcExpr,
        int depth,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        string targetMemberName,
        EnumStrategy enumStrategy,
        StringComparer? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;

        // ── Depth guard ───────────────────────────────────────────────────────
        if (depth > ProjectionMaxDepth)
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                $"projection nesting depth exceeded {ProjectionMaxDepth}; split into a runtime mapper");
            return null;
        }

        // ── Pre-check: collection/dictionary targets BEFORE implicit-conversion ──
        // EF Core cannot translate HashSet/Dictionary/immutable collection projections even
        // when source==target (same type is directly assignable but NOT SQL-translatable).
        // We must check collection-shaped types BEFORE the HasImplicitConversion fast-path.
        if (CollectionConverter.TryResolve(srcType, tgtType,
                out var srcElem, out var tgtElem, out var shape))
        {
            if (!CollectionConverter.IsTargetKindTranslatable(shape.Target))
            {
                var tgtTypeName = tgtType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                EmitDWARF028(diagnostics, location, targetMemberName,
                    $"collection type '{tgtTypeName}' is not translatable in projection (HashSet/ISet/immutable/Dictionary targets are not supported by EF Core)");
                return null;
            }

            // Translatable collection: emit .Select(...).ToList()/.ToArray()/lazy
            var elemParam = $"__i{depth}";
            // C4: propagate comparer into element expression resolver.
            var elemExpr = ResolveProjectionExpr(
                srcElem, tgtElem, elemParam, depth + 1,
                compilation, location, diagnostics, targetMemberName, enumStrategy, comparer);
            if (elemExpr is null) return null; // DWARF028 already emitted

            // Use fully-qualified Enumerable.Select to avoid needing 'using System.Linq' in generated code.
            var srcElemFqn = srcElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var tgtElemFqn = tgtElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var selectCall =
                $"global::System.Linq.Enumerable.Select<{srcElemFqn}, {tgtElemFqn}>({srcExpr}, {elemParam} => {elemExpr})";
            var collectionExpr = shape.Target switch
            {
                CollectionConverter.TargetKind.Array =>
                    $"global::System.Linq.Enumerable.ToArray<{tgtElemFqn}>({selectCall})",
                CollectionConverter.TargetKind.IEnumerable =>
                    selectCall, // lazy, no terminal
                _ =>
                    $"global::System.Linq.Enumerable.ToList<{tgtElemFqn}>({selectCall})"
            };
            // Guard the source collection with a null-conditional ternary ONLY when it may actually be
            // null (nullable-annotated or nullable-oblivious). A non-nullable source needs no guard —
            // guarding it would assign null to a non-nullable target (CS8601). EF translates the ternary.
            if (ProjectionSourceMayBeNull(srcType)) return $"{srcExpr} == null ? null : {collectionExpr}";
            return collectionExpr;
        }

        // ── Pre-check: Dictionary targets (always non-translatable in projection) ──
        // Check before HasImplicitConversion to catch same-type dictionary members.
        if (DictionaryConverter.TryResolve(srcType, tgtType,
                out _, out _, out _, out _, out _, out _))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "Dictionary targets are not translatable in projection; map at runtime");
            return null;
        }

        // ── 1. Direct-assignable (implicit — covers widening numeric, same-type, etc.) ──
        if (HasImplicitConversion(compilation, srcType, tgtType)) return srcExpr;

        // ── 2. Enum by-value cast (enum→enum) ─────────────────────────────────
        if (srcType.TypeKind == TypeKind.Enum && tgtType.TypeKind == TypeKind.Enum
                                              && enumStrategy == EnumStrategy.ByValue)
        {
            var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({tgtFqn}){srcExpr}";
        }

        // ── C6: enum↔integral inline cast (SQL-translatable as a direct cast) ──
        // enum→integral (e.g. Status→int): cast to the integral type.
        // integral→enum (e.g. int→Status): cast to the enum type.
        // Only emit when the conversion is widening or same-width (safe). Narrowing (enum:long→int)
        // would need CreateChecked — fall through to DWARF028 for that case.
        if (srcType.TypeKind == TypeKind.Enum && TypeInterfaces.IsIntegral(tgtType))
        {
            // Get the enum's underlying integral type for a width-safety check.
            var enumUnderlying = ((INamedTypeSymbol)srcType).EnumUnderlyingType;
            if (enumUnderlying is not null && IsWideningOrSameWidth(enumUnderlying, tgtType))
            {
                var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"({tgtFqn}){srcExpr}";
            }

            // Narrowing / lossy (e.g. enum:long→int, or unsigned-underlying enum:uint→int) — the source
            // underlying does not fit the target range and a projection can't do a checked cast.
            EmitDWARF028(diagnostics, location, targetMemberName,
                "enum→integral conversion is narrowing (the enum's underlying type does not fit the target integral type) and cannot be range-checked in a projection; map it at runtime");
            return null;
        }

        if (TypeInterfaces.IsIntegral(srcType) && tgtType.TypeKind == TypeKind.Enum)
        {
            // integral→enum: safe when source integral width ≤ enum underlying width.
            var enumUnderlying = ((INamedTypeSymbol)tgtType).EnumUnderlyingType;
            if (enumUnderlying is not null && IsWideningOrSameWidth(srcType, enumUnderlying))
            {
                var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"({tgtFqn}){srcExpr}";
            }

            // Narrowing / lossy (e.g. long→enum:int, or int→enum:uint sign change) — the source does not
            // fit the enum's underlying range and a projection can't do a checked cast.
            EmitDWARF028(diagnostics, location, targetMemberName,
                "integral→enum conversion is narrowing (the source does not fit the enum's underlying type) and cannot be range-checked in a projection; map it at runtime");
            return null;
        }

        // ── UNSAFE: enum by-name (enumStrategy == ByName, different enum types) ──
        if ((srcType.TypeKind == TypeKind.Enum || tgtType.TypeKind == TypeKind.Enum)
            && enumStrategy == EnumStrategy.ByName)
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "enum by-name mapping is not translatable in projection; use EnumStrategy.ByValue or map at runtime");
            return null;
        }

        // ── UNSAFE: numeric narrowing (NumericConverter would fire: both integral, no implicit) ──
        if (TypeInterfaces.IsIntegral(srcType) && TypeInterfaces.IsIntegral(tgtType))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "narrowing numeric conversion is not SQL-translatable (would need CreateChecked); map at runtime or use a widening target type");
            return null;
        }

        // ── UNSAFE: string↔T parsable (ParsableConverter would fire) ─────────
        if ((srcType.SpecialType == SpecialType.System_String
             && tgtType.TypeKind != TypeKind.Enum
             && TypeInterfaces.ImplementsIParsable(compilation, tgtType))
            || (tgtType.SpecialType == SpecialType.System_String
                && srcType.SpecialType != SpecialType.System_String
                && srcType.TypeKind != TypeKind.Enum
                && (TypeInterfaces.ImplementsIFormattable(srcType)
                    || srcType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char)))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "string parse/format is not translatable in projection (IParsable/IFormattable); map at runtime");
            return null;
        }

        // ── 3. Nested named object (recursive) ───────────────────────────────
        if (srcType is INamedTypeSymbol namedSrc && tgtType is INamedTypeSymbol namedTgt
                                                 && IsMappableObjectPair(compilation, srcType, namedTgt))
            // C4: pass comparer into nested object resolver.
            return ResolveProjectionNestedObjectExpr(
                namedSrc, namedTgt, srcExpr, depth, compilation, location, diagnostics,
                targetMemberName, enumStrategy, comparer);

        // ── Nullable T? → nullable U? or non-nullable U ───────────────────────
        // C5: when source is Nullable<T> and target is also Nullable<U>, emit a null-preserving
        // HasValue ternary (SQL-translatable) instead of .Value (throws on null).
        if (IsNullableValue(srcType, out var srcUnderlying))
        {
            if (IsNullableValue(tgtType, out var tgtUnderlying))
            {
                // int?→long?: null-preserving ternary: __s.X.HasValue ? (long?)__s.X.Value : null
                var innerExpr = ResolveProjectionExpr(
                    srcUnderlying, tgtUnderlying, srcExpr + ".Value", depth,
                    compilation, location, diagnostics, targetMemberName, enumStrategy, comparer);
                if (innerExpr is null) return null;
                var tgtNullableFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"{srcExpr}.HasValue ? ({tgtNullableFqn}){innerExpr} : null";
            }
            else
            {
                // int?→long (non-nullable target): keep .Value (user asked for non-null; throws on null).
                var innerExpr = ResolveProjectionExpr(
                    srcUnderlying, tgtType, srcExpr + ".Value", depth,
                    compilation, location, diagnostics, targetMemberName, enumStrategy, comparer);
                return innerExpr;
            }
        }

        // ── Fallback: no translatable conversion found ────────────────────────
        EmitDWARF028(diagnostics, location, targetMemberName,
            "no translatable conversion found; map at runtime instead");
        return null;
    }

    /// <summary>
    ///     C6 helper: returns true when a cast from <paramref name="src" /> to <paramref name="tgt" /> is
    ///     widening or same-width (thus safe as a direct inline cast in SQL projection).
    ///     Both must be integral types.
    /// </summary>
    private static bool IsWideningOrSameWidth(ITypeSymbol src, ITypeSymbol tgt)
    {
        // (bit width, isSigned) per integral type. Honours the enum's ACTUAL underlying type
        // (byte/short/uint/long/…), not a fixed int assumption.
        static bool IntegralInfo(ITypeSymbol t, out int width, out bool signed)
        {
            switch (t.SpecialType)
            {
                case SpecialType.System_Byte:
                    width = 8;
                    signed = false;
                    return true;
                case SpecialType.System_SByte:
                    width = 8;
                    signed = true;
                    return true;
                case SpecialType.System_UInt16:
                    width = 16;
                    signed = false;
                    return true;
                case SpecialType.System_Int16:
                    width = 16;
                    signed = true;
                    return true;
                case SpecialType.System_UInt32:
                    width = 32;
                    signed = false;
                    return true;
                case SpecialType.System_Int32:
                    width = 32;
                    signed = true;
                    return true;
                case SpecialType.System_UInt64:
                    width = 64;
                    signed = false;
                    return true;
                case SpecialType.System_Int64:
                    width = 64;
                    signed = true;
                    return true;
                default:
                    width = 0;
                    signed = false;
                    return false;
            }
        }

        if (!IntegralInfo(src, out var sw, out var ss)) return false;
        if (!IntegralInfo(tgt, out var tw, out var ts)) return false;

        // A plain (unchecked) cast src→tgt is lossless — safe to inline in a projection that can't do a
        // checked conversion — ONLY when the target's representable range fully contains the source's:
        //   • same signedness    → target width must be ≥ source width  (short→int, uint→ulong)
        //   • unsigned → signed  → target needs a strictly wider type for the sign bit  (byte→short, uint→long)
        //   • signed → unsigned  → never lossless (source may be negative)
        // Anything else (e.g. uint→int, ushort→short, long→int) is narrowing and falls through to DWARF028.
        if (ss == ts) return tw >= sw;
        if (!ss && ts) return tw > sw;
        return false;
    }

    /// <summary>
    ///     Whether a projection source expression needs a null-navigation guard. A reference type needs one
    ///     only when it is nullable-annotated (<c>T?</c>) or nullable-oblivious (compiled with
    ///     <c>#nullable disable</c>). A NON-nullable-annotated reference is guaranteed non-null, so guarding it
    ///     would assign <c>null</c> to a (possibly non-nullable) target — a false CS8601/CS8603 in strict-
    ///     nullable hosts. This honours the consumer's own nullable annotations instead of guarding blindly.
    /// </summary>
    private static bool ProjectionSourceMayBeNull(ITypeSymbol type)
    {
        return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated;
    }

    /// <summary>
    ///     Build an inline member-init expression for a nested object target.
    ///     For nullable reference source: emits null-navigation ternary.
    ///     For non-null / value-type source: emits plain member-init.
    /// </summary>
    /// <param name="comparer">
    ///     C4: the case-sensitivity comparer for member name matching, propagated from the top-level
    ///     call site so CaseInsensitive works at all nesting depths.
    /// </param>
    private static string? ResolveProjectionNestedObjectExpr(
        INamedTypeSymbol srcType, INamedTypeSymbol tgtType,
        string srcExpr, int depth,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        string targetMemberName, EnumStrategy enumStrategy,
        StringComparer? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // C4: use the configured comparer for member lookup so CaseInsensitive applies here.
        var srcReadable = BuildProjectionSourceLookup(srcType, comparer, location, diagnostics);

        // Build member-init or ctor expression for the nested object.
        // Mirror the decision logic in ResolveProjectionMembers:
        //   • If the target has NO public parameterless constructor (positional record / ctor-only),
        //     use constructor projection new T(arg0, arg1) — EF/expression-trees disallow named args.
        //   • Otherwise use member-init new T { P1 = ..., P2 = ... }.
        // The original check "writableTargetMembers.Count == 0" only catches types with no
        // settable/init properties at all; it misses positional records whose init properties
        // exist but whose constructor has no parameterless overload (CS7036 at compile time).
        var writableTargetMembers = WritableMembers(tgtType)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var hasParameterlessCtor = tgtType.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && !c.IsStatic
            && c.Parameters.Length == 0);

        string innerBodyExpr;

        if (writableTargetMembers.Count == 0 || !hasParameterlessCtor)
        {
            // Try ctor projection: use the public ctor with the most parameters.
            // Expression trees require POSITIONAL args (CS0853: named args not allowed).
            var bestCtor = tgtType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length > 0)
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();

            if (bestCtor is null)
            {
                EmitDWARF028(diagnostics, location, targetMemberName,
                    $"nested type '{tgtFqn}' has no writable members and no usable constructor");
                return null;
            }

            // C4: pass the configured comparer (not hardcoded Ordinal) so CaseInsensitive propagates.
            var ctorExpr = ResolveProjectionCtorExpr(
                bestCtor, srcType, srcExpr, depth,
                compilation, location, diagnostics, tgtType, enumStrategy,
                comparer);
            if (ctorExpr is null) return null;
            innerBodyExpr = ctorExpr;
        }
        else
        {
            // Member-init expression: new T { P1 = expr1, P2 = expr2 }
            var memberParts = new List<string>();
            var anyFailed = false;

            foreach (var tgtMember in writableTargetMembers)
            {
                if (!srcReadable.TryGetValue(tgtMember.Name, out var srcMember))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.UnmappedMember, location, targetMemberName + "." + tgtMember.Name));
                    anyFailed = true;
                    continue;
                }

                var memberSrcExpr = srcExpr + "." + srcMember.Name;
                // C4: propagate comparer into recursive member resolution.
                var memberInlineExpr = ResolveProjectionExpr(
                    srcMember.Type, tgtMember.Type, memberSrcExpr, depth + 1,
                    compilation, location, diagnostics,
                    targetMemberName + "." + tgtMember.Name, enumStrategy, comparer);

                if (memberInlineExpr is null)
                {
                    anyFailed = true;
                    continue;
                }

                memberParts.Add($"{tgtMember.Name} = {memberInlineExpr}");
            }

            if (anyFailed) return null;
            innerBodyExpr = $"new {tgtFqn} {{ {string.Join(", ", memberParts)} }}";
        }

        // Wrap with a null-navigation ternary ONLY when the source may actually be null (nullable-
        // annotated or nullable-oblivious). A non-nullable source needs no guard (guarding it would
        // assign null to a non-nullable target — CS8603).
        if (ProjectionSourceMayBeNull(srcType)) return $"{srcExpr} == null ? null : {innerBodyExpr}";
        return innerBodyExpr;
    }

    /// <summary>
    ///     Build an inline constructor-call expression for targets with only ctor params (records etc.).
    ///     e.g. "new global::D.DstRec(x: __s.X, y: __s.Y)"
    /// </summary>
    private static string? ResolveProjectionCtorExpr(
        IMethodSymbol ctor,
        ITypeSymbol srcType,
        string srcExpr,
        int depth,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        INamedTypeSymbol tgtType,
        EnumStrategy enumStrategy,
        StringComparer comparer)
    {
        var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var srcReadable = BuildProjectionSourceLookup(srcType, comparer, location, diagnostics);

        var argParts = new List<string>();
        var anyFailed = false;

        foreach (var param in ctor.Parameters)
        {
            if (!srcReadable.TryGetValue(param.Name, out var srcMember))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ConstructorParameterUnmapped, location, param.Name));
                anyFailed = true;
                continue;
            }

            var paramSrcExpr = srcExpr + "." + srcMember.Name;
            // C4: propagate comparer into ctor param expression resolver.
            var paramInlineExpr = ResolveProjectionExpr(
                srcMember.Type, param.Type, paramSrcExpr, depth + 1,
                compilation, location, diagnostics, param.Name, enumStrategy, comparer);

            if (paramInlineExpr is null)
            {
                anyFailed = true;
                continue;
            }

            // Expression trees do not allow named arguments (CS0853): emit positional args.
            argParts.Add(paramInlineExpr);
        }

        if (anyFailed) return null;
        return $"new {tgtFqn}({string.Join(", ", argParts)})";
    }

    /// <summary>
    ///     DFS reachability: can we reach <paramref name="target" /> starting from <paramref name="start" />
    ///     by following edges in the call graph? Used to detect recursive method cycles.
    /// </summary>
    private static bool CanReach(
        Dictionary<string, HashSet<string>> graph,
        string start,
        string target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        if (!graph.TryGetValue(start, out var startDeps)) return false;
        foreach (var dep in startDeps)
            stack.Push(dep);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (string.Equals(current, target, StringComparison.Ordinal)) return true;
            if (!visited.Add(current)) continue;
            if (graph.TryGetValue(current, out var deps))
                foreach (var dep in deps)
                    stack.Push(dep);
        }

        return false;
    }

    /// <summary>
    ///     Recognises <c>System.Span&lt;T&gt;</c> / <c>System.ReadOnlySpan&lt;T&gt;</c>, returning the element
    ///     type and whether it is the read-only form. Used to detect zero-alloc span map methods.
    /// </summary>
    private static bool TryGetSpanElement(ITypeSymbol t, out ITypeSymbol element, out bool isReadOnly)
    {
        element = null!;
        isReadOnly = false;
        if (t is INamedTypeSymbol n
            && n.TypeArguments.Length == 1
            && n.ContainingNamespace is { Name: "System" } ns
            && ns.ContainingNamespace?.IsGlobalNamespace == true
            && (string.Equals(n.Name, "Span", StringComparison.Ordinal)
                || string.Equals(n.Name, "ReadOnlySpan", StringComparison.Ordinal)))
        {
            element = n.TypeArguments[0];
            isReadOnly = string.Equals(n.Name, "ReadOnlySpan", StringComparison.Ordinal);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Recognises <c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c>, returning the element type.
    ///     Used to detect async streaming map methods.
    /// </summary>
    private static bool TryGetAsyncEnumerableElement(ITypeSymbol t, out ITypeSymbol element)
    {
        element = null!;
        if (t is INamedTypeSymbol n
            && n.TypeArguments.Length == 1
            && string.Equals(n.Name, "IAsyncEnumerable", StringComparison.Ordinal)
            && n.ContainingNamespace is { Name: "Generic" } g
            && g.ContainingNamespace is { Name: "Collections" } c
            && c.ContainingNamespace is { Name: "System" } s
            && s.ContainingNamespace.IsGlobalNamespace)
        {
            element = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Emits DWARF038 for a non-lossless implicit basic-type conversion: an Info-level suggestion when
    ///     <paramref name="implicitConversions" /> is true (permissive — the conversion is still applied), or a
    ///     build Error when false (strict — the user must opt in via <c>[MapProperty(Use = …)]</c>).
    /// </summary>
    /// <summary>
    ///     True when one type is integer-kind and the other is floating/decimal-kind (e.g. int↔double,
    ///     long↔float, int↔decimal) — a cross-category numeric conversion. Same-category pairs (int↔long,
    ///     float↔double) return false.
    /// </summary>
    /// <summary>True for <c>System.Threading.CancellationToken</c>.</summary>
    private static bool IsCancellationToken(ITypeSymbol t)
    {
        return t is INamedTypeSymbol { Name: "CancellationToken" } n
               && n.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }
}
