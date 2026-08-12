// SPDX-License-Identifier: GPL-2.0-only
// CASE: A Use= converter that exists but has the wrong signature
// WHY:  The named method is right there in the class, so "not found" alone sends the reader looking for a
//       typo they will not find. Converters are matched by SIGNATURE — here Render takes an int and the
//       member is a Guid — so the message has to say so, or the reader is hunting the wrong thing.
// EXPECT: DWARF014, DWARF078
// EXPECT-MESSAGE DWARF014: Render
// EXPECT-MESSAGE DWARF014: signature
// EXPECT-CS: CS8795

using System;
using DwarfMapper;

namespace Demo;

public class Src
{
    public Guid Code { get; set; }
}

public class Dst
{
    public string Code { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [MapProperty(nameof(Src.Code), nameof(Dst.Code), Use = nameof(Render))]
    public partial Dst Map(Src source);

    private static string Render(int notTheSourceType) => notTheSourceType.ToString();
}
