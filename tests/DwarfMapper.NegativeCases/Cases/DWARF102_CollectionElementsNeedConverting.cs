// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateView<Src, Dst>] over a pair whose collection member has elements needing conversion
//       (List<int> -> List<long>) — round 29, Phase 1
// WHY:  A view is the create map's member resolution evaluated LAZILY against the source: every property is
//       an expression over a source the view borrows, and nothing is allocated. A converted collection has
//       to be BUILT, which is the one thing a view does not do — so the view is refused rather than emitted
//       with a member that quietly allocates on every read.
//
//       An ERROR, unlike DWARF100/101/103 beside it in this range. Those describe a mapping that is already
//       correct; this one says the thing the caller declared does not exist. A warning would leave
//       [GenerateView<Src, Dst>] written, accepted, and silently absent — the "the caller wrote something,
//       the generator accepted it, changed nothing, and said nothing" shape round 29 spent itself removing.
//
//       SCOPED to the view: the [GenerateMap] pair on the same class still generates. Taking a whole mapper
//       down because one member of one view is not viewable would be worse collateral than the fact
//       reported — the boundary DWARF028/DWARF096 established for an untranslatable projection member.
//       DWARF078 ("no code was generated") must therefore NOT appear.
// EXPECT: DWARF102
// EXPECT-MESSAGE DWARF102: 'Tags'
// EXPECT-MESSAGE DWARF102: would mean allocating the converted collection
// EXPECT-MESSAGE DWARF102: map the element type to itself

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public sealed class Src
{
    public int Id { get; set; }

    public List<int> Tags { get; set; } = new();
}

public sealed class Dst
{
    public int Id { get; set; }

    public List<long> Tags { get; set; } = new();
}

[DwarfMapper]
[GenerateMap<Src, Dst>]
[GenerateView<Src, Dst>]
public partial class M
{
}
