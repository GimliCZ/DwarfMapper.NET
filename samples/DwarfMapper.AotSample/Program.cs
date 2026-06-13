// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using DwarfMapper;

// ── Basic identity mapper (original gate) ─────────────────────────────────────
var mapper = new SampleMapper();
var dto = mapper.ToDto(new Source { Id = 7, Label = "vein" });
Console.WriteLine($"{dto.Id}:{dto.Label}");

// ── Checked integral narrowing: long → int ────────────────────────────────────
// Emits: global::System.Int32.CreateChecked(v)  — AOT-safe static call.
var narrowMapper = new NarrowMapper();

var inRange = narrowMapper.Map(new LongHolder { V = 42L });
Console.WriteLine($"narrow in-range: {inRange.V}"); // 42

try
{
    narrowMapper.Map(new LongHolder { V = (long)int.MaxValue + 1L });
    Console.WriteLine("ERROR: expected OverflowException");
}
catch (OverflowException)
{
    Console.WriteLine("narrow overflow: OverflowException (correct)");
}

// ── string → int via IParsable<int>.Parse ─────────────────────────────────────
// Emits: global::System.Int32.Parse(v, CultureInfo.InvariantCulture)  — AOT-safe.
var strToIntMapper = new StringToIntAotMapper();
var parsed = strToIntMapper.Map(new StringHolder { V = "99" });
Console.WriteLine($"string→int: {parsed.V}"); // 99

try
{
    strToIntMapper.Map(new StringHolder { V = "not-a-number" });
    Console.WriteLine("ERROR: expected FormatException");
}
catch (FormatException)
{
    Console.WriteLine("string→int bad input: FormatException (correct)");
}

// ── string → Guid via IParsable<Guid>.Parse ───────────────────────────────────
var guidStr = "12345678-1234-5678-1234-567812345678";
var strToGuidMapper = new StringToGuidAotMapper();
var guidResult = strToGuidMapper.Map(new StringHolder { V = guidStr });
Console.WriteLine($"string→Guid: {guidResult.V}"); // 12345678-1234-5678-1234-567812345678

// ── int → string via IFormattable.ToString(null, InvariantCulture) ────────────
var intToStrMapper = new IntToStringAotMapper();
var strResult = intToStrMapper.Map(new IntHolder2 { V = -42 });
Console.WriteLine($"int→string: {strResult.V}"); // -42

// ── Positional record target with a converted ctor param (AOT gate) ────────────
// Emits: new RecordDest(Id: ..., Score: global::System.Int32.CreateChecked(__s.Score))
// Named-argument ctor call is concrete → AOT-safe (no reflection, no dynamic dispatch).
var recordMapper = new AotRecordMapper();
var recResult = recordMapper.Map(new AotRecordSrc { Id = 1, Label = "anvil", Score = 100L });
Console.WriteLine($"record: {recResult.Id}:{recResult.Label}:{recResult.Score}");
if (recResult.Id != 1 || recResult.Label != "anvil" || recResult.Score != 100)
{
    Console.WriteLine("ERROR: record mapping values incorrect");
    return 1;
}

try
{
    recordMapper.Map(new AotRecordSrc { Id = 2, Label = "overflow", Score = (long)int.MaxValue + 1L });
    Console.WriteLine("ERROR: expected OverflowException for record ctor param");
    return 1;
}
catch (OverflowException)
{
    Console.WriteLine("record ctor param overflow: OverflowException (correct)");
}

// ── Auto-synthesized 2-level nested object mapper (Plan 19 Part A) ───────────
// Generator synthesizes a private __DwarfMap_Obj_... method — no reflection.
var aotNestedMapper = new AotNestedMapper();
var aotNestedSrc = new AotOuterSrc
{
    Name = "Dwarven Gate",
    Inner = new AotInnerSrc { X = 10, Y = 20 }
};
var aotNestedDst = aotNestedMapper.Map(aotNestedSrc);
Console.WriteLine($"nested auto-map: {aotNestedDst.Name}, ({aotNestedDst.Inner.X},{aotNestedDst.Inner.Y})");
if (aotNestedDst.Name != "Dwarven Gate" || aotNestedDst.Inner.X != 10 || aotNestedDst.Inner.Y != 20)
{
    Console.WriteLine("ERROR: nested auto-map values incorrect");
    return 1;
}

