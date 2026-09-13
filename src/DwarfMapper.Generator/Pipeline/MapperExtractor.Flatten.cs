// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     Resolves every <c>[Flatten("Root")]</c> directive to the root member and the leaves it pulls up,
        ///     reporting <c>DWARF016</c> for a root that names nothing, names a scalar, or has no readable members.
        ///     <para>
        ///         Shared by the runtime resolver (<c>ResolveMembers</c>) and the projection resolver
        ///         (<c>ResolveProjectionMembers</c>). It was inline in the first of those and the second had no copy
        ///         at all, which is exactly the divergence recorded as <c>D10</c>: the directive resolved at
        ///         create-map and update-into and was discarded without a word at projection. One walk rather than
        ///         two, so a root the runtime path refuses cannot be silently accepted by the other.
        ///     </para>
        /// </summary>
        /// <param name="flattenRoots">
        ///     The root names, already read through <see cref="ReadFlattenRoots" /> — which drops an absent or
        ///     non-string argument, so a malformed directive reaches neither the model nor a message.
        /// </param>
        /// <param name="sourceType">The mapping's source type, whose members the roots name.</param>
        /// <param name="comparer">The mapper's member-name comparer, so <c>CaseInsensitive</c> reaches roots too.</param>
        /// <param name="allowNonPublic">
        ///     Whether non-public members are readable. Deliberately a parameter: the projection resolver passes
        ///     <c>ProjectionPublicOnly</c> because an expression tree cannot read a non-public member, and inheriting
        ///     the mapper's value here would resolve a leaf the provider cannot translate.
        /// </param>
        /// <param name="warnNullableHop">
        ///     Whether a nullable-reference root is ELIGIBLE for <c>DWARF044</c>. True for the runtime path, where
        ///     the emitted <c>src.Root.Leaf</c> throws on a null root; FALSE for projection, where the provider
        ///     translates the path to a join that yields null — the same choice the dotted <c>[MapProperty]</c>
        ///     source path already makes at that endpoint, and stating it as a parameter keeps the two from
        ///     drifting apart. Eligibility is not the report: see <c>NullableHop</c> on the returned tuple.
        /// </param>
        /// <param name="location">Where to report.</param>
        /// <param name="diagnostics">The collector.</param>
        /// <returns>
        ///     One entry per resolved root: its name, its readable leaves, and whether a leaf actually pulled up
        ///     from it would dereference a nullable reference —
        ///     <b>the caller reports <c>DWARF044</c>, and only for
        ///         a root some destination member really consumed</b>
        ///     (B26, round 22 W2). This walk used to report it
        ///     the moment the root resolved, so a <c>[Flatten]</c> whose leaves landed nowhere still warned that
        ///     "a null value throws at runtime when its flattened members are read" — about members nobody reads.
        ///     Harmless as advice, corrosive as evidence: it is exactly what made <c>D10</c> read <c>Refused</c> at
        ///     CreateMap and UpdateInto for four rounds while the emitted output was byte-identical, letting the
        ///     finding claim the directive was honoured there. A diagnostic that fires on a directive with no
        ///     effect is a cell that looks measured and is not.
        /// </returns>
        /// <param name="compilation">The compilation the root's member walk runs against.</param>
        private static List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves, bool NullableHop)>
            ResolveFlattenInfos(
                IReadOnlyList<string> flattenRoots,
                ITypeSymbol sourceType,
                StringComparer comparer,
                Compilation compilation,
                bool allowNonPublic,
                bool warnNullableHop,
                LocationInfo? location,
                List<DiagnosticInfo> diagnostics)
        {
            var flattenInfos =
                new List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves, bool NullableHop)>();
            foreach (var root in flattenRoots)
            {
                var match = ReadableMembers(sourceType, compilation, allowNonPublic)
                    .Where(m => comparer.Equals(m.Name, root))
                    .Select(m => ((string Name, ITypeSymbol Type)?)m)
                    .FirstOrDefault();
                if (match is null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                    continue;
                }

                var rootType = match.Value.Type;
                // Scalars (string, primitives, enums) are not flattenable roots — flattening their
                // BCL members (e.g. string.Length) is never intended and must not happen silently.
                if (rootType.SpecialType != SpecialType.None || rootType.TypeKind == TypeKind.Enum)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                    continue;
                }

                var leaves = ReadableMembers(rootType, compilation, allowNonPublic).ToList();
                if (leaves.Count == 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                    continue;
                }

                // A [Flatten] over a nullable-reference root emits unguarded `src.Root.Leaf` accesses that NRE
                // at runtime if the root is null. The dotted [MapProperty] path warns DWARF044 for the same
                // hazard; the [Flatten] path must be consistent (loud, never silent) — but only once an access
                // actually exists to be unguarded. The verdict is carried out; the report is the caller's
                // (ReportUnguardedFlattenHops), which knows which roots a destination member consumed.
                flattenInfos.Add((match.Value.Name, leaves, warnNullableHop && SourceMayBeNullRef(rootType)));
            }

            return flattenInfos;
        }

        /// <summary>
        ///     Reports <c>DWARF044</c> for every <c>[Flatten]</c> root that is a nullable reference AND that some
        ///     destination member actually pulled a leaf up from — the unguarded <c>src.Root.Leaf</c> the warning
        ///     is about (B26, round 22 W2).
        ///     <para>
        ///         Deliberately a separate step, called once at the end of the resolver's walk, for the reason
        ///         <c>DWARF070</c> is: the answer is only knowable after every pass has had its chance to consume —
        ///         or refuse — a leaf. Reporting it at resolution time made the warning fire on a directive with no
        ///         effect, which is a cell that looks measured and is not.
        ///     </para>
        ///     <para>
        ///         Ordered by root name so generator output stays deterministic, and reported once per root even
        ///         when several destination members read leaves from it: the hazard is the one null dereference,
        ///         not one per member.
        ///     </para>
        /// </summary>
        private static void ReportUnguardedFlattenHops(
            List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves, bool NullableHop)> flattenInfos,
            HashSet<string> consumedRoots,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            foreach (var fi in flattenInfos
                         .Where(fi => fi.NullableHop && consumedRoots.Contains(fi.Root))
                         .OrderBy(fi => fi.Root, StringComparer.Ordinal))
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathNullableHop,
                    location,
                    $"[Flatten] source '{fi.Root}' is a nullable reference; a null value throws at runtime when its flattened members are read"));
        }

        /// <summary>
        ///     The extractor's name for <see cref="MemberFacts.TryResolvePath" />. The walk itself is shared because
        ///     <c>ConstructorSelector</c> has to answer the same question when it scores which parameters have a
        ///     source, and a second copy is how the two came to disagree (R18-31).
        /// </summary>
        private static bool TryResolveSourcePath(
            ITypeSymbol root,
            string dottedPath,
            Compilation compilation,
            bool allowNonPublic,
            out ITypeSymbol? leafType,
            out bool nullableHop,
            out string badSegment)
        {
            return MemberFacts.TryResolvePath(root,
                dottedPath,
                compilation,
                allowNonPublic,
                out leafType,
                out nullableHop,
                out badSegment);
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
            ITypeSymbol sourceType,
            string srcName,
            string tgtName,
            string? useMethod,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            HashSet<string> handledTargets,
            HashSet<string> unflattenRoots,
            Dictionary<string, ITypeSymbol> writableByName,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            EnumPolicy enumPolicy,
            Dictionary<string, SynthesizedMethod> synthesized,
            NullStrategy nullStrategy,
            bool autoNest,
            NestedMappingRegistry? nestedRegistry,
            bool nullAsNull,
            bool isPreserve,
            bool isSetNull,
            bool implicitConversions,
            bool allowNonPublic,
            List<MemberMap> result)
        {
            // Resolve the source (simple or dotted) to its leaf type.
            ITypeSymbol? uSrc;
            if (srcName.IndexOf('.') >= 0)
            {
                if (!TryResolveSourcePath(sourceType,
                        srcName,
                        compilation,
                        allowNonPublic,
                        out uSrc,
                        out _,
                        out var uBad))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathSegmentNotFound,
                        location,
                        $"[MapProperty] source path '{srcName}' has no member '{uBad}'"));
                    return;
                }
            }
            else
            {
                uSrc = ReadableMembers(sourceType, compilation, allowNonPublic)
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
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid,
                    location,
                    $"unflatten target '{tgtName}' must have exactly one intermediate (e.g. \"Address.City\"); deeper paths are not yet supported"));
                return;
            }

            var rootName = segs[0];
            var leafName = segs[1];

            // Conflict only when the root is mapped DIRECTLY; a prior unflatten leaf into the same root is fine.
            if (handledTargets.Contains(rootName) && !unflattenRoots.Contains(rootName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenConflict,
                    location,
                    $"unflatten target '{tgtName}' conflicts with a direct mapping of intermediate '{rootName}'"));
                return;
            }

            if (!writableByName.TryGetValue(rootName, out var rootType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid,
                    location,
                    $"unflatten intermediate '{rootName}' is not a writable destination member"));
                return;
            }

            if (rootType is not INamedTypeSymbol rootNamed ||
                !rootType.IsReferenceType ||
                rootType.TypeKind != TypeKind.Class ||
                !rootNamed.InstanceConstructors.Any(c =>
                    c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length == 0))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid,
                    location,
                    $"unflatten intermediate '{rootName}' (type '{rootType.ToDisplayString()}') must be a class with a public parameterless constructor"));
                return;
            }

            var leafType = WritableMembers(rootType, compilation, allowNonPublic)
                .Where(m => StringComparer.Ordinal.Equals(m.Name, leafName))
                .Select(m => (ITypeSymbol?)m.Type)
                .FirstOrDefault();
            if (leafType is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid,
                    location,
                    $"unflatten intermediate '{rootName}' (type '{rootType.ToDisplayString()}') has no writable member '{leafName}'"));
                return;
            }

            if (TryResolveConversion(compilation,
                    uSrc!,
                    leafType,
                    useMethod,
                    allMethods,
                    autoCandidates,
                    enumPolicy,
                    synthesized,
                    nullStrategy,
                    location,
                    tgtName,
                    diagnostics,
                    out var uConv,
                    out var uNullH,
                    out var uNeedsCtx,
                    out _,
                    autoNest,
                    nestedRegistry,
                    nullAsNull,
                    isPreserve,
                    isSetNull: isSetNull,
                    implicitConversions: implicitConversions))
            {
                var rootFqn = rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                result.Add(new MemberMap(tgtName,
                    srcName,
                    uConv,
                    uNullH,
                    uNeedsCtx,
                    SourceMayBeNullRef(uSrc!),
                    UnflattenIntermediateFqn: rootFqn,
                    // Round 29 T2.9: the unflatten leaf writes its converter's result into a member of the
                    // intermediate, and answers the RETURN question the same way every other member edge does.
                    ConverterReturnIsNullableRef: ForgiveConverterNullableReturn(uConv,
                        leafType,
                        autoCandidates,
                        allMethods,
                        tgtName,
                        location,
                        diagnostics)));
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
                if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapPropertyFqn || attr.ConstructorArguments.Length < 2 || attr.ConstructorArguments[1].Value is not string target)
                {
                    continue;
                }

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

                if (hasNullSub || when is not null)
                {
                    result.Add((target, hasNullSub, nullSub, when, null));
                }
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
            IMethodSymbol method,
            ITypeSymbol srcType,
            INamedTypeSymbol tgtType,
            Compilation compilation,
            bool allowNonPublic,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            List<MemberMap> members)
        {
            // Read through ReadCollectionKeys, which is also what the endpoints that DISCARD this directive
            // report through — so a refusal names exactly the applications this apply path would have acted on.
            foreach (var (collectionMember, keyMember) in ReadCollectionKeys(method))
            {
                // The RAW name, not EmitTargetName: `collectionMember` is the string the consumer wrote in the
                // attribute, so it is `event` and never `@event`. Matching the escaped form made
                // [MapCollectionKey("event", …)] report DWARF074 "is not a mapped destination member" against a
                // member that plainly was one — the mirror image of the emission defect, an escape leaking into
                // a COMPARISON, and reachable only for a keyword-named member (every other name is identity).
                var idx = members.FindIndex(m => StringComparer.Ordinal.Equals(m.TargetName, collectionMember));
                if (idx < 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid,
                        location,
                        $"[MapCollectionKey] target '{collectionMember}' is not a mapped destination member of this update-into method"));
                    continue;
                }

                var tgtMemberType = MemberTypeByName(tgtType, collectionMember);
                var srcMemberType = MemberTypeByName(srcType, members[idx].SourceName);

                if (!IsListOfT(tgtMemberType, out var tgtElem) || !IsListOfT(srcMemberType, out var srcElem))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid,
                        location,
                        $"[MapCollectionKey] member '{collectionMember}' must be a List<T> on both source and destination (v1)"));
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(srcElem, tgtElem))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid,
                        location,
                        $"[MapCollectionKey] member '{collectionMember}' requires the same element type on source and destination (v1); got '{srcElem!.ToDisplayString()}' and '{tgtElem!.ToDisplayString()}'"));
                    continue;
                }

                var keyType = ReadableMembers(tgtElem!, compilation, allowNonPublic)
                    .Where(m => StringComparer.Ordinal.Equals(m.Name, keyMember))
                    .Select(m => (ITypeSymbol?)m.Type)
                    .FirstOrDefault();
                if (keyType is null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionKeyInvalid,
                        location,
                        $"[MapCollectionKey] key '{keyMember}' is not a readable member of element type '{tgtElem!.ToDisplayString()}'"));
                    continue;
                }

                members[idx] = members[idx] with
                {
                    UpsertKeyMember = keyMember,
                    UpsertKeyTypeFqn = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
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
            if (type is INamedTypeSymbol { Name: "List", TypeArguments.Length: 1 } nt && nt.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
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
                if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapPropertyFqn || attr.ConstructorArguments.Length < 2 || attr.ConstructorArguments[1].Value is not string target)
                {
                    continue;
                }

                foreach (var na in attr.NamedArguments)
                    if (na.Key == "StringFormat" && na.Value.Value is string fmt)
                    {
                        result[target] = fmt;
                    }
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
            INamedTypeSymbol classSymbol,
            IMethodSymbol method,
            ITypeSymbol sourceType,
            ITypeSymbol targetType,
            IReadOnlyList<(string Source, string Target, string? Use)> ownExplicit,
            List<DiagnosticInfo> diagnostics,
            LocationInfo? location)
        {
            var added = new List<(string, string, string?)>();
            IMethodSymbol? forward = null;
            foreach (var f in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (!SymbolEqualityComparer.Default.Equals(f, method) &&
                    HasReverseMap(f) &&
                    f.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(
                        f.Parameters[0].Type,
                        targetType) &&
                    SymbolEqualityComparer.Default.Equals(f.ReturnType,
                        sourceType))
                {
                    forward = f;
                    break;
                }

            if (forward is null)
            {
                return added;
            }

            var ownTargets = new HashSet<string>(ownExplicit.Select(e => e.Target), StringComparer.Ordinal);
            var fwdExtraTargets =
                new HashSet<string>(ReadMapPropertyExtras(forward).Select(e => e.Target), StringComparer.Ordinal);
            foreach (var (a, b, use) in ReadExplicitMaps(forward))
            {
                var invertible = use is null && a.IndexOf('.') < 0 && b.IndexOf('.') < 0 && !fwdExtraTargets.Contains(b);
                if (!invertible)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReverseMapNonInvertible,
                        location,
                        $"[ReverseMap]: forward mapping '{a}' → '{b}' cannot be auto-inverted; declare the reverse on '{method.Name}' explicitly"));
                    continue;
                }

                if (!ownTargets.Contains(a))
                {
                    added.Add((b, a, null));
                }
            }

            return added;
        }

        private static List<string> ReadFlattenRoots(ISymbol method)
        {
            var roots = new List<string>();
            foreach (var attr in method.GetAttributes())
                if (attr.AttributeClass?.ToDisplayString() == KnownNames.FlattenFqn && attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string s)
                {
                    roots.Add(s);
                }

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
                if (attr.AttributeClass?.ToDisplayString() == KnownNames.FlattenGraphFqn && attr.ConstructorArguments.Length == 2 && attr.ConstructorArguments[0].Value is string src && attr.ConstructorArguments[1].Value is string tgt)
                {
                    result.Add((src, tgt));
                }

            return result;
        }

        /// <summary>
        ///     Checks whether <paramref name="t" /> is a named generic type with the given
        ///     <paramref name="name" /> and <paramref name="ns" /> with exactly <paramref name="arity" />
        ///     type arguments. Returns the first type argument via <paramref name="firstArg" /> if matched.
        /// </summary>
        private static bool IsExactNamedTypeHelper(
            ITypeSymbol t,
            string name,
            string ns,
            int arity,
            out ITypeSymbol? firstArg)
        {
            firstArg = null;
            if (t is INamedTypeSymbol n && n.Name == name && n.TypeArguments.Length == arity && n.ContainingNamespace?.ToDisplayString() == ns)
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
                EnumPolicy enumPolicy,
                Dictionary<string, SynthesizedMethod> synthesized,
                NullStrategy nullStrategy,
                bool autoNest,
                NestedMappingRegistry? nestedRegistry,
                bool isPreserve,
                bool allowNonPublic,
                HashSet<string> consumedTargets,
                IReadOnlyList<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, bool WrittenGeneric)>? rawDerivedPairs = null)
        {
            var directives = new List<FlattenGraphDirective>();
            var injected = new List<MemberMap>();

            // DWARF087: one destination collection, one directive. Both branches below end in `injected.Add`
            // keyed by tgtCollName, and `injected` is a list — so a second directive naming the same collection
            // used to append a SECOND initializer for it and the emission became `new Dst { Flat = …, Flat = … }`,
            // i.e. CS1912, reported against the generated file the consumer never wrote. The check is here rather
            // than inside either branch precisely because both of them reach that Add, and it keys on the TARGET
            // rather than on the pair, because ("Entry","Nodes") beside ("Other","Nodes") emitted the same CS1912
            // from two directives that are not duplicates of each other.
            //
            // Report-and-skip, like every other per-directive check in this loop: the remaining directives are
            // still validated in the same pass, so a method with two mistakes reports both. The skip is not what
            // makes the build legible — an Error suppresses the whole emission anyway, and the ordinary
            // CS8795/DWARF078 refusal cascade follows exactly as it does for DWARF008 or DWARF011. It is what
            // guarantees the duplicate initializer cannot be emitted at all, including if this id's effective
            // severity is ever configured below Error.
            var seenTargets = new HashSet<string>(StringComparer.Ordinal);

            // Built once: every directive is resolved against the same context, and they all write into the
            // same six collections. Which side a name falls on was measured -- see FlattenGraphRequest for the
            // two parameters that are deliberately not carried.
            var req = new FlattenGraphRequest(sourceType,
                targetType,
                compilation,
                location,
                allMethods,
                autoCandidates,
                enumPolicy,
                nullStrategy,
                autoNest,
                nestedRegistry,
                isPreserve,
                allowNonPublic,
                rawDerivedPairs);
            var acc = new FlattenGraphAccumulators(directives,
                injected,
                diagnostics,
                synthesized,
                consumedTargets,
                seenTargets);

            foreach (var (srcNavName, tgtCollName) in rawDirectives)
            {
                ResolveOneFlattenGraphDirective(req, acc, srcNavName, tgtCollName);
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
            NullHandling nh,
            bool needsBang,
            bool resultNeedsBang = false)
        {
            // Escaped HERE, once, rather than at each of this helper's call sites: `memberName` is a member of
            // the consumer's NODE type and `conv` may be a converter method the consumer declared, and both are
            // written into the synthesized flat-node helper verbatim.
            var access = paramName + "." + Identifiers.EscapePath(memberName);
            conv = conv is null ? null : Identifiers.Escape(conv);
            // Round 29 T2.9: a user-declared converter DECLARED to return a nullable reference, feeding a DTO
            // member that forbids null. Every arm below writes the call, so every arm carries the suppression;
            // it is never true without DWARF107 having been reported for the same leaf.
            var resultBang = resultNeedsBang ? "!" : "";
            if (conv is not null)
            {
                // The null handling must reach the flat-node emitter too: a converter does NOT make it moot
                // (the third site of the same defect — see CollectionConverter.ElementExpr and
                // DictionaryConverter.Expr; TASKS.md I5/I7, round 23 N1/N2). Reaching either lift here needs a
                // nullable-capable DTO member, which the leaf resolver only produces for a nullable leaf — so
                // this is a guard-inheritance fix rather than a measured repro, and it costs nothing to keep
                // all four consumers of NullHandling saying the same thing.
                switch (nh)
                {
                    case NullHandling.NullableProject:
                        sb.Append(access).Append(".HasValue ? ")
                            .Append(conv).Append('(').Append(access).Append(".Value)").Append(resultBang).Append(" : null");
                        return;

                    case NullHandling.NullableProjectRef:
                        sb.Append(access).Append(" is null ? null : ")
                            .Append(conv).Append('(').Append(access).Append(')').Append(resultBang);
                        return;

                    // Same lift, null-forgiven: the destination member's annotation forbids the preserved null,
                    // and the plain form would be CS8601 inside the generated file.
                    case NullHandling.NullableProjectRefForgiving:
                        sb.Append(access).Append(" is null ? null! : ")
                            .Append(conv).Append('(').Append(access).Append(')').Append(resultBang);
                        return;

                    case NullHandling.ThrowIfNull:
                        sb.Append(conv).Append('(').Append(access)
                            .Append(" ?? throw new global::System.InvalidOperationException(\"Source member '")
                            .Append(memberName).Append("' was null\")").Append(')').Append(resultBang);
                        return;

                    case NullHandling.ValueOrDefault:
                        sb.Append(conv).Append('(').Append(access).Append(".GetValueOrDefault())").Append(resultBang);
                        return;
                }

                sb.Append(conv).Append('(').Append(access).Append(needsBang ? "!" : "").Append(')').Append(resultBang);
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
                        // Direct assignment of a nullable-ref leaf into a non-nullable DTO member emits CS8601 from
                        // inside the generated (#nullable enable) file — an unfixable warning that TreatWarningsAsErrors
                        // turns into a hard build break, exactly like the main emitter's NullRefIntoNonNullable path.
                        // Null-forgive it (audit R7). needsBang was computed by the caller from leaf/DTO nullability.
                        sb.Append(access).Append(needsBang ? "!" : "");
                        break;
                }
            }
        }

        /// <summary>
        ///     Whether a flat-node leaf value must be null-forgiven — the flatten-path analogue of the main emitter's
        ///     needsBang. For a DIRECT assignment (<paramref name="conv" /> null): forgive a nullable-ref leaf going
        ///     into a non-nullable DTO member (CS8601). For a CONVERTER: forgive a synthesized converter's argument
        ///     (which null-guards internally), OR a nullable-ref leaf into a USER-declared converter with a
        ///     non-nullable ref parameter (CS8604) — recovering, via <see cref="ConverterParamIsNonNullableRef" />,
        ///     the fact the bare <c>IsSynthesized</c> proxy discarded. A null-tolerant user converter (nullable
        ///     parameter) is not forgiven and keeps its null.
        ///     <para>
        ///         Round 29 T2.9: the user-declared arm FORGAVE and said nothing, which is the one thing the
        ///         coupling contract on <see cref="ForgiveNestedNullableArg" /> forbids — a null the DTO member's
        ///         annotation forbids was silenced inside a <c>__DwarfMap_FlatNode_*</c> helper with no signal
        ///         anywhere. DWARF070 is reported here, on the same annotation-strict gate every other edge uses
        ///         (<see cref="NullRefIntoNonNullableRef" />), so the EMITTED text is unchanged and only the
        ///         signal is added. The <c>IsSynthesized</c> arm keeps its long-standing silence deliberately: that
        ///         helper is null-TOLERANT (<c>if (s is null) return null!;</c>), so the null is preserved rather
        ///         than smuggled past a converter that would reject it — the class-wide exception recorded by
        ///         T2.6/T2.7/T2.8 and carried into this task's report rather than changed underneath it.
        ///     </para>
        /// </summary>
        private static bool FlatLeafNeedsBang(
            string? conv,
            ITypeSymbol leafType,
            ITypeSymbol dtoMemberType,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            string leafName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            if (conv is null)
            {
                // Round 29 T2.9 audit: the DIRECT-assign arm forgave and said nothing too — `Name = n.Name!`
                // for a `string?` leaf into a non-nullable DTO member, since audit R7 (4190ace, 2026-07-25)
                // introduced the '!'.
                // Same shape, same gate and same report as the member path's own raw-assign
                // (MemberMap.NullRefIntoNonNullable / MapperExtractor.Members' DWARF070 loop).
                if (NullRefIntoNonNullableRef(leafType, dtoMemberType))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.NullableRefSourceToNonNullableTarget,
                        location,
                        NullSourceLabel(leafName)));
                    return true;
                }

                return false;
            }

            if (NullRefIntoNonNullableRef(leafType, dtoMemberType) && ConverterParamIsNonNullableRef(conv, autoCandidates, allMethods))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NullableRefSourceToNonNullableTarget,
                    location,
                    NullSourceLabel(leafName)));
            }

            return GeneratedNames.IsSynthesized(conv) || (SourceMayBeNullRef(leafType) && ConverterParamIsNonNullableRef(conv, autoCandidates, allMethods));
        }

        /// <summary>
        ///     The RETURN-side twin of <see cref="FlatLeafNeedsBang" />: whether the flat-node leaf's converter is
        ///     declared to hand back a nullable reference the DTO member cannot hold. Reports DWARF107 when it
        ///     does, through the one shared decision, so the [FlattenGraph] path cannot answer this differently
        ///     from the member and element paths.
        /// </summary>
        private static bool FlatLeafResultNeedsBang(
            string? conv,
            ITypeSymbol dtoMemberType,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            string dtoMemberName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            return ForgiveConverterNullableReturn(conv,
                dtoMemberType,
                autoCandidates,
                allMethods,
                dtoMemberName,
                location,
                diagnostics);
        }
    }
}
