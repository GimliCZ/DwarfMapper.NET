// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateMap] would emit a Map method whose exact signature and return type already exists
// WHY:  Every [GenerateMap]-synthesized method is named `Map` and overloaded by the SOURCE type, so the same
//       pair declared twice — or declared beside a partial mapping method over the same pair — would emit two
//       members with an identical signature AND return type: CS0111 in the generated file plus a CS0121
//       ambiguity cascade at every call site, with no DwarfMapper word about a collision the generator
//       created (B27; eight surface-matrix cells sat in EmittedInvalidCode for exactly this). It is the gap
//       between DWARF060 (same signature, DIFFERENT return types) and DWARF057 (the generated mapper TYPE
//       collides). Refused rather than deduplicated, matching DWARF087 on [FlattenGraph]: keeping one of two
//       identical directives silently hides the mistake, and a co-located host's member directives bind to
//       declared pairs POSITIONALLY, so a duplicated pair shifts what a directive configures. Both shapes are
//       in this one file because the remedy sentence differs per shape and both wordings are pinned below.
// EXPECT: DWARF094, DWARF078
// EXPECT-MESSAGE DWARF094: Duplicate [GenerateMap]
// EXPECT-MESSAGE DWARF094: declared more than once on this class
// EXPECT-MESSAGE DWARF094: remove the duplicate [GenerateMap] attribute
// EXPECT-MESSAGE DWARF094: already declares a partial method with that exact signature over the same pair
// EXPECT-MESSAGE DWARF094: remove the [GenerateMap] and keep the partial method, or delete the partial method
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class DupSrc
{
    public int Id { get; set; }
}

public class DupDst
{
    public int Id { get; set; }
}

// Shape 1: the same pair declared twice — two identical `public DupDst Map(DupSrc)` would be emitted.
[DwarfMapper]
[GenerateMap<DupSrc, DupDst>]
[GenerateMap<DupSrc, DupDst>]
public partial class DupPairMapper
{
}

// Shape 2: the pair is already mapped by a declared partial method with the same name and signature.
// The partial is what makes the CS8795 below: DWARF094 is an Error, so the class emits nothing and the
// declared method loses its implementing part (see DWARF078 — the cascade is named, not separate).
[DwarfMapper]
[GenerateMap<DupSrc, DupDst>]
public partial class DupPartialMapper
{
    public partial DupDst Map(DupSrc s);
}
