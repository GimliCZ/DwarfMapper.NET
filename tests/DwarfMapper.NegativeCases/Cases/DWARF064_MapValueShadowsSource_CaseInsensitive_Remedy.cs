// SPDX-License-Identifier: GPL-2.0-only
// CASE: the CaseInsensitive DWARF064 shape with the remedy its message names — spelled as the SOURCE spells it
// REMEDY-FOR: DWARF064
// WHY:  Second half of the CaseInsensitive pair. The one attribute added is [MapIgnoreSource("name")] — the
//       real source spelling the sibling's message names, not the target's `Name`. This failed before the
//       shadow rule was taught to test disowning by real name: it consulted the ignore set under the TARGET's
//       spelling, so the attribute source coverage understands was invisible to DWARF064, and the reader had
//       to write the same disowning twice, once per spelling, to satisfy two rules that claim to read one set.
//       `EXPECT: none` is the exact set, so a remedy that silenced the shadow by breaking the mapping would
//       surface here as some other id rather than as silence.
// EXPECT: none

using DwarfMapper;

namespace Demo;

public class Src
{
    public string name { get; set; } = "";
    public int Id { get; set; }
}

public class Dst
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

[DwarfMapper(CaseInsensitive = true)]
public partial class M
{
    [MapValue(nameof(Dst.Name), "constant")]
    [MapIgnoreSource("name")]
    public partial Dst Map(Src source);
}
