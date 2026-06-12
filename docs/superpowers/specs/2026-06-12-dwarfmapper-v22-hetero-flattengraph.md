# DwarfMapper.NET — Spec (Plan 22): Heterogeneous `[FlattenGraph]`

**Goal.** Extend Plan-20 `[FlattenGraph]` (reachability degradation → flat collection, edges dropped) to graphs whose nodes are of **different runtime types** in one hierarchy (e.g. a filesystem of `Folder`/`File : FsNode`, or `Manager`/`Worker : Employee`). Each reachable node is mapped to the correct **derived DTO** via `[MapDerivedType]`-style dispatch, with its inter-node edges nulled, into a flat `List<TBaseDto>` on the root.

Composes Plan 20 (FlattenGraph traversal + degradation) + Plan 21 (`[MapDerivedType]` runtime-type dispatch).

## Trigger / mode
- Declared as before: `[FlattenGraph(sourceNavigation, targetCollection)]` on a method, PLUS one `[MapDerivedType<TNodeDerived, TNodeDerivedDto>]` per concrete node subtype, on the SAME method.
- **Heterogeneous mode** when: the node base type (the static type entered via `sourceNavigation`, or the element type of a node-collection navigation) is abstract/interface, OR `[MapDerivedType]` registrations are present. Else **homogeneous** (Plan 20, unchanged).
- Target collection element type = the **base DTO** (`TBaseDto`); each arm's `TNodeDerivedDto` MUST be assignable to it (else DWARF034).

## Semantics (heterogeneous)
- **Node set:** all distinct reference nodes reachable from `root.<sourceNavigation>` by following EDGE members, deduped by `ReferenceEqualityComparer.Instance` (records not merged). Deterministic BFS.
- **Edge member (per concrete node type):** a readable member whose type is assignable to the node base `TNodeBase`, or `IEnumerable<E>`/array/collection where `E` is assignable to `TNodeBase`. (Catches `FsNode? Parent`, `List<FsNode> Children`, `List<Folder> SubFolders`, etc. — inherited base edges included per derived type.)
- **Flat-node mapping (dispatch + degrade):** each node → its correct derived DTO via a runtime-type `switch` (most-derived-first, reuse Plan 21 ordering), where each per-type arm maps `TDerived → TDerivedDto` with LEAF members mapped (full pipeline) and EDGE members nulled. Unregistered concrete runtime type → loud `ArgumentException` (consistent with `[MapDerivedType]`).
- **Cycle/depth safety:** visited-set terminates any cycle (incl. cross-type cycles `Folder→File→Folder`). Null entry → empty. Null edges skipped.

## Emitted helpers (AOT-safe, reflection-free)
```
private List<FsNodeDto> __DwarfMap_FlattenGraph_<hash>(FsNode entry) {
    var __result = new List<FsNodeDto>();
    if (entry is null) return __result;
    var __visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var __queue = new Queue<FsNode>();
    __visited.Add(entry); __queue.Enqueue(entry);
    while (__queue.Count > 0) {
        var __n = __queue.Dequeue();
        __result.Add(__DwarfMap_FlatNodeDispatch_<hash>(__n));
        // enqueue edges by runtime type (most-derived-first):
        switch (__n) {
            case global::Ns.Folder __f:
                foreach (var __c in __f.Children) if (__c is not null && __visited.Add(__c)) __queue.Enqueue(__c);
                break;
            case global::Ns.File __fl:
                if (__fl.Parent is { } __p && __visited.Add(__p)) __queue.Enqueue(__p);
                break;
        }
    }
    return __result;
}
private FsNodeDto __DwarfMap_FlatNodeDispatch_<hash>(FsNode n) => n switch {
    global::Ns.Folder __s => __DwarfMap_FlatNode_Folder_<hash>(__s),
    global::Ns.File   __s => __DwarfMap_FlatNode_File_<hash>(__s),
    _ => throw new global::System.ArgumentException("DwarfMapper [FlattenGraph]: no [MapDerivedType] registered for runtime node type '" + n.GetType() + "'.", nameof(n)),
};
private FolderDto __DwarfMap_FlatNode_Folder_<hash>(Folder f) => new FolderDto { Name = f.Name, /*leaf*/, Children = null /*edge degraded*/ };
private FileDto   __DwarfMap_FlatNode_File_<hash>(File fl)   => new FileDto { Name = fl.Name, Size = fl.Size, Parent = null /*edge*/ };
```
Root method assigns `dst.<targetCollection> = __DwarfMap_FlattenGraph_<hash>(src.<sourceNavigation>);`; other root members map normally.

## Diagnostics
- Extend **DWARF034 (InvalidFlattenGraph)** for heterogeneous misconfig: node base abstract/interface but NO `[MapDerivedType]` registrations; a registered `TNodeDerivedDto` not assignable to the target collection element (base DTO); a registered derived node type not assignable to the node base; duplicate derived-source. (Reuse DWARF035 wording where the issue is a `[MapDerivedType]` pair specifically.)

## Tasks (TDD)
1. Generator: detect heterogeneous mode (abstract/interface node base OR `[MapDerivedType]` present on a `[FlattenGraph]` method). Read the `[MapDerivedType]` arms (reuse Plan-21 `ReadDerivedTypeAttributes`). Validate (→ DWARF034/035). Compute, per registered concrete node type, its EDGE members (assignable-to-base / collection-thereof) and LEAF members. Sort dispatch/edge arms most-derived-first.
2. Synthesize: `__FlattenGraph` traversal with a runtime-type `switch` for edge enumeration; `__FlatNodeDispatch` switch; per-type `__FlatNode_<T>` degraded mappers (leaf via TryResolveConversion, edges nulled). Reuse Plan-20 homogeneous path when not heterogeneous.
3. Tests (TDD; generator + runtime):
   - **Heterogeneous tree** `Folder{ List<FsNode> Children }` + `File{ long Size }` : `FsNode` → flat `List<FsNodeDto>` (`FolderDto`/`FileDto : FsNodeDto`): a folder-of-files+subfolders → each node mapped to the right derived DTO, leaf members preserved, edges (Children/Parent) nulled, distinct nodes once.
   - **Cross-type cycle** (`Folder→File→Folder` via Parent) → terminates, each node once.
   - **Diamond** (a File reachable from two Folders) → collected once.
   - **Unregistered runtime node type** reachable → ArgumentException (loud).
   - **Edge member on the BASE** (`FsNode.Parent`) inherited by all → traversed for every type.
   - **Mixed edge kinds** (single-ref edge + collection edge across types).
   - Null entry → empty; null edge → skipped.
   - DWARF034/035 matrix: abstract base no [MapDerivedType]; derived DTO not assignable to base DTO; unmappable derived pair.
   - Regression: homogeneous Plan-20 FlattenGraph unchanged; full suite green (base 1390).
   - **Golden snapshot**: one heterogeneous `[FlattenGraph]` mapper (Snapshots/ Verify pattern; accept baseline; deterministic).
   - **AOT**: add a heterogeneous `[FlattenGraph]` mapper to `samples/DwarfMapper.AotSample`; publish (report IL).
4. README/spec: document heterogeneous `[FlattenGraph]` ( `[FlattenGraph]` + `[MapDerivedType]` together; per-type edge/leaf; dispatch; degrade; cross-type cycles; deferred items).

## Deferred
- Auto-discovery of node subtypes (v2 requires explicit `[MapDerivedType]`, per the explicit-no-magic ethos).
- Per-target-type partitioned collections (e.g. separate `Folders`/`Files` lists) — v2 = single flat base-DTO collection.
- Configurable edge-fate (v2 always degrades).
