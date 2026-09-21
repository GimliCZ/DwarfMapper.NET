// SPDX-License-Identifier: GPL-2.0-only
// CASE: the DWARF101 shape with the remedy its own message names — the same five fields, declared in the
//       order the diagnostic printed — which must go silent
// REMEDY-FOR: DWARF101
// WHY:  Second half of the DWARF101 pair. Identical to DWARF101_PaddedStruct except for the one edit the
//       message asks for: "declaring its fields as Id, Value, Code, Ok, Kind makes it 24 bytes". Same names,
//       same types, same mapping, same block copy — 24 bytes instead of 40, and nothing left to report.
//
//       `EXPECT: none` is the whole assertion, and it pins both directions at once. The remedy has to
//       actually silence the diagnostic (a hint whose own fix does not work is worse than no hint), and the
//       set being EXACT means it must not silence it by breaking something else: a remedy that dropped the
//       pair out of the fast path, or off the mapping, would surface here as another id rather than as
//       silence.
//
//       It also pins the lower edge of the thresholds from the useful side. The packed struct still wastes
//       4 bytes of its 24 — a sixth, and under the 8-byte floor — so a threshold loosened in either
//       direction turns this file red.
// EXPECT: none

using DwarfMapper;

namespace Demo;

public struct Sample
{
    public long Id;

    public double Value;

    public short Code;

    public bool Ok;

    public byte Kind;
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
