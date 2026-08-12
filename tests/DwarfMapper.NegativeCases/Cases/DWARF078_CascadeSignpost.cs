// SPDX-License-Identifier: GPL-2.0-only
// CASE: Three unmappable members, one signpost, and the CS8795 wall it explains
// WHY:  When any diagnostic on a mapper is an error the generator emits nothing, so every partial method
//       loses its implementing part at once and the build fills with CS8795. That wall looks IDENTICAL to the
//       other common cause — a project that never wired the analyzer — and the two have opposite fixes. A
//       ~300-map migration spent a debugging session on the wrong one. The signpost must therefore name the
//       mapper, name the real ids, and actively rule out the look-alike.
// EXPECT: DWARF007, DWARF078
// EXPECT-MESSAGE DWARF078: 'M'
// EXPECT-MESSAGE DWARF078: DWARF007
// EXPECT-MESSAGE DWARF078: CS8795
// EXPECT-MESSAGE DWARF078: analyzer reference
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class Src
{
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
}

public class Dst
{
    public int A { get; }
    public int B { get; }
    public int C { get; }
}

[DwarfMapper]
public partial class M
{
    public partial Dst First(Src source);

    public partial Dst Second(Src source);
}
