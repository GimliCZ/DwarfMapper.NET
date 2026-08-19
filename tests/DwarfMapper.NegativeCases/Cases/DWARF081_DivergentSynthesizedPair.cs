// SPDX-License-Identifier: GPL-2.0-only
// CASE: One nested pair, two mappers, two different mappings
// WHY:  A synthesized helper inherits the policy of the mapper that REACHED it, so two mappers reaching the
//       same (S, T) each get a private copy — and if their options differ, so do the copies. Both compile and
//       both are correct in isolation. Round 18 hit it because SkipNullSourceMembers was class-scoped: a
//       profile mixing patch-merge maps with ordinary ones had to be split into two classes, and the split
//       silently produced one null-guarded and one unguarded copy of the same pair. Not cosmetic — the store
//       could deserialize nulls into those members.
// EXPECT: DWARF081, DWARF058
// NOTE: DWARF058 comes with the shape and is correct — two mappers from one source type cannot both own the
//       x.ToOuterDto() convenience extension, so it is dropped. Declared rather than engineered away: the
//       minimal source that provokes DWARF081 provokes this too.
// EXPECT-MESSAGE DWARF081: 'Patch'
// EXPECT-MESSAGE DWARF081: 'Replace'
// EXPECT-MESSAGE DWARF081: Demo.InnerDto
// EXPECT-MESSAGE DWARF081: Note
// EXPECT-MESSAGE DWARF081: [MapNullSkip
// EXPECT-MESSAGE DWARF058: was not generated
// EXPECT-MESSAGE DWARF058: GenerateExtensions = false

using DwarfMapper;

namespace Demo;

public class Inner
{
    public string? Note { get; set; }
}

public class InnerDto
{
    public string? Note { get; set; }
}

public class Outer
{
    public Inner Child { get; set; } = new();
}

public class OuterDto
{
    public InnerDto Child { get; set; } = new();
}

[DwarfMapper]
[GenerateMap<Outer, OuterDto>]
public partial class Replace;

[DwarfMapper(SkipNullSourceMembers = true)]
[GenerateMap<Outer, OuterDto>]
public partial class Patch;
