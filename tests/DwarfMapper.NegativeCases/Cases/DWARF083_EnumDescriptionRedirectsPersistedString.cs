// SPDX-License-Identifier: GPL-2.0-only
// CASE: [Description] on an enum member quietly becoming the persisted string
// WHY:  Round 18 came within one code review of shipping this. DispatchChannel.NextDay carried
//       [Description("Next-Day")], the previous mapper used .ToString(), and the migration would have started
//       writing "Next-Day" into a store full of "NextDay" — breaking reads of every existing document. The
//       precedence is a deliberate feature (InProgress -> "in_progress" with no converter); the hazard is
//       that [Description] is overwhelmingly a DISPLAY annotation, so the message must say so.
// EXPECT: DWARF083
// EXPECT-MESSAGE DWARF083: DispatchChannel
// EXPECT-MESSAGE DWARF083: NextDay
// EXPECT-MESSAGE DWARF083: Next-Day
// EXPECT-MESSAGE DWARF083: display

using System.ComponentModel;
using DwarfMapper;

namespace Demo;

public enum DispatchChannel
{
    [Description("Next-Day")] NextDay,
    Standard
}

public class Dispatch
{
    public DispatchChannel Source { get; set; }
}

public class DispatchDocument
{
    public string Source { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<Dispatch, DispatchDocument>]
public partial class M;
