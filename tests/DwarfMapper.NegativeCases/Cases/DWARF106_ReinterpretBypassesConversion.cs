// SPDX-License-Identifier: GPL-2.0-only
// CASE: a member carrying [Reinterpret] whose element pair ALSO has a user-declared conversion method on the
//       mapper (round 29, T0.2c review fix 3)
// WHY:  T0.2c made one rule general — a proof enables a fast path, it never changes semantics — so a user
//       conversion for the element pair now keeps the element loop and the block copy stands down. [Reinterpret]
//       is the single deliberate exception: it names ONE member explicitly, while an auto-adopted converter is
//       ambient and may well have been written for a different member entirely. The explicit instruction wins.
//
//       Winning silently is the part that is not acceptable. The [Reinterpret] and the converter sit in
//       different places, nothing else in the build relates them, and the consequence — Scale() is never called
//       for these elements — is invisible in the output, which is a correct block copy either way.
//
//       INFORMATIONAL, and both louder options are wrong. An Error (the shape DWARF022 would have given) is a
//       false positive on correct code: this same mapper may use Scale legitimately for a scalar member, and
//       refusing the build over a combination that is intentional helps nobody. A Warning becomes a build
//       FAILURE under TreatWarningsAsErrors — the trap dwarf070 sprang on this project once already. An
//       intentional bypass is exactly what an Info is for; silence and refusal are the two wrong ends.
//
//       What is NOT here: a [Reinterpret] member with no conversion in sight. That is the ordinary, intended
//       use of the attribute and says nothing at all — the diagnostic reports a CONFLICT, not the attribute.
//       BlitSoundnessTests pins that silence so this row cannot drift into ambient noise.
// EXPECT: DWARF106
// EXPECT-MESSAGE DWARF106: takes the block copy
// EXPECT-MESSAGE DWARF106: 'Scale'
// EXPECT-MESSAGE DWARF106: remove [Reinterpret]

using DwarfMapper;

namespace Demo;

public struct BypassSrc
{
    public int X;
}

public struct BypassDst
{
    public int X;
}

public class BypassSource
{
    public BypassSrc[] Data { get; set; } = [];
}

public class BypassTarget
{
    public BypassDst[] Data { get; set; } = [];
}

[DwarfMapper]
public partial class BypassMapper
{
    public static BypassDst Scale(BypassSrc s)
    {
        return new BypassDst { X = s.X * 2 };
    }

    [Reinterpret("Data")]
    public partial BypassTarget Map(BypassSource s);
}
