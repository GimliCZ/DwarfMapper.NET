// SPDX-License-Identifier: GPL-2.0-only
//
// Round 35 probe, not part of any build: a console program (net10.0, RollForward LatestMajor) that references the
// built DwarfMapper.IntegrationTests.dll and DwarfMapper.dll for Item19Mapper.
// Results and reading: ROUND35-TASKS.md, "The exact-zero tests read on a fresh thread" and the note after it.

using System;
using System.Collections.Generic;
using System.Threading;
using DwarfMapper.IntegrationTests;

// One window of 1000 maps per trial, measured on a FRESH thread after a blocking gen-0 collection on that thread, which
// retires its allocation buffer. "dirty": the thread allocates first, as a thread start does on macOS (152 B measured
// there). Usage: gcwin <map|mapdirty|plant|plantdirty> <bgc|rare|idle> <seconds>
internal static class Program
{
    private static object? _sink;

    private sealed class Window
    {
        public Item19Mapper Mapper = null!;
        public Item19StructSrc[] Src = null!;
        public Item19StructDst[] Dst = null!;
        public bool Plant;
        public bool Dirty;
        public long Moved;
        public long AtStart;

        public void Run()
        {
            if (Dirty) _sink = new byte[152];                      // what macOS's thread start leaves: the thread has allocated
            GC.Collect(0, GCCollectionMode.Forced, true);          // retires this thread's allocation buffer, exactly
            var before = GC.GetAllocatedBytesForCurrentThread();
            AtStart = before;
            for (var i = 0; i < 1000; i++) Mapper.MapSpan(Src, Dst);
            if (Plant) _sink = new object();
            Moved = GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private static void Main(string[] args)
    {
        var plant = args[0].StartsWith("plant");
        var dirty = args[0].EndsWith("dirty");
        var neighbour = args[1];
        var seconds = int.Parse(args[2]);
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

        long trials = 0, moved = 0, startedNonZero = 0, maxStart = 0;
        var until = Environment.TickCount64 + seconds * 1000L;
        while (Environment.TickCount64 < until)
        {
            var w = new Window { Mapper = mapper, Src = new Item19StructSrc[64], Dst = new Item19StructDst[64], Plant = plant, Dirty = dirty };
            var t = new Thread(w.Run);
            t.Start();
            t.Join();
            trials++;
            if (w.Moved != 0) moved++;
            if (w.AtStart != 0) { startedNonZero++; if (w.AtStart > maxStart) maxStart = w.AtStart; }
        }

        Volatile.Write(ref stop, true);
        other.Join();
        GC.KeepAlive(_sink);
        Console.WriteLine($"runtime {Environment.Version} {(plant ? "PLANTED" : "real map")} neighbour {neighbour}: trials {trials}; window non-zero {moved}; " +
                          $"thread had allocated before the window {startedNonZero} (max {maxStart} B)");
    }
}
