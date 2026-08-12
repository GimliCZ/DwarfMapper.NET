// SPDX-License-Identifier: GPL-2.0-only
// CASE: [RestatesBase] with nothing to restate
// WHY:  The attribute exists so the drift check has something to compare. With no base pair declared there is
//       nothing to compare against, and the check the author asked for would silently not run — which is
//       precisely the drift risk they were guarding against. Refused for the same reason as DWARF082.
// EXPECT: DWARF084, DWARF078
// EXPECT-MESSAGE DWARF084: no base pair
// EXPECT-MESSAGE DWARF084: base class
// NOTE: no EXPECT-CS. The mapper declares its pairs by attribute rather than by partial method, so there is no
//       implementing part to go missing and the usual CS8795 cascade does not arise — DWARF078 still fires,
//       because emission is suppressed either way.

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
[GenerateMap<AliasCommand, AliasCommandDto>]
[MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
[RestatesBase<AliasCommand, AliasCommandDto>]
public partial class M;
