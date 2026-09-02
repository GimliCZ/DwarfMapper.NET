// SPDX-License-Identifier: GPL-2.0-only
// CASE: the DWARF064 shape with the remedy its own message names, which must go silent
// REMEDY-FOR: DWARF064
// WHY:  Second half of the DWARF064 pair. Identical to DWARF064_MapValueShadowsSource except for the one
//       attribute the diagnostic tells the reader to add: [MapIgnoreSource("Name")], declaring that shadowing
//       the source member is deliberate.
//
//       `EXPECT: none` is the whole assertion, and it is a stronger one than it looks. It failed before the
//       fix that accompanies it: TryValidateMapValueTarget consulted only whether a same-named source member
//       existed and never whether the mapper had disowned it, so the attribute changed nothing and this file
//       would still have reported DWARF064. The set is EXACT, so it also catches the opposite failure — a
//       remedy that silences the diagnostic by breaking the mapping would surface as some other id here
//       rather than as silence.
// EXPECT: none
// EXPECT-CS:

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
    [MapIgnoreSource(nameof(Src.Name))]
    public partial Dst Map(Src source);

    private static string FixedName()
    {
        return "constant";
    }
}
