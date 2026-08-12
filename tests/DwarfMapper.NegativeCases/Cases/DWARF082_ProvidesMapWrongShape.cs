// SPDX-License-Identifier: GPL-2.0-only
// CASE: [ProvidesMap] on a method the ambient registry cannot hold
// WHY:  Refused rather than silently skipped. The author asked for a registration; dropping it quietly would
//       leave every facade call site for that pair throwing at run time with nothing to explain why — which
//       is the exact failure [ProvidesMap] exists to prevent. Two parameters is the common near-miss.
// EXPECT: DWARF082, DWARF078
// EXPECT-MESSAGE DWARF082: Convert
// EXPECT-MESSAGE DWARF082: public
// EXPECT-MESSAGE DWARF082: one parameter

using DwarfMapper;

namespace Demo;

public class Legacy
{
    public string Name { get; set; } = "";
}

public class Modern
{
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [ProvidesMap]
    public static Modern Convert(Legacy source, string culture) => new() { Name = source.Name + culture };
}
