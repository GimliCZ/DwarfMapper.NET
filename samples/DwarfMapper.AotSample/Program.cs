// SPDX-License-Identifier: GPL-2.0-only
using System;
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

// ── Preserve mode: 2-node cycle (Plan 19 Part C2) ────────────────────────────
// ReferenceEqualityComparer + Dictionary<object,object> are reflection-free and AOT-safe.
public class CycleNode    { public int V { get; set; } public CycleNode?    Next { get; set; } }
public class CycleNodeDto { public int V { get; set; } public CycleNodeDto? Next { get; set; } }

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class AotCycleMapper { public partial CycleNodeDto Map(CycleNode n); }
