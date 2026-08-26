// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
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
        ///     The HETEROGENEOUS <c>[FlattenGraph]</c> path: an abstract or interface node base, or one with
        ///     declared <c>[MapDerivedType]</c> arms, where each concrete node type needs its own traversal.
        /// </summary>
        /// <remarks>
        ///     Entering this path always ENDS the directive -- the branch it came from finished with an
        ///     unconditional <c>return</c>, so there is no falling through to the homogeneous path below it. That
        ///     is why the caller returns immediately after the call rather than testing a result: reporting a
        ///     verdict nobody could act on would have invented a state the original did not have.
        /// </remarks>
        private static void ResolveHeterogeneousFlattenGraph(
            FlattenGraphRequest req,
            FlattenGraphAccumulators acc,
            FlattenNavShape nav,
            string srcNavName,
            string tgtCollName,
            bool nodeIsAbstractOrInterface,
            IReadOnlyList<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, bool WrittenGeneric)> effectiveDerivedPairs)
        {
            // Validate: abstract/interface node base requires at least one [MapDerivedType]
            if (nodeIsAbstractOrInterface && effectiveDerivedPairs.Count == 0)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                    req.Location,
                    $"[FlattenGraph] node base type '{nav.NodeType.Name}' is abstract or an interface; " +
                    $"add [MapDerivedType<TNodeDerived, TNodeDerivedDto>] for each concrete node type."));
                return;
            }

            // Validate arms and collect resolved arms
            var heteroArms = new List<(INamedTypeSymbol NodeDerived, INamedTypeSymbol DtoDerived,
                string FlatNodeHelperName,
                List<(string Name, bool IsCollection, bool IsDictValue)> EdgeMembers,
                List<(string Name, ITypeSymbol Type)> LeafMembers)>();
            var seenSrcFqns = new HashSet<string>(StringComparer.Ordinal);
            var anyArmError = false;

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
                var nodeBaseNoAnnot = nav.NodeType.WithNullableAnnotation(NullableAnnotation.None);
                var derivedEdgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue)>();
                var derivedLeafMembers = new List<(string Name, ITypeSymbol Type)>();

                foreach (var nm in ReadableMembers(derivedSrc, req.Compilation, req.AllowNonPublic))
                {
                    var memberTypeNoAnnot = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                    // Single-ref edge: type assignable to nodeBase (includes exact type and subtypes)
                    if (HasImplicitConversion(req.Compilation, memberTypeNoAnnot, nav.NodeType))
                    {
                        derivedEdgeMembers.Add((nm.Name, false, false));
                        continue;
                    }

                    // Nullable<T> where T is assignable to nodeBase
                    if (nm.Type is INamedTypeSymbol nmNamed &&
                        nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                        HasImplicitConversion(req.Compilation,
                            nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None),
                            nav.NodeType))
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
                    // MF-D fix: use throw-away synth dict + skip complex acc.Synthesized converters.
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
                            req.AutoNest,
                            req.NestedRegistry);
                        if (!leafResolved)
                        {
                            acc.Diagnostics.AddRange(leafTestDiags);
                            continue;
                        }

                        // MF-D: skip only COMPLEX helpers (Obj/Coll/Dict) that may become 3-param.
                        // Numeric/enum/parsable helpers are always single-arg and are safe.
                        if (GeneratedNames.IsComplexHelper(leafConv))
                        {
                            continue; // complex acc.Synthesized helper — skip (topology degradation)
                        }

                        foreach (var kv in leafThrowAwaySynth)
                            if (!acc.Synthesized.ContainsKey(kv.Key))
                            {
                                acc.Synthesized[kv.Key] = kv.Value;
                            }

                        sbArm.Append("            ").Append(leaf.Name).Append(" = ");
                        AppendFlatNodeMemberExpr(sbArm,
                            "n",
                            leaf.Name,
                            leafConv,
                            leafNull,
                            FlatLeafNeedsBang(leafConv, leaf.Type, dtoMemberType, req.AutoCandidates, req.AllMethods));
                        sbArm.AppendLine(",");
                    }

                    // Edge members on derived DTO: null them (topology degradation)
                    foreach (var edge in derivedEdgeMembers)
                    {
                        if (!dtoWritableDerived.ContainsKey(edge.Name))
                        {
                            continue;
                        }

                        sbArm.Append("            ").Append(edge.Name).AppendLine(" = null,");
                    }

                    sbArm.AppendLine("        };");
                    sbArm.AppendLine("    }");
                    acc.Synthesized[perTypeHelperName] = new SynthesizedMethod(perTypeHelperName, sbArm.ToString());
                }

                heteroArms.Add((derivedSrc, derivedTgt, perTypeHelperName, derivedEdgeMembers, derivedLeafMembers));
            }

            if (anyArmError && heteroArms.Count == 0)
            {
                return; // all arms had errors; skip this directive
            }

            // Sort arms most-derived-first (reuse Plan-21 sort)
            var sortedHeteroArms = heteroArms
                .Select((arm, idx) => (arm, idx, depth: InheritanceDepth(arm.NodeDerived)))
                .OrderByDescending(x => x.depth)
                .ThenBy(x => x.idx)
                .Select(x => x.arm)
                .ToList();

            // Build dispatch helper name and traversal helper name from the hetero hash
            var heteroHashKey = nav.NodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "=>" + nav.NodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "@Hetero";
            var heteroHash = StableHash.Fnv1a(heteroHashKey);

            var dispatchHelperName = GeneratedNames.FlatNodeDispatch + heteroHash;
            var traversalHelperNameH = GeneratedNames.FlattenGraph + heteroHash;

            // Synthesize __DwarfMap_FlatNodeDispatch_<hash>(TBase n) => n switch { ... }
            if (!acc.Synthesized.ContainsKey(dispatchHelperName))
            {
                var nodeBaseFq = nav.NodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoBaseFq = nav.NodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
                acc.Synthesized[dispatchHelperName] = new SynthesizedMethod(dispatchHelperName, sbDisp.ToString());
            }

            // Synthesize __DwarfMap_FlattenGraph_<hash>(TBase entry) BFS traversal
            if (!acc.Synthesized.ContainsKey(traversalHelperNameH))
            {
                var nodeBaseFq = nav.NodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoBaseFq = nav.NodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var listFqH = "global::System.Collections.Generic.List<" + dtoBaseFq + ">";
                var queueFqH = "global::System.Collections.Generic.Queue<" + nodeBaseFq + ">";
                var hashSetFqH = "global::System.Collections.Generic.HashSet<object>";

                var sbBfs = new StringBuilder();
                sbBfs.Append("    private ").Append(listFqH).Append(' ').Append(traversalHelperNameH)
                    .Append('(');
                // MF-B: use the correct parameter type for the entry parameter.
                if (nav.SrcNavIsArray)
                {
                    sbBfs.Append(nodeBaseFq).AppendLine("[]? entry)");
                }
                else if (nav.SrcNavIsDict)
                {
                    sbBfs.Append(nav.SrcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .AppendLine("? entry)");
                }
                else if (nav.SrcNavIsCollection)
                {
                    sbBfs.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeBaseFq)
                        .AppendLine(">? entry)");
                }
                else
                {
                    sbBfs.Append(nodeBaseFq).AppendLine("? entry)");
                }

                sbBfs.AppendLine("    {");
                sbBfs.Append("        var __result = new ").Append(listFqH).AppendLine("();");

                if (nav.SrcNavIsDict)
                {
                    // SF-F3: dict source nav — seed BFS from dict values.
                    sbBfs.AppendLine("        if (entry is null) return __result;");
                    sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                        .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                    sbBfs.AppendLine(
                        "        foreach (var __kv in entry) if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                }
                else if (nav.SrcNavIsCollection)
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
                        {
                            sbBfs.Append("                    if (__t.").Append(edge.Name)
                                .Append(" is { } __d_").Append(edge.Name)
                                .Append(") foreach (var __kv in __d_").Append(edge.Name)
                                .AppendLine(
                                    ") if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                        }
                        else if (!edge.IsCollection)
                        {
                            sbBfs.Append("                    if (__t.").Append(edge.Name)
                                .Append(" is { } __e_").Append(edge.Name)
                                .Append(" && __visited.Add(__e_").Append(edge.Name)
                                .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                        }
                        else
                        {
                            sbBfs.Append("                    if (__t.").Append(edge.Name)
                                .Append(" is { } __c_").Append(edge.Name)
                                .Append(") foreach (var __x in __c_").Append(edge.Name)
                                .AppendLine(") if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);");
                        }

                    sbBfs.AppendLine("                    break;");
                    sbBfs.AppendLine("                }");
                }

                sbBfs.AppendLine("            }");

                sbBfs.AppendLine("        }");
                sbBfs.AppendLine("        return __result;");
                sbBfs.AppendLine("    }");
                acc.Synthesized[traversalHelperNameH] = new SynthesizedMethod(traversalHelperNameH, sbBfs.ToString());
            }

            // For array targets, synthesize a thin .ToArray() wrapper
            string converterHelperNameH;
            if (nav.NeedsToArray)
            {
                var nodeBaseFq = nav.NodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoBaseFq = nav.NodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var wrapperNameH = GeneratedNames.FlattenGraphArr + heteroHash;
                if (!acc.Synthesized.ContainsKey(wrapperNameH))
                {
                    var sbWr = new StringBuilder();
                    sbWr.Append("    private ").Append(dtoBaseFq).Append("[] ").Append(wrapperNameH)
                        .Append('(');
                    // MF-B: match the traversal helper's parameter type.
                    if (nav.SrcNavIsArray)
                    {
                        sbWr.Append(nodeBaseFq).AppendLine("[]? entry)");
                    }
                    else if (nav.SrcNavIsCollection)
                    {
                        sbWr.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeBaseFq)
                            .AppendLine(">? entry)");
                    }
                    else
                    {
                        sbWr.Append(nodeBaseFq).AppendLine("? entry)");
                    }

                    sbWr.Append("        => ").Append(traversalHelperNameH).AppendLine("(entry).ToArray();");
                    acc.Synthesized[wrapperNameH] = new SynthesizedMethod(wrapperNameH, sbWr.ToString());
                }

                converterHelperNameH = wrapperNameH;
            }
            else
            {
                converterHelperNameH = traversalHelperNameH;
            }

            acc.ConsumedTargets.Add(tgtCollName);
            acc.Injected.Add(new MemberMap(
                tgtCollName,
                srcNavName,
                converterHelperNameH,
                NullHandling.None,
                false,
                true));
            acc.Directives.Add(new FlattenGraphDirective(
                srcNavName,
                tgtCollName,
                traversalHelperNameH,
                converterHelperNameH));
            return; // skip the homogeneous path below
        }
    }
}
