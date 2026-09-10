// SPDX-License-Identifier: GPL-2.0-only
// CASE: [MapDenseEnumKeys] where one enum member falls outside the destination inline array (round 29 T3.2)
// WHY:  The directive replaces a hash lookup with a raw index — dst[(int)kv.Key - Offset] — so it is a
//       mapping only while every index it can produce is inside the array. `Platform.Desktop = 3` is not:
//       the destination declares three slots, so the legal indices are 0, 1 and 2.
//
//       An ERROR rather than a quiet fall-back to the ordinary dictionary copy, and that is the whole point
//       of this case. A fall-back would leave the consumer believing a directive is in force that is not,
//       and a bounds-checked slow path would be exactly the "hide an unprovable shape behind a runtime
//       check" this feature is forbidden to emit. The fixes the message names are all cheap: widen the
//       inline array, set Offset, or delete the attribute and map the dictionary.
//
//       The message names the MEMBER and its value, not just the mapper. The consumer's next action is to
//       open their enum, and an error that only said "some member is out of range" would not tell them
//       which one — with a fifteen-member enum that is the difference between a fix and a search.
//
//       Note what is NOT here, and is the tier below this one: an enum member at exactly n-1 is ACCEPTED.
//       Off-by-one is this feature's whole failure mode, so the accepted side of the boundary is pinned by
//       MapDenseEnumKeysTests.The_last_slot_is_accepted_and_one_past_it_is_refused rather than left to
//       inference from this refusal.
//
//       DWARF078 accompanies it because DWARF105 is an ERROR: the mapper is refused, so the partial Map
//       method reports CS8795, which DWARF078 exists to signpost.
// EXPECT: DWARF105, DWARF078
// EXPECT-MESSAGE DWARF105: Demo.Platform.Desktop
// EXPECT-MESSAGE DWARF105: Widen the inline array, set Offset

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DwarfMapper;

namespace Demo;

public enum Platform
{
    Web = 0,
    Ios = 1,
    Android = 2,
    Desktop = 3
}

[InlineArray(3)]
public struct Counts3
{
    private int _e0;
}

public class DenseSrc
{
    public int UserId { get; set; }

    public Dictionary<Platform, int> Counts { get; set; } = new();
}

public class DenseDst
{
    public int UserId { get; set; }

    public Counts3 Counts { get; set; }
}

[DwarfMapper]
public partial class DenseEnumKeyOutOfRangeMapper
{
    [MapDenseEnumKeys("Counts")]
    public partial DenseDst Map(DenseSrc s);
}
