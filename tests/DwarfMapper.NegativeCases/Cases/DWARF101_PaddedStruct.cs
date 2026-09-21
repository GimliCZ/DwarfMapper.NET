// SPDX-License-Identifier: GPL-2.0-only
// CASE: a transfer-model struct whose declaration order spends half its bytes on padding, mapped as the
//       element of an array (round 29, T0.3)
// WHY:  This is the shape DWARF101 exists for: the mapping SUCCEEDS, takes the block copy, and is completely
//       silent about copying 20 bytes of nothing per element. The fields are the same fields either way —
//       reordering them costs the consumer one edit and makes every array of this type 40 % smaller, which is
//       measured rather than asserted (Issues/round29/RESEARCH-hardware-mode.md, row "D. layout hygiene":
//       0.57x-0.62x the time, 0.60x the memory, on this exact struct).
//
//       INFORMATIONAL on purpose. Nothing is wrong with the mapping, and nothing is wrong with the struct
//       either: grouping fields by meaning is a real reason to leave bytes on the floor. Raising this to a
//       warning would turn a taste-dependent hint into a build break under TreatWarningsAsErrors — the trap
//       DWARF070 already taught this project once.
//
//       The EXACT expected set is the other half of the assertion. The pair here BLITS, so DWARF100 must not
//       appear beside this: the near-miss speaks only where the fast path was refused, and a padded pair that
//       took it would otherwise never hear about its padding at all.
//
//       One report, not two, though the type appears on both sides of the pair and on two members: the
//       message names the TYPE and nothing about the member it was reached through, so the second and third
//       copies are dropped. An Info repeated per member is the shape consumers suppress wholesale.
// EXPECT: DWARF101
// EXPECT-MESSAGE DWARF101: 'Demo.Sample' is 40 bytes with 20 bytes of padding
// EXPECT-MESSAGE DWARF101: declaring its fields as Id, Value, Code, Ok, Kind makes it 24 bytes
// EXPECT-MESSAGE DWARF101: smaller arrays, and a layout-identical twin can take the blit

using DwarfMapper;

namespace Demo;

public struct Sample
{
    public bool Ok;

    public long Id;

    public byte Kind;

    public double Value;

    public short Code;
}

public class PaddedSource
{
    public Sample[] Rows { get; set; } = [];

    public Sample[] Archive { get; set; } = [];
}

public class PaddedTarget
{
    public Sample[] Rows { get; set; } = [];

    public Sample[] Archive { get; set; } = [];
}

[DwarfMapper]
public partial class PaddedMapper
{
    public partial PaddedTarget Map(PaddedSource s);
}