// ── Depth-guarded recursive map: linked list (Plan 19 Part C1) ───────────────
// The generator emits a companion __DwarfMap_Depth_Map method with a depth guard.
// Chains within MaxDepth(8) map correctly; deeper chains throw DwarfMappingDepthException.
var depthMapper = new AotDepthNodeMapper();

// Chain of 5 (within MaxDepth=8) → maps successfully
var chain5Root = new AotNode { V = 1, Next = new AotNode { V = 2, Next = new AotNode { V = 3, Next = new AotNode { V = 4, Next = new AotNode { V = 5 } } } } };
var chain5Dto = depthMapper.Map(chain5Root);
var count = 0;
for (var n = chain5Dto; n is not null; n = n.Next) count++;
Console.WriteLine($"depth-guarded chain5: mapped {count} nodes (expected 5)");
if (count != 5) { Console.WriteLine("ERROR: chain5 count wrong"); return 1; }

// Chain of 10 (over MaxDepth=8) → DwarfMappingDepthException (not StackOverflow)
var chain10Root = new AotNode { V = 0 };
var cur = chain10Root;
for (var i = 1; i < 10; i++) { var next = new AotNode { V = i }; cur.Next = next; cur = next; }
try
{
    depthMapper.Map(chain10Root);
    Console.WriteLine("ERROR: expected DwarfMappingDepthException for deep chain");
    return 1;
}
catch (DwarfMapper.DwarfMappingDepthException ex)
{
    Console.WriteLine($"depth-guarded chain10: DwarfMappingDepthException caught (MaxDepth={ex.MaxDepth}) — correct");
}

// ── None mode: depth guard THROUGH a collection edge (Plan 19 Part-C follow-up) ─
// A self-referential type reached via a List<T> depth-caps with a catchable exception instead of
// a silent StackOverflow — the shared depth ctx is threaded into the (re-synthesized) element mapper.
var collDepthMapper = new AotCollDepthMapper();
var cdHead = new AotCollNode { V = 0 };
var cdCur = cdHead;
for (var i = 1; i < 20; i++) { var n = new AotCollNode { V = i }; cdCur.Kids = new System.Collections.Generic.List<AotCollNode> { n }; cdCur = n; }
try
{
    collDepthMapper.Map(cdHead); // 20 deep > MaxDepth(8)
    Console.WriteLine("ERROR: expected DwarfMappingDepthException for deep collection chain");
    return 1;
}
catch (DwarfMapper.DwarfMappingDepthException)
{
    Console.WriteLine("none-mode collection depth: DwarfMappingDepthException (not StackOverflow) — correct");
}

// ── Preserve mode: 2-node cycle — topology reconstruction (Plan 19 Part C2) ───
// The generator emits register-before-populate code so every source node is mapped
// exactly once and back-edges are relinked using a ReferenceEqualityComparer-keyed
// identity map.  ReferenceEqualityComparer and Dictionary are AOT-safe.
var cycleMapper = new AotCycleMapper();

var cA = new CycleNode { V = 10 };
var cB = new CycleNode { V = 20 };
cA.Next = cB;
cB.Next = cA; // cycle: A→B→A

var tA = cycleMapper.Map(cA);
Console.WriteLine($"preserve cycle: tA.V={tA.V}, tA.Next.V={tA.Next!.V}");
if (tA.V != 10) { Console.WriteLine("ERROR: preserve cycle tA.V wrong"); return 1; }
if (tA.Next.V != 20) { Console.WriteLine("ERROR: preserve cycle tA.Next.V wrong"); return 1; }
if (!ReferenceEquals(tA, tA.Next.Next))
{
    Console.WriteLine("ERROR: preserve cycle back-edge not closed (tA.Next.Next != tA)");
    return 1;
}
Console.WriteLine("preserve cycle back-edge: closed correctly (AOT-safe)");

// ── OnCycle = SetNull: None-mode cycle breaking (Plan 19 Part C) ──────────────
// AOT-safe: on-stack HashSet<object>(ReferenceEqualityComparer) guard; the re-entrant
// back-edge is nulled (≡ System.Text.Json IgnoreCycles) — no reflection, no dynamic dispatch.
var setNullMapper = new AotSetNullMapper();

var snA = new SetNullNode { V = 10 };
var snB = new SetNullNode { V = 20 };
snA.Next = snB;
snB.Next = snA; // cycle: A→B→A

