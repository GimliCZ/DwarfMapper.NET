// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     Splits a node type's readable members into EDGES -- the ones that walk further into the graph --
        ///     and LEAVES, the scalar values copied straight across.
        /// </summary>
        /// <remarks>
        ///     Fills the two lists rather than returning them: both outlive this step and several later ones read
        ///     them, so handing them in keeps the caller's ownership visible instead of hiding it behind a tuple
        ///     the caller would immediately destructure back into the same two names.
        /// </remarks>
        private static void PartitionNodeMembers(
            FlattenGraphRequest req,
            FlattenNavShape nav,
            IReadOnlyList<(string Name, ITypeSymbol Type)> nodeMembers,
            ITypeSymbol nodeTypeNoAnnotation,
            List<(string Name, bool IsCollection, bool IsDictValue, bool NeedsNodeCast)> edgeMembers,
            List<(string Name, ITypeSymbol Type)> leafMembers)
        {
            foreach (var nm in nodeMembers)
            {
                var memberTypeNoAnnotation = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                // SF-F4 fix: Direct node reference — recognise as a graph edge if:
                //   (a) memberType is assignable to nav.NodeType (e.g. a derived subtype field), OR
                //   (b) nav.NodeType is assignable to memberType (e.g. edge typed as an interface/base
                //       that the node implements/derives — "INode? Link" where node is a class : INode).
                // Was: exact equality only — missed interface-typed and base-typed edges.
                var directToNode = HasImplicitConversion(req.Compilation, memberTypeNoAnnotation, nav.NodeType);
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
                    var innerToNode = HasImplicitConversion(req.Compilation, innerNoAnnot, nav.NodeType);
                    var nodeToInner = !innerToNode &&
                                      HasImplicitConversion(req.Compilation, nodeTypeNoAnnotation, innerNoAnnot);
                    if (innerToNode || nodeToInner)
                    {
                        edgeMembers.Add((nm.Name, false, false, nodeToInner));
                        continue;
                    }
                }

                // SF-F3 fix: Dictionary<K,V> where V is assignable to nav.NodeType → dict-value edge.
                // Only V assignable to nav.NodeType qualifies; keys are not traversed (v1: values only).
                if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValType) &&
                    HasImplicitConversion(req.Compilation,
                        dictValType.WithNullableAnnotation(NullableAnnotation.None),
                        nav.NodeType))
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
                    if (HasImplicitConversion(req.Compilation, arrElemNoAnnot, nav.NodeType))
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
                    if (HasImplicitConversion(req.Compilation, edgeElemNoAnnot, nav.NodeType))
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
        }
    }
}
