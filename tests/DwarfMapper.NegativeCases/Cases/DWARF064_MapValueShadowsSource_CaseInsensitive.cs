// SPDX-License-Identifier: GPL-2.0-only
// CASE: the DWARF064 shadow under CaseInsensitive, where the shadowed source member is NOT spelled like the target
// WHY:  Under [DwarfMapper(CaseInsensitive = true)] `Dst.Name` auto-matches `Src.name`, so a [MapValue] on
//       `Name` shadows `name`. The remedy the message names is [MapIgnoreSource], and [MapIgnoreSource] is read
//       by REAL source name everywhere else — source coverage disowns `name`, not `Name`. So the message must
//       spell the source member's real name, or it sends the reader to write an attribute that silences this
//       diagnostic and disowns nothing. The two EXPECT-MESSAGE lines pin exactly that: the shadowed member is
//       named as `name`, and so is the remedy.
//
//       Sibling _CaseInsensitive_Remedy applies that remedy verbatim and must go silent.
// EXPECT: DWARF064
// EXPECT-MESSAGE DWARF064: overrides the source member 'name'
// EXPECT-MESSAGE DWARF064: [MapIgnoreSource("name")]

using DwarfMapper;

namespace Demo;

public class Src
{
    public string name { get; set; } = "";
    public int Id { get; set; }
}

public class Dst
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

[DwarfMapper(CaseInsensitive = true)]
public partial class M
{
    [MapValue(nameof(Dst.Name), "constant")]
    public partial Dst Map(Src source);
}