var snResult = setNullMapper.Map(snA);
Console.WriteLine($"setnull cycle: A.V={snResult.V}, A.Next.V={snResult.Next!.V}, A.Next.Next={(snResult.Next.Next is null ? "null" : "NOT-null")}");
if (snResult.V != 10) { Console.WriteLine("ERROR: setnull A.V wrong"); return 1; }
if (snResult.Next.V != 20) { Console.WriteLine("ERROR: setnull A.Next.V wrong"); return 1; }
if (snResult.Next.Next is not null)
{
    Console.WriteLine("ERROR: setnull back-edge not nulled (B.Next should be null)");
    return 1;
}
Console.WriteLine("setnull cycle: back-edge nulled, finite projection (AOT-safe)");

// ── OnCycle = SetNull through a COLLECTION edge (Plan 19 Part-C follow-up) ─────
// The shared ctx threads through the collection element mapper, so a cycle routed through
// a List<T> breaks (the re-entrant element becomes null) — AOT-safe, no fresh context.
var setNullCollMapper = new AotSetNullCollMapper();
var snRoot = new SetNullTreeNode { V = 1 };
var snChild = new SetNullTreeNode { V = 2 };
snRoot.Children = new System.Collections.Generic.List<SetNullTreeNode> { snChild };
snChild.Children = new System.Collections.Generic.List<SetNullTreeNode> { snRoot }; // cycle through the list
var snCollResult = setNullCollMapper.Map(snRoot);
if (snCollResult.V != 1 || snCollResult.Children is null || snCollResult.Children.Count != 1 || snCollResult.Children[0].V != 2)
{
    Console.WriteLine("ERROR: setnull collection mapping values incorrect");
    return 1;
}
if (snCollResult.Children[0].Children is null || snCollResult.Children[0].Children!.Count != 1 || snCollResult.Children[0].Children![0] is not null)
{
    Console.WriteLine("ERROR: setnull collection back-edge not nulled");
    return 1;
}
Console.WriteLine("setnull collection cycle: list back-edge nulled, terminates (AOT-safe)");

// ── IEnumerable<T> target (lazy path) + ImmutableArray<T>? (A2 coverage) ─────
var collAotMapper = new CollAotMapper();
var collSrc = new CollAotSrc
{
    Items = new int[] { 1, 2, 3 },
    Names = new string[] { "ore", "stone" },
};
var collDst = collAotMapper.Map(collSrc);
var itemList = new System.Collections.Generic.List<int>(collDst.Items);
if (itemList.Count != 3 || itemList[0] != 1)
{
    Console.WriteLine("ERROR: IEnumerable<int> mapping incorrect");
    return 1;
}
if (!collDst.Names.HasValue || collDst.Names.Value.Length != 2 || collDst.Names.Value[0] != "ore")
{
    Console.WriteLine("ERROR: ImmutableArray<string> mapping incorrect");
    return 1;
}
Console.WriteLine($"coll aot: IEnumerable={itemList.Count} ImmutableArray={collDst.Names!.Value.Length} items");

// null source + AsNull → ImmutableArray<int>? must yield HasValue=false
var nullCollSrc = new CollAotSrc { Items = null, Names = null };
var nullCollDst = collAotMapper.Map(nullCollSrc);
if (nullCollDst.Names.HasValue)
{
    Console.WriteLine("ERROR: null source should yield ImmutableArray<int>? HasValue=false");
    return 1;
}
Console.WriteLine("coll aot AsNull: ImmutableArray<string>? null source → HasValue=false (correct)");

// ── [FlattenGraph]: BFS graph collapse with topology degradation (Plan 20) ─────
// ReferenceEqualityComparer + Queue + HashSet are AOT-safe (no reflection).
// A 2-node cycle: fgA → fgB → fgA; flattened → 2 distinct nodes, edges nulled.
var fgMapper = new AotFlattenGraphMapper();
var fgA = new FgAotNode { Name = "alpha" };
var fgB = new FgAotNode { Name = "beta" };
fgA.Next = fgB;
fgB.Next = fgA; // cycle

var fgRoot = new FgAotRoot { Entry = fgA, Label = "mine" };
var fgResult = fgMapper.Map(fgRoot);
Console.WriteLine($"flatten-graph: nodes={fgResult.Nodes.Count}, label={fgResult.Label}");
if (fgResult.Nodes.Count != 2) { Console.WriteLine("ERROR: flatten-graph node count wrong"); return 1; }
if (fgResult.Label != "mine") { Console.WriteLine("ERROR: flatten-graph root label wrong"); return 1; }
// Edges must be null (topology degraded)
if (fgResult.Nodes.Any(n => n.Next is not null)) { Console.WriteLine("ERROR: flatten-graph edge not nulled"); return 1; }
Console.WriteLine("flatten-graph cycle: 2 nodes, edges nulled (topology degradation correct, AOT-safe)");

