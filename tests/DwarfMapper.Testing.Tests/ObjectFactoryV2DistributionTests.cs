// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;

namespace DwarfMapper.Testing.Tests;

/// <summary>
///     Meta-tests for what the fuzz INPUT generator can actually produce (ISSUE-031 / ISSUE-032).
///     <para>
///     A fuzz suite over a shrunken input space goes green no matter what — it cannot fail on a value it never
///     generates. ObjectFactoryV2 used to unwrap <c>Nullable&lt;T&gt;</c> to <c>T</c> and always return a value,
///     never returned a null reference, and drew every integral as <c>rng.Next(1, MaxValue)</c> — so no fuzz test
///     ever saw a null, a zero, a negative, or a type boundary. The whole null-handling engine (NullStrategy,
///     NullSubstitute, nullable→non-nullable, SkipNullSourceMembers, nullAsNull, null-propagation in synthesized
///     helpers) and most of the narrowing/sign-conversion machinery were therefore unfuzzed.
///     </para>
///     These assert the generator's REACH, so the space can never silently shrink back.
/// </summary>
public class ObjectFactoryV2DistributionTests
{
    private const int Seeds = 400;

    private static IEnumerable<object?> Sample(Type t, int seeds = Seeds, int depth = 0)
    {
        for (var seed = 0; seed < seeds; seed++)
            yield return ObjectFactoryV2.Create(t, new Random(seed), depth);
    }

    [Fact]
    public void Nullable_value_types_are_sometimes_null()
    {
        Assert.Contains(Sample(typeof(int?)), v => v is null);
        Assert.Contains(Sample(typeof(DateTime?)), v => v is null);

        // …and are not ALWAYS null either — a generator that only produced null would be just as blind.
        Assert.Contains(Sample(typeof(int?)), v => v is not null);
    }

    [Fact]
    public void Reference_types_are_sometimes_null_below_the_root()
    {
        // Depth > 0 only: roots are materialised with `!` by every caller, so a null root is a broken
        // fixture rather than an interesting input.
        Assert.Contains(Sample(typeof(string), depth: 1), v => v is null);
        Assert.DoesNotContain(Sample(typeof(string)), v => v is null);
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(short))]
    [InlineData(typeof(long))]
    public void Signed_integrals_reach_zero_negative_and_both_limits(Type t)
    {
        ArgumentNullException.ThrowIfNull(t);

        var values = Sample(t).Where(v => v is not null).Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToHashSet();

        var min = Convert.ToDecimal(t.GetField("MinValue")!.GetValue(null), CultureInfo.InvariantCulture);
        var max = Convert.ToDecimal(t.GetField("MaxValue")!.GetValue(null), CultureInfo.InvariantCulture);

        Assert.Contains(0m, values);
        Assert.Contains(values, v => v < 0m);
        Assert.Contains(min, values);
        Assert.Contains(max, values);
    }

    [Theory]
    [InlineData(typeof(byte))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(ulong))]
    public void Unsigned_integrals_reach_zero_and_max(Type t)
    {
        ArgumentNullException.ThrowIfNull(t);

        var values = Sample(t).Where(v => v is not null).Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToHashSet();
        Assert.Contains(0m, values);
        Assert.Contains(Convert.ToDecimal(t.GetField("MaxValue")!.GetValue(null), CultureInfo.InvariantCulture), values);
    }

    [Fact]
    public void Strings_are_sometimes_empty()
    {
        Assert.Contains(Sample(typeof(string)), v => (v as string)?.Length == 0);
    }

    [Fact]
    public void Collections_are_sometimes_empty_and_sometimes_large()
    {
        var sizes = Sample(typeof(List<int>))
            .Where(v => v is not null)
            .Select(v => ((ICollection)v!).Count)
            .ToHashSet();

        Assert.Contains(0, sizes);                  // empty-collection paths (AsEmpty/AsNull, unknown-count)
        Assert.Contains(sizes, n => n >= 8);        // beyond the old hard 1..3 ceiling
    }

    [Fact]
    public void Dictionary_keys_are_never_null()
    {
        // A null dictionary key throws, so it is not a legal input at any probability.
        foreach (var d in Sample(typeof(Dictionary<string, int>)).OfType<IDictionary>())
            foreach (var k in d.Keys)
                Assert.NotNull(k);
    }
}
