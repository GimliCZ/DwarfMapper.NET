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
//       need it most. The site reads `Kind == TooLarge || SuggestIn`, and this file is what fails if anyone
//       simplifies that back to the flag.
//
//       THE BLOCK-COPY CLAUSE IS ABSENT HERE, deliberately, and that absence is asserted by the EXACT set
//       plus the messages below rather than merely hoped for: `OversizedSource` holds a string, so it is not
//       transfer-model shaped without a reference member, and BlittableProof requires BOTH element types to
//       be unmanaged. The pair can never blit whatever the consumer declares, and the message that hedges
//       its byte count for that same reason must not then assert the consequence as fact. Its counterpart
//       DWARF103_TransferModelElement.cs pins the clause where it IS earned.
// EXPECT: DWARF103
// EXPECT-MESSAGE DWARF103: 'Demo.OversizedSource' → 'Demo.OversizedDto' allocates one 'Demo.OversizedDto' per element
// EXPECT-MESSAGE DWARF103: it is 72 bytes, and the collection becomes one allocation instead of one per element
// EXPECT-MESSAGE DWARF103: At 72 bytes it is over the 32-byte threshold for copying by value, so pass it by 'in'

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
