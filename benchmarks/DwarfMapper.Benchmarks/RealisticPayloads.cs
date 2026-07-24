// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper.Testing;

/// <summary>
///     Builds benchmark payloads from <see cref="ObjectFactoryV2" /> — the same fixture/fuzz source the test
///     suites drive the mapper with — instead of hand-written literals.
///     <para>
///     The suite previously constructed its data inline (<c>Id = i</c>, <c>Name = "n" + i</c>,
///     <c>Active = i % 2 == 0</c>): near-constant string lengths, no nulls, no boundary numerics, perfectly
///     regular branches. That measures a workload nobody has, and it under-measures exactly where mappers
///     differ — null branches, null-substitute paths, conversion edge cases, and branch misprediction.
///     <see cref="ObjectFactoryV2" /> draws null for a nullable position with probability
///     <see cref="ObjectFactoryV2.NullProbability" /> and a boundary value (0, MinValue, MaxValue, ±1) with
///     probability <see cref="ObjectFactoryV2.EdgeProbability" />, which is precisely the distribution the
///     fuzzers use.
///     </para>
///     <para>
///     <b>Collection SIZE stays explicit.</b> The factory generates 1–3 element collections, which would
///     silently shrink an N=1000 benchmark to N≈2. So the element COUNT is controlled here (driven by the
///     benchmark's <c>[Params]</c>) while each element's CONTENT comes from the factory. Element roots are
///     created at depth 0, which the factory never returns null for, so a collection never contains a null
///     element — only null MEMBERS inside elements.
///     </para>
///     <para>
///     Reflection is used here deliberately and only inside <c>[GlobalSetup]</c>, which BenchmarkDotNet does
///     not measure. Nothing on a measured path touches it.
///     </para>
/// </summary>
internal static class RealisticPayloads
{
    /// <summary>
    ///     Pinned so a run is reproducible and two commits are comparable. Changing it re-rolls every payload
    ///     and invalidates comparison with previously recorded results — treat it as part of the benchmark
    ///     contract, not a knob.
    /// </summary>
    public const int Seed = 20260724;

    /// <summary>
    ///     <paramref name="count" /> independently-seeded instances. Each element gets its own seed so the
    ///     collection is not <paramref name="count" /> copies of one draw — which would let the branch
    ///     predictor learn the shape and flatter every mapper equally.
    /// </summary>
    public static T[] Elements<T>(int count, int salt = 0)
    {
        var items = new T[count];
        for (var i = 0; i < count; i++) items[i] = ObjectFactoryV2.Create<T>(Seed + salt + i);
        return items;
    }

    /// <summary>One instance, for the single-object categories.</summary>
    public static T One<T>(int salt = 0)
    {
        return ObjectFactoryV2.Create<T>(Seed + salt);
    }

    /// <summary>
    ///     A dictionary of <paramref name="count" /> entries with factory-drawn values. Keys are generated
    ///     locally rather than by the factory: a drawn key can repeat or be null, and both silently shrink the
    ///     dictionary below <paramref name="count" /> — changing the measured workload size without failing.
    /// </summary>
    public static Dictionary<string, int> Map(int count, int salt = 0)
    {
        var d = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
            d["k" + i.ToString(CultureInfo.InvariantCulture)] = ObjectFactoryV2.Create<int>(Seed + salt + i);
        return d;
    }

    /// <summary>
    ///     Fails the run when the generated payload is degenerate — i.e. when it contains no nulls and no
    ///     boundary values and is therefore the uniform data this change exists to replace.
    ///     <para>
    ///     A claim nobody checks is worthless: without this, a future change to the factory's probabilities (or
    ///     a seed that happens to draw nothing interesting) would quietly restore the old flat distribution
    ///     while every benchmark still reported green. Called from <c>[GlobalSetup]</c>.
    ///     </para>
    /// </summary>
    public static void AssertRealistic<T>(IReadOnlyList<T> sample, string label)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var nulls = 0;
        var edges = 0;

        foreach (var item in sample)
        foreach (var p in props)
        {
            if (!p.CanRead) continue;
            var v = p.GetValue(item);
            if (v is null)
            {
                nulls++;
                continue;
            }

            // Boundary draws the old hand-built data could never produce.
            switch (v)
            {
                case int n when n is 0 or -1 or 1 or int.MaxValue or int.MinValue:
                case long l when l is 0 or -1 or 1 or long.MaxValue or long.MinValue:
                case string s when s.Length == 0:
                    edges++;
                    break;
            }
        }

        if (nulls == 0 && edges == 0)
            throw new InvalidOperationException(
                $"{label}: payload contains neither a null nor a boundary value across {sample.Count} instances "
                + $"of {typeof(T).Name}. The generator has regressed to a uniform distribution, which is exactly "
                + "what this payload source replaced — investigate before trusting any number from this run.");
    }
}
