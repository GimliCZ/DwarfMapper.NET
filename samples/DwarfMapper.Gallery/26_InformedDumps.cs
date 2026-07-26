// SPDX-License-Identifier: GPL-2.0-only

// 26 — What a failure actually tells you, which is the reason the previous example is worth having.
//
// This mapper is deliberately LOSSY: Coin.Mint has nowhere to go in CoinDto, so mapping back cannot restore
// it. That is legal — only destination members must be mapped — and it is exactly the bug that survives code
// review. RoundTrip.Verify catches it and throws a STRUCTURAL diff: which member diverged, by member path,
// with expected vs. actual and the replay seed. Not two object dumps for you to eyeball.
//
// RoundTrip.Verify is the ad-hoc form; prefer [RoundTrip] (example 25) for the zero-boilerplate path.
// ObjectFactory.Create<T>(seed) and Fuzzer.Generate<T>(count, seed) build seeded fixtures for your own tests.

using DwarfMapper.Testing;

namespace DwarfMapper.Gallery.Ex26;

public sealed class Coin
{
    public int Id { get; set; }
    public string Mint { get; set; } = "";
}

public sealed class CoinDto
{
    public int Id { get; set; }   // no Mint — the round trip cannot survive this
}

// <snippet: informed-dumps>
[DwarfMapper]
public partial class Mapper
{
    public partial CoinDto ToDto(Coin c);

    [MapIgnore(nameof(Coin.Mint))]   // nothing to restore it from — stated, not forgotten
    public partial Coin FromDto(CoinDto d);
}
// </snippet>

[DocExample(26, Tier.Testing, "Informed failure dumps",
    Shows = "a failed round trip names the diverging member path, not two object dumps")]
public static class Example
{
    public static void Run()
    {
        var mapper = new Mapper();

        try
        {
            RoundTrip.Verify<Coin, CoinDto>(mapper.ToDto, mapper.FromDto, 7, 20);
            Console.WriteLine("26 Informed dumps     -> UNEXPECTED: the lossy map round-tripped");
        }
        catch (RoundTripException ex)
        {
            // One line of it is enough to make the point: the failure names the member, not the object.
            var headline = ex.Message.Split('\n')
                .FirstOrDefault(l => l.Contains("Mint", StringComparison.Ordinal))?.Trim()
                ?? ex.Message.Split('\n')[0].Trim();

            Console.WriteLine($"26 Informed dumps     -> caught as designed: {headline}");
        }
    }
}
