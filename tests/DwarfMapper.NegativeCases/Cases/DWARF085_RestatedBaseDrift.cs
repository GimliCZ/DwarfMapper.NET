// SPDX-License-Identifier: GPL-2.0-only
// CASE: A restatement that is present but no longer does the same thing
// WHY:  The failure worth catching, and the one a MIRRORS BASE comment convention cannot see. The base pair
//       converts Raw through Clean; the derived pair still names the members but dropped the converter, so it
//       silently maps the raw value. Restatement drift is always in this direction — toward wrong data.
// EXPECT: DWARF085
// EXPECT-MESSAGE DWARF085: Text
// EXPECT-MESSAGE DWARF085: Demo.CommandDto
// EXPECT-MESSAGE DWARF085: Restate
// EXPECT-MESSAGE DWARF085: Overrides

using DwarfMapper;

namespace Demo;

public class Command
{
    public string Raw { get; set; } = "";
}

public class AliasCommand : Command
{
    public string Alias { get; set; } = "";
}

public class CommandDto
{
    public string Text { get; set; } = "";
}

public class AliasCommandDto : CommandDto
{
    public string Alias { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<Command, CommandDto>]
[MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
[GenerateMap<AliasCommand, AliasCommandDto>]
[RestatesBase<AliasCommand, AliasCommandDto>]
[MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
public partial class M
{
    private static string Clean(string raw) => raw.Trim();
}
