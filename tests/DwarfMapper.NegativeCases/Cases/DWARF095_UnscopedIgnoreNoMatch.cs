// SPDX-License-Identifier: GPL-2.0-only
// CASE: an unscoped [MapIgnore("Name")] whose name matches no destination member anywhere it is read
// WHY:  The destination-name set is matched ORDINALLY (MapperExtractor.IgnoreNameComparer), so
//       [MapIgnore("Typo")] against nothing — or [MapIgnore("id")] against a property Id, even under
//       [DwarfMapper(CaseInsensitive = true)], which fuzzes AUTO-MATCHING and never the binding of a name
//       the caller wrote — was accepted, excluded nothing, and produced no diagnostic at any endpoint
//       (surface-matrix finding B20). The caller believed they excluded a member; the completeness gate went
//       on demanding it, and DWARF001, when it fired, named the member rather than the dead directive. The
//       pair-scoped forms already had this guard (DWARF056, "matches no mapped pair"); the unscoped form
//       being exempt was an asymmetry, not a policy.
//
//       A METHOD-site name is judged against that method's own destination; a CLASS-site name is class-WIDE
//       and judged against every pair the class maps, so a class ignore that is about ONE of the class's
//       pairs is legitimately silent on the others (pinned as a legal neighbour in
//       UnscopedIgnoreNoMatchTests). Both shapes are in this file because the two statements name different
//       scopes and both wordings are pinned below. A Warning like DWARF056: the mapper works, the directive
//       does nothing.
// EXPECT: DWARF095, DWARF001, DWARF097
// EXPECT-MESSAGE DWARF095: [MapIgnore("Extar")] on 'Map' names no destination member of 'IgnDst'
// EXPECT-MESSAGE DWARF095: excludes nothing (directive names match exactly, including case)
// EXPECT-MESSAGE DWARF095: [MapIgnore("id")] on mapper 'ClassSiteMapper' names no destination member of any pair this mapper maps
// EXPECT-MESSAGE DWARF001: Extra

using DwarfMapper;

namespace Demo;

public class IgnSrc
{
    public int Id { get; set; }
    public string? Extra { get; set; }
}

public class IgnDst
{
    public int Id { get; set; }
    public string? Extra { get; set; }
}

// Shape 1, METHOD site: the caller meant "Extra" and typo'd it — the ignore excludes nothing, and without
// this id the only signal would be... nothing at all here ("Extra" auto-matches, so the mapping quietly
// includes the member the caller tried to exclude).
[DwarfMapper]
public partial class MethodSiteMapper
{
    [MapIgnore("Extar")]
    public partial IgnDst Map(IgnSrc s);
}

// Shape 2, CLASS site, with B21's exact case: CaseInsensitive = true makes `id` AUTO-MATCH `Id` between
// source and destination, but a directive names a member exactly, so [MapIgnore("id")] matches no pair's
// member and is dead. The DWARF001 alongside is the finding's own evidence shape: the caller who believes
// `Extra` is handled still owes the completeness gate an answer for it — here it is left genuinely unmapped
// (no source member), so the gate names the member while this id names the dead directive.
[DwarfMapper(CaseInsensitive = true)]
[MapIgnore("id")]
public partial class ClassSiteMapper
{
    public partial ClassDst Map(ClassSrc s);
}

public class ClassSrc
{
    public int Id { get; set; }
}

public class ClassDst
{
    public int Id { get; set; }
    public string? Extra { get; set; }
}
