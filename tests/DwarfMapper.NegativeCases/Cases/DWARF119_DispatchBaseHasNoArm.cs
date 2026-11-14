// SPDX-License-Identifier: GPL-2.0-only
// CASE: a [MapDerivedType] dispatch over a class that is not abstract, with an arm for the derived type only.
// WHY:  Round 35. A dispatching method maps nothing itself: an instance matches an arm or reaches the fallback, which
//       throws ArgumentException. An instance of exactly the base type matches no derived arm, and a class that is
//       not abstract has such instances. T13 made every nested route dispatch as the direct call does, which turned
//       a base instance nested under Preserve or SetNull from "mapped as the base" into that exception; the owner
//       ruled that the declaration, which shows the case, is where it is refused. An arm for the base itself
//       answers it, and so does making the base abstract.
// EXPECT: DWARF119, DWARF078
// EXPECT-MESSAGE DWARF119: 'ToDto' dispatches over 'Demo.Command', which is not abstract, and has no [MapDerivedType] arm for 'Demo.Command' itself
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class Command
{
    public string Name { get; set; } = "";
}

public class AliasCommand : Command
{
    public string Alias { get; set; } = "";
}

public class CommandDto
{
    public string Name { get; set; } = "";
}

public class AliasCommandDto : CommandDto
{
    public string Alias { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [MapDerivedType<AliasCommand, AliasCommandDto>]
    public partial CommandDto ToDto(Command command);
}
