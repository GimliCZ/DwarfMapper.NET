// SPDX-License-Identifier: GPL-2.0-only
// CASE: [MapValue] supplies a constant for a target that also has a same-named readable source member
// WHY:  The constant silently masks real source data — typically a stub left from before the source member
//       existed. Informational, because the mapping still works and the shadow may be deliberate.
//
//       This case exists as the FIRST half of a pair. Its sibling _Remedy case applies the fix the message
//       names and must go silent. That pairing is the point: until it existed, nothing in this suite asked
//       whether a diagnostic's advice can actually be followed, and DWARF064's advice could not be —
//       [MapIgnoreSource] never reached the check, so a reader who did exactly what the message said watched
//       the diagnostic survive. A message naming an inert remedy is worse than one naming none, because the
//       reader concludes the tool is broken rather than the sentence.
// EXPECT: DWARF064
// EXPECT-MESSAGE DWARF064: overrides the source member 'Name'
// EXPECT-MESSAGE DWARF064: [MapIgnoreSource("Name")]

using DwarfMapper;

namespace Demo;

public class Src
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

public class Dst
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

[DwarfMapper]
public partial class M
{
    [MapValue(nameof(Dst.Name), Use = nameof(FixedName))]
    public partial Dst Map(Src source);

    private static string FixedName()
    {
        return "constant";
    }
}
