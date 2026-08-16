// SPDX-License-Identifier: GPL-2.0-only
// CASE: the same unprovided [UsesMap] with no validation root stays silent
// WHY:  The control for DWARF061_ValidationRootFindsUnprovidedMap, and the half that is easy to lose. A
//       refusal that fired without the marker would break every mid-tier assembly in a consumer graph, which
//       sees only a partial reference graph and therefore cannot know whether a pair is provided elsewhere.
//       Byte-for-byte the same source as that case minus one line, so the marker is demonstrably what selects
//       the check rather than something else in the shape.
// EXPECT: none

using DwarfMapper;

[assembly: UsesMap(typeof(Demo.Doc), typeof(Demo.Model))]

namespace Demo;

public class Doc
{
    public string Name { get; set; } = "";
}

public class Model
{
    public string Name { get; set; } = "";
}
