// SPDX-License-Identifier: GPL-2.0-only
// CASE: [MapIgnore] on a `required` destination member
// WHY:  The most-repeated friction point of the Round-18 migration — three separate conversions hit it and
//       each reinvented the same workaround. It is common in migrating code precisely because AutoMapper
//       built targets reflectively and bypassed the rule: .Ignore() on a required member simply left it null.
//       Without this diagnostic the consumer sees CS9035 pointed at generated code they never wrote.
// EXPECT: DWARF079, DWARF078
// EXPECT-MESSAGE DWARF079: 'Id'
// EXPECT-MESSAGE DWARF079: CS9035
// EXPECT-MESSAGE DWARF079: [MapValue
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class Src
{
    public string Name { get; set; } = "";
}

public class Doc
{
    public required string Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [MapIgnore(nameof(Doc.Id))]
    public partial Doc Map(Src source);
}
