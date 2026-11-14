// SPDX-License-Identifier: GPL-2.0-only
//
// Round 35 probe, not part of any build: a console program (net10.0, top-level statements).
// Usage: dotnet gcprobe.dll <gc|full|bgc|total|totalmem|nogc|loh|alloc|idle> [once]
// Results and reading: ROUND35-TASKS.md, "A red net11 check on PR #13, and its cause".

using System;
using System.Collections.Generic;
using System.Threading;

// Does something ANOTHER thread does move GC.GetAllocatedBytesForCurrentThread() on a thread that allocates nothing?
var stop = false;
var mode = args.Length > 0 ? args[0] : "gc";
var other = new Thread(() =>
{
    var junk = new List<byte[]>();
    while (!Volatile.Read(ref stop))
    {
        switch (mode)
        {
            case "gc": GC.Collect(0); break;
            case "full": GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); break;
            case "bgc": junk.Add(new byte[100_000]); if (junk.Count > 200) junk.Clear(); GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false); break;
            case "total": GC.GetTotalAllocatedBytes(true); break;
            case "totalmem": GC.GetTotalMemory(true); break;
            case "nogc": if (GC.TryStartNoGCRegion(1_000_000)) { try { GC.EndNoGCRegion(); } catch (InvalidOperationException) { } } break;
            case "alloc": junk.Add(new byte[4096]); if (junk.Count > 2000) junk.Clear(); break;
            case "loh": junk.Add(new byte[200_000]); if (junk.Count > 50) junk.Clear(); break;
            default: Thread.SpinWait(100); break;
        }
    }
});
other.Start();

var rearm = args.Length < 2;                   // a second argument: allocate once, before the first window only
var firstMoved = -1L; var lastMoved = -1L;
var keep = new object();                       // gives this thread a live allocation context
long moved = 0, windows = 0, movedUnmarked = 0, marked = 0;
var seen = new SortedDictionary<long, int>();
var until = Environment.TickCount64 + 4000;
var gcs0 = GC.CollectionCount(0);
var gcs2 = GC.CollectionCount(2);
while (Environment.TickCount64 < until)
{
    if (rearm) keep = new object();
    var g2 = GC.CollectionCount(2);
    var before = GC.GetAllocatedBytesForCurrentThread();
    Thread.SpinWait(3000);                     // no allocation
    var d = GC.GetAllocatedBytesForCurrentThread() - before;
    var sawGen2 = GC.CollectionCount(2) != g2;
    windows++;
    if (sawGen2) marked++;
    if (d != 0) { moved++; seen[d] = seen.GetValueOrDefault(d) + 1; if (!sawGen2) movedUnmarked++; if (firstMoved < 0) firstMoved = windows; lastMoved = windows; }
}
Volatile.Write(ref stop, true);
other.Join();
GC.KeepAlive(keep);
Console.WriteLine($"runtime {Environment.Version} mode {mode}: {windows} windows, {GC.CollectionCount(0) - gcs0} gen0 / {GC.CollectionCount(2) - gcs2} gen2 GCs, {moved} windows moved; {marked} windows saw the gen2 count change; MOVED WITHOUT A COUNT CHANGE: {movedUnmarked}; rearm {rearm}; first/last moved window {firstMoved}/{lastMoved}");
var n = 0;
foreach (var kv in seen) { if (n++ < 8) Console.WriteLine($"   delta {kv.Key} B x {kv.Value}"); }
