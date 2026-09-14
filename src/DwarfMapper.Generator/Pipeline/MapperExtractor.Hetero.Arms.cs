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
        ///     Validates and prepares one arm per declared <c>[MapDerivedType]</c> pair: each concrete node type
        ///     gets its own flat-node helper, edge members and leaf members.
        /// </summary>
        /// <remarks>
        ///     <paramref name="anyArmError" /> is <c>ref</c> rather than a return value because a rejected arm does
        ///     NOT stop the others -- every pair is still validated so a directive with two bad arms reports both,
        ///     the same report-and-skip discipline the directive loop follows. The caller reads the flag once, after
        ///     all arms have had their turn.
        /// </remarks>
        private static void ResolveDerivedTypeArms(
            FlattenGraphRequest req,
            FlattenGraphAccumulators acc,
            FlattenNavShape nav,
            IReadOnlyList<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, bool WrittenGeneric)> effectiveDerivedPairs,
            List<(INamedTypeSymbol NodeDerived, INamedTypeSymbol DtoDerived, string FlatNodeHelperName,
                List<(string Name, bool IsCollection, bool IsDictValue)> EdgeMembers,
                List<(string Name, ITypeSymbol Type)> LeafMembers)> heteroArms,
            HashSet<string> seenSrcFqns,
            ref bool anyArmError)
        {
            foreach (var (derivedSrc, derivedTgt, _) in effectiveDerivedPairs)
            {
                var srcFqn = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var tgtFqnArm = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // 1. derivedSrc must be assignable to nav.NodeType (the node base)
                if (!HasImplicitConversion(req.Compilation, derivedSrc, nav.NodeType))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        req.Location,
                        $"[MapDerivedType] source type '{srcFqn}' is not assignable to node base type " +
                        $"'{nav.NodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                        $"each derived node type must inherit from or implement the node base."));
                    anyArmError = true;
                    continue;
                }

                // 2. derivedTgt must be assignable to nav.NodeDtoType (the base DTO = collection element)
                if (!HasImplicitConversion(req.Compilation, derivedTgt, nav.NodeDtoType))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        req.Location,
                        $"[MapDerivedType] target type '{tgtFqnArm}' is not assignable to target collection " +
                        $"element type '{nav.NodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                        $"each derived DTO type must inherit from or implement the base DTO."));
                    anyArmError = true;
                    continue;
                }

                // 3. No duplicate derived source types
                if (!seenSrcFqns.Add(srcFqn))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        req.Location,
                        $"[MapDerivedType] duplicate derived source type '{srcFqn}'; each concrete node type may only be registered once."));
                    anyArmError = true;
                    continue;
                }

                // 4. Compute EDGE members for this concrete derived type.
                // An EDGE member is any readable member whose type is assignable to nav.NodeType, or a
                // collection thereof (including inherited base edges).
                // SF-F3 fix: also detect Dictionary<K,V> where V is assignable to nav.NodeType.
                var derivedEdgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue)>();
                var derivedLeafMembers = new List<(string Name, ITypeSymbol Type)>();

                foreach (var nm in ReadableMembers(derivedSrc, req.Compilation, req.AllowNonPublic))
                {
                    var memberTypeNoAnnot = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                    // Single-ref edge: type assignable to nodeBase (includes exact type and subtypes). A Nullable<T>
                    // member lands here too: C# boxes T? implicitly to every interface and base class T converts to.
                    if (HasImplicitConversion(req.Compilation, memberTypeNoAnnot, nav.NodeType))
                    {
                        derivedEdgeMembers.Add((nm.Name, false, false));
                        continue;
                    }

                    // SF-F3: Dictionary<K,V> where V is assignable to nodeBase → dict-value edge.
                    if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValTypeD) &&
                        HasImplicitConversion(req.Compilation,
                            dictValTypeD.WithNullableAnnotation(NullableAnnotation.None),
                            nav.NodeType))
                    {
                        derivedEdgeMembers.Add((nm.Name, false, true));
                        continue;
                    }

                    // Collection edge: element type assignable to nodeBase
                    var isEdgeColl = false;
                    if (nm.Type is IArrayTypeSymbol arrEdge &&
                        arrEdge.Rank == 1 &&
                        HasImplicitConversion(req.Compilation,
                            arrEdge.ElementType.WithNullableAnnotation(
                                NullableAnnotation.None),
                            nav.NodeType))
                    {
                        isEdgeColl = true;
                    }
                    else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _) &&
                             HasImplicitConversion(req.Compilation,
                                 edgeElem.WithNullableAnnotation(NullableAnnotation.None),
                                 nav.NodeType))
                    {
                        isEdgeColl = true;
                    }

                    if (isEdgeColl)
                    {
                        derivedEdgeMembers.Add((nm.Name, true, false));
                    }
                    else
                    {
                        derivedLeafMembers.Add((nm.Name, nm.Type));
                    }
                }

                // 5. Build the __DwarfMap_FlatNode_<TypeName>_<hash> helper for this concrete type
                var armHashKey = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "=>" + derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "@FG";
                var armHash = StableHash.Fnv1a(armHashKey);
                var typeName = derivedSrc.Name;
                var perTypeHelperName = GeneratedNames.FlatNode + typeName + "_" + armHash;

                if (!acc.Synthesized.ContainsKey(perTypeHelperName))
                {
                    var nodeFqDerived = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoFqDerived = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoWritableDerived = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
                    foreach (var wm in WritableMembers(derivedTgt, req.Compilation, req.AllowNonPublic))
                        dtoWritableDerived[wm.Name] = wm.Type;

                    var sbArm = new StringBuilder();
                    sbArm.Append("    private ").Append(dtoFqDerived).Append(' ').Append(perTypeHelperName)
                        .Append('(').Append(nodeFqDerived).AppendLine(" n)");
                    sbArm.AppendLine("    {");
                    sbArm.Append("        return new ").Append(dtoFqDerived).AppendLine();
                    sbArm.AppendLine("        {");

                    // Leaf members: map with conversion where available.
                    // MF-D fix: use a throw-away synth dict; complex converters are skipped only under Preserve.
                    // SF-LEAFDIAG fix: propagate unmappable-leaf errors to real acc.Diagnostics.
                    foreach (var leaf in derivedLeafMembers)
                    {
                        if (!dtoWritableDerived.TryGetValue(leaf.Name, out var dtoMemberType))
                        {
                            continue;
                        }

                        var leafThrowAwaySynth = new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal);
                        var leafTestDiags = new List<DiagnosticInfo>();
                        var leafResolved = TryResolveConversion(req.Compilation,
                            leaf.Type,
                            dtoMemberType,
                            null,
                            req.AllMethods,
                            req.AutoCandidates,
                            req.EnumPolicy,
                            leafThrowAwaySynth,
                            req.NullStrategy,
                            req.Location,
                            leaf.Name,
                            leafTestDiags,
                            out var leafConv,
                            out var leafNull,
                            out _,
                            out _,
                            req.AutoNest,
                            req.NestedRegistry);
                        if (!leafResolved)
                        {
                            acc.Diagnostics.AddRange(leafTestDiags);
                            continue;
                        }

                        // MF-D / ISSUE-001: the same decision as the homogeneous loop. This used to skip every
                        // complex leaf in every mode, silently; now only Preserve skips it, and says so (DWARF075).
                        if (FlatLeafBlockedUnderPreserve(req, acc, leaf.Name, leaf.Type, leafConv))
                        {
                            continue;
                        }

                        foreach (var kv in leafThrowAwaySynth)
                            if (!acc.Synthesized.ContainsKey(kv.Key))
                            {
                                acc.Synthesized[kv.Key] = kv.Value;
                            }

                        sbArm.Append("            ").Append(Identifiers.Escape(leaf.Name)).Append(" = ");
                        AppendFlatNodeMemberExpr(sbArm,
                            "n",
                            leaf.Name,
                            leafConv,
                            leafNull,
                            FlatLeafNeedsBang(leafConv, leaf.Type, dtoMemberType, req.AutoCandidates, req.AllMethods, leaf.Name, req.Location, acc.Diagnostics),
                        FlatLeafResultNeedsBang(leafConv, dtoMemberType, req.AutoCandidates, req.AllMethods, leaf.Name, req.Location, acc.Diagnostics));
                        sbArm.AppendLine(",");
                    }

                    // Edge members on derived DTO: null them (topology degradation)
                    foreach (var edge in derivedEdgeMembers)
                    {
                        if (!dtoWritableDerived.ContainsKey(edge.Name))
                        {
                            continue;
                        }

                        sbArm.Append("            ").Append(Identifiers.Escape(edge.Name)).AppendLine(" = null,");
                    }

                    sbArm.AppendLine("        };");
                    sbArm.AppendLine("    }");
                    acc.Synthesized[perTypeHelperName] = new SynthesizedMethod(perTypeHelperName, sbArm.ToString());
                }

                heteroArms.Add((derivedSrc, derivedTgt, perTypeHelperName, derivedEdgeMembers, derivedLeafMembers));
            }
        }

        /// <summary>
        ///     Whether a flat-node data leaf resolved to <paramref name="leafConv" /> must be left out of the helper —
        ///     and when it must, reports <c>DWARF075</c> so it is not left out in silence. The ONE decision for both
        ///     leaf loops: the homogeneous flat-node helper and the helper built for each <c>[MapDerivedType]</c> arm.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         A COMPLEX synthesized helper (<c>__DwarfMap_Obj_</c>, <c>_Coll_</c>, <c>_Dict_</c>) may be
        ///         force-marked recursion-capable (3-param) by the Preserve post-processing, and would then mismatch
        ///         the one-argument call a flat-node helper emits (MF-D, CS7036). That force-marking is guarded by
        ///         Preserve, so the hazard exists only there. Numeric, enum and parsable helpers are always single-arg.
        ///         Outside Preserve the leaf is flattened; the caller's merge registers the helper so the call resolves.
        ///     </para>
        ///     <para>
        ///         3ade4ee (ISSUE-001) made that split in the homogeneous loop and did not touch the derived-arm loop,
        ///         which went on skipping every complex leaf in every mode with no diagnostic. One helper, so a third
        ///         leaf loop cannot repeat that.
        ///     </para>
        /// </remarks>
        private static bool FlatLeafBlockedUnderPreserve(
            FlattenGraphRequest req,
            FlattenGraphAccumulators acc,
            string leafName,
            ITypeSymbol leafType,
            string? leafConv)
        {
            if (!req.IsPreserve || !GeneratedNames.IsComplexHelper(leafConv))
            {
                return false;
            }

            acc.Diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.FlattenGraphLeafNotFlattened,
                req.Location,
                $"[FlattenGraph] cannot flatten member '{leafName}' of type " + $"'{leafType.ToDisplayString()}' under ReferenceHandling = Preserve; it is left " + "at the destination's default. Map the member explicitly, or use " + "ReferenceHandling = None for this mapper."));
            return true;
        }
    }
}
