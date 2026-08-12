// SPDX-License-Identifier: GPL-2.0-only
// CASE: A [MapConstructor] factory that owns an init-only member the source would have filled
// WHY:  The build is green and the data is gone. Round 18 hit this twice in one codebase: an entity lost its
//       Identifier through an .Empty factory that minted a fresh Guid, and a second map "compiled green but
//       silently dropped Identifier, TotalArguments and IsCoreCommand" and had to be backed out. Info rather
//       than Warning because the generator cannot see inside the factory — both shapes are legitimate.
// EXPECT: DWARF080
// EXPECT-MESSAGE DWARF080: 'Identifier'
// EXPECT-MESSAGE DWARF080: [MapProperty]

using System;
using DwarfMapper;

namespace Demo;

public class Src
{
    public Guid Identifier { get; set; }
    public string Name { get; set; } = "";
}

public class Dst
{
    private Dst() { }

    public Guid Identifier { get; init; }
    public string Name { get; set; } = "";

    public static Dst Empty => new() { Identifier = Guid.NewGuid() };
}

[DwarfMapper]
[GenerateMap<Src, Dst>]
[MapConstructor<Src, Dst>(nameof(Create))]
public partial class M
{
    private static Dst Create(Src source) => Dst.Empty;
}
