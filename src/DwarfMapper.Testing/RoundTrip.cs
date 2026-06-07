// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Testing;

/// <summary>Verifies that mapping a value forward and back reproduces the original, over fuzzed inputs.</summary>
public static class RoundTrip
{
    /// <summary>
    /// Generate <paramref name="iterations"/> seeded <typeparamref name="TSource"/> instances and assert
    /// <c>backward(forward(x))</c> structurally equals <c>x</c>. Throws <see cref="RoundTripException"/> on
    /// the first mismatch, with a mapping-aware diff.
    /// </summary>
    public static void Verify<TSource, TDto>(
        Func<TSource, TDto> forward, Func<TDto, TSource> backward, int seed = 12345, int iterations = 100)
    {
        if (forward is null)
        {
            throw new ArgumentNullException(nameof(forward));
        }
        if (backward is null)
        {
            throw new ArgumentNullException(nameof(backward));
        }

        var rng = new Random(seed);
        for (var i = 0; i < iterations; i++)
        {
            var itemSeed = rng.Next();
            var original = ObjectFactory.Create<TSource>(itemSeed);
            var roundTripped = backward(forward(original));
            var diffs = StructuralComparer.Diff(original, roundTripped);
            if (diffs.Count > 0)
            {
                throw new RoundTripException(itemSeed, i, diffs);
            }
        }
    }
}
