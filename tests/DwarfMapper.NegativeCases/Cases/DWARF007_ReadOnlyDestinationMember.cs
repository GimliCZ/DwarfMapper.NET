// SPDX-License-Identifier: GPL-2.0-only
// CASE: A get-only destination member the source WOULD have filled
// WHY:  The source has a Value and the destination has a Value, so it looks mapped; but the destination's is
//       get-only, so the value is silently dropped. This is the shape that started the CS8795 hunt in Round
//       18 — the real signal was here, buried under the cascade it caused.
// EXPECT: DWARF007, DWARF078
// EXPECT-MESSAGE DWARF007: 'Value'
// EXPECT-MESSAGE DWARF007: [MapIgnore(
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class Src
{
    public int Value { get; set; }
}

public class Dst
{
    public int Value { get; }
}

[DwarfMapper]
public partial class M
{
    public partial Dst Map(Src source);
}
