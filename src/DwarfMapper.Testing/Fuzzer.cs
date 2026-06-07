// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;

namespace DwarfMapper.Testing;

/// <summary>Generates deterministic sequences of populated instances for property-based tests.</summary>
public static class Fuzzer
{
    /// <summary>Yield <paramref name="count"/> seeded instances of <typeparamref name="T"/>.</summary>
    public static IEnumerable<T> Generate<T>(int count, int seed = 0)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            yield return (T)ObjectFactory.Create(typeof(T), rng, 0)!;
        }
    }
}