// ── [MapDerivedType] polymorphic dispatch (Plan 21 AOT gate) ──────────────────
var polyMapper = new AotPolyMapper();
var dogResult = polyMapper.Map(new AotPolyDog { Name = "Rex", Breed = "Husky" });
Console.WriteLine($"poly Dog: Name={dogResult.Name}");
if (dogResult.Name != "Rex") { Console.WriteLine("ERROR: poly Dog name wrong"); return 1; }
var catResult = polyMapper.Map(new AotPolyCat { Name = "Luna", Lives = 9 });
Console.WriteLine($"poly Cat: Name={catResult.Name}");
try
{
    polyMapper.Map(new AotPolyUnknown { Name = "?" });
    Console.WriteLine("ERROR: expected ArgumentException for unregistered type");
    return 1;
}
catch (ArgumentException)
{
    Console.WriteLine("poly unregistered: ArgumentException (correct)");
}
Console.WriteLine("poly dispatch: all AOT checks passed.");

// ── Heterogeneous [FlattenGraph] (Plan 22 AOT gate) ───────────────────────────
// AOT-safe: BFS with HashSet<object>(ReferenceEqualityComparer) + runtime-type switch
// (no reflection). Each concrete node type maps to the correct derived DTO.
var heteroFgMapper = new AotHeteroFgMapper();

// Build: root-folder → [file1, sub-folder → [file2]]
// Total nodes: root, file1, sub, file2 = 4
var aotFile1 = new AotFsFile { Name = "readme.txt", Size = 42L };
var aotFile2 = new AotFsFile { Name = "main.cs",    Size = 100L };
var aotSub   = new AotFsFolder { Name = "src",
    Children = new System.Collections.Generic.List<AotFsNode> { aotFile2 } };
var aotRoot  = new AotFsFolder { Name = "root",
    Children = new System.Collections.Generic.List<AotFsNode> { aotFile1, aotSub } };

var heteroTree = new AotFsTree { Root = aotRoot, Tag = "plan22" };
var heteroResult = heteroFgMapper.Map(heteroTree);

Console.WriteLine($"hetero-flatten: nodes={heteroResult.Nodes.Count}, tag={heteroResult.Tag}");
if (heteroResult.Nodes.Count != 4) { Console.WriteLine($"ERROR: hetero-flatten node count wrong (got {heteroResult.Nodes.Count})"); return 1; }
if (heteroResult.Tag != "plan22") { Console.WriteLine("ERROR: hetero-flatten root tag wrong"); return 1; }

// Verify correct derived DTO types: 2 folders (root, src) + 2 files (readme, main.cs)
var folderDtos = heteroResult.Nodes.OfType<AotFsFolderDto>().ToList();
var fileDtos   = heteroResult.Nodes.OfType<AotFsFileDto>().ToList();
if (folderDtos.Count != 2) { Console.WriteLine($"ERROR: hetero-flatten expected 2 FolderDto, got {folderDtos.Count}"); return 1; }
if (fileDtos.Count != 2)   { Console.WriteLine($"ERROR: hetero-flatten expected 2 FileDto, got {fileDtos.Count}"); return 1; }

// Verify edges are degraded (null) — topology intentionally lost
if (folderDtos.Any(f => f.Children is not null)) { Console.WriteLine("ERROR: hetero-flatten FolderDto.Children not null (topology not degraded)"); return 1; }

// Verify leaf members preserved
var file1Dto = fileDtos.FirstOrDefault(f => f.Name == "readme.txt");
if (file1Dto is null) { Console.WriteLine("ERROR: hetero-flatten file1 not found by name"); return 1; }
if (file1Dto.Size != 42L) { Console.WriteLine($"ERROR: hetero-flatten file1 Size wrong (got {file1Dto.Size})"); return 1; }

// Cross-type cycle: folder → file → folder via Parent (cycle safety)
var aotCycleFile   = new AotFsFile   { Name = "cycleFile.txt", Size = 1L };
var aotCycleFolder = new AotFsFolder { Name = "cycleFolder",
    Children = new System.Collections.Generic.List<AotFsNode> { aotCycleFile } };
