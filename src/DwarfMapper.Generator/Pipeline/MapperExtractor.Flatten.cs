// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    /// <summary>
    ///     Resolves a dotted source path (e.g. <c>"Customer.Name"</c>) hop-by-hop from <paramref name="root" />,
    ///     returning the leaf member's type. <paramref name="nullableHop" /> is set when an <i>interior</i> hop
    ///     (any but the last) is a nullable/oblivious reference — dereferencing it can throw at runtime
    ///     (DWARF044). On failure, <paramref name="badSegment" /> names the first unresolved segment (DWARF043).
    ///     Segments are matched by exact ordinal name (member names never contain dots).
    /// </summary>
    private static bool TryResolveSourcePath(
        ITypeSymbol root, string dottedPath, out ITypeSymbol? leafType, out bool nullableHop, out string badSegment)
    {
        leafType = null;
        nullableHop = false;
        badSegment = "";
        var segments = dottedPath.Split('.');
        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var member = ReadableMembers(current)
                .Where(m => StringComparer.Ordinal.Equals(m.Name, seg))
                .Select(m => ((string Name, ITypeSymbol Type)?)m)
                .FirstOrDefault();
            if (member is null)
            {
                badSegment = seg;
                return false;
            }

            if (i < segments.Length - 1
                && member.Value.Type.IsReferenceType
                && member.Value.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
                nullableHop = true;
            current = member.Value.Type;
        }

        leafType = current;
        return true;
    }

    /// <summary>
    ///     Resolves an unflatten target path (single level, e.g. <c>"Address.City"</c>): the intermediate root
    ///     must be a writable destination member whose type is a class with a public parameterless constructor;
    ///     the leaf must be a writable member of that type. On success appends a <see cref="MemberMap" /> whose
    ///     <see cref="MemberMap.UnflattenIntermediateFqn" /> drives post-construction instantiation, and marks
    ///     the root handled (suppressing DWARF001 and blocking auto-match). Emits DWARF045 (invalid path /
    ///     non-constructible intermediate / deeper-than-one-level) or DWARF046 (root already mapped directly).
    /// </summary>
    private static void ResolveUnflattenTarget(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, string srcName, string tgtName, string? useMethod,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        HashSet<string> handledTargets, HashSet<string> unflattenRoots, Dictionary<string, ITypeSymbol> writableByName,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized, NullStrategy nullStrategy,
        bool autoNest, NestedMappingRegistry? nestedRegistry, bool nullAsNull, bool isPreserve, bool isSetNull,
        bool implicitConversions, List<MemberMap> result)
    {
        // Resolve the source (simple or dotted) to its leaf type.
        ITypeSymbol? uSrc;
        if (srcName.IndexOf('.') >= 0)
        {
            if (!TryResolveSourcePath(sourceType, srcName, out uSrc, out _, out var uBad))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathSegmentNotFound, location,
                    $"[MapProperty] source path '{srcName}' has no member '{uBad}'"));
                return;
            }
        }
        else
        {
            uSrc = ReadableMembers(sourceType)
                .Where(m => StringComparer.Ordinal.Equals(m.Name, srcName))
                .Select(m => (ITypeSymbol?)m.Type)
                .FirstOrDefault();
            if (uSrc is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                return;
            }
        }

        var segs = tgtName.Split('.');
        if (segs.Length != 2)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid, location,
                $"unflatten target '{tgtName}' must have exactly one intermediate (e.g. \"Address.City\"); deeper paths are not yet supported"));
            return;
        }

        var rootName = segs[0];
        var leafName = segs[1];

        // Conflict only when the root is mapped DIRECTLY; a prior unflatten leaf into the same root is fine.
        if (handledTargets.Contains(rootName) && !unflattenRoots.Contains(rootName))
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenConflict, location,
                $"unflatten target '{tgtName}' conflicts with a direct mapping of intermediate '{rootName}'"));
            return;
        }

        if (!writableByName.TryGetValue(rootName, out var rootType))
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid, location,
                $"unflatten intermediate '{rootName}' is not a writable destination member"));
            return;
        }

        if (rootType is not INamedTypeSymbol rootNamed || !rootType.IsReferenceType ||
            rootType.TypeKind != TypeKind.Class
            || !rootNamed.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length == 0))
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid, location,
                $"unflatten intermediate '{rootName}' (type '{rootType.ToDisplayString()}') must be a class with a public parameterless constructor"));
            return;
        }

        var leafType = WritableMembers(rootType)
            .Where(m => StringComparer.Ordinal.Equals(m.Name, leafName))
            .Select(m => (ITypeSymbol?)m.Type)
            .FirstOrDefault();
        if (leafType is null)
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid, location,
                $"unflatten intermediate '{rootName}' (type '{rootType.ToDisplayString()}') has no writable member '{leafName}'"));
            return;
        }

        if (TryResolveConversion(compilation, uSrc!, leafType!, useMethod, allMethods, autoCandidates, enumStrategy,
                synthesized, nullStrategy, location, tgtName, diagnostics, out var uConv, out var uNullH,
                out var uNeedsCtx,
                autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull,
                implicitConversions: implicitConversions))
        {
            var rootFqn = rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            result.Add(new MemberMap(tgtName, srcName, uConv, uNullH, uNeedsCtx,
                SourceMayBeNullRef(uSrc!), UnflattenIntermediateFqn: rootFqn));
            handledTargets.Add(rootName);
            unflattenRoots.Add(rootName);
        }
    }

    /// <summary>
    ///     Reads the optional <c>NullSubstitute</c> / <c>When</c> named arguments of <c>[MapProperty]</c>
    ///     (Phase 8), keyed by destination target. Separate from <see cref="ReadExplicitMaps" /> so the shared
    ///     (Source, Target, Use) tuple — also consumed by constructor-argument resolution — is unchanged.
    /// </summary>
    private static List<(string Target, bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>
        ReadMapPropertyExtras(ISymbol method)
    {
        var result = new List<(string, bool, TypedConstant, string?, string?)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapPropertyFqn
                || attr.ConstructorArguments.Length < 2
                || attr.ConstructorArguments[1].Value is not string target)
                continue;
            var hasNullSub = false;
            TypedConstant nullSub = default;
            string? when = null;
            foreach (var na in attr.NamedArguments)
                if (na.Key == "NullSubstitute")
                {
                    hasNullSub = true;
                    nullSub = na.Value;
                }
                else if (na.Key == "When" && na.Value.Value is string w)
                {
                    when = w;
                }

            if (hasNullSub || when is not null) result.Add((target, hasNullSub, nullSub, when, null));
        }

        return result;
    }

    /// <summary>
    ///     Applies <c>[MapCollectionKey]</c> to the update-into member list: converts a matched
    ///     <c>List&lt;T&gt;</c> member from whole-collection replacement into a key-based in-place upsert.
    ///     v1 scope — <c>List&lt;T&gt;</c> with the SAME element type on both sides and a readable key member;
    ///     anything else is refused with DWARF074 rather than silently ignored.
    /// </summary>
    private static void ApplyCollectionKeyUpserts(
        IMethodSymbol method, ITypeSymbol srcType, INamedTypeSymbol tgtType, Compilation compilation,
        LocationInfo? location, List<DiagnosticInfo> diagnostics, List<MemberMap> members)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapCollectionKeyFqn
                || attr.ConstructorArguments.Length < 2
                || attr.ConstructorArguments[0].Value is not string collectionMember
                || attr.ConstructorArguments[1].Value is not string keyMember)
                continue;

            var idx = members.FindIndex(m => StringComparer.Ordinal.Equals(m.TargetName, collectionMember));
            if (idx < 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid, location,
                    $"[MapCollectionKey] target '{collectionMember}' is not a mapped destination member of this update-into method"));
                continue;
            }

            var tgtMemberType = MemberTypeByName(tgtType, collectionMember);
            var srcMemberType = MemberTypeByName(srcType, members[idx].SourceName);

            if (!IsListOfT(tgtMemberType, out var tgtElem) || !IsListOfT(srcMemberType, out var srcElem))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid, location,
                    $"[MapCollectionKey] member '{collectionMember}' must be a List<T> on both source and destination (v1)"));
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(srcElem, tgtElem))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid, location,
                    $"[MapCollectionKey] member '{collectionMember}' requires the same element type on source and destination (v1); got '{srcElem!.ToDisplayString()}' and '{tgtElem!.ToDisplayString()}'"));
                continue;
            }

            var keyType = ReadableMembers(tgtElem!, compilation)
                .Where(m => StringComparer.Ordinal.Equals(m.Name, keyMember))
                .Select(m => (ITypeSymbol?)m.Type)
                .FirstOrDefault();
            if (keyType is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid, location,
                    $"[MapCollectionKey] key '{keyMember}' is not a readable member of element type '{tgtElem!.ToDisplayString()}'"));
                continue;
            }

            members[idx] = members[idx] with
            {
                UpsertKeyMember = keyMember,
                UpsertKeyTypeFqn = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            };
        }
    }

    private static ITypeSymbol? MemberTypeByName(ITypeSymbol type, string name)
    {
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            foreach (var m in t.GetMembers(name))
                switch (m)
                {
                    case IPropertySymbol p: return p.Type;
                    case IFieldSymbol f: return f.Type;
                }

        return null;
    }

    private static bool IsListOfT(ITypeSymbol? type, out ITypeSymbol? element)
    {
        if (type is INamedTypeSymbol { Name: "List", TypeArguments.Length: 1 } nt
            && nt.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
        {
            element = nt.TypeArguments[0];
            return true;
        }

        element = null;
        return false;
    }

    /// <summary>
    ///     Reads <c>[MapProperty("src", "tgt", StringFormat = "…")]</c> named arguments into a
    ///     target-name → format-string map. Kept separate from <see cref="ReadMapPropertyExtras" /> because a
    ///     StringFormat can appear with no NullSubstitute/When, which that reader would drop.
    /// </summary>
    private static Dictionary<string, string> ReadStringFormats(ISymbol method)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapPropertyFqn
                || attr.ConstructorArguments.Length < 2
                || attr.ConstructorArguments[1].Value is not string target)
                continue;
            foreach (var na in attr.NamedArguments)
                if (na.Key == "StringFormat" && na.Value.Value is string fmt)
                    result[target] = fmt;
        }

        return result;
    }

    private static bool HasReverseMap(IMethodSymbol m)
    {
        return m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == KnownNames.ReverseMapFqn);
    }

    /// <summary>
    ///     If <paramref name="method" /> is the inverse of some forward <c>[ReverseMap]</c> method
    ///     (forward source == this target, forward target == this source), returns the inverted simple renames
    ///     (<c>A→B</c> ⇒ <c>B→A</c>) to inherit. Forward renames that cannot be auto-inverted — a <c>Use=</c>
    ///     converter, a dotted path, or a <c>NullSubstitute</c>/<c>When</c> — are reported as DWARF051 and
    ///     skipped (declare those reverse renames explicitly). A rename whose inverse target the inverse method
    ///     already maps itself is also skipped (the explicit one wins).
    /// </summary>
    private static List<(string Source, string Target, string? Use)> CollectReverseRenames(
        INamedTypeSymbol classSymbol, IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol targetType,
        IReadOnlyList<(string Source, string Target, string? Use)> ownExplicit,
        List<DiagnosticInfo> diagnostics, LocationInfo? location)
    {
        var added = new List<(string, string, string?)>();
        IMethodSymbol? forward = null;
        foreach (var f in classSymbol.GetMembers().OfType<IMethodSymbol>())
            if (!SymbolEqualityComparer.Default.Equals(f, method) && HasReverseMap(f)
                                                                  && f.Parameters.Length == 1
                                                                  && SymbolEqualityComparer.Default.Equals(
                                                                      f.Parameters[0].Type, targetType)
                                                                  && SymbolEqualityComparer.Default.Equals(f.ReturnType,
                                                                      sourceType))
            {
                forward = f;
                break;
            }

        if (forward is null) return added;

        var ownTargets = new HashSet<string>(ownExplicit.Select(e => e.Target), StringComparer.Ordinal);
        var fwdExtraTargets =
            new HashSet<string>(ReadMapPropertyExtras(forward).Select(e => e.Target), StringComparer.Ordinal);
        foreach (var (a, b, use) in ReadExplicitMaps(forward))
        {
            var invertible = use is null && a.IndexOf('.') < 0 && b.IndexOf('.') < 0 && !fwdExtraTargets.Contains(b);
            if (!invertible)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReverseMapNonInvertible, location,
                    $"[ReverseMap]: forward mapping '{a}' → '{b}' cannot be auto-inverted; declare the reverse on '{method.Name}' explicitly"));
                continue;
            }

            if (!ownTargets.Contains(a)) added.Add((b, a, null));
        }

        return added;
    }

    private static List<string> ReadFlattenRoots(ISymbol method)
    {
        var roots = new List<string>();
        foreach (var attr in method.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == KnownNames.FlattenFqn
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string s)
                roots.Add(s);

        return roots;
    }

    // ── Plan 20: [FlattenGraph] ───────────────────────────────────────────────

    /// <summary>
    ///     Reads raw [FlattenGraph(srcNav, tgtColl)] annotation pairs from a method symbol.
    /// </summary>
    private static List<(string SourceNavigation, string TargetCollection)> ReadFlattenGraphAttributes(ISymbol method)
    {
        var result = new List<(string, string)>();
        foreach (var attr in method.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == KnownNames.FlattenGraphFqn
                && attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string src
                && attr.ConstructorArguments[1].Value is string tgt)
                result.Add((src, tgt));

        return result;
    }

    /// <summary>
    ///     Checks whether <paramref name="t" /> is a named generic type with the given
    ///     <paramref name="name" /> and <paramref name="ns" /> with exactly <paramref name="arity" />
    ///     type arguments. Returns the first type argument via <paramref name="firstArg" /> if matched.
    /// </summary>
    private static bool IsExactNamedTypeHelper(
        ITypeSymbol t, string name, string ns, int arity, out ITypeSymbol? firstArg)
    {
        firstArg = null;
        if (t is INamedTypeSymbol n
            && n.Name == name
            && n.TypeArguments.Length == arity
            && n.ContainingNamespace?.ToDisplayString() == ns)
        {
            firstArg = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Resolves and validates [FlattenGraph] directives for a single method, synthesizes
    ///     the required BFS traversal and flat-node helpers, and returns the resolved directives
    ///     plus the MemberMap entries to inject into the method's normal member list.
    ///     <para>
    ///         Mutates: <paramref name="synthesized" /> (adds helpers), <paramref name="consumedTargets" />
    ///         (adds target collection member names so ResolveMembers skips them).
    ///     </para>
    /// </summary>
    private static (List<FlattenGraphDirective> Directives, List<MemberMap> InjectedMembers)
        ResolveFlattenGraphDirectives(
            ITypeSymbol sourceType,
            INamedTypeSymbol targetType,
            IReadOnlyList<(string SourceNavigation, string TargetCollection)> rawDirectives,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            EnumStrategy enumStrategy,
            Dictionary<string, SynthesizedMethod> synthesized,
            NullStrategy nullStrategy,
            bool autoNest,
            NestedMappingRegistry? nestedRegistry,
            bool nullAsNull,
            bool isPreserve,
            HashSet<string> consumedTargets,
            IReadOnlyList<(INamedTypeSymbol Src, INamedTypeSymbol Tgt)>? rawDerivedPairs = null)
    {
        var directives = new List<FlattenGraphDirective>();
        var injected = new List<MemberMap>();

        foreach (var (srcNavName, tgtCollName) in rawDirectives)
        {
            // 1. Resolve source navigation member on sourceType
            ITypeSymbol? srcNavType = null;
            foreach (var m in ReadableMembers(sourceType))
                if (string.Equals(m.Name, srcNavName, StringComparison.Ordinal))
                {
                    srcNavType = m.Type;
                    break;
                }

            if (srcNavType is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] source navigation '{srcNavName}' does not exist or is not readable on '{sourceType.Name}'"));
                continue;
            }

            // 2. Determine TNode type: directly a named reference type, or element of a collection
            ITypeSymbol nodeType;
            bool srcNavIsCollection;
            // MF-B: track whether the source nav is specifically an array so we can emit
            // the correct traversal helper parameter type:
            //   array      → TNode[]?          (T[] is both IEnumerable<T> and exact)
            //   other coll → IEnumerable<TNode>? (handles List<T>, HashSet<T>, IReadOnlyList<T>, …)
            //   dict        → use srcNavType directly, seed BFS from .Values
            //   single ref  → TNode?
            bool srcNavIsArray;
            // SF-F3: when the source nav is a Dictionary<K,V> where V is a reference type, seed
            // the BFS from the dictionary's values (.Values) rather than enumerating KeyValuePairs.
            bool srcNavIsDict;

            if (srcNavType is IArrayTypeSymbol arrNav && arrNav.Rank == 1
                                                      && arrNav.ElementType.IsReferenceType)
            {
                nodeType = arrNav.ElementType;
                srcNavIsCollection = true;
                srcNavIsArray = true;
                srcNavIsDict = false;
            }
            else if (DictionaryConverter.TryGetDictionaryValueType(srcNavType, out var dictNavNodeType)
                     && dictNavNodeType.IsReferenceType)
            {
                // SF-F3: Dictionary<K, Node> source nav — node type is V, seed from .Values.
                nodeType = dictNavNodeType;
                srcNavIsCollection = true;
                srcNavIsArray = false;
                srcNavIsDict = true;
            }
            else if (CollectionConverter.TryGetEnumerableElement(srcNavType, out var navElemType, out _)
                     && navElemType.IsReferenceType)
            {
                nodeType = navElemType;
                srcNavIsCollection = true;
                srcNavIsArray = false;
                srcNavIsDict = false;
            }
            else if (srcNavType.IsReferenceType && srcNavType.TypeKind != TypeKind.Array)
            {
                nodeType = srcNavType;
                srcNavIsCollection = false;
                srcNavIsArray = false;
                srcNavIsDict = false;
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] source navigation '{srcNavName}' must be a reference type or a collection of a reference type (the node type)"));
                continue;
            }

            // 3. Resolve target collection member on targetType
            ITypeSymbol? tgtCollType = null;
            foreach (var m in WritableMembers(targetType))
                if (string.Equals(m.Name, tgtCollName, StringComparison.Ordinal))
                {
                    tgtCollType = m.Type;
                    break;
                }

            if (tgtCollType is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] target collection '{tgtCollName}' does not exist or is not writable on '{targetType.Name}'"));
                continue;
            }

            // 4. Determine nodeDtoType: element of the target collection, and the suffix kind
            ITypeSymbol nodeDtoType;
            bool needsToArray;

            if (tgtCollType is IArrayTypeSymbol arrTgt && arrTgt.Rank == 1)
            {
                nodeDtoType = arrTgt.ElementType;
                needsToArray = true;
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "List", "System.Collections.Generic", 1, out var listElem))
            {
                nodeDtoType = listElem!;
                needsToArray = false;
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IReadOnlyList", "System.Collections.Generic", 1,
                         out var rlElem))
            {
                nodeDtoType = rlElem!;
                needsToArray = false; // List<T> implements IReadOnlyList<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "ICollection", "System.Collections.Generic", 1,
                         out var icElem))
            {
                nodeDtoType = icElem!;
                needsToArray = false; // List<T> implements ICollection<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IReadOnlyCollection", "System.Collections.Generic", 1,
                         out var ircElem))
            {
                nodeDtoType = ircElem!;
                needsToArray = false; // List<T> implements IReadOnlyCollection<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IList", "System.Collections.Generic", 1, out var ilElem))
            {
                nodeDtoType = ilElem!;
                needsToArray = false; // List<T> implements IList<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IEnumerable", "System.Collections.Generic", 1,
                         out var ieElem))
            {
                nodeDtoType = ieElem!;
                needsToArray = false; // List<T> implements IEnumerable<T>
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] target collection '{tgtCollName}' on '{targetType.Name}' must be " +
                    $"List<T>, T[], IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T>, IList<T>, or IEnumerable<T>"));
                continue;
            }

            // ── Plan 22: Heterogeneous branch ────────────────────────────────
            // Detect hetero mode: abstract/interface node base OR [MapDerivedType] pairs present.
            var nodeIsAbstractOrInterface =
                nodeType.TypeKind == TypeKind.Interface || nodeType.IsAbstract;
            var effectiveDerivedPairs = rawDerivedPairs ?? Array.Empty<(INamedTypeSymbol, INamedTypeSymbol)>();
            var isHetero = nodeIsAbstractOrInterface || effectiveDerivedPairs.Count > 0;

            if (isHetero)
            {
                // Validate: abstract/interface node base requires at least one [MapDerivedType]
                if (nodeIsAbstractOrInterface && effectiveDerivedPairs.Count == 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                        $"[FlattenGraph] node base type '{nodeType.Name}' is abstract or an interface; " +
                        $"add [MapDerivedType<TNodeDerived, TNodeDerivedDto>] for each concrete node type."));
                    continue;
                }

                // Validate arms and collect resolved arms
                var heteroArms = new List<(INamedTypeSymbol NodeDerived, INamedTypeSymbol DtoDerived,
                    string FlatNodeHelperName,
                    List<(string Name, bool IsCollection, bool IsDictValue)> EdgeMembers,
                    List<(string Name, ITypeSymbol Type)> LeafMembers)>();
                var seenSrcFqns = new HashSet<string>(StringComparer.Ordinal);
                var anyArmError = false;

                foreach (var (derivedSrc, derivedTgt) in effectiveDerivedPairs)
                {
                    var srcFqn = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var tgtFqnArm = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    // 1. derivedSrc must be assignable to nodeType (the node base)
                    if (!HasImplicitConversion(compilation, derivedSrc, nodeType))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType, location,
                            $"[MapDerivedType] source type '{srcFqn}' is not assignable to node base type " +
                            $"'{nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                            $"each derived node type must inherit from or implement the node base."));
                        anyArmError = true;
                        continue;
                    }

                    // 2. derivedTgt must be assignable to nodeDtoType (the base DTO = collection element)
                    if (!HasImplicitConversion(compilation, derivedTgt, nodeDtoType))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType, location,
                            $"[MapDerivedType] target type '{tgtFqnArm}' is not assignable to target collection " +
                            $"element type '{nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                            $"each derived DTO type must inherit from or implement the base DTO."));
                        anyArmError = true;
                        continue;
                    }

                    // 3. No duplicate derived source types
                    if (!seenSrcFqns.Add(srcFqn))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType, location,
                            $"[MapDerivedType] duplicate derived source type '{srcFqn}'; each concrete node type may only be registered once."));
                        anyArmError = true;
                        continue;
                    }

                    // 4. Compute EDGE members for this concrete derived type.
                    // An EDGE member is any readable member whose type is assignable to nodeType, or a
                    // collection thereof (including inherited base edges).
                    // SF-F3 fix: also detect Dictionary<K,V> where V is assignable to nodeType.
                    var nodeBaseNoAnnot = nodeType.WithNullableAnnotation(NullableAnnotation.None);
                    var derivedEdgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue)>();
                    var derivedLeafMembers = new List<(string Name, ITypeSymbol Type)>();

                    foreach (var nm in ReadableMembers(derivedSrc))
                    {
                        var memberTypeNoAnnot = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                        // Single-ref edge: type assignable to nodeBase (includes exact type and subtypes)
                        if (HasImplicitConversion(compilation, memberTypeNoAnnot, nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, false));
                            continue;
                        }

                        // Nullable<T> where T is assignable to nodeBase
                        if (nm.Type is INamedTypeSymbol nmNamed
                            && nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                            && HasImplicitConversion(compilation,
                                nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None), nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, false));
                            continue;
                        }

                        // SF-F3: Dictionary<K,V> where V is assignable to nodeBase → dict-value edge.
                        if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValTypeD)
                            && HasImplicitConversion(compilation,
                                dictValTypeD.WithNullableAnnotation(NullableAnnotation.None), nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, true));
                            continue;
                        }

                        // Collection edge: element type assignable to nodeBase
                        var isEdgeColl = false;
                        if (nm.Type is IArrayTypeSymbol arrEdge && arrEdge.Rank == 1
                                                                && HasImplicitConversion(compilation,
                                                                    arrEdge.ElementType.WithNullableAnnotation(
                                                                        NullableAnnotation.None), nodeType))
                            isEdgeColl = true;
                        else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _)
                                 && HasImplicitConversion(compilation,
                                     edgeElem.WithNullableAnnotation(NullableAnnotation.None), nodeType))
                            isEdgeColl = true;

                        if (isEdgeColl)
                            derivedEdgeMembers.Add((nm.Name, true, false));
                        else
                            derivedLeafMembers.Add((nm.Name, nm.Type));
                    }

                    // 5. Build the __DwarfMap_FlatNode_<TypeName>_<hash> helper for this concrete type
                    var armHashKey = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                     + "=>"
                                     + derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                     + "@FG";
                    var armHash = StableHash.Fnv1a(armHashKey);
                    var typeName = derivedSrc.Name;
                    var perTypeHelperName = GeneratedNames.FlatNode + typeName + "_" + armHash;

                    if (!synthesized.ContainsKey(perTypeHelperName))
                    {
                        var nodeFqDerived = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var dtoFqDerived = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var dtoWritableDerived = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
                        foreach (var wm in WritableMembers(derivedTgt))
                            dtoWritableDerived[wm.Name] = wm.Type;

                        var sbArm = new StringBuilder();
                        sbArm.Append("    private ").Append(dtoFqDerived).Append(' ').Append(perTypeHelperName)
                            .Append('(').Append(nodeFqDerived).AppendLine(" n)");
                        sbArm.AppendLine("    {");
                        sbArm.Append("        return new ").Append(dtoFqDerived).AppendLine();
                        sbArm.AppendLine("        {");

                        // Leaf members: map with conversion where available.
                        // MF-D fix: use throw-away synth dict + skip complex synthesized converters.
                        // SF-LEAFDIAG fix: propagate unmappable-leaf errors to real diagnostics.
                        foreach (var leaf in derivedLeafMembers)
                        {
                            if (!dtoWritableDerived.TryGetValue(leaf.Name, out var dtoMemberType))
                                continue;
                            var leafThrowAwaySynth = new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal);
                            var leafTestDiags = new List<DiagnosticInfo>();
                            var leafResolved = TryResolveConversion(compilation, leaf.Type, dtoMemberType, null,
                                allMethods, autoCandidates, enumStrategy, leafThrowAwaySynth, nullStrategy,
                                location, leaf.Name, leafTestDiags, out var leafConv, out var leafNull, out _,
                                autoNest, nestedRegistry);
                            if (!leafResolved)
                            {
                                diagnostics.AddRange(leafTestDiags);
                                continue;
                            }

                            // MF-D: skip only COMPLEX helpers (Obj/Coll/Dict) that may become 3-param.
                            // Numeric/enum/parsable helpers are always single-arg and are safe.
                            if (GeneratedNames.IsComplexHelper(leafConv))
                                continue; // complex synthesized helper — skip (topology degradation)
                            foreach (var kv in leafThrowAwaySynth)
                                if (!synthesized.ContainsKey(kv.Key))
                                    synthesized[kv.Key] = kv.Value;
                            sbArm.Append("            ").Append(leaf.Name).Append(" = ");
                            AppendFlatNodeMemberExpr(sbArm, "n", leaf.Name, leafConv, leafNull);
                            sbArm.AppendLine(",");
                        }

                        // Edge members on derived DTO: null them (topology degradation)
                        foreach (var edge in derivedEdgeMembers)
                        {
                            if (!dtoWritableDerived.ContainsKey(edge.Name))
                                continue;
                            sbArm.Append("            ").Append(edge.Name).AppendLine(" = null,");
                        }

                        sbArm.AppendLine("        };");
                        sbArm.AppendLine("    }");
                        synthesized[perTypeHelperName] = new SynthesizedMethod(perTypeHelperName, sbArm.ToString());
                    }

                    heteroArms.Add((derivedSrc, derivedTgt, perTypeHelperName, derivedEdgeMembers, derivedLeafMembers));
                }

                if (anyArmError && heteroArms.Count == 0)
                    continue; // all arms had errors; skip this directive

                // Sort arms most-derived-first (reuse Plan-21 sort)
                var sortedHeteroArms = heteroArms
                    .Select((arm, idx) => (arm, idx, depth: InheritanceDepth(arm.NodeDerived)))
                    .OrderByDescending(x => x.depth)
                    .ThenBy(x => x.idx)
                    .Select(x => x.arm)
                    .ToList();

                // Build dispatch helper name and traversal helper name from the hetero hash
                var heteroHashKey = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                    + "=>"
                                    + nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                    + "@Hetero";
                var heteroHash = StableHash.Fnv1a(heteroHashKey);

                var dispatchHelperName = GeneratedNames.FlatNodeDispatch + heteroHash;
                var traversalHelperNameH = GeneratedNames.FlattenGraph + heteroHash;

                // Synthesize __DwarfMap_FlatNodeDispatch_<hash>(TBase n) => n switch { ... }
                if (!synthesized.ContainsKey(dispatchHelperName))
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var sbDisp = new StringBuilder();
                    sbDisp.Append("    private ").Append(dtoBaseFq).Append(' ').Append(dispatchHelperName)
                        .Append('(').Append(nodeBaseFq).AppendLine(" n)");
                    sbDisp.AppendLine("        => n switch");
                    sbDisp.AppendLine("        {");
                    foreach (var arm in sortedHeteroArms)
                    {
                        var armSrcFq = arm.NodeDerived.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        sbDisp.Append("            ").Append(armSrcFq).Append(" __s => ")
                            .Append(arm.FlatNodeHelperName).AppendLine("(__s),");
                    }

                    sbDisp.Append("            _ => throw new global::System.ArgumentException(")
                        .Append(
                            "\"DwarfMapper [FlattenGraph]: no [MapDerivedType] registered for runtime node type '\" + ")
                        .Append("n.GetType() + \"'.\", nameof(n)),");
                    sbDisp.AppendLine();
                    sbDisp.AppendLine("        };");
                    synthesized[dispatchHelperName] = new SynthesizedMethod(dispatchHelperName, sbDisp.ToString());
                }

                // Synthesize __DwarfMap_FlattenGraph_<hash>(TBase entry) BFS traversal
                if (!synthesized.ContainsKey(traversalHelperNameH))
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var listFqH = "global::System.Collections.Generic.List<" + dtoBaseFq + ">";
                    var queueFqH = "global::System.Collections.Generic.Queue<" + nodeBaseFq + ">";
                    var hashSetFqH = "global::System.Collections.Generic.HashSet<object>";

                    var sbBfs = new StringBuilder();
                    sbBfs.Append("    private ").Append(listFqH).Append(' ').Append(traversalHelperNameH)
                        .Append('(');
                    // MF-B: use the correct parameter type for the entry parameter.
                    if (srcNavIsArray)
                        sbBfs.Append(nodeBaseFq).AppendLine("[]? entry)");
                    else if (srcNavIsDict)
                        sbBfs.Append(srcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            .AppendLine("? entry)");
                    else if (srcNavIsCollection)
                        sbBfs.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeBaseFq)
                            .AppendLine(">? entry)");
                    else
                        sbBfs.Append(nodeBaseFq).AppendLine("? entry)");
                    sbBfs.AppendLine("    {");
                    sbBfs.Append("        var __result = new ").Append(listFqH).AppendLine("();");

                    if (srcNavIsDict)
                    {
                        // SF-F3: dict source nav — seed BFS from dict values.
                        sbBfs.AppendLine("        if (entry is null) return __result;");
                        sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                            .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                        sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                        sbBfs.AppendLine(
                            "        foreach (var __kv in entry) if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                    }
                    else if (srcNavIsCollection)
                    {
                        sbBfs.AppendLine("        if (entry is null) return __result;");
                        sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                            .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                        sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                        sbBfs.AppendLine(
                            "        foreach (var __seed in entry) if (__seed is not null && __visited.Add(__seed)) __queue.Enqueue(__seed);");
                    }
                    else
                    {
                        sbBfs.AppendLine("        if (entry is null) return __result;");
                        sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                            .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                        sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                        sbBfs.AppendLine("        __visited.Add(entry);");
                        sbBfs.AppendLine("        __queue.Enqueue(entry);");
                    }

                    sbBfs.AppendLine("        while (__queue.Count > 0)");
                    sbBfs.AppendLine("        {");
                    sbBfs.AppendLine("            var __n = __queue.Dequeue();");
                    sbBfs.Append("            __result.Add(").Append(dispatchHelperName).AppendLine("(__n));");

                    // Edge enumeration: runtime-type switch over concrete node types (most-derived-first)
                    sbBfs.AppendLine("            switch (__n)");
                    sbBfs.AppendLine("            {");
                    foreach (var arm in sortedHeteroArms)
                    {
                        var armSrcFq = arm.NodeDerived.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        // Use braces around each case block to create a new scope (avoids CS0128
                        // when two arms both introduce locals with the same base name, e.g. __e_Parent).
                        sbBfs.Append("                case ").Append(armSrcFq).AppendLine(" __t:");
                        sbBfs.AppendLine("                {");
                        // Enqueue each edge member of this arm
                        foreach (var edge in arm.EdgeMembers)
                            if (edge.IsDictValue)
                                // SF-F3: Dictionary<K,V> where V is a node — traverse values.
                                sbBfs.Append("                    if (__t.").Append(edge.Name)
                                    .Append(" is { } __d_").Append(edge.Name)
                                    .Append(") foreach (var __kv in __d_").Append(edge.Name)
                                    .AppendLine(
                                        ") if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                            else if (!edge.IsCollection)
                                sbBfs.Append("                    if (__t.").Append(edge.Name)
                                    .Append(" is { } __e_").Append(edge.Name)
                                    .Append(" && __visited.Add(__e_").Append(edge.Name)
                                    .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                            else
                                sbBfs.Append("                    if (__t.").Append(edge.Name)
                                    .Append(" is { } __c_").Append(edge.Name)
                                    .Append(") foreach (var __x in __c_").Append(edge.Name)
                                    .AppendLine(") if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);");

                        sbBfs.AppendLine("                    break;");
                        sbBfs.AppendLine("                }");
                    }

                    sbBfs.AppendLine("            }");

                    sbBfs.AppendLine("        }");
                    sbBfs.AppendLine("        return __result;");
                    sbBfs.AppendLine("    }");
                    synthesized[traversalHelperNameH] = new SynthesizedMethod(traversalHelperNameH, sbBfs.ToString());
                }

                // For array targets, synthesize a thin .ToArray() wrapper
                string converterHelperNameH;
                if (needsToArray)
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var wrapperNameH = GeneratedNames.FlattenGraphArr + heteroHash;
                    if (!synthesized.ContainsKey(wrapperNameH))
                    {
                        var sbWr = new StringBuilder();
                        sbWr.Append("    private ").Append(dtoBaseFq).Append("[] ").Append(wrapperNameH)
                            .Append('(');
                        // MF-B: match the traversal helper's parameter type.
                        if (srcNavIsArray)
                            sbWr.Append(nodeBaseFq).AppendLine("[]? entry)");
                        else if (srcNavIsCollection)
                            sbWr.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeBaseFq)
                                .AppendLine(">? entry)");
                        else
                            sbWr.Append(nodeBaseFq).AppendLine("? entry)");
                        sbWr.Append("        => ").Append(traversalHelperNameH).AppendLine("(entry).ToArray();");
                        synthesized[wrapperNameH] = new SynthesizedMethod(wrapperNameH, sbWr.ToString());
                    }

                    converterHelperNameH = wrapperNameH;
                }
                else
                {
                    converterHelperNameH = traversalHelperNameH;
                }

                consumedTargets.Add(tgtCollName);
                injected.Add(new MemberMap(
                    tgtCollName,
                    srcNavName,
                    converterHelperNameH,
                    NullHandling.None,
                    false,
                    true));
                directives.Add(new FlattenGraphDirective(
                    srcNavName, tgtCollName, traversalHelperNameH, converterHelperNameH));
                continue; // skip the homogeneous path below
            }
            // ── End Plan 22 heterogeneous branch ─────────────────────────────

            // 5. Validate nodeType → nodeDtoType structural compatibility.
            // We do NOT call TryResolveConversion here because it eagerly synthesizes helpers
            // (including recursion-capable Obj mappers and collection helpers) with baked-in
            // signatures that may become inconsistent once the drain loop marks them recursion-capable.
            // Instead: check structural compatibility — at minimum, nodeDtoType must be a named type
            // with a public parameterless constructor (or be constructable), OR there must be a declared
            // mapper for this pair. The flat-node helper only maps leaf members; unmappable leaves are
            // silently skipped, so the check is just a sanity gate on type kind.
            var nodeDtoIsConstructible = false;
            if (nodeDtoType is INamedTypeSymbol namedDtoCheck)
                nodeDtoIsConstructible =
                    (namedDtoCheck.TypeKind == TypeKind.Class || namedDtoCheck.TypeKind == TypeKind.Struct)
                    && namedDtoCheck.SpecialType == SpecialType.None
                    && namedDtoCheck.InstanceConstructors.Any(c =>
                        c.DeclaredAccessibility == Accessibility.Public);
            // Also accept if there's an explicit declared mapper method for this pair.
            var hasDeclaredMapper = false;
            foreach (var m in allMethods)
                if (HasImplicitConversion(compilation, nodeType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, nodeDtoType))
                {
                    hasDeclaredMapper = true;
                    break;
                }

            // Accept implicit conversion too (value types, same type, etc.)
            var hasImplicit = HasImplicitConversion(compilation, nodeType, nodeDtoType);

            if (!nodeDtoIsConstructible && !hasDeclaredMapper && !hasImplicit)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] node DTO type '{nodeDtoType.Name}' is not constructible " +
                    $"(must be a class or struct with a public constructor)"));
                continue;
            }

            // 6. Partition TNode readable members into edge (same type as nodeType or collection thereof) vs leaf
            // SF-F4 fix: use bidirectional assignability (HasImplicitConversion both ways) instead of exact
            //   type equality so that interface-typed edges (e.g. "INode? Link" where node : INode) and
            //   base-class-typed edges are traversed.  For edges typed as an ancestor/interface of nodeType,
            //   the BFS enqueue must cast via `is TNode __var` (since the queue holds TNode, not the interface).
            // SF-F3 fix: detect Dictionary<K,V> where V is assignable to nodeType as a dict-value edge.
            var nodeMembers = ReadableMembers(nodeType).ToList();
            // Edge tuple: (Name, IsCollection, IsDictValue, NeedsNodeCast)
            // NeedsNodeCast=true: member type is an ancestor/interface of nodeType → enqueue via `is TNode` cast.
            var edgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue, bool NeedsNodeCast)>();
            var leafMembers = new List<(string Name, ITypeSymbol Type)>();

            var nodeTypeNoAnnotation = nodeType.WithNullableAnnotation(NullableAnnotation.None);

            foreach (var nm in nodeMembers)
            {
                var memberTypeNoAnnotation = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                // SF-F4 fix: Direct node reference — recognise as a graph edge if:
                //   (a) memberType is assignable to nodeType (e.g. a derived subtype field), OR
                //   (b) nodeType is assignable to memberType (e.g. edge typed as an interface/base
                //       that the node implements/derives — "INode? Link" where node is a class : INode).
                // Was: exact equality only — missed interface-typed and base-typed edges.
                var directToNode = HasImplicitConversion(compilation, memberTypeNoAnnotation, nodeType);
                var nodeToMember = !directToNode &&
                                   HasImplicitConversion(compilation, nodeTypeNoAnnotation, memberTypeNoAnnotation);
                if (directToNode || nodeToMember)
                {
                    // NeedsNodeCast: when the member type is a base/interface (reverse direction), we
                    // must cast via `is TNode __var` before enqueuing so the Queue<TNode> accepts it.
                    edgeMembers.Add((nm.Name, false, false, nodeToMember));
                    continue;
                }

                // Nullable<TNode> (for structs — unlikely but supported)
                if (nm.Type is INamedTypeSymbol nmNamed
                    && nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    var innerNoAnnot = nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None);
                    var innerToNode = HasImplicitConversion(compilation, innerNoAnnot, nodeType);
                    var nodeToInner = !innerToNode &&
                                      HasImplicitConversion(compilation, nodeTypeNoAnnotation, innerNoAnnot);
                    if (innerToNode || nodeToInner)
                    {
                        edgeMembers.Add((nm.Name, false, false, nodeToInner));
                        continue;
                    }
                }

                // SF-F3 fix: Dictionary<K,V> where V is assignable to nodeType → dict-value edge.
                // Only V assignable to nodeType qualifies; keys are not traversed (v1: values only).
                if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValType)
                    && HasImplicitConversion(compilation, dictValType.WithNullableAnnotation(NullableAnnotation.None),
                        nodeType))
                {
                    edgeMembers.Add((nm.Name, false, true, false)); // IsDictValue=true, no cast needed
                    continue;
                }

                // Collection of TNode (SF-F4 fix: use bidirectional assignability for element type)
                var isEdgeColl = false;
                var collNeedsCast = false;
                if (nm.Type is IArrayTypeSymbol arrEdge && arrEdge.Rank == 1)
                {
                    var arrElemNoAnnot = arrEdge.ElementType.WithNullableAnnotation(NullableAnnotation.None);
                    if (HasImplicitConversion(compilation, arrElemNoAnnot, nodeType))
                    {
                        isEdgeColl = true;
                    }
                    else if (HasImplicitConversion(compilation, nodeTypeNoAnnotation, arrElemNoAnnot))
                    {
                        isEdgeColl = true;
                        collNeedsCast = true;
                    }
                }
                else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _))
                {
                    var edgeElemNoAnnot = edgeElem.WithNullableAnnotation(NullableAnnotation.None);
                    if (HasImplicitConversion(compilation, edgeElemNoAnnot, nodeType))
                    {
                        isEdgeColl = true;
                    }
                    else if (HasImplicitConversion(compilation, nodeTypeNoAnnotation, edgeElemNoAnnot))
                    {
                        isEdgeColl = true;
                        collNeedsCast = true;
                    }
                }

                if (isEdgeColl)
                    edgeMembers.Add((nm.Name, true, false, collNeedsCast));
                else
                    leafMembers.Add((nm.Name, nm.Type));
            }

            // 7. Get writable members of nodeDtoType for the flat-node helper
            var dtoWritable = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            foreach (var m in WritableMembers(nodeDtoType))
                dtoWritable[m.Name] = m.Type;

            // 8. Build hash key and helper names
            var hashKey = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                          + "=>"
                          + nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var hash = StableHash.Fnv1a(hashKey);

            var flatNodeHelperName = GeneratedNames.FlatNode + hash;
            var traversalHelperName = GeneratedNames.FlattenGraph + hash;

            // 9. Synthesize __DwarfMap_FlatNode_HASH (maps one TNode leaf-only → TNodeDto)
            if (!synthesized.ContainsKey(flatNodeHelperName))
            {
                var nodeFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var sb = new StringBuilder();
                sb.Append("    private ").Append(dtoFq).Append(' ').Append(flatNodeHelperName)
                    .Append('(').Append(nodeFq).AppendLine("? n)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (n is null) return null!;");
                sb.Append("        return new ").Append(dtoFq).AppendLine();
                sb.AppendLine("        {");

                // Leaf members: map with conversion where available.
                // MF-D fix: flat-node leaf synthesis must NEVER call a synthesized complex helper
                // (one that starts with "__DwarfMap_") because those helpers may later be force-marked
                // recursion-capable (3-param) by the Preserve force-marking loop, creating a
                // signature mismatch when the flat-node helper calls them with 1 arg → CS7036.
                // Strategy: use a THROW-AWAY synthesized dict for complex resolutions — this
                // prevents polluting the main dict with a shared Obj/Dict/Coll helper that will
                // later become 3-param.  Only emit the leaf if the resulting converter is either:
                //   (a) null (direct assignment — primitive/same-type),
                //   (b) a declared user method (doesn't start with "__DwarfMap_"), OR
                //   (c) a synthesized PRIMITIVE helper (enum/numeric/parsable, which are never 3-param).
                // If the resolved converter is a synthesized complex helper, skip the member
                // (leave DTO default) — that's correct topology-degraded behaviour for flat-graph.
                // SF-LEAFDIAG fix: propagate diagnostics for truly unmappable leaf members to the
                // real diagnostics list so callers get a DWARF005/etc. error rather than silence.
                foreach (var leaf in leafMembers)
                {
                    if (!dtoWritable.TryGetValue(leaf.Name, out var dtoMemberType))
                        continue;

                    // Use a throw-away synth dict so complex helpers (Obj/Dict/Coll) are NOT
                    // registered in the main dict and cannot be force-marked 3-param later.
                    var leafThrowAwaySynth = new Dictionary<string, SynthesizedMethod>(
                        StringComparer.Ordinal);
                    var leafTestDiags = new List<DiagnosticInfo>();
                    var leafResolved = TryResolveConversion(compilation, leaf.Type, dtoMemberType, null,
                        allMethods, autoCandidates, enumStrategy, leafThrowAwaySynth, nullStrategy,
                        location, leaf.Name, leafTestDiags, out var leafConv, out var leafNull, out _,
                        autoNest, nestedRegistry);

                    if (!leafResolved)
                    {
                        // SF-LEAFDIAG: propagate errors from unmappable leaf members (not silently dropped).
                        diagnostics.AddRange(leafTestDiags);
                        continue;
                    }

                    // MF-D: a synthesized COMPLEX helper (object mapper, collection helper, dict helper) may be
                    // force-marked recursion-capable (3-param) by the Preserve post-processing, which would then
                    // mismatch the single-argument call the flat-node helper emits.
                    // Unsafe prefixes: __DwarfMap_Obj_, __DwarfMap_Coll_, __DwarfMap_Dict_
                    // Safe prefixes:   __DwarfMap_Num_, __DwarfMap_Enum_, __DwarfMap_Pars_, __DwarfMap_Blit_
                    //
                    // That hazard is real ONLY under Preserve: the force-marking block is guarded by
                    // `if (isPreserveMode)`, so in None/SetNull mode the helper stays single-arg and is safe to
                    // call. This used to `continue` unconditionally, which silently left a data-bearing leaf
                    // (a `List<string> Tags`, a nested `Money Price`) at the DTO's default with no diagnostic —
                    // distinct from EDGE members, which are nulled deliberately as documented topology
                    // degradation. So: flatten it when we can, and when we genuinely cannot, say so.
                    if (GeneratedNames.IsComplexHelper(leafConv))
                    {
                        if (isPreserve)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.FlattenGraphLeafNotFlattened, location,
                                $"[FlattenGraph] cannot flatten member '{leaf.Name}' of type "
                                + $"'{leaf.Type.ToDisplayString()}' under ReferenceHandling = Preserve; it is left "
                                + "at the destination's default. Map the member explicitly, or use "
                                + "ReferenceHandling = None for this mapper."));
                            continue;
                        }

                        // Not Preserve — fall through and emit the leaf; the merge below registers the complex
                        // helper in the main dict so the call resolves.
                    }

                    // Safe: emit the leaf member.  Merge any non-complex throw-away entries
                    // (numeric, enum, parsable helpers that are never force-marked 3-param).
                    foreach (var kv in leafThrowAwaySynth)
                        if (!synthesized.ContainsKey(kv.Key))
                            synthesized[kv.Key] = kv.Value;

                    sb.Append("            ").Append(leaf.Name).Append(" = ");
                    AppendFlatNodeMemberExpr(sb, "n", leaf.Name, leafConv, leafNull);
                    sb.AppendLine(",");
                }

                // Edge members on DTO: null them out (topology degradation — the point of [FlattenGraph])
                foreach (var edge in edgeMembers)
                {
                    if (!dtoWritable.ContainsKey(edge.Name))
                        continue;
                    sb.Append("            ").Append(edge.Name).AppendLine(" = null,");
                }

                sb.AppendLine("        };");
                sb.AppendLine("    }");
                synthesized[flatNodeHelperName] = new SynthesizedMethod(flatNodeHelperName, sb.ToString());
            }

            // 10. Synthesize __DwarfMap_FlattenGraph_HASH (BFS traversal → List<TNodeDto>)
            if (!synthesized.ContainsKey(traversalHelperName))
            {
                var nodeFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var listFq = "global::System.Collections.Generic.List<" + dtoFq + ">";
                var queueFq = "global::System.Collections.Generic.Queue<" + nodeFq + ">";
                var hashSetFq = "global::System.Collections.Generic.HashSet<object>";

                var sb = new StringBuilder();
                sb.Append("    private ").Append(listFq).Append(' ').Append(traversalHelperName)
                    .Append('(');
                // MF-B: use the correct parameter type for the entry parameter.
                // Array nav    → TNode[]?  (exact array type, avoids CS1503 with T[])
                // Dict nav     → DictType? (seed from .Values — SF-F3 dict source nav)
                // Non-array coll nav → IEnumerable<TNode>? (accepts List<T>, HashSet<T>, IReadOnlyList<T>, …)
                // Single-ref nav → TNode? (nullable reference)
                if (srcNavIsArray)
                    sb.Append(nodeFq).AppendLine("[]? entry)");
                else if (srcNavIsDict)
                    sb.Append(srcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .AppendLine("? entry)");
                else if (srcNavIsCollection)
                    sb.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeFq).AppendLine(">? entry)");
                else
                    sb.Append(nodeFq).AppendLine("? entry)");
                sb.AppendLine("    {");
                sb.Append("        var __result = new ").Append(listFq).AppendLine("();");

                if (srcNavIsDict)
                {
                    // SF-F3: dict source nav — seed BFS from dict values (not kvp pairs).
                    sb.AppendLine("        if (entry is null) return __result;");
                    sb.Append("        var __visited = new ").Append(hashSetFq)
                        .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sb.Append("        var __queue = new ").Append(queueFq).AppendLine("();");
                    sb.AppendLine(
                        "        foreach (var __kv in entry) if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                }
                else if (srcNavIsCollection)
                {
                    // Entry is a collection — seed the queue from all non-null elements
                    sb.AppendLine("        if (entry is null) return __result;");
                    sb.Append("        var __visited = new ").Append(hashSetFq)
                        .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sb.Append("        var __queue = new ").Append(queueFq).AppendLine("();");
                    sb.AppendLine(
                        "        foreach (var __seed in entry) if (__seed is not null && __visited.Add(__seed)) __queue.Enqueue(__seed);");
                }
                else
                {
                    sb.AppendLine("        if (entry is null) return __result;");
                    sb.Append("        var __visited = new ").Append(hashSetFq)
                        .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sb.Append("        var __queue = new ").Append(queueFq).AppendLine("();");
                    sb.AppendLine("        __visited.Add(entry);");
                    sb.AppendLine("        __queue.Enqueue(entry);");
                }

                sb.AppendLine("        while (__queue.Count > 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            var __n = __queue.Dequeue();");
                sb.Append("            __result.Add(").Append(flatNodeHelperName).AppendLine("(__n));");

                // Enqueue reachable nodes via edge members of TNode
                foreach (var edge in edgeMembers)
                    if (edge.IsDictValue)
                    {
                        // SF-F3: Dictionary<K,V> where V is a node — traverse values, not keys.
                        sb.Append("            if (__n.").Append(edge.Name)
                            .Append(" is { } __d_").Append(edge.Name)
                            .Append(") foreach (var __kv in __d_").Append(edge.Name)
                            .AppendLine(
                                ") if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                    }
                    else if (!edge.IsCollection)
                    {
                        if (edge.NeedsNodeCast)
                            // SF-F4: edge typed as interface/base → use `is TNode` pattern to cast
                            // and filter to only concrete TNode values (safe: we're BFS-ing a TNode graph).
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is ").Append(nodeFq).Append(" __e_").Append(edge.Name)
                                .Append(" && __visited.Add(__e_").Append(edge.Name)
                                .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                        else
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is { } __e_").Append(edge.Name)
                                .Append(" && __visited.Add(__e_").Append(edge.Name)
                                .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                    }
                    else
                    {
                        if (edge.NeedsNodeCast)
                            // SF-F4: collection of interface/base elements → cast each.
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is { } __c_").Append(edge.Name)
                                .Append(") foreach (var __xi in __c_").Append(edge.Name)
                                .Append(") if (__xi is ").Append(nodeFq)
                                .AppendLine(" __x && __visited.Add(__x)) __queue.Enqueue(__x);");
                        else
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is { } __c_").Append(edge.Name)
                                .Append(") foreach (var __x in __c_").Append(edge.Name)
                                .AppendLine(") if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);");
                    }

                sb.AppendLine("        }");
                sb.AppendLine("        return __result;");
                sb.AppendLine("    }");
                synthesized[traversalHelperName] = new SynthesizedMethod(traversalHelperName, sb.ToString());
            }

            // 11. For array targets, synthesize a thin .ToArray() wrapper
            string converterHelperName;
            if (needsToArray)
            {
                var nodeFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var wrapperName = GeneratedNames.FlattenGraphArr + hash;
                if (!synthesized.ContainsKey(wrapperName))
                {
                    var sb = new StringBuilder();
                    sb.Append("    private ").Append(dtoFq).Append("[] ").Append(wrapperName)
                        .Append('(');
                    // MF-B: match the traversal helper's parameter type.
                    if (srcNavIsArray)
                        sb.Append(nodeFq).AppendLine("[]? entry)");
                    else if (srcNavIsCollection)
                        sb.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeFq)
                            .AppendLine(">? entry)");
                    else
                        sb.Append(nodeFq).AppendLine("? entry)");
                    sb.Append("        => ").Append(traversalHelperName).AppendLine("(entry).ToArray();");
                    synthesized[wrapperName] = new SynthesizedMethod(wrapperName, sb.ToString());
                }

                converterHelperName = wrapperName;
            }
            else
            {
                converterHelperName = traversalHelperName;
            }

            // 12. Mark target collection as consumed (ResolveMembers must skip it)
            consumedTargets.Add(tgtCollName);

            // 13. Build a MemberMap for the injection — emitter handles it like any other member
            //     SourceIsNullableRef=true ensures '!' is added if needed (the traversal helper handles null internally)
            injected.Add(new MemberMap(
                tgtCollName,
                srcNavName,
                converterHelperName,
                NullHandling.None,
                false,
                true));

            // 14. Record directive (for model completeness / snapshot tests)
            directives.Add(new FlattenGraphDirective(
                srcNavName, tgtCollName, traversalHelperName, converterHelperName));
        }

        return (directives, injected);
    }

    /// <summary>
    ///     Appends a member-access value expression to <paramref name="sb" /> for use inside
    ///     a <c>__DwarfMap_FlatNode_*</c> helper. Does NOT append trailing comma or newline.
    /// </summary>
    private static void AppendFlatNodeMemberExpr(
        StringBuilder sb,
        string paramName,
        string memberName,
        string? conv,
        NullHandling nh)
    {
        var access = paramName + "." + memberName;
        if (conv is not null)
        {
            var needsBang = GeneratedNames.IsSynthesized(conv);
            sb.Append(conv).Append('(').Append(access).Append(needsBang ? "!" : "").Append(')');
        }
        else
        {
            switch (nh)
            {
                case NullHandling.ThrowIfNull:
                    sb.Append(access)
                        .Append(" ?? throw new global::System.InvalidOperationException(\"Source member '")
                        .Append(memberName).Append("' was null\")");
                    break;
                case NullHandling.ValueOrDefault:
                    sb.Append(access).Append(".GetValueOrDefault()");
                    break;
                default:
                    sb.Append(access);
                    break;
            }
        }
    }
}
