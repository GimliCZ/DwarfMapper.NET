// SPDX-License-Identifier: GPL-2.0-only
// CASE: the MEMBER-placement overload of [MapProperty] and [MapIgnore], written on a mapping method
// WHY:  Both attributes carry two placements behind one name, each with its own constructor. The member form
//       ([MapProperty("Dest")], bare [MapIgnore]) says something about THE ANNOTATED MEMBER and only means
//       anything where the annotated type declares its own mapping — a [MapTo] source, a [GenerateMap] host.
//       On a mapping method there is no annotated member, and both were skipped without a word:
//       ReadExplicitMaps accepts only the two-argument application and ReadIgnores only the one-argument one.
//       The [MapProperty] half is the sharper of the two, because the named arguments ride on that SAME
//       one-argument constructor — so `Use = nameof(Shout)` below went into the bin with the binding and the
//       caller's converter was never called. Refusal is right whichever way the directive is read: honouring
//       it binds Name to itself, which is what auto-matching already does. An ERROR, matching the registry
//       mirror DWARFR04, so the class stops emitting and CS8795 follows from the unimplemented partial —
//       which is why DWARF078, the cascade signpost, is part of the declared set.
// EXPECT: DWARF088, DWARF078
// EXPECT-MESSAGE DWARF088: MEMBER-placement overload
// EXPECT-MESSAGE DWARF088: [MapProperty("Name", "<destination>")]
// EXPECT-MESSAGE DWARF088: [MapIgnore("<destination>")]
// EXPECT-MESSAGE DWARF088: mapping method
// EXPECT-CS: CS8795

using DwarfMapper;

namespace Demo;

public class Source
{
    public string Name { get; set; } = "";
}

public class Target
{
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    [MapProperty("Name", Use = nameof(Shout))]
    [MapIgnore]
    public partial Target Map(Source s);

    private static string Shout(string s) => s.ToUpperInvariant();
}
