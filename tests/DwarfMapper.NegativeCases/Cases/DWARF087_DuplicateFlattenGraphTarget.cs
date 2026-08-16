// SPDX-License-Identifier: GPL-2.0-only
// CASE: two [FlattenGraph] directives fill one destination collection
// WHY:  [FlattenGraph] is AllowMultiple so one method can flatten SEVERAL graphs into several collections, but
//       each directive appends its own initializer for the collection it names. Two directives naming the same
//       collection therefore emitted `new RootDto { Nodes = …, Nodes = … }` — CS1912, reported against
//       Demo.M.g.cs, a generated file the consumer never wrote and cannot edit. That is the whole reason this
//       id exists: the build could not succeed and the error named someone else's source. Refused rather than
//       collapsed, matching DWARF011 on [MapProperty]: a repeated directive is a copy-paste mistake, and
//       keeping one of them silently hides the mistake from the only person able to fix it.
// EXPECT: DWARF087, DWARF078
// EXPECT-MESSAGE DWARF087: Nodes
// EXPECT-MESSAGE DWARF087: more than one [FlattenGraph] directive
// EXPECT-MESSAGE DWARF087: Keep exactly one [FlattenGraph] per destination collection
// EXPECT-MESSAGE DWARF087: name a different collection member
// EXPECT-CS: CS8795

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public class Node
{
    public string Name { get; set; } = "";

    public Node? Next { get; set; }
}

public class NodeDto
{
    public string Name { get; set; } = "";

    public NodeDto? Next { get; set; }
}

public class Root
{
    public Node? Entry { get; set; }
}

public class RootDto
{
    public List<NodeDto> Nodes { get; set; } = new();
}

[DwarfMapper]
public partial class M
{
    [FlattenGraph("Entry", "Nodes")]
    [FlattenGraph("Entry", "Nodes")]
    public partial RootDto Map(Root r);
}
