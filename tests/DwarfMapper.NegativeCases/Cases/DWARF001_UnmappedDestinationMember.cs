// SPDX-License-Identifier: GPL-2.0-only
// CASE: A destination member with nothing to fill it
// WHY:  Silence here is how AutoMapper loses data — an unmatched destination member simply stays default and
//       the build stays green. DwarfMapper refuses instead, and the refusal has to name the escape hatch or
//       it reads as a wall rather than a decision point.
// EXPECT: DWARF001, DWARF097
// EXPECT-MESSAGE DWARF001: 'Nickname'
// EXPECT-MESSAGE DWARF001: [MapIgnore(
// EXPECT-CS: CS8795
// NOTE: DWARF097, not DWARF078 (I17). The refusal is confined to the method that is actually incomplete, so
//       the class is still emitted and only THIS method loses its implementing part — one CS8795 instead of
//       one per partial method on the mapper. This case has a single method, so the CS count is the same; the
//       DIFFERENCE is that a sibling method would now survive, which CompletenessScopedRefusalTests counts.

using DwarfMapper;

namespace Demo;

public class User
{
    public string Name { get; set; } = "";
}

public class UserDto
{
    public string Name { get; set; } = "";
    public string Nickname { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    public partial UserDto Map(User source);
}