aotCycleFile.Parent = aotCycleFolder; // cross-type back-edge
var cycleTree = new AotFsTree { Root = aotCycleFolder };
var cycleResult = heteroFgMapper.Map(cycleTree);
if (cycleResult.Nodes.Count != 2) { Console.WriteLine($"ERROR: hetero-flatten cycle node count wrong (got {cycleResult.Nodes.Count})"); return 1; }
Console.WriteLine("hetero-flatten: cycle terminates correctly (AOT-safe)");

// Unregistered type → ArgumentException (loud, never silent)
var aotUnknown = new AotFsSymlink { Name = "link.lnk" };
var unknownTree = new AotFsTree { Root = aotUnknown };
try
{
    heteroFgMapper.Map(unknownTree);
    Console.WriteLine("ERROR: hetero-flatten expected ArgumentException for unregistered type");
    return 1;
}
catch (ArgumentException)
{
    Console.WriteLine("hetero-flatten unregistered: ArgumentException (correct, loud)");
}

Console.WriteLine("hetero-flatten [FlattenGraph]: all AOT checks passed.");

// ── Update-into-existing: void/T Map(S src, T dest) (Planned → shipped) ───────
// Maps onto an existing instance (no construction; identity preserved). AOT-safe — plain assignment.
var updMapper = new AotUpdateMapper();
var existing = new AotUpdDst { Id = 0, Label = "old", Score = -1 };
var updated = updMapper.Update(new AotUpdSrc { Id = 9, Label = "new", Score = 7L }, existing);
if (!ReferenceEquals(existing, updated) || updated.Id != 9 || updated.Label != "new" || updated.Score != 7)
{
    Console.WriteLine("ERROR: update-into mapping incorrect");
    return 1;
}
Console.WriteLine($"update-into: same instance, Id={updated.Id} Label={updated.Label} Score={updated.Score} (AOT-safe)");

// ── Zero-alloc span map: void Map(ReadOnlySpan<S> src, Span<D> dst) (Planned → shipped) ─
// Element-wise map into a stack-allocated buffer — no heap allocation, AOT-safe (no reflection).
var spanMapper = new AotSpanMapper();
Span<long> spanDst = stackalloc long[3];
ReadOnlySpan<int> spanSrc = stackalloc int[] { 11, 22, 33 };
spanMapper.Map(spanSrc, spanDst);
if (spanDst[0] != 11L || spanDst[1] != 22L || spanDst[2] != 33L)
{
    Console.WriteLine("ERROR: span map values incorrect");
    return 1;
}
var spanThrew = false;
try { Span<long> tiny = stackalloc long[1]; spanMapper.Map(spanSrc, tiny); }
catch (ArgumentException) { spanThrew = true; }
if (!spanThrew) { Console.WriteLine("ERROR: span map should throw on too-small destination"); return 1; }
Console.WriteLine("span map: mapped into stack buffer; too-small dest throws (AOT-safe, zero-alloc)");

Console.WriteLine("AOT gate: all checks passed.");
return 0;

// ── Types ─────────────────────────────────────────────────────────────────────

public class Source { public int Id { get; set; } public string Label { get; set; } = ""; }
public class Target { public int Id { get; set; } public string Label { get; set; } = ""; }

[DwarfMapper]
public partial class SampleMapper
{
    public partial Target ToDto(Source s);
}

public class LongHolder { public long V { get; set; } }
public class IntHolder  { public int  V { get; set; } }

[DwarfMapper]
public partial class NarrowMapper { public partial IntHolder Map(LongHolder s); }

public class StringHolder { public string V { get; set; } = ""; }
public class IntHolder2   { public int    V { get; set; } }
public class GuidHolder   { public Guid   V { get; set; } }
public class StrHolder2   { public string V { get; set; } = ""; }

[DwarfMapper] public partial class StringToIntAotMapper  { public partial IntHolder2 Map(StringHolder s); }
[DwarfMapper] public partial class StringToGuidAotMapper { public partial GuidHolder  Map(StringHolder s); }
[DwarfMapper] public partial class IntToStringAotMapper  { public partial StrHolder2  Map(IntHolder2 s); }

// ── Positional record target with converted ctor param ───────────────────────
// Score: long → int via CreateChecked (loud on overflow) — concrete named-arg call, AOT-safe.
public class AotRecordSrc { public int Id { get; set; } public string Label { get; set; } = ""; public long Score { get; set; } }
public record AotRecordDest(int Id, string Label, int Score);

