// SPDX-License-Identifier: GPL-2.0-only
// CASE: a mapper with TWO Map methods over two pairs, where only ONE of them has an unmapped destination
// WHY:  DWARF001 is an Error and an error used to suppress the whole class, so this mapper generated
//       NOTHING — MapGood included — and DWARF078 announced it. MapGood was collateral: completeness is
//       evaluated over ONE (source, target) pair and honours ONE method's [MapIgnore] set, and the
//       diagnostic's own remedy names the method ("annotate the method with [MapIgnore(...)]"). Its unit of
//       evaluation and its unit of remedy are both the method, so it says nothing about the method beside it
//       (TASKS.md I17). A refusal is now proportional to what was refused: MapBad is withheld, MapGood is
//       emitted, and DWARF097 signposts the ONE CS8795 that follows instead of DWARF078 announcing a wall.
//
//       Note what is NOT here: DWARF078. Its absence is the assertion — the NegativeCases runner matches the
//       EXPECT set exactly, so a regression that restores the whole-class kill fails this case twice
//       (DWARF097 missing, DWARF078 unexpected). Note also that there is exactly ONE EXPECT-CS line: before
//       I17 there were two CS8795, one of them on a method nobody had broken.
//
//       A CLASS-level error still takes the class down, and so does an incomplete SYNTHESIZED pair — a
//       synthesized mapper is shared by every route that reaches it, so its incompleteness is true of each
//       of them. Both controls are pinned in CompletenessScopedRefusalTests, which also proves MapGood's
//       emitted body is byte-identical to what a solo mapper produces.
// EXPECT: DWARF001, DWARF097
// EXPECT-MESSAGE DWARF001: 'Missing'
// EXPECT-MESSAGE DWARF097: Mapping method 'MapBad' was not generated
// EXPECT-MESSAGE DWARF097: The rest of this mapper WAS generated: only this method is missing
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class GoodSrc
{
    public int Value { get; set; }
}

public class GoodDst
{
    public int Value { get; set; }
}

public class BadSrc
{
    public int Present { get; set; }
}

public class BadDst
{
    public int Present { get; set; }

    // Nothing on BadSrc fills this, and no [MapIgnore] excuses it.
    public int Missing { get; set; }
}

[DwarfMapper]
public partial class TwoPairMapper
{
    public partial GoodDst MapGood(GoodSrc s);

    public partial BadDst MapBad(BadSrc s);
}
