// SPDX-License-Identifier: GPL-2.0-only
//
// Round 35 probe, not part of any build: a console program (net10.0, RollForward LatestMajor) that references the
// built DwarfMapper.IntegrationTests.dll and DwarfMapper.dll for Item19Mapper.
// Results and reading: ROUND35-TASKS.md, "The exact-zero tests read on a fresh thread" and the note after it.

using System;
using System.Collections.Generic;
using System.Threading;
using DwarfMapper.IntegrationTests;

// One window of 1000 maps per trial ON THE CALLING THREAD, which has just allocated (so it owns a part-used
// allocation buffer). Mode "retire": a blocking gen-0 collection right before the window. Mode "plain": none.
// Usage: gcwin2 <plain|retire> <map|plant> <bgc|rare|idle> <seconds>
internal static class Program
{
    private static object? _sink;

    private static void Main(string[] args)
    {
        var retire = args[0] == "retire";
        var plant = args[1] == "plant";
        var neighbour = args[2];
        var seconds = int.Parse(args[3]);
        var stop = false;
        var other = new Thread(() =>
        {
            var junk = new List<byte[]>();
            while (!Volatile.Read(ref stop))
            {
                if (neighbour == "idle") { Thread.SpinWait(100); continue; }
                junk.Add(new byte[100_000]);
                if (junk.Count > 200) junk.Clear();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false);
                if (neighbour == "rare") Thread.Sleep(20);
            }
        });
        other.Start();

        var mapper = new Item19Mapper();
        var warm = new Item19StructSrc[64];
        var warmDst = new Item19StructDst[64];
        for (var i = 0; i < 200; i++) mapper.MapSpan(warm, warmDst);
        GC.Collect(0, GCCollectionMode.Forced, true);

        long trials = 0, moved = 0, max = 0;
        var until = Environment.TickCount64 + seconds * 1000L;
        while (Environment.TickCount64 < until)
        {
            var src = new Item19StructSrc[64];          // the thread allocates, as a test does before its window
            var dst = new Item19StructDst[64];
            _sink = new byte[trials % 3000 + 24];       // and leaves its buffer at a varying fill
            if (retire) GC.Collect(0, GCCollectionMode.Forced, true);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) mapper.MapSpan(src, dst);
            if (plant) _sink = new object();
            var d = GC.GetAllocatedBytesForCurrentThread() - before;
            trials++;
            if (d != 0) { moved++; if (d > max) max = d; }
        }

        Volatile.Write(ref stop, true);
        other.Join();
        GC.KeepAlive(_sink);
        Console.WriteLine($"runtime {Environment.Version} {args[0]} {(plant ? "PLANTED" : "real map")} neighbour {neighbour}: trials {trials}; window non-zero {moved} (largest {max} B)");
    }
}