[DwarfMapper]
public partial class AotRecordMapper { public partial AotRecordDest Map(AotRecordSrc s); }

// ── Auto-nested types (Plan 19 Part A) ───────────────────────────────────────
public class AotInnerSrc { public int X { get; set; } public int Y { get; set; } }
public class AotOuterSrc { public string Name { get; set; } = ""; public AotInnerSrc Inner { get; set; } = new(); }
public record AotInnerDst(int X, int Y);
public record AotOuterDst(string Name, AotInnerDst Inner);

[DwarfMapper]
public partial class AotNestedMapper { public partial AotOuterDst Map(AotOuterSrc s); }

// ── Depth-guarded recursive linked list (Plan 19 Part C1) ────────────────────
// MaxDepth=8 means chains longer than 8 throw DwarfMappingDepthException, not StackOverflow.
public class AotNode    { public int V { get; set; } public AotNode?    Next { get; set; } }
public class AotNodeDto { public int V { get; set; } public AotNodeDto? Next { get; set; } }

[DwarfMapper(MaxDepth = 8)]
public partial class AotDepthNodeMapper { public partial AotNodeDto Map(AotNode n); }

// None-mode self-referential type through a collection edge — depth-guarded (no silent SO).
public class AotCollNode    { public int V { get; set; } public System.Collections.Generic.IReadOnlyList<AotCollNode>? Kids { get; set; } }
public class AotCollNodeDto { public int V { get; set; } public System.Collections.Generic.IReadOnlyList<AotCollNodeDto>? Kids { get; set; } }

[DwarfMapper(MaxDepth = 8)]
public partial class AotCollDepthMapper { public partial AotCollNodeDto Map(AotCollNode n); }

// Update-into-existing target types.
public class AotUpdSrc { public int Id { get; set; } public string Label { get; set; } = ""; public long Score { get; set; } }
public class AotUpdDst { public int Id { get; set; } public string Label { get; set; } = ""; public int Score { get; set; } }

[DwarfMapper]
public partial class AotUpdateMapper { public partial AotUpdDst Update(AotUpdSrc src, AotUpdDst dest); }

// Zero-alloc span map.
[DwarfMapper]
public partial class AotSpanMapper { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }

