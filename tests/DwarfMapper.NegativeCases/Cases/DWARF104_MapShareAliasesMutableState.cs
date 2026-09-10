// SPDX-License-Identifier: GPL-2.0-only
// CASE: [MapShare] on a collection whose ELEMENT has a settable property (round 29 T3.1)
// WHY:  Sharing assigns the source's own reference to the destination, so the two objects hold one instance
//       afterwards. That is a mapping only when nothing reachable through the reference can be written —
//       and `Loose.Name` has a setter, so a write through either graph is visible in the other. Nothing
//       detects that after the fact: there is no exception, no diagnostic and no observable difference until
//       two things that were supposed to be independent disagree.
//
//       An ERROR rather than a warning, on DWARF022's precedent. The caller wrote a directive that cannot be
//       honoured, and honouring it half-way — copying while saying nothing — is the "accepted it, changed
//       nothing, said nothing" silence this round exists to remove. Deleting the attribute is always a valid
//       fix and costs exactly one copy.
//
//       What is NOT here, and is the point of the tier below this one: an INTERFACE is not refused.
//       IReadOnlyList<T> is unprovable rather than disproven — the proof cannot see the instance behind it —
//       so [MapShare] shares it on the caller's assertion, exactly as [Reinterpret] forces a layout the blit
//       proof declines to confirm. Only a fact the generator can SEE overrides the caller's word, which is
//       why this case puts the setter on the element rather than on the collection interface.
//
//       DWARF078 accompanies it because DWARF104 is an ERROR: the mapper is refused, so the partial Map
//       method reports CS8795, which DWARF078 exists to signpost.
// EXPECT: DWARF104, DWARF078
// EXPECT-MESSAGE DWARF104: not immutable: sharing would alias mutable state
// EXPECT-MESSAGE DWARF104: Loose.Name

using System.Collections.Immutable;
using DwarfMapper;

namespace Demo;

public sealed class Loose
{
    public string Name { get; set; } = "";
}

public class ShareSrc
{
    public int Id { get; set; }

    public ImmutableList<Loose> Items { get; set; } = ImmutableList<Loose>.Empty;
}

public class ShareDst
{
    public int Id { get; set; }

    public ImmutableList<Loose> Items { get; set; } = ImmutableList<Loose>.Empty;
}

[DwarfMapper]
public partial class MapShareAliasesMutableStateMapper
{
    [MapShare("Items")]
    public partial ShareDst Map(ShareSrc s);
}
