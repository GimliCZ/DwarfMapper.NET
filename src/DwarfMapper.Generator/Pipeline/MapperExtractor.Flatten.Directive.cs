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
        ///     Resolves ONE <c>[FlattenGraph]</c> directive: the body of the loop in
        ///     <c>ResolveFlattenGraphDirectives</c>, which was 1,073 of that method's 1,116 lines.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Returning early means SKIP THIS DIRECTIVE and carry on with the next -- it was
        ///         <c>continue</c> when this was a loop body, and it keeps the report-and-skip discipline the
        ///         loop's own comment describes: every directive is validated in the same pass, so a method with
        ///         two mistakes reports both rather than stopping at the first.
        ///     </para>
        ///     <para>
        ///         Nine <c>continue</c> statements became <c>return</c>; seventeen others target inner loops and
        ///         were left alone. Which nine was decided by the compiler, not by reading: wrapping the body in a
        ///         local function makes exactly the jumps that targeted the outer loop fail with CS0139. The same
        ///         probe proved no <c>break</c> escaped, which is what makes this expressible as a void method --
        ///         a <c>break</c> would have needed a return value to stop the loop.
        ///     </para>
        /// </remarks>
        private static void ResolveOneFlattenGraphDirective(
            FlattenGraphRequest req,
            FlattenGraphAccumulators acc,
            string srcNavName,
            string tgtCollName)
        {
            if (!acc.SeenTargets.Add(tgtCollName))
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateFlattenGraphTarget,
                    req.Location,
                    tgtCollName));
                return;
            }

            // 1. Resolve source navigation member on req.SourceType
            ITypeSymbol? srcNavType = null;
            foreach (var m in ReadableMembers(req.SourceType, req.Compilation, req.AllowNonPublic))
                if (string.Equals(m.Name, srcNavName, StringComparison.Ordinal))
                {
                    srcNavType = m.Type;
                    break;
                }

            if (srcNavType is null)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                    req.Location,
                    $"[FlattenGraph] source navigation '{srcNavName}' does not exist or is not readable on '{req.SourceType.Name}'"));
                return;
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

            if (srcNavType is IArrayTypeSymbol arrNav && arrNav.Rank == 1 && arrNav.ElementType.IsReferenceType)
            {
                nodeType = arrNav.ElementType;
                srcNavIsCollection = true;
                srcNavIsArray = true;
                srcNavIsDict = false;
            }
            else if (DictionaryConverter.TryGetDictionaryValueType(srcNavType, out var dictNavNodeType) && dictNavNodeType.IsReferenceType)
            {
                // SF-F3: Dictionary<K, Node> source nav — node type is V, seed from .Values.
                nodeType = dictNavNodeType;
                srcNavIsCollection = true;
                srcNavIsArray = false;
                srcNavIsDict = true;
            }
            else if (CollectionConverter.TryGetEnumerableElement(srcNavType, out var navElemType, out _) && navElemType.IsReferenceType)
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
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                    req.Location,
                    $"[FlattenGraph] source navigation '{srcNavName}' must be a reference type or a collection of a reference type (the node type)"));
                return;
            }

            // 3. Resolve target collection member on req.TargetType
            ITypeSymbol? tgtCollType = null;
            foreach (var m in WritableMembers(req.TargetType, req.Compilation, req.AllowNonPublic))
                if (string.Equals(m.Name, tgtCollName, StringComparison.Ordinal))
                {
                    tgtCollType = m.Type;
                    break;
                }

            if (tgtCollType is null)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                    req.Location,
                    $"[FlattenGraph] target collection '{tgtCollName}' does not exist or is not writable on '{req.TargetType.Name}'"));
                return;
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
            else if (IsExactNamedTypeHelper(tgtCollType,
                         "IReadOnlyList",
                         "System.Collections.Generic",
                         1,
                         out var rlElem))
            {
                nodeDtoType = rlElem!;
                needsToArray = false; // List<T> implements IReadOnlyList<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType,
                         "ICollection",
                         "System.Collections.Generic",
                         1,
                         out var icElem))
            {
                nodeDtoType = icElem!;
                needsToArray = false; // List<T> implements ICollection<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType,
                         "IReadOnlyCollection",
                         "System.Collections.Generic",
                         1,
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
            else if (IsExactNamedTypeHelper(tgtCollType,
                         "IEnumerable",
                         "System.Collections.Generic",
                         1,
                         out var ieElem))
            {
                nodeDtoType = ieElem!;
                needsToArray = false; // List<T> implements IEnumerable<T>
            }
            else
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                    req.Location,
                    $"[FlattenGraph] target collection '{tgtCollName}' on '{req.TargetType.Name}' must be " +
                    $"List<T>, T[], IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T>, IList<T>, or IEnumerable<T>"));
                return;
            }

            // ── Plan 22: Heterogeneous branch ────────────────────────────────
            // Detect hetero mode: abstract/interface node base OR [MapDerivedType] pairs present.
            var nodeIsAbstractOrInterface =
                nodeType.TypeKind == TypeKind.Interface || nodeType.IsAbstract;
            var effectiveDerivedPairs = req.RawDerivedPairs ?? Array.Empty<(INamedTypeSymbol, INamedTypeSymbol, bool)>();
            var isHetero = nodeIsAbstractOrInterface || effectiveDerivedPairs.Count > 0;

            if (isHetero)
            {
                // Validate: abstract/interface node base requires at least one [MapDerivedType]
                if (nodeIsAbstractOrInterface && effectiveDerivedPairs.Count == 0)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                        req.Location,
                        $"[FlattenGraph] node base type '{nodeType.Name}' is abstract or an interface; " +
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

                    // 1. derivedSrc must be assignable to nodeType (the node base)
                    if (!HasImplicitConversion(req.Compilation, derivedSrc, nodeType))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                            req.Location,
                            $"[MapDerivedType] source type '{srcFqn}' is not assignable to node base type " +
                            $"'{nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                            $"each derived node type must inherit from or implement the node base."));
                        anyArmError = true;
                        continue;
                    }

                    // 2. derivedTgt must be assignable to nodeDtoType (the base DTO = collection element)
                    if (!HasImplicitConversion(req.Compilation, derivedTgt, nodeDtoType))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                            req.Location,
                            $"[MapDerivedType] target type '{tgtFqnArm}' is not assignable to target collection " +
                            $"element type '{nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
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
                    // An EDGE member is any readable member whose type is assignable to nodeType, or a
                    // collection thereof (including inherited base edges).
                    // SF-F3 fix: also detect Dictionary<K,V> where V is assignable to nodeType.
                    var nodeBaseNoAnnot = nodeType.WithNullableAnnotation(NullableAnnotation.None);
                    var derivedEdgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue)>();
                    var derivedLeafMembers = new List<(string Name, ITypeSymbol Type)>();

                    foreach (var nm in ReadableMembers(derivedSrc, req.Compilation, req.AllowNonPublic))
                    {
                        var memberTypeNoAnnot = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                        // Single-ref edge: type assignable to nodeBase (includes exact type and subtypes)
                        if (HasImplicitConversion(req.Compilation, memberTypeNoAnnot, nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, false));
                            continue;
                        }

                        // Nullable<T> where T is assignable to nodeBase
                        if (nm.Type is INamedTypeSymbol nmNamed &&
                            nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                            HasImplicitConversion(req.Compilation,
                                nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None),
                                nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, false));
                            continue;
                        }

                        // SF-F3: Dictionary<K,V> where V is assignable to nodeBase → dict-value edge.
                        if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValTypeD) &&
                            HasImplicitConversion(req.Compilation,
                                dictValTypeD.WithNullableAnnotation(NullableAnnotation.None),
                                nodeType))
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
                                nodeType))
                        {
                            isEdgeColl = true;
                        }
                        else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _) &&
                                 HasImplicitConversion(req.Compilation,
                                     edgeElem.WithNullableAnnotation(NullableAnnotation.None),
                                     nodeType))
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
                var heteroHashKey = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "=>" + nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "@Hetero";
                var heteroHash = StableHash.Fnv1a(heteroHashKey);

                var dispatchHelperName = GeneratedNames.FlatNodeDispatch + heteroHash;
                var traversalHelperNameH = GeneratedNames.FlattenGraph + heteroHash;

                // Synthesize __DwarfMap_FlatNodeDispatch_<hash>(TBase n) => n switch { ... }
                if (!acc.Synthesized.ContainsKey(dispatchHelperName))
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
                    acc.Synthesized[dispatchHelperName] = new SynthesizedMethod(dispatchHelperName, sbDisp.ToString());
                }

                // Synthesize __DwarfMap_FlattenGraph_<hash>(TBase entry) BFS traversal
                if (!acc.Synthesized.ContainsKey(traversalHelperNameH))
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
                    {
                        sbBfs.Append(nodeBaseFq).AppendLine("[]? entry)");
                    }
                    else if (srcNavIsDict)
                    {
                        sbBfs.Append(srcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            .AppendLine("? entry)");
                    }
                    else if (srcNavIsCollection)
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
                if (needsToArray)
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var wrapperNameH = GeneratedNames.FlattenGraphArr + heteroHash;
                    if (!acc.Synthesized.ContainsKey(wrapperNameH))
                    {
                        var sbWr = new StringBuilder();
                        sbWr.Append("    private ").Append(dtoBaseFq).Append("[] ").Append(wrapperNameH)
                            .Append('(');
                        // MF-B: match the traversal helper's parameter type.
                        if (srcNavIsArray)
                        {
                            sbWr.Append(nodeBaseFq).AppendLine("[]? entry)");
                        }
                        else if (srcNavIsCollection)
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
            {
                nodeDtoIsConstructible =
                    (namedDtoCheck.TypeKind == TypeKind.Class || namedDtoCheck.TypeKind == TypeKind.Struct) &&
                    namedDtoCheck.SpecialType == SpecialType.None &&
                    namedDtoCheck.InstanceConstructors.Any(c =>
                        c.DeclaredAccessibility == Accessibility.Public);
            }

            // Also accept if there's an explicit declared mapper method for this pair.
            var hasDeclaredMapper = false;
            foreach (var m in req.AllMethods)
                if (HasImplicitConversion(req.Compilation, nodeType, m.ParamType) && HasImplicitConversion(req.Compilation, m.ReturnType, nodeDtoType))
                {
                    hasDeclaredMapper = true;
                    break;
                }

            // Accept implicit conversion too (value types, same type, etc.)
            var hasImplicit = HasImplicitConversion(req.Compilation, nodeType, nodeDtoType);

            if (!nodeDtoIsConstructible && !hasDeclaredMapper && !hasImplicit)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph,
                    req.Location,
                    $"[FlattenGraph] node DTO type '{nodeDtoType.Name}' is not constructible " +
                    $"(must be a class or struct with a public constructor)"));
                return;
            }

            // 6. Partition TNode readable members into edge (same type as nodeType or collection thereof) vs leaf
            // SF-F4 fix: use bidirectional assignability (HasImplicitConversion both ways) instead of exact
            //   type equality so that interface-typed edges (e.g. "INode? Link" where node : INode) and
            //   base-class-typed edges are traversed.  For edges typed as an ancestor/interface of nodeType,
            //   the BFS enqueue must cast via `is TNode __var` (since the queue holds TNode, not the interface).
            // SF-F3 fix: detect Dictionary<K,V> where V is assignable to nodeType as a dict-value edge.
            var nodeMembers = ReadableMembers(nodeType, req.Compilation, req.AllowNonPublic).ToList();
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
                var directToNode = HasImplicitConversion(req.Compilation, memberTypeNoAnnotation, nodeType);
                var nodeToMember = !directToNode &&
                                   HasImplicitConversion(req.Compilation, nodeTypeNoAnnotation, memberTypeNoAnnotation);
                if (directToNode || nodeToMember)
                {
                    // NeedsNodeCast: when the member type is a base/interface (reverse direction), we
                    // must cast via `is TNode __var` before enqueuing so the Queue<TNode> accepts it.
                    edgeMembers.Add((nm.Name, false, false, nodeToMember));
                    continue;
                }

                // Nullable<TNode> (for structs — unlikely but supported)
                if (nm.Type is INamedTypeSymbol nmNamed && nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    var innerNoAnnot = nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None);
                    var innerToNode = HasImplicitConversion(req.Compilation, innerNoAnnot, nodeType);
                    var nodeToInner = !innerToNode &&
                                      HasImplicitConversion(req.Compilation, nodeTypeNoAnnotation, innerNoAnnot);
                    if (innerToNode || nodeToInner)
                    {
                        edgeMembers.Add((nm.Name, false, false, nodeToInner));
                        continue;
                    }
                }

                // SF-F3 fix: Dictionary<K,V> where V is assignable to nodeType → dict-value edge.
                // Only V assignable to nodeType qualifies; keys are not traversed (v1: values only).
                if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValType) &&
                    HasImplicitConversion(req.Compilation,
                        dictValType.WithNullableAnnotation(NullableAnnotation.None),
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
                    if (HasImplicitConversion(req.Compilation, arrElemNoAnnot, nodeType))
                    {
                        isEdgeColl = true;
                    }
                    else if (HasImplicitConversion(req.Compilation, nodeTypeNoAnnotation, arrElemNoAnnot))
                    {
                        isEdgeColl = true;
                        collNeedsCast = true;
                    }
                }
                else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _))
                {
                    var edgeElemNoAnnot = edgeElem.WithNullableAnnotation(NullableAnnotation.None);
                    if (HasImplicitConversion(req.Compilation, edgeElemNoAnnot, nodeType))
                    {
                        isEdgeColl = true;
                    }
                    else if (HasImplicitConversion(req.Compilation, nodeTypeNoAnnotation, edgeElemNoAnnot))
                    {
                        isEdgeColl = true;
                        collNeedsCast = true;
                    }
                }

                if (isEdgeColl)
                {
                    edgeMembers.Add((nm.Name, true, false, collNeedsCast));
                }
                else
                {
                    leafMembers.Add((nm.Name, nm.Type));
                }
            }

            // 7. Get writable members of nodeDtoType for the flat-node helper
            var dtoWritable = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            foreach (var m in WritableMembers(nodeDtoType, req.Compilation, req.AllowNonPublic))
                dtoWritable[m.Name] = m.Type;

            // 8. Build hash key and helper names
            var hashKey = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "=>" + nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var hash = StableHash.Fnv1a(hashKey);

            var flatNodeHelperName = GeneratedNames.FlatNode + hash;
            var traversalHelperName = GeneratedNames.FlattenGraph + hash;

            // 9. Synthesize __DwarfMap_FlatNode_HASH (maps one TNode leaf-only → TNodeDto)
            if (!acc.Synthesized.ContainsKey(flatNodeHelperName))
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
                // MF-D fix: flat-node leaf synthesis must NEVER call a acc.Synthesized complex helper
                // (one that starts with "__DwarfMap_") because those helpers may later be force-marked
                // recursion-capable (3-param) by the Preserve force-marking loop, creating a
                // signature mismatch when the flat-node helper calls them with 1 arg → CS7036.
                // Strategy: use a THROW-AWAY acc.Synthesized dict for complex resolutions — this
                // prevents polluting the main dict with a shared Obj/Dict/Coll helper that will
                // later become 3-param.  Only emit the leaf if the resulting converter is either:
                //   (a) null (direct assignment — primitive/same-type),
                //   (b) a declared user method (doesn't start with "__DwarfMap_"), OR
                //   (c) a acc.Synthesized PRIMITIVE helper (enum/numeric/parsable, which are never 3-param).
                // If the resolved converter is a acc.Synthesized complex helper, skip the member
                // (leave DTO default) — that's correct topology-degraded behaviour for flat-graph.
                // SF-LEAFDIAG fix: propagate acc.Diagnostics for truly unmappable leaf members to the
                // real acc.Diagnostics list so callers get a DWARF005/etc. error rather than silence.
                foreach (var leaf in leafMembers)
                {
                    if (!dtoWritable.TryGetValue(leaf.Name, out var dtoMemberType))
                    {
                        continue;
                    }

                    // Use a throw-away synth dict so complex helpers (Obj/Dict/Coll) are NOT
                    // registered in the main dict and cannot be force-marked 3-param later.
                    var leafThrowAwaySynth = new Dictionary<string, SynthesizedMethod>(
                        StringComparer.Ordinal);
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
                        // SF-LEAFDIAG: propagate errors from unmappable leaf members (not silently dropped).
                        acc.Diagnostics.AddRange(leafTestDiags);
                        continue;
                    }

                    // MF-D: a acc.Synthesized COMPLEX helper (object mapper, collection helper, dict helper) may be
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
                        if (req.IsPreserve)
                        {
                            acc.Diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.FlattenGraphLeafNotFlattened,
                                req.Location,
                                $"[FlattenGraph] cannot flatten member '{leaf.Name}' of type " + $"'{leaf.Type.ToDisplayString()}' under ReferenceHandling = Preserve; it is left " + "at the destination's default. Map the member explicitly, or use " + "ReferenceHandling = None for this mapper."));
                            continue;
                        }

                        // Not Preserve — fall through and emit the leaf; the merge below registers the complex
                        // helper in the main dict so the call resolves.
                    }

                    // Safe: emit the leaf member.  Merge any non-complex throw-away entries
                    // (numeric, enum, parsable helpers that are never force-marked 3-param).
                    foreach (var kv in leafThrowAwaySynth)
                        if (!acc.Synthesized.ContainsKey(kv.Key))
                        {
                            acc.Synthesized[kv.Key] = kv.Value;
                        }

                    sb.Append("            ").Append(leaf.Name).Append(" = ");
                    AppendFlatNodeMemberExpr(sb,
                        "n",
                        leaf.Name,
                        leafConv,
                        leafNull,
                        FlatLeafNeedsBang(leafConv, leaf.Type, dtoMemberType, req.AutoCandidates, req.AllMethods));
                    sb.AppendLine(",");
                }

                // Edge members on DTO: null them out (topology degradation — the point of [FlattenGraph])
                foreach (var edge in edgeMembers)
                {
                    if (!dtoWritable.ContainsKey(edge.Name))
                    {
                        continue;
                    }

                    sb.Append("            ").Append(edge.Name).AppendLine(" = null,");
                }

                sb.AppendLine("        };");
                sb.AppendLine("    }");
                acc.Synthesized[flatNodeHelperName] = new SynthesizedMethod(flatNodeHelperName, sb.ToString());
            }

            // 10. Synthesize __DwarfMap_FlattenGraph_HASH (BFS traversal → List<TNodeDto>)
            if (!acc.Synthesized.ContainsKey(traversalHelperName))
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
                {
                    sb.Append(nodeFq).AppendLine("[]? entry)");
                }
                else if (srcNavIsDict)
                {
                    sb.Append(srcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .AppendLine("? entry)");
                }
                else if (srcNavIsCollection)
                {
                    sb.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeFq).AppendLine(">? entry)");
                }
                else
                {
                    sb.Append(nodeFq).AppendLine("? entry)");
                }

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
                        {
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is ").Append(nodeFq).Append(" __e_").Append(edge.Name)
                                .Append(" && __visited.Add(__e_").Append(edge.Name)
                                .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                        }
                        else
                        {
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is { } __e_").Append(edge.Name)
                                .Append(" && __visited.Add(__e_").Append(edge.Name)
                                .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                        }
                    }
                    else
                    {
                        if (edge.NeedsNodeCast)
                            // SF-F4: collection of interface/base elements → cast each.
                        {
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is { } __c_").Append(edge.Name)
                                .Append(") foreach (var __xi in __c_").Append(edge.Name)
                                .Append(") if (__xi is ").Append(nodeFq)
                                .AppendLine(" __x && __visited.Add(__x)) __queue.Enqueue(__x);");
                        }
                        else
                        {
                            sb.Append("            if (__n.").Append(edge.Name)
                                .Append(" is { } __c_").Append(edge.Name)
                                .Append(") foreach (var __x in __c_").Append(edge.Name)
                                .AppendLine(") if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);");
                        }
                    }

                sb.AppendLine("        }");
                sb.AppendLine("        return __result;");
                sb.AppendLine("    }");
                acc.Synthesized[traversalHelperName] = new SynthesizedMethod(traversalHelperName, sb.ToString());
            }

            // 11. For array targets, synthesize a thin .ToArray() wrapper
            string converterHelperName;
            if (needsToArray)
            {
                var nodeFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var wrapperName = GeneratedNames.FlattenGraphArr + hash;
                if (!acc.Synthesized.ContainsKey(wrapperName))
                {
                    var sb = new StringBuilder();
                    sb.Append("    private ").Append(dtoFq).Append("[] ").Append(wrapperName)
                        .Append('(');
                    // MF-B: match the traversal helper's parameter type.
                    if (srcNavIsArray)
                    {
                        sb.Append(nodeFq).AppendLine("[]? entry)");
                    }
                    else if (srcNavIsCollection)
                    {
                        sb.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeFq)
                            .AppendLine(">? entry)");
                    }
                    else
                    {
                        sb.Append(nodeFq).AppendLine("? entry)");
                    }

                    sb.Append("        => ").Append(traversalHelperName).AppendLine("(entry).ToArray();");
                    acc.Synthesized[wrapperName] = new SynthesizedMethod(wrapperName, sb.ToString());
                }

                converterHelperName = wrapperName;
            }
            else
            {
                converterHelperName = traversalHelperName;
            }

            // 12. Mark target collection as consumed (ResolveMembers must skip it)
            acc.ConsumedTargets.Add(tgtCollName);

            // 13. Build a MemberMap for the injection — emitter handles it like any other member
            //     SourceIsNullableRef=true ensures '!' is added if needed (the traversal helper handles null internally)
            acc.Injected.Add(new MemberMap(
                tgtCollName,
                srcNavName,
                converterHelperName,
                NullHandling.None,
                false,
                true));

            // 14. Record directive (for model completeness / snapshot tests)
            acc.Directives.Add(new FlattenGraphDirective(
                srcNavName,
                tgtCollName,
                traversalHelperName,
                converterHelperName));
        }
    }
}