// ── Preserve mode: 2-node cycle (Plan 19 Part C2) ────────────────────────────
// ReferenceEqualityComparer + Dictionary<object,object> are reflection-free and AOT-safe.
public class CycleNode    { public int V { get; set; } public CycleNode?    Next { get; set; } }
public class CycleNodeDto { public int V { get; set; } public CycleNodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class AotCycleMapper { public partial CycleNodeDto Map(CycleNode n); }

// ── OnCycle = SetNull: None-mode cycle breaking (Plan 19 Part C) ──────────────
// On-stack HashSet<object>(ReferenceEqualityComparer) guard — reflection-free, AOT-safe.
public class SetNullNode    { public int V { get; set; } public SetNullNode?    Next { get; set; } }
public class SetNullNodeDto { public int V { get; set; } public SetNullNodeDto? Next { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class AotSetNullMapper { public partial SetNullNodeDto Map(SetNullNode n); }

// SetNull through a collection edge — shared ctx threaded into the collection element mapper.
public class SetNullTreeNode    { public int V { get; set; } public System.Collections.Generic.IReadOnlyList<SetNullTreeNode>? Children { get; set; } }
public class SetNullTreeNodeDto { public int V { get; set; } public System.Collections.Generic.IReadOnlyList<SetNullTreeNodeDto>? Children { get; set; } }

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class AotSetNullCollMapper { public partial SetNullTreeNodeDto Map(SetNullTreeNode n); }

// ── IEnumerable<T> target + ImmutableArray<T>? with AsNull (A5 AOT coverage) ─
// IEnumerable<int> target: generator emits lazy Enumerable.Select (no materialisation).
// ImmutableArray<string>?: AsNull + null source yields HasValue=false (A2 coverage).
public class CollAotSrc
{
    public System.Collections.Generic.IReadOnlyList<int>? Items { get; set; }
    public System.Collections.Generic.IReadOnlyList<string>? Names { get; set; }
}
public class CollAotDst
{
    public System.Collections.Generic.IEnumerable<int> Items { get; set; } = System.Array.Empty<int>();
    public System.Collections.Immutable.ImmutableArray<string>? Names { get; set; }
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class CollAotMapper { public partial CollAotDst Map(CollAotSrc s); }

// ── [FlattenGraph] types (Plan 20 AOT gate) ───────────────────────────────────
// AOT-safe: ReferenceEqualityComparer, Queue<T>, HashSet<object> — no reflection.
public class FgAotNode    { public string Name { get; set; } = ""; public FgAotNode? Next { get; set; } }
public class FgAotNodeDto { public string Name { get; set; } = ""; public FgAotNodeDto? Next { get; set; } }
public class FgAotRoot    { public FgAotNode? Entry { get; set; } public string Label { get; set; } = ""; }
public class FgAotRootDto { public System.Collections.Generic.IReadOnlyList<FgAotNodeDto> Nodes { get; set; } = new System.Collections.Generic.List<FgAotNodeDto>(); public string Label { get; set; } = ""; }

[DwarfMapper]
public partial class AotFlattenGraphMapper
{
    [FlattenGraph(nameof(FgAotRoot.Entry), nameof(FgAotRootDto.Nodes))]
    public partial FgAotRootDto Map(FgAotRoot root);
}

// ── [MapDerivedType] types (Plan 21 AOT gate) ─────────────────────────────────
public abstract class AotPolyAnimalBase { public string Name { get; set; } = ""; }
public class AotPolyDog : AotPolyAnimalBase { public string Breed { get; set; } = ""; }
public class AotPolyCat : AotPolyAnimalBase { public int Lives { get; set; } }
public class AotPolyUnknown : AotPolyAnimalBase { }
public class AotPolyAnimalBaseDto { public string Name { get; set; } = ""; }
public class AotPolyDogDto : AotPolyAnimalBaseDto { public string Breed { get; set; } = ""; }
public class AotPolyCatDto : AotPolyAnimalBaseDto { public int Lives { get; set; } }

[DwarfMapper]
public partial class AotPolyMapper
{
    [MapDerivedType<AotPolyDog, AotPolyDogDto>]
    [MapDerivedType<AotPolyCat, AotPolyCatDto>]
    public partial AotPolyAnimalBaseDto Map(AotPolyAnimalBase a);

    public partial AotPolyDogDto Map(AotPolyDog d);
    public partial AotPolyCatDto Map(AotPolyCat c);
}

// ── Heterogeneous [FlattenGraph] types (Plan 22 AOT gate) ─────────────────────
// AOT-safe: concrete switch (no reflection), ReferenceEqualityComparer, no dynamic dispatch.
// Each concrete node type maps to the correct derived DTO; cross-type cycles terminate.
public abstract class AotFsNode
{
    public string Name { get; set; } = "";
    public AotFsNode? Parent { get; set; }
}
public class AotFsFolder : AotFsNode
{
    // IReadOnlyList<> satisfies CA1002 (prefer read-only collection interface) and CA2227 (no public setter)
    public System.Collections.Generic.IReadOnlyList<AotFsNode> Children { get; set; } = new System.Collections.Generic.List<AotFsNode>();
}
public class AotFsFile   : AotFsNode { public long Size { get; set; } }
public class AotFsSymlink : AotFsNode { } // unregistered — used for loud-failure test
public abstract class AotFsNodeDto { public string Name { get; set; } = ""; }
public class AotFsFolderDto : AotFsNodeDto
{
    public System.Collections.Generic.IReadOnlyList<AotFsNodeDto>? Children { get; set; }
    public AotFsNodeDto? Parent { get; set; }
}
public class AotFsFileDto   : AotFsNodeDto { public long Size { get; set; } public AotFsNodeDto? Parent { get; set; } }
public class AotFsTree    { public AotFsNode? Root { get; set; } public string Tag { get; set; } = ""; }
public class AotFsTreeDto
{
    public System.Collections.Generic.IReadOnlyList<AotFsNodeDto> Nodes { get; set; } = new System.Collections.Generic.List<AotFsNodeDto>();
    public string Tag { get; set; } = "";
}

[DwarfMapper]
public partial class AotHeteroFgMapper
{
    [FlattenGraph(nameof(AotFsTree.Root), nameof(AotFsTreeDto.Nodes))]
    [MapDerivedType<AotFsFolder, AotFsFolderDto>]
    [MapDerivedType<AotFsFile, AotFsFileDto>]
    public partial AotFsTreeDto Map(AotFsTree tree);
}
