// SPDX-License-Identifier: GPL-2.0-only
// CASE: [DwarfProvidesMap] written by hand instead of emitted
// WHY:  The manifest is the generator's OUTPUT, and the [DwarfMapperValidationRoot] compilation reads it back
//       as a description of what each assembly registers. A hand-written row claims a map nothing produced, so
//       DWARF061 passes against a manifest that does not describe the assembly and the failure reappears at the
//       first call site at run time — the exact failure the root check exists to pull forward.
// EXPECT: DWARF086
// EXPECT-MESSAGE DWARF086: DwarfProvidesMapAttribute
// EXPECT-MESSAGE DWARF086: emitted by the generator
// EXPECT-MESSAGE DWARF086: [UsesMap
// EXPECT-MESSAGE DWARF086: [ProvidesMap]

using DwarfMapper;

[assembly: DwarfProvidesMap(typeof(Demo.Legacy), typeof(Demo.Modern))]

namespace Demo;

public class Legacy
{
    public string Name { get; set; } = "";
}

public class Modern
{
    public string Name { get; set; } = "";
}
