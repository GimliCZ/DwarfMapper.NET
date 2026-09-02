// SPDX-License-Identifier: GPL-2.0-only
// CASE: the DWARF064 shadow at the PROJECTION endpoint
// WHY:  The shadow rule is one method both endpoints call (TryValidateMapValueTarget), so a [MapValue] on a
//       projection method that masks a real source member reports here too, and as a report rather than a
//       refusal — the constant lands in the SELECT. This case exists so the endpoint has its own remedy pair:
//       the create map's pair proved the remedy could be followed there, and "there" is where the fix was
//       first applied while this endpoint kept passing a bare "does a source member exist" lookup. Sibling
//       _ProjectionRemedy applies [MapIgnoreSource] and must go silent.
// EXPECT: DWARF064
// EXPECT-MESSAGE DWARF064: overrides the source member 'Name'
// EXPECT-MESSAGE DWARF064: [MapIgnoreSource("Name")]

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
    public partial IQueryable<Dst> Project(IQueryable<Src> query);
}
