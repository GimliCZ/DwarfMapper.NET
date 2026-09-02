// SPDX-License-Identifier: GPL-2.0-only

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

                // There was a Nullable<TNode> branch here, for a member typed `T?` where T is a struct that
                // converts to or from the node type. It was REMOVED in round 27 because it could never fire,
                // and the proof is worth keeping so it is not re-added:
                //
                //   * Reaching this method means the node type is a REFERENCE type (every branch that assigns
                //     nodeType above requires IsReferenceType; anything else is refused with DWARF034) and is
                //     neither abstract nor an interface (those route to the heterogeneous path instead).
                //   * T is a struct, because Nullable<T> constrains it to one.
                //   * HasImplicitConversion is `IsImplicit && !IsUserDefined`, so only a BUILT-IN conversion
                //     counts. The built-in implicit conversions out of a struct are boxing — to object,
                //     ValueType, Enum, or an interface T implements — and there is no built-in implicit
                //     conversion INTO a struct from a reference type at all. ValueType and Enum are abstract
                //     and interfaces are excluded, so the only surviving candidate is `object`.
                //   * `object` declares no properties or fields, so ReadableMembers returns nothing for it and
                //     this loop has no iterations when the node type is object.
                //
                // Confirmed empirically before removal: replacing the branch body with a throw and running the
                // whole suite raised nothing across 8,259 tests, and the golden manifest is byte-identical with
                // the branch gone. The heterogeneous twin of this branch is NOT dead — a node base may be an
                // interface there, which makes boxing a built-in conversion to it — and it stays.

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
