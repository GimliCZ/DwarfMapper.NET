# DwarfMapper.NET — Spec (Plan 20): `[FlattenGraph]` reachability degradation

**Goal.** Collapse a transitively-reachable object graph into a **flat collection of distinct nodes on the root**, intentionally **degrading** (dropping) the edges among nodes. For the owner's example `A→B, B⇄C, C⇄D, D⇄B`, mapping `A` produces `ADto` whose designated collection holds `{B', C', D'}` (each distinct node once), with each node's inter-node edges nulled — i.e. `A→B, A→C, A→D`.

**Why it's cheap.** We already have reference-identity dedup traversal (Preserve). FlattenGraph is a self-contained worklist traversal with a `ReferenceEqualityComparer` visited-set that collects each distinct reachable node once and maps it with edges severed.

## Owner-locked v1 semantics (stated for course-correction)
- **Declaration:** `[FlattenGraph(sourceNavigation, targetCollection)]` on a partial mapping method. `sourceNavigation` = a member on the PARAMETER (root) type that enters the graph (its type is the node type, or a collection of nodes). `targetCollection` = a collection member on the RETURN type whose element type is the node-DTO type.
- **Target shape:** a flat collection (`List<TNodeDto>` / any supported collection target). Named per-node slots are NOT supported (a cyclic cluster has no compile-time-known node identities). Other members of the return type map normally.
- **Node set:** all distinct reference-type nodes of type `TNode` reachable from `root.<sourceNavigation>` by following `TNode`-typed and `IEnumerable<TNode>`-typed members transitively. Dedup by REFERENCE identity (`ReferenceEqualityComparer.Instance`) — records/value-equality types are NOT merged. Deterministic BFS order from the entry.
- **Degradation (the intentional loss):** each collected `TNodeDto` has its **graph-edge members** (members whose type is `TNode` or a collection of `TNode`) set to **null/empty**; all other (scalar/leaf/non-node) members map normally via the standard node mapper. The edge-drop is the explicit point of the feature — opting into `[FlattenGraph]` IS the acknowledgment of topology loss (no silent surprise: the attribute name + docs make it explicit).
- **Cycle/depth safety:** the visited-set terminates on any cycle (each node enqueued once). The existing `MaxDepth` guard is a backstop. Null entry → empty collection (never NRE).
- **Homogeneous v1:** a single node type `TNode`→`TNodeDto`. Heterogeneous node hierarchies (`[MapDerivedType]`) are deferred.
- **Independent of `ReferenceHandling`:** FlattenGraph owns its traversal; it doesn't require Preserve and isn't affected by it.

## Mechanism (emitted, AOT-safe, reflection-free)
A synthesized helper per (root-nav, target) pair:
```
private global::System.Collections.Generic.List<TNodeDto> __DwarfMap_FlattenGraph_<hash>(TNode entry)
{
    var __result = new List<TNodeDto>();
    if (entry is null) return __result;
    var __visited = new HashSet<object>(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);
    var __queue = new Queue<TNode>();
    __visited.Add(entry); __queue.Enqueue(entry);
    while (__queue.Count > 0)
    {
        var __n = __queue.Dequeue();
        __result.Add(__DwarfMap_FlatNode_<hash>(__n));      // node mapper with edges nulled
        // enqueue reachable nodes via each edge member:
        if (__n.EdgeRef is { } __e && __visited.Add(__e)) __queue.Enqueue(__e);
        if (__n.EdgeColl is { } __c) foreach (var __x in __c) if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);
    }
    return __result;
}
private TNodeDto __DwarfMap_FlatNode_<hash>(TNode n)
    => new TNodeDto { Scalar = n.Scalar, /* non-edge members mapped */, EdgeMemberDto = null /* degraded */ };
```
The root method assigns `dst.<targetCollection> = __DwarfMap_FlattenGraph_...(src.<sourceNavigation>);` and maps the root's other members normally.

## Diagnostics
- **DWARF034 (InvalidFlattenGraph):** `sourceNavigation` not found / not a node-or-node-collection member; `targetCollection` not found / not a collection of a mappable node-DTO; node type not mappable to the element type. Loud build error. Add to `DiagnosticDescriptors` + `AnalyzerReleases.Unshipped.md` (DWARF032/033 used; 034 is next; 004/029 reserved).

## Tasks (TDD)
1. `[FlattenGraph(string sourceNavigation, string targetCollection)]` attribute in `src/DwarfMapper/` (AttributeTargets.Method, AllowMultiple=true for multiple flatten members). SPDX header + XML doc making the edge-drop explicit.
2. Generator: read the attribute in `MapperExtractor`; resolve sourceNav (parameter-type member → node element type), targetCollection (return-type member → node-DTO element type); validate → DWARF034. Determine the node type's EDGE members (typed `TNode` or `IEnumerable<TNode>`) vs LEAF members. Synthesize the two helpers (traversal + flat-node mapper). The flat-node mapper maps leaf members via the normal pipeline (TryResolveConversion) and emits edge members as null/empty. Wire the target-collection assignment in the root method; mark the target collection member consumed (exclude from normal mapping + completeness). 
3. Emit (MapEmitter / synthesized methods): the worklist traversal + flat-node mapper. AOT-safe (ReferenceEqualityComparer + Queue/HashSet, no reflection).
4. Tests (TDD): the owner graph `A→B,B⇄C,C⇄D,D⇄B` → `ADto.Nodes` contains 3 distinct nodes (by value), each node's edge members null (degraded), no duplicates, terminates (no depth throw) on the cycle; a DAG/diamond (shared node) → collected once; self-loop; null entry → empty; leaf members preserved; collection-typed edges (a node with `List<Node> Children`) traversed; DWARF034 for misconfig (target not a collection / source nav not a node). Runtime + generator tests. Regression: full suite green; existing graph/Preserve tests unaffected. AOT sample: add a FlattenGraph mapper; publish (report IL).
5. README/spec: document `[FlattenGraph]` (explicit topology degradation; flat-collection target; homogeneous v1; deferred: heterogeneous/[MapDerivedType], named-slot targets).

## Deferred
- Heterogeneous node types / `[MapDerivedType]` element partitioning.
- Named per-node-type slots on the target.
- Configurable edge-fate (keep-if-present) — v1 always degrades (drops) edges.
- Ordering options (v1 = deterministic BFS).
