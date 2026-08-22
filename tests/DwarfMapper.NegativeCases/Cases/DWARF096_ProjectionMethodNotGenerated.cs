// SPDX-License-Identifier: GPL-2.0-only
// CASE: a mapper carrying BOTH a Map and a Project, where one PROJECTED member cannot be translated
// WHY:  DWARF028 is an Error and an error used to suppress the whole class, so this mapper generated
//       NOTHING — the Map method included — and DWARF078 announced it. The Map methods were collateral:
//       nothing about them is translated by a query provider, so nothing about them can fail to translate
//       (TASKS.md I14). A refusal is now proportional to what was refused. The Project method is dropped,
//       the class is emitted with its Map, and DWARF096 signposts the ONE CS8795 that follows instead of
//       DWARF078 announcing a wall of them.
//
//       Note what is NOT here: DWARF078. Its absence is the assertion — the NegativeCases runner matches
//       the EXPECT set exactly, so a regression that restores the whole-class kill fails this case twice
//       (DWARF096 missing, DWARF078 unexpected).
//
//       A CLASS-level error still takes the class down; only DWARF028 is scoped, because "untranslatable"
//       is the one error that is a property of the ENDPOINT rather than of the mapping. That control is
//       pinned in ProjectionScopedRefusalTests, which also proves the Map body really is emitted.
// EXPECT: DWARF028, DWARF096
// EXPECT-MESSAGE DWARF028: is not translatable in projection
// EXPECT-MESSAGE DWARF096: Projection method 'Project' was not generated
// EXPECT-MESSAGE DWARF096: The rest of this mapper WAS generated: only this method is missing

using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace Demo;

public class ProjSrc
{
    public int Id { get; set; }

    public List<int> Tags { get; set; } = new();
}

public class ProjDst
{
    public int Id { get; set; }

    // A HashSet target: EF Core cannot build one inside a translated projection. `.Map` builds it happily.
    public HashSet<int> Tags { get; set; } = new();
}

[DwarfMapper]
public partial class ProjectionAndMapMapper
{
    public partial ProjDst Map(ProjSrc s);

    public partial IQueryable<ProjDst> Project(IQueryable<ProjSrc> q);
}
