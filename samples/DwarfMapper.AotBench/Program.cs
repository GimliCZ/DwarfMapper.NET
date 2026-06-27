// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Diagnostics;
using System.Globalization;
using DwarfMapper;

// NativeAOT benchmark + STABILITY harness. Published with PublishAot=true and run as a native binary, this
// (1) measures DwarfMapper's hot paths under real AOT codegen, and (2) hunts for instabilities that only an
// actually-AOT-compiled run can reveal:
//   • SIMD widen/blit bit-exactness at EVERY size around the vector boundary (the vector-body/scalar-tail
//     seam is the classic place AOT codegen can diverge from JIT);
//   • intermittent failures or non-determinism over many repeated runs (Preserve topology, depth guard);
//   • timing variance (reported, not failed — OS jitter is expected on a non-RT box).
// Exit 0 = stable + correct; 1 = a correctness/stability instability was detected.

var ci = CultureInfo.InvariantCulture;
var failures = 0;
void Fail(string msg) { Console.WriteLine("  INSTABILITY: " + msg); failures++; }

Console.WriteLine("== DwarfMapper NativeAOT bench + stability ==");
Console.WriteLine("Vector.IsHardwareAccelerated = " + System.Numerics.Vector.IsHardwareAccelerated.ToString(ci)
    + " ; Vector<int>.Count = " + System.Numerics.Vector<int>.Count.ToString(ci));

// ── 1. SIMD widen (int[] -> long[]) bit-exactness across the vector boundary ──────────────────
Console.WriteLine("[1] SIMD widen boundary correctness (incl. negatives / sign extension)");
foreach (var n in new[] { 0, 1, 2, 3, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 1000, 1001 })
{
    var src = new int[n];
    for (var i = 0; i < n; i++) src[i] = i - n / 2; // mix of negative + positive
    var dst = new WidenMapper().Map(new WidenSrc { V = src }).V;
    if (dst.Length != n) { Fail($"widen length n={n}: got {dst.Length}"); continue; }
    for (var i = 0; i < n; i++)
    {
        if (dst[i] != src[i]) { Fail($"widen n={n} i={i}: {dst[i].ToString(ci)} != {((long)src[i]).ToString(ci)}"); break; }
    }
}

// ── 2. SIMD blit (Vec3[] struct reinterpret) bit-exactness across the boundary ────────────────
Console.WriteLine("[2] SIMD blit boundary correctness (struct reinterpret)");
foreach (var n in new[] { 0, 1, 2, 7, 8, 9, 16, 17, 64, 1000 })
{
    var src = new Vec3Src[n];
    for (var i = 0; i < n; i++) src[i] = new Vec3Src { X = i, Y = i + 0.5f, Z = i + 0.25f };
    var dst = new BlitMapper().Map(new BlitSrc { Items = src }).Items;
    if (dst.Length != n) { Fail($"blit length n={n}: got {dst.Length}"); continue; }
    for (var i = 0; i < n; i++)
    {
        if (dst[i].X != src[i].X || dst[i].Y != src[i].Y || dst[i].Z != src[i].Z) { Fail($"blit n={n} i={i}"); break; }
    }
}

// ── 3. Preserve topology determinism over many runs ───────────────────────────────────────────
Console.WriteLine("[3] Preserve cycle determinism x100000");
for (var r = 0; r < 100_000; r++)
{
    var a = new Node { V = 10 };
    var b = new Node { V = 20 };
    a.Next = b; b.Next = a; // 2-node cycle
    var t = new PreserveMapper().Map(a);
    if (t.V != 10 || t.Next is null || t.Next.V != 20 || !ReferenceEquals(t.Next.Next, t))
    {
        Fail($"preserve cycle non-deterministic at run {r}");
        break;
    }
}

// ── 4. Depth guard is catchable (never a StackOverflow), repeatable ───────────────────────────
Console.WriteLine("[4] Depth-guard catchable x20000");
for (var r = 0; r < 20_000; r++)
{
    var head = new Node { V = 1 };
    head.Next = head; // self-cycle exceeds MaxDepth
    try { new DepthMapper().Map(head); Fail($"depth guard did not throw at run {r}"); break; }
    catch (DwarfMappingDepthException) { /* correct, catchable */ }
}

// ── 5. SetNull yields a finite acyclic projection, repeatable ─────────────────────────────────
Console.WriteLine("[5] OnCycle=SetNull acyclic projection x50000");
for (var r = 0; r < 50_000; r++)
{
    var head = new Node { V = 7 };
    head.Next = head;
    var t = new SetNullMapper().Map(head);
    if (t.V != 7 || t.Next != null) { Fail($"setnull back-edge not nulled at run {r}"); break; }
}

