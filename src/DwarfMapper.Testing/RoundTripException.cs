// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DwarfMapper.Testing;

/// <summary>Thrown when a round-trip mapping fails to reproduce the original, with an informed dump.</summary>
public sealed class RoundTripException : Exception
{
    /// <summary>Creates a round-trip failure with the offending seed, iteration, and diffs.</summary>
    public RoundTripException(int seed, int iteration, IReadOnlyList<MemberDiff> diffs)
        : base(Build(seed, iteration, diffs))
    {
        Seed = seed;
        Iteration = iteration;
        Diffs = diffs;
    }

    /// <summary>The fuzz seed that produced the failure (replay with this seed).</summary>
    public int Seed { get; }

    /// <summary>The iteration index within the run.</summary>
    public int Iteration { get; }

    /// <summary>The structural differences found.</summary>
    public IReadOnlyList<MemberDiff> Diffs { get; }

    private static string Build(int seed, int iteration, IReadOnlyList<MemberDiff> diffs)
    {
        var header = string.Format(
            CultureInfo.InvariantCulture,
            "Round-trip mismatch [seed: {0}, iteration: {1}]\n", seed, iteration);
        return header + StructuralComparer.Render(diffs);
    }
}
