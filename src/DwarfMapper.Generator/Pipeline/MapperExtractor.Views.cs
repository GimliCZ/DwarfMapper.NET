// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     Turns every <c>[GenerateView&lt;S,T&gt;]</c> on the mapper class into a <see cref="ViewModel" />, by
        ///     running the SAME member resolution the create map runs and then re-expressing each resolved member
        ///     as an expression over the source instead of an assignment into a new object.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Runs beside <c>ExtractGenerateMapPairs</c> and BEFORE the nested-mapping drain, because it
        ///         resolves members and a resolution that ran after the drain would queue pairs nothing would
        ///         build.
        ///     </para>
        ///     <para>
        ///         The resolution runs against a THROWAWAY <see cref="NestedMappingRegistry" /> and a throwaway
        ///         helper table, and only the helpers a view's properties actually name are merged back (see
        ///         <see cref="MergeReferencedHelpers" />). A view's resolution reserves helpers it will not call:
        ///         the <c>__DwarfMap_Obj_*</c> object mapper for a member that becomes a nested VIEW, and the
        ///         <c>__DwarfMapColl_*</c> collection helper for one the view hands back as the source collection.
        ///         Left in the mapper's own tables, those become private methods no emitted line reaches, in a
        ///         file the consumer cannot delete — which the first probe of this feature emitted. Helper NAMES
        ///         are a hash of the pair (<c>NestedMappingRegistry.BuildMethodName</c>), not of the registry, so a
        ///         view and a create map over the same pair still agree about what a helper is called, and a
        ///         helper the create map already emitted is reused rather than duplicated.
        ///     </para>
        /// </remarks>
        private static void ExtractViews(
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            Compilation comp,
            LocationInfo? classLoc,
            bool separateEmit)
        {
            var declared = ReadViewDeclarations(decls.ClassSymbol);
            if (declared.Count == 0)
            {
                return;
            }

            // A co-located host's mapping is emitted into a BRAND-NEW `<Host>Mapper` type, and a view is a nested
            // type on the mapper the consumer wrote. Emitting one into a generated type the consumer never named
            // would give them a `ref struct` they cannot reference by any name they chose, so the endpoint is
            // refused rather than half-served. Loud, because the alternative is the silence this attribute's own
            // arrival was rejected for once already.
            if (separateEmit)
            {
                foreach (var v in declared)
                    acc.Diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MemberNotViewable,
                        v.Loc ?? classLoc,
                        $"[GenerateView<{v.Source.ToDisplayString()}, {v.Target.ToDisplayString()}>] sits on '{decls.ClassSymbol.Name}', which is a co-located [GenerateMap] host rather than a [DwarfMapper] class — its mapping is emitted into a separate generated type, so there is no class of yours for the view to be nested in; move the attribute to a [DwarfMapper] partial class",
                        ScopedToMethod: true));

                return;
            }

            // Which mapper method names need `_m.` in front of them inside the view, and which must NOT have it.
            // A nested type reaches its enclosing type's private members, but an instance one still needs an
            // instance (CS0120) and a static one must not be reached through one (CS0176) — both measured.
            var (instanceNames, staticNames) = ClassifyMapperMethodNames(decls.ClassSymbol);

            // Pair -> view type name, so a pair reached twice (two declarations, or a nested member two views
            // share) is emitted once. Keyed on the pair rather than on the name so a NAME collision between two
            // different pairs stays visible as a collision instead of silently merging them.
            var byPair = new Dictionary<(string Src, string Tgt), string>();
            var byName = new Dictionary<string, (string Src, string Tgt)>(StringComparer.Ordinal);
            var built = new List<ViewModel>();
            var factorySources = new Dictionary<string, string>(StringComparer.Ordinal);

            var queue = new Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, LocationInfo? Loc, bool Root)>();

            foreach (var v in declared)
            {
                var name = string.IsNullOrEmpty(v.Name) ? v.Target.Name + "View" : v.Name!;

                if (v.Source.IsValueType)
                {
                    acc.Diagnostics.Add(ViewRefusal(v.Loc ?? classLoc,
                        $"[GenerateView<{v.Source.ToDisplayString()}, {v.Target.ToDisplayString()}>] has a value-type source: a view holds its source in a field, so it would hold a COPY — it would neither be zero-copy nor read through to the source the way the contract says. Use Map for this pair, or make '{v.Source.ToDisplayString()}' a class"));
                    continue;
                }

                // Two views over one source type would both want `public <View> View(TSource)`, which differ only
                // in return type: CS0111, out of a file the consumer cannot edit. The same shape DWARF060 refuses
                // for two [GenerateMap] pairs sharing a source.
                var srcFqn = v.Source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (factorySources.TryGetValue(srcFqn, out var firstTarget))
                {
                    acc.Diagnostics.Add(ViewRefusal(v.Loc ?? classLoc,
                        $"[GenerateView<{v.Source.ToDisplayString()}, {v.Target.ToDisplayString()}>] and the view to '{firstTarget}' share the source type '{v.Source.ToDisplayString()}', so both would emit `View({v.Source.ToDisplayString()})` and the two would differ only in return type (CS0111). Declare one of them on a second [DwarfMapper] class"));
                    continue;
                }

                factorySources[srcFqn] = v.Target.ToDisplayString();
                queue.Enqueue((v.Source, v.Target, name, v.Loc ?? classLoc, true));
            }

            // A registry AND a helper table of its own — see the remarks above. Both are throwaway because a
            // view's resolution reserves helpers it will not call: the object mapper for a member that becomes a
            // nested view, and the collection helper for one the view hands back as the source collection.
            // Measured: without this the first probe emitted a private __DwarfMapColl_* nothing referenced.
            // Only the helpers a view's property actually NAMES are merged back, by MergeReferencedHelpers.
            var viewRegistry = new NestedMappingRegistry();
            viewRegistry.SetPairCustomizationRule((s, t) => DescribeElementPairCustomization(decls, comp, s, t));
            var viewSynthesized = new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal);

            while (queue.Count > 0)
            {
                var (src, tgt, name, loc, root) = queue.Dequeue();
                var key = (src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    tgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                if (byPair.ContainsKey(key))
                {
                    continue; // already built — this is also what makes a CYCLIC view terminate (see below)
                }

                if (byName.TryGetValue(name, out var owner))
                {
                    acc.Diagnostics.Add(ViewRefusal(loc,
                        $"two views on this mapper would both be named '{name}': {key.Item1} -> {key.Item2} and {owner.Src} -> {owner.Tgt}. Give one of them [GenerateView(Name = \"...\")]"));
                    continue;
                }

                byPair[key] = name;
                byName[name] = key;

                var model = BuildView(ctx, decls, policy, acc, comp, viewRegistry, viewSynthesized, src, tgt, name,
                    loc, root, instanceNames, staticNames, queue);
                if (model is not null)
                {
                    built.Add(model);
                }
            }

            foreach (var view in built) MergeReferencedHelpers(view, viewSynthesized, acc.Synthesized);

            acc.Views.AddRange(built);
        }

        /// <summary>
        ///     Resolves one <c>(source, target)</c> pair into a view, enqueueing every nested pair it reaches.
        /// </summary>
        /// <remarks>
        ///     A view that reaches ITSELF — <c>Node -&gt; NodeDto</c> whose <c>Next</c> is another
        ///     <c>Node -&gt; NodeDto</c> — needs no depth guard and no refusal, which is the one place this
        ///     implementation departs from the plan's text (it specified "a cycle is DWARF102 cyclic view").
        ///     Measured: a <c>ref struct</c> whose PROPERTY returns its own type compiles and runs, because the
        ///     property is an expression rather than a field, and the dequeue-if-already-built check above makes
        ///     the type set finite. Refusing it would refuse the shape the feature is best at — walking a linked
        ///     structure without materialising it.
        /// </remarks>
        private static ViewModel? BuildView(
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            Compilation comp,
            NestedMappingRegistry viewRegistry,
            Dictionary<string, SynthesizedMethod> viewSynthesized,
            ITypeSymbol src,
            INamedTypeSymbol tgt,
            string viewTypeName,
            LocationInfo? loc,
            bool root,
            HashSet<string> instanceNames,
            HashSet<string> staticNames,
            Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, LocationInfo? Loc, bool Root)> queue)
        {
            // A collection, dictionary, enum or primitive target has no members to view — the create map treats
            // it as a VALUE to convert, and converting is what a view does not do.
            if (CollectionConverter.TryResolve(tgt, tgt, out _, out _, out _) ||
                DictionaryConverter.TryResolve(tgt, tgt, out _, out _, out _, out _, out _) ||
                tgt.TypeKind == TypeKind.Enum ||
                tgt.SpecialType != SpecialType.None)
            {
                acc.Diagnostics.Add(ViewRefusal(loc,
                    $"[GenerateView] cannot view '{tgt.ToDisplayString()}': it is a collection, dictionary or scalar, which a map CONVERTS rather than constructs member by member — there is nothing to expose as properties. Use Map for this pair"));
                return null;
            }

            var (explicitMaps, extras) = MatchPairProps(decls.PairProps, src, tgt);
            var ignores = new HashSet<string>(decls.ClassIgnores, IgnoreNameComparer);
            foreach (var im in MatchPairIgnores(decls.PairIgnores, tgt)) ignores.Add(im);

            // The view's own diagnostics, deduplicated against what the class has already said. A pair that
            // carries BOTH [GenerateMap<S,T>] and [GenerateView<S,T>] — the natural combination — resolves twice,
            // and every hint the resolution raises (DWARF038/070/100/103, …) would otherwise be printed twice
            // for one fact. Matched on id + message rather than on the whole record, because the two copies
            // legitimately carry different locations.
            var viewDiagnostics = new List<DiagnosticInfo>();

            var members = ResolveMembers(
                src,
                tgt,
                ignores,
                comp,
                loc,
                viewDiagnostics,
                new MapperOptions(
                    CaseInsensitive: policy.CaseInsensitive,
                    AutoNest: policy.ClassAutoNest,
                    NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                    IsPreserve: policy.IsPreserveMode,
                    IsSetNull: policy.IsSetNullMode,
                    ImplicitConversions: policy.ImplicitConversions,
                    NameConvention: 0,
                    SkipNullSourceMembers: ResolveNullSkip(decls.PairNullSkips, null, src, tgt, policy.SkipNullSrc),
                    AllowNonPublic: policy.AllowNonPublic,
                    ExplicitOnly: policy.ExplicitOnly,
                    IgnoreObsolete: policy.IgnoreObsolete),
                explicitMaps,
                decls.AllMethods,
                decls.MapperMethods,
                policy.EnumPolicy,
                viewSynthesized,
                policy.NullStrategy,
                Array.Empty<string>(),
                new List<string>(),
                // A view constructs nothing, so no constructor consumes a member and `required` constrains
                // nothing: every destination member is answered by a property, or by DWARF001.
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                viewRegistry,
                MatchPairValues(decls.PairValues, tgt),
                decls.ValueProviders,
                mapPropertyExtras: extras,
                mapperReservedConverters: decls.MapperReservedConverters,
                requiredMembersAlreadySatisfied: true,
                ignoredSourceMembers: ClassIgnoredSources(decls));

            foreach (var d in viewDiagnostics)
                if (!acc.Diagnostics.Exists(e => ReferenceEquals(e.Descriptor, d.Descriptor) && string.Equals(e.MessageArg, d.MessageArg, StringComparison.Ordinal) && string.Equals(e.MessageArg2, d.MessageArg2, StringComparison.Ordinal)))
                {
                    acc.Diagnostics.Add(d);
                }

            var viewMembers = new List<ViewMemberModel>();
            var needsOwner = false;

            foreach (var member in members)
            {
                // An UNFLATTEN member's target name is a PATH ("Address.City"): the create map assigns it in two
                // steps after constructing the intermediate. A view has no construction moment and no such
                // property name to declare.
                if (member.UnflattenIntermediateFqn is not null || member.TargetName.IndexOf('.') >= 0)
                {
                    acc.Diagnostics.Add(ViewRefusal(loc,
                        $"destination member '{member.TargetName}' on '{tgt.ToDisplayString()}' is mapped through an UNFLATTEN path, which the create map assigns by building the intermediate object first — a view builds nothing, and '{member.TargetName}' is not a property name it could declare. Use Map for this pair"));
                    return null;
                }

                var targetType = FindMemberType(tgt, member.TargetName, policy.AllowNonPublic);
                if (targetType is null)
                {
                    // The resolver matched a destination member this lookup cannot see. Refusing is the only
                    // honest answer: without its declared type there is no property signature to write.
                    acc.Diagnostics.Add(ViewRefusal(loc,
                        $"[GenerateView] cannot determine the declared type of destination member '{member.TargetName}' on '{tgt.ToDisplayString()}', so it cannot give the view a property for it. Use Map for this pair"));
                    return null;
                }

                // A collection or dictionary member. If the source member is assignable to the destination as it
                // stands, the view hands back the SOURCE collection — that is the whole zero-copy claim, and it
                // is also a real change of meaning against Map, which copies. If it is not assignable, the
                // conversion would have to allocate, and the view refuses.
                if (GeneratedNames.IsComplexHelper(member.ConverterMethod) && !GeneratedNames.IsObjectMap(member.ConverterMethod))
                {
                    var sourceType = FindMemberType(src, member.SourceName, policy.AllowNonPublic);
                    if (sourceType is null || !HasImplicitConversion(comp, sourceType, targetType))
                    {
                        acc.Diagnostics.Add(ViewRefusal(loc,
                            $"destination member '{member.TargetName}' on '{tgt.ToDisplayString()}' is a collection whose ELEMENTS need converting, and converting them would mean allocating the converted collection — which is the one thing a view does not do. Use Map for this member's pair, or map the element type to itself so the source collection can be handed back as it is"));
                        return null;
                    }

                    viewMembers.Add(new ViewMemberModel(member.EmitTargetName,
                        Render(targetType),
                        new MemberMap(member.TargetName, member.SourceName)));
                    continue;
                }

                // A nested object member becomes a nested VIEW rather than a call to the object helper: the
                // helper allocates the nested destination, which is exactly what is being avoided.
                if (GeneratedNames.IsObjectMap(member.ConverterMethod))
                {
                    var sourceType = FindMemberType(src, member.SourceName, policy.AllowNonPublic);
                    if (sourceType is null || targetType is not INamedTypeSymbol nestedTarget || sourceType.IsValueType)
                    {
                        acc.Diagnostics.Add(ViewRefusal(loc,
                            $"destination member '{member.TargetName}' on '{tgt.ToDisplayString()}' maps a nested object whose SOURCE is a value type (or whose types cannot be resolved): a nested view would hold a copy of it rather than read through to it. Use Map for this member's pair"));
                        return null;
                    }

                    var nestedName = nestedTarget.Name + "View";
                    queue.Enqueue((sourceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated), nestedTarget, nestedName, loc, false));
                    viewMembers.Add(new ViewMemberModel(member.EmitTargetName,
                        nestedName,
                        NestedViewTypeName: nestedName,
                        NestedSourceMember: member.EmitSourceName,
                        NestedSourceIsNullable: member.SourceIsNullableRef));
                    continue;
                }

                if (!TryQualify(member, decls, instanceNames, staticNames, out var qualified, out var ambiguous))
                {
                    acc.Diagnostics.Add(ViewRefusal(loc,
                        $"destination member '{member.TargetName}' on '{tgt.ToDisplayString()}' is mapped through '{ambiguous}', which this mapper declares BOTH as a static and as an instance method. A view is a nested type: it must write a static call unqualified and an instance call through the mapper, and with both present it cannot tell which one the mapping picked. Rename one of the overloads"));
                    return null;
                }

                needsOwner |= !ReferenceEquals(qualified, member);
                viewMembers.Add(new ViewMemberModel(member.EmitTargetName, Render(targetType), qualified));
            }

            _ = ctx;
            return new ViewModel(viewTypeName,
                src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                EquatableArray.From(viewMembers),
                needsOwner,
                root);
        }

        /// <summary>
        ///     Copies into the mapper's real helper table exactly those synthesized helpers a view's properties
        ///     NAME, and no others.
        /// </summary>
        /// <remarks>
        ///     The resolution a view runs reserves helpers it then does not call — the object mapper for a member
        ///     that becomes a nested view, the collection helper for one handed back as the source collection.
        ///     Emitting those would put private methods no emitted line reaches into a file the consumer cannot
        ///     delete, which the first probe of this feature did. The helpers that survive are the scalar ones
        ///     (<c>__DwarfMap_Num_</c>, <c>_EnumStr_</c>, <c>_StrEnum_</c>, <c>_UserConv_</c>, the parse/format
        ///     pair): each is self-contained and calls no other helper, which is what makes copying them one at a
        ///     time correct rather than a transitive walk waiting to be wrong.
        /// </remarks>
        private static void MergeReferencedHelpers(
            ViewModel view,
            Dictionary<string, SynthesizedMethod> from,
            Dictionary<string, SynthesizedMethod> into)
        {
            foreach (var member in view.Members)
            {
                var converter = member.Value?.ConverterMethod;
                if (converter is null)
                {
                    continue;
                }

                // The owner qualification the view may have added is not part of the helper's NAME. Synthesized
                // helpers are never owner-qualified (they are static), but stripping keeps this readable as
                // "the method this property calls" rather than as an assumption about which ones can be.
                var name = converter.StartsWith("_m.", StringComparison.Ordinal) ? converter.Substring(3) : converter;
                if (!into.ContainsKey(name) && from.TryGetValue(name, out var helper))
                {
                    into[name] = helper;
                }
            }
        }

        /// <summary>
        ///     Rewrites the three strings a view's property body can name a MAPPER METHOD through — the converter,
        ///     a <c>[MapValue(Use = …)]</c> provider, and a <c>When =</c> predicate — so an instance one is
        ///     reached through <c>_m.</c> and a static one is not.
        /// </summary>
        /// <returns>
        ///     <c>false</c> when one of those names is declared both statically and as an instance method, which
        ///     no single spelling can satisfy; <paramref name="ambiguous" /> then names it.
        /// </returns>
        private static bool TryQualify(
            MemberMap member,
            MapperDeclarations decls,
            HashSet<string> instanceNames,
            HashSet<string> staticNames,
            out MemberMap result,
            out string? ambiguous)
        {
            ambiguous = null;
            result = member;

            string? converter = member.ConverterMethod;
            string? valueExpression = member.ValueExpression;
            string? whenPredicate = member.WhenPredicate;
            var changed = false;

            if (converter is not null && !GeneratedNames.IsAnySynthesized(converter))
            {
                switch (Decide(converter))
                {
                    case null:
                        ambiguous = converter;
                        return false;
                    case true:
                        converter = "_m." + converter;
                        changed = true;
                        break;
                }
            }

            // [MapValue(Use = nameof(Now))] is carried as the literal expression `Now()`; a CONSTANT [MapValue]
            // is a literal and names nothing. Matched against the declared providers rather than by parsing the
            // string, so a constant that happens to look like a call cannot be rewritten.
            if (valueExpression is not null)
            {
                foreach (var (providerName, _) in decls.ValueProviders)
                {
                    if (!string.Equals(valueExpression, providerName + "()", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    switch (Decide(providerName))
                    {
                        case null:
                            ambiguous = providerName;
                            return false;
                        case true:
                            valueExpression = "_m." + valueExpression;
                            changed = true;
                            break;
                    }

                    break;
                }
            }

            if (whenPredicate is not null)
            {
                switch (Decide(whenPredicate))
                {
                    case null:
                        ambiguous = whenPredicate;
                        return false;
                    case true:
                        whenPredicate = "_m." + whenPredicate;
                        changed = true;
                        break;
                }
            }

            if (changed)
            {
                result = member with
                {
                    ConverterMethod = converter, ValueExpression = valueExpression, WhenPredicate = whenPredicate
                };
            }

            return true;

            // true = instance (qualify), false = static or not a mapper method (leave alone), null = both.
            bool? Decide(string name)
            {
                var inst = instanceNames.Contains(name);
                var stat = staticNames.Contains(name);
                return inst && stat ? null : inst;
            }
        }

        /// <summary>Mapper method names that have an instance declaration, and those that have a static one.</summary>
        private static (HashSet<string> Instance, HashSet<string> Static) ClassifyMapperMethodNames(
            INamedTypeSymbol classSymbol)
        {
            var instance = new HashSet<string>(StringComparer.Ordinal);
            var statics = new HashSet<string>(StringComparer.Ordinal);
            for (INamedTypeSymbol? t = classSymbol; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
                {
                    if (m.MethodKind != MethodKind.Ordinary)
                    {
                        continue;
                    }

                    _ = m.IsStatic ? statics.Add(m.Name) : instance.Add(m.Name);
                }

            return (instance, statics);
        }

        /// <summary>The declared type of <paramref name="name" /> on <paramref name="type" /> or a base of it.</summary>
        private static ITypeSymbol? FindMemberType(ITypeSymbol type, string name, bool allowNonPublic)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var m in t.GetMembers())
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!allowNonPublic && m.DeclaredAccessibility != Accessibility.Public)
                    {
                        continue;
                    }

                    switch (m)
                    {
                        case IPropertySymbol { IsIndexer: false } p:
                            return p.Type;
                        case IFieldSymbol { IsImplicitlyDeclared: false } f:
                            return f.Type;
                    }
                }

            return null;
        }

        /// <summary>
        ///     A member type as it must be written in a view's property signature — <c>global::</c>-rooted AND
        ///     carrying its nullable annotation. Dropping the <c>?</c> would put a CS8603 inside a <c>.g.cs</c>,
        ///     which no consumer can suppress.
        /// </summary>
        private static string Render(ITypeSymbol type)
        {
            return type.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat);
        }

        private static DiagnosticInfo ViewRefusal(LocationInfo? loc, string message)
        {
            // Scoped to the view, so a member that cannot be viewed drops the VIEW and leaves the Map methods
            // beside it standing — the boundary DWARF028/DWARF096 established for an untranslatable projection.
            return new DiagnosticInfo(DiagnosticDescriptors.MemberNotViewable, loc, message, ScopedToMethod: true);
        }

        /// <summary>Every <c>[GenerateView&lt;S,T&gt;]</c> on the mapper class, in declaration order.</summary>
        private static List<(ITypeSymbol Source, INamedTypeSymbol Target, string? Name, LocationInfo? Loc)>
            ReadViewDeclarations(INamedTypeSymbol classSymbol)
        {
            var result = new List<(ITypeSymbol, INamedTypeSymbol, string?, LocationInfo?)>();
            foreach (var attr in classSymbol.GetAttributes())
            {
                if (attr.AttributeClass is not { Name: KnownNames.GenerateView } ac ||
                    ac.TypeArguments.Length != 2 ||
                    ac.ContainingNamespace?.ToDisplayString() != KnownNames.Ns ||
                    ac.TypeArguments[1] is not INamedTypeSymbol target)
                {
                    continue;
                }

                string? name = null;
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Name" && na.Value.Value is string n && n.Length > 0)
                    {
                        name = n;
                    }

                result.Add((ac.TypeArguments[0],
                    target,
                    name,
                    LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None)));
            }

            return result;
        }
    }
}
