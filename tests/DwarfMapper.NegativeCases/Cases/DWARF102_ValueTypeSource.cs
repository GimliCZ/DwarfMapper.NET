// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateView<Src, Dst>] whose SOURCE is a struct — round 29, Phase 1
// WHY:  A view holds its source in a field. For a value type that field is a COPY, so the view would
//       neither be zero-copy (the copy is the copy) nor read through to the source the way the contract
//       says. Measured on a hand-written probe before this refusal was written: a copy-holding view
//       returned the pre-mutation value where a `ref readonly` one returned the post-mutation value, so
//       "the view is a window, not a snapshot" would have been false for exactly this shape and true
//       everywhere else — a divergence with no diagnostic.
//
//       Refused rather than served by `ref readonly`: that would change the factory's signature for one
//       source kind, and `in _s.Member` on a nested member is a lifetime question this version does not
//       answer. Refusing states the boundary; serving half of it would not.
//
//       The REMEDY is both halves — use Map, which copies on purpose, or make the source a class, which
//       makes the borrow meaningful.
// EXPECT: DWARF102
// EXPECT-MESSAGE DWARF102: value-type source
// EXPECT-MESSAGE DWARF102: it would hold a COPY
// EXPECT-MESSAGE DWARF102: Use Map for this pair

using DwarfMapper;

namespace Demo;

public struct Src
{
    public int Id { get; set; }
}

public sealed class Dst
{
    public int Id { get; set; }
}

[DwarfMapper]
[GenerateView<Src, Dst>]
public partial class M
{
}
