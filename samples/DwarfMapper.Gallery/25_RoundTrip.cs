// SPDX-License-Identifier: GPL-2.0-only

// 25 — The headline correctness feature. Tag a forward method [RoundTrip] and the generator emits
// VerifyRoundTrip_<Name>(seed, count), which fuzzes seeded inputs through forward-then-back and asserts
// Back(Forward(x)) is structurally equal to x. One attribute replaces the fixtures you used to hand-write.
//
// This is a TEST-TIME check: it needs the reflection-based DwarfMapper.Testing package. Without that
// reference the attribute is a no-op — the mapper itself stays reflection-free and AOT-safe. You would
// normally call the generated verifier from a [Fact]; this sample calls it directly so it runs in the
// Gallery.
//
// On mismatch it does not dump two objects at you: see example 26.

namespace DwarfMapper.Gallery.Ex25;

public sealed class Ledger
{
    public int Id { get; set; }
    public string Keeper { get; set; } = "";
}

public sealed class LedgerDto
{
    public int Id { get; set; }
    public string Keeper { get; set; } = "";
}

// <snippet: round-trip>
[DwarfMapper]
public partial class Mapper
{
    [RoundTrip]                                       // emits VerifyRoundTrip_ToDto(seed, count)
    public partial LedgerDto ToDto(Ledger l);

    public partial Ledger FromDto(LedgerDto d);       // the inverse it verifies against
}
// </snippet>

[DocExample(25, Tier.Testing, "`[RoundTrip]` verification",
    Shows = "one attribute emits a fuzzing harness asserting `Back(Forward(x)) == x`")]
public static class Example
{
    public static void Run()
    {
        // Normally: [Fact] public void Ledger_roundtrips() => new Mapper().VerifyRoundTrip_ToDto();
        new Mapper().VerifyRoundTrip_ToDto(7, 50);

        Console.WriteLine("25 [RoundTrip]        -> 50 fuzzed inputs survived forward-then-back (seed 7)");
    }
}
