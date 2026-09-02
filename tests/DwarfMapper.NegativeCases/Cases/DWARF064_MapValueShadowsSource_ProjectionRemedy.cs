// SPDX-License-Identifier: GPL-2.0-only
// CASE: the projection DWARF064 shape with the remedy its own message names, which must go silent
// REMEDY-FOR: DWARF064
// WHY:  Second half of the projection pair. Identical to DWARF064_MapValueShadowsSource_Projection except for
//       [MapIgnoreSource("Name")]. It failed before the projection resolver was handed the mapper's
//       [MapIgnoreSource] set: the create map had been given it, the projection had not, and the same
//       attribute silenced the same diagnostic on one method of a mapper and not on the next. `EXPECT: none`
//       is exact — a remedy that went silent by refusing the projection would surface as DWARF028 or DWARF096.
// EXPECT: none

using System.Linq;
using DwarfMapper;

namespace Demo;

public class Src
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

public class Dst
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

[DwarfMapper]
public partial class M
{
    [MapValue(nameof(Dst.Name), "constant")]
    [MapIgnoreSource(nameof(Src.Name))]
    public partial IQueryable<Dst> Project(IQueryable<Src> query);
}
