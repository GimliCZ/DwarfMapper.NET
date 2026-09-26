// SPDX-License-Identifier: GPL-2.0-only
// CASE: a pair-scoped [MapIgnore<T>("Name")] whose type argument matches a mapped pair but whose name matches no
//       member of that type
// WHY:  The pair-scoped form had half the guard. DWARF056 reports a [MapIgnore<T>] whose TYPE matches no mapped pair;
//       DWARF095 reports an unscoped [MapIgnore("Name")] whose NAME matches no destination member. A pair-scoped
//       ignore whose type matched and whose name did not fell between the two: it was consumed by the pair,
//       excluded nothing, and said nothing, so the member the caller meant to exclude ("Extra", typo'd "Extar"
//       below) went on being mapped. Round 30 found it by probing the pair-scoped reader. It is the same dead
//       directive DWARF095 exists for, so it takes the same id, with a message that names the pair-scoped
//       attribute and the type it was judged against. A Warning like the rest of DWARF095: the mapper works, the
//       directive does nothing.
// EXPECT: DWARF095
// EXPECT-MESSAGE DWARF095: [MapIgnore<IgnDst>("Extar")] on mapper 'PairSiteMapper' names no destination member of 'IgnDst'
// EXPECT-MESSAGE DWARF095: fix the name or remove the attribute
// EXPECT-CS:
// NOTE: The mapper compiles and maps Extra — which is exactly what the caller did not intend, and why this reports.

using DwarfMapper;

namespace Demo;

public class IgnSrc
{
    public int Id { get; set; }
    public string? Extra { get; set; }
}

public class IgnDst
{
    public int Id { get; set; }
    public string? Extra { get; set; }
}

[DwarfMapper]
[GenerateMap<IgnSrc, IgnDst>]
[MapIgnore<IgnDst>("Extar")]
public partial class PairSiteMapper
{
}
