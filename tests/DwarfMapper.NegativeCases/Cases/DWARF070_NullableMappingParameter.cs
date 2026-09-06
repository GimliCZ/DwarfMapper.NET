// SPDX-License-Identifier: GPL-2.0-only
// CASE: An extra mapping parameter declared nullable, feeding a destination member that is not
// WHY:  DWARF070 used to be a statement about a source MEMBER, and round 29 task 2.7 taught it to fire for a
//       Phase 5 mapping PARAMETER as well. A shared message then has to be true of both — and the one it had
//       was not: it called the parameter a "member" and offered [MapProperty(NullSubstitute = …)] and
//       SkipNullSourceMembers, two source-member instruments that cannot reach a parameter. This row pins the
//       wording a reader of the parameter case actually needs, so a future rewording cannot quietly drop it
//       back to the member-only spelling. The member spelling is pinned beside it, for the same reason.
// EXPECT: DWARF070
// EXPECT-MESSAGE DWARF070: Mapping parameter 'inner'
// EXPECT-MESSAGE DWARF070: For a mapping PARAMETER neither attribute reaches it
// EXPECT-MESSAGE DWARF070: declare the parameter non-nullable
// EXPECT-MESSAGE DWARF070: dotnet_diagnostic.DWARF070.severity = none
// EXPECT-CS:
// NOTE: No CS at all is the other half of the case. The generated file is warning-free — the mapper emits the
//       null-forgiving `!` on the argument — which is exactly the trade this diagnostic exists to make: an
//       unsuppressible CS8604 inside a .g.cs becomes a suppressible DWARF070 against code the reader owns.

#nullable enable

using DwarfMapper;

namespace Demo;

public class Child
{
    public int V { get; set; }
}

public class ChildDto
{
    public int V { get; set; }
}

public class Src
{
    public int Id { get; set; }
}

public class Dst
{
    public int Id { get; set; }

    public ChildDto Inner { get; set; } = new();
}

[DwarfMapper]
public partial class M
{
    public partial Dst Map(Src s, Child? inner);

    public partial ChildDto ToDto(Child c);
}
