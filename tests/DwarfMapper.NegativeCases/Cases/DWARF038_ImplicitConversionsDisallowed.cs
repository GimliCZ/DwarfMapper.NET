// SPDX-License-Identifier: GPL-2.0-only
// CASE: [DwarfMapper(ImplicitConversions = false)] turns a narrowing conversion into a refusal
// WHY:  This option's entire observable effect is the escalation: `true` is the default and changes nothing,
//       and `false` turns the DWARF038 suggestion into a build error. That is why it can never appear in a
//       runnable sample — the sample demonstrating the difference would not compile — and why it used to sit
//       in a NotDemonstrable dictionary as a one-line excuse instead of being proved anywhere.
//       The message must carry BOTH halves: that the conversion is disallowed, and the remedy, because a
//       refusal a consumer cannot act on is a wall rather than a boundary.
// EXPECT: DWARF038, DWARF078
// EXPECT-MESSAGE DWARF038: is disallowed
// EXPECT-MESSAGE DWARF038: ImplicitConversions = false
// EXPECT-MESSAGE DWARF038: Map it explicitly with [MapProperty(Use = nameof(...))]
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class Src
{
    public long Value { get; set; }
}

public class Doc
{
    // Narrowing: long -> int loses the high half. Under the DEFAULT this is a suggestion and the map is
    // generated; the option is what makes it stop the build.
    public int Value { get; set; }
}

[DwarfMapper(ImplicitConversions = false)]
public partial class M
{
    public partial Doc Map(Src source);
}
