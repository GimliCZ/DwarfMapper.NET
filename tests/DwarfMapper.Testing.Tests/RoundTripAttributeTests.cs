// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.Testing.Tests;

public class RtOrder { public int Id { get; set; } public string Name { get; set; } = ""; }
public class RtOrderDto { public int Id { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class RtMapper
{
    [RoundTrip] public partial RtOrderDto ToDto(RtOrder o);
    public partial RtOrder FromDto(RtOrderDto d);
}

public class RoundTripAttributeTests
{
    [Fact]
    public void Generated_verifier_passes_for_lossless_pair()
    {
        // The generator emitted VerifyRoundTrip_ToDto; calling it fuzz-verifies the round trip.
        new RtMapper().VerifyRoundTrip_ToDto(seed: 7, iterations: 50);
    }
}
