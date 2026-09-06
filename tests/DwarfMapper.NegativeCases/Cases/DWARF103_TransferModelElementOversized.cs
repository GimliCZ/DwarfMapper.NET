// SPDX-License-Identifier: GPL-2.0-only
// CASE: a transfer-model class past the 64-byte band, mapped as a collection element — still worth being a
//       struct, and now worth passing by `in` (round 29, T2.2)
// WHY:  The third of the three rows the T2.2 brief specifies, and the one that pins the `in` remedy. The
//       classifier answers TooLarge above 64 bytes rather than refusing: a large transfer model is still a
//       struct worth having — the array is still one allocation instead of N — it is merely too big to copy
//       by value at every call. Nine longs is 72 bytes.
//
//       IT IS THE FLAG'S TRAP THAT MAKES THIS ROW LOAD-BEARING. `Verdict.SuggestIn` is a BAND flag, true
//       between 32 and 64 bytes and FALSE on TooLarge, so a report reading it alone would print the `in`
//       advice for a 40-byte model and omit it for a 72-byte one — dropping it for exactly the types that
//       need it most. The site reads `Kind == TooLarge`, which is the OTHER half of that trap: reading
//       `TooLarge || SuggestIn` advises `in` at 40 bytes too, and the measurement does not support it (a
//       64-byte struct still beat its class by value, 26.4 ns against 29.9 ns). The spec tiers the bands
//       ≤32 B silent, 32-64 B Info, >64 B suggest `in`, and the project owner ruled that tiering reasonable
//       (Issues/round29/RESEARCH-hardware-mode.md section 8, ruling (b); the measurement is section 7).
//       This file fails if the top band
//       ever loses the advice; its middle-band sibling in TransferModelDiagnosticTests fails if the middle
//       band gains it.
//
//       THE BLOCK-COPY CLAUSE IS ABSENT HERE, deliberately: `OversizedSource` holds a string, and
//       BlittableProof requires BOTH element types to be unmanaged, so the pair can never blit whatever the
//       consumer declares. That absence is NOT asserted by this file, and the correction matters more than
//       the claim did (T2.2 fix round 2): EXPECT-MESSAGE is a SUBSTRING match — NegativeCaseTests asks
//       `rendered.Exists(m => m.Contains(substring))` — so a row here can pin what a message says and never
//       what it does not. The absence is pinned where it can be, by the DoesNotContain assertions in
//       TransferModelDiagnosticTests, including two asymmetric fixtures that hold each half of the gate
//       independently. What this file pins is the `in` wording, which had no pin outside the unit suite.
// EXPECT: DWARF103
// EXPECT-MESSAGE DWARF103: 'Demo.OversizedSource' → 'Demo.OversizedDto' allocates one 'Demo.OversizedDto' per element
// EXPECT-MESSAGE DWARF103: it is 72 bytes, and the collection becomes one allocation instead of one per element
// EXPECT-MESSAGE DWARF103: At 72 bytes it is over the 64-byte limit for copying by value, so pass it by 'in'

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public sealed class OversizedSource
{
    public long A { get; set; }

    public long B { get; set; }

    public long C { get; set; }

    public long D { get; set; }

    public long E { get; set; }

    public long F { get; set; }

    public long G { get; set; }

    public long H { get; set; }

    public long I { get; set; }

    public string Note { get; set; } = "";
}

public sealed class OversizedDto
{
    public long A { get; set; }

    public long B { get; set; }

    public long C { get; set; }

    public long D { get; set; }

    public long E { get; set; }

    public long F { get; set; }

    public long G { get; set; }

    public long H { get; set; }

    public long I { get; set; }
}

public class OversizedHolder
{
    public List<OversizedSource> Rows { get; set; } = [];
}

public class OversizedHolderDto
{
    public List<OversizedDto> Rows { get; set; } = [];
}

[DwarfMapper]
public partial class OversizedTransferModelMapper
{
    public partial OversizedHolderDto Map(OversizedHolder s);
}
