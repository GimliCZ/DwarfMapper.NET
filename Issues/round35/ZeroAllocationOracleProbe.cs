// SPDX-License-Identifier: GPL-2.0-only
//
// Round 35 probe, not part of any build: a console program (net10.0) that references the built
// DwarfMapper.IntegrationTests.dll and DwarfMapper.dll for Item19Mapper.
// Usage: dotnet spanprobe.dll <map|plant> <bgc|idle>      (DOTNET_gcConcurrent=0 for the control)
// Results and reading: ROUND35-TASKS.md, "A red net11 check on PR #13, and its cause".

using System;
using System.Collections.Generic;
using System.Threading;
using DwarfMapper.IntegrationTests;

// The zero-allocation span test's own body (Item19Mapper.MapSpan from the built test assembly) as TRIALS of five
// windows, beside a thread that keeps background collections going. Each trial first allocates, as the test does
// (its arrays), so this thread holds a part-used allocation buffer when the windows start.
//   mode "map"   - the real map, which allocates nothing: how often does ONE window read non-zero (the old test's
//                  failure), and how often do ALL FIVE (the new test's)?
//   mode "plant" - the same with one allocation planted per window: the new oracle must fail every trial.
internal static class Program
{
    private static object? _sink;

    private static void Main(string[] args)
    {
        var plant = args.Length > 0 && args[0] == "plant";
        var neighbour = args.Length > 1 ? args[1] : "bgc";
        var stop = false;
        var other = new Thread(() =>
        {
            var junk = new List<byte[]>();
            while (!Volatile.Read(ref stop))
            {
                if (neighbour == "bgc")
                {
                    junk.Add(new byte[100_000]);
                    if (junk.Count > 200) junk.Clear();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false);
                }
                else
                {
                    Thread.SpinWait(100);
                }
            }
        });
        other.Start();

        var mapper = new Item19Mapper();
        var warm = new Item19StructSrc[64];
        var warmDst = new Item19StructDst[64];
        for (var i = 0; i < 200; i++) mapper.MapSpan(warm, warmDst);

        long trials = 0, firstWindowMoved = 0, anyWindowMoved = 0, allFiveMoved = 0, twoOrMore = 0;
        var windows = new long[5];
        var until = Environment.TickCount64 + 6000;
        while (Environment.TickCount64 < until)
        {
            var src = new Item19StructSrc[64];          // the test's own allocations, before its windows
            var dst = new Item19StructDst[64];
            for (var w = 0; w < windows.Length; w++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1000; i++) mapper.MapSpan(src, dst);
                if (plant) _sink = new object();
                windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            trials++;
            var moved = 0;
            foreach (var w in windows) if (w != 0) moved++;
            if (windows[0] != 0) firstWindowMoved++;
            if (moved > 0) anyWindowMoved++;
            if (moved >= 2) twoOrMore++;
            if (moved == windows.Length) allFiveMoved++;
        }

        Volatile.Write(ref stop, true);
        other.Join();
        GC.KeepAlive(_sink);
        Console.WriteLine($"runtime {Environment.Version} {(plant ? "PLANTED allocation" : "real map")}, neighbour {neighbour}: {trials} trials; " +
                          $"first window non-zero (old test fails) {firstWindowMoved}; any window {anyWindowMoved}; two or more {twoOrMore}; " +
                          $"all five non-zero (new test fails) {allFiveMoved}");
    }
}