// ── 6. Timing under AOT (median ns/op + min/max → variance signal) ────────────────────────────
Console.WriteLine("[6] Timing under NativeAOT (ns/op: median [min..max])");
var flat = new FlatSrc { Id = 7, Name = "vein", Score = 42, Active = true };
var arr = new ArraySrc { Items = MakeFlat(1000) };
var blit = new BlitSrc { Items = MakeVecs(1000) };
var widen = new WidenSrc { V = MakeInts(1000) };
var flatM = new FlatMapper();
var arrM = new ArrayMapper();
var blitM = new BlitMapper();
var widenM = new WidenMapper();

Report("Flat (1 obj)", 2_000_000, () => flatM.Map(flat));
Report("Array (1000)", 5_000, () => arrM.Map(arr));
Report("Blit (1000)", 20_000, () => blitM.Map(blit));
Report("Widen (1000)", 20_000, () => widenM.Map(widen));

Console.WriteLine(failures == 0
    ? "== AOT STABILITY: all correctness/determinism checks passed =="
    : $"== AOT STABILITY: {failures.ToString(ci)} INSTABILITY(IES) DETECTED ==");
return failures == 0 ? 0 : 1;

void Report(string name, int iters, Action body)
{
    for (var i = 0; i < Math.Min(iters, 2000); i++) body(); // warmup
    var samples = new double[15];
    for (var s = 0; s < samples.Length; s++)
    {
        GC.Collect();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iters; i++) body();
        sw.Stop();
        samples[s] = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iters;
    }
    Array.Sort(samples);
    var med = samples[samples.Length / 2];
    var min = samples[0];
    var max = samples[samples.Length - 1];
    var ratio = min > 0 ? max / min : 0;
    var note = ratio > 5 ? "  <- high variance" : "";
    Console.WriteLine($"  {name,-14} {med.ToString("F1", ci),9} [{min.ToString("F1", ci)}..{max.ToString("F1", ci)}]{note}");
}

static FlatSrc[] MakeFlat(int n)
{
    var a = new FlatSrc[n];
    for (var i = 0; i < n; i++) a[i] = new FlatSrc { Id = i, Name = "n", Score = i, Active = i % 2 == 0 };
    return a;
}
static Vec3Src[] MakeVecs(int n)
{
    var a = new Vec3Src[n];
    for (var i = 0; i < n; i++) a[i] = new Vec3Src { X = i, Y = i + 1, Z = i + 2 };
    return a;
}
static int[] MakeInts(int n)
{
    var a = new int[n];
    for (var i = 0; i < n; i++) a[i] = i - n / 2;
    return a;
}

// ── Domain types + mappers ────────────────────────────────────────────────────────────────────
public sealed class FlatSrc { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } public bool Active { get; set; } }
public sealed class FlatDst { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } public bool Active { get; set; } }

public sealed class ArraySrc { public FlatSrc[] Items { get; set; } = Array.Empty<FlatSrc>(); }
public sealed class ArrayDst { public FlatDst[] Items { get; set; } = Array.Empty<FlatDst>(); }

public struct Vec3Src { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
public struct Vec3Dst { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
public sealed class BlitSrc { public Vec3Src[] Items { get; set; } = Array.Empty<Vec3Src>(); }
public sealed class BlitDst { public Vec3Dst[] Items { get; set; } = Array.Empty<Vec3Dst>(); }

public sealed class WidenSrc { public int[] V { get; set; } = Array.Empty<int>(); }
public sealed class WidenDst { public long[] V { get; set; } = Array.Empty<long>(); }

public sealed class Node { public int V { get; set; } public Node? Next { get; set; } }
public sealed class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }

[DwarfMapper] public partial class FlatMapper { public partial FlatDst Map(FlatSrc s); }
[DwarfMapper] public partial class ArrayMapper { public partial ArrayDst Map(ArraySrc s); }
[DwarfMapper] public partial class BlitMapper { public partial BlitDst Map(BlitSrc s); }
[DwarfMapper] public partial class WidenMapper { public partial WidenDst Map(WidenSrc s); }
[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)] public partial class PreserveMapper { public partial NodeDto Map(Node n); }
[DwarfMapper(MaxDepth = 16)] public partial class DepthMapper { public partial NodeDto Map(Node n); }
[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)] public partial class SetNullMapper { public partial NodeDto Map(Node n); }
