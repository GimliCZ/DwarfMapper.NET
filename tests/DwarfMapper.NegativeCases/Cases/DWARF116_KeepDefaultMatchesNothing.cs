// SPDX-License-Identifier: GPL-2.0-only
// CASE: [MapProperty(KeepDefault = true)] where no null default can be left - no When guard beside it, and a member
//       with its own non-null default - beside one on a guarded member whose default IS a null, which it accepts.
// WHY:  Round 32, under the owner's 2026-09-27 ruling that no DWARF diagnostic may need a project-wide NoWarn: DWARF066
//       (Info) reports a When guard that can leave a non-nullable member at its default, and its answer for an
//       intended default was "confirm the unset default is intended", with nothing to confirm it with. KeepDefault says
//       so for the one member. A statement that accepts nothing is not harmless - left on a member whose default later
//       becomes a null, it would accept that unreviewed - so it is reported, with the reason.
// EXPECT: DWARF116
// EXPECT-MESSAGE DWARF116: [MapProperty(KeepDefault = true)] on 'Code' accepts an unset default that is not there: there is no When guard on it, so nothing leaves it unassigned
// EXPECT-MESSAGE DWARF116: [MapProperty(KeepDefault = true)] on 'Title' accepts an unset default that is not there: it has its own non-null default
// EXPECT-CS:
// NOTE: 'Note' is the statement doing its job - guarded, defaulting to a null, and no DWARF066 for it.

#nullable enable

using DwarfMapper;

namespace Demo;

public class Src
{
    public string Note { get; set; } = "";
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Ready { get; set; }
}

public class Dst
{
    public string Note { get; set; } = null!;
    public string Code { get; set; } = "";
    public string Title { get; set; } = "(untitled)";
}

[DwarfMapper]
public partial class M
{
    private static bool IsReady(Src s) => s.Ready;

    [MapProperty(nameof(Src.Note), nameof(Dst.Note), When = nameof(IsReady), KeepDefault = true)]
    [MapProperty(nameof(Src.Code), nameof(Dst.Code), KeepDefault = true)]
    [MapProperty(nameof(Src.Title), nameof(Dst.Title), When = nameof(IsReady), KeepDefault = true)]
    public partial Dst Map(Src s);
}
