// SPDX-License-Identifier: GPL-2.0-only

using System;
using BenchmarkDotNet.Running;
using DwarfMapper.UnionBenchmarks;

// `dry`: every arm on every payload once, the results compared with the hand-written arm's - seconds, no measurement.
// Anything else is BenchmarkDotNet's own command line (--filter, --job, --inProcess, ...), forwarded; with no filter
// both classes run.
if (args.Length == 1 && args[0] == "dry")
{
    return DryRun.Run();
}

BenchmarkRunner.Run([typeof(PaymentBenchmarks), typeof(ReadingBenchmarks)], null, args);
return 0;
