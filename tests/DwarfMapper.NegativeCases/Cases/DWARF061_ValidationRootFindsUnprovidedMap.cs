// SPDX-License-Identifier: GPL-2.0-only
// CASE: [assembly: DwarfMapperValidationRoot] refuses a consumed ambient map nothing provides
// WHY:  The validation root's whole observable effect is this refusal — it emits no runtime behaviour a
//       sample could assert, so a passing sample cannot contain the failure it guards against. Without it the
//       consumer gets DwarfMapMissingException at run time, at whichever call site happens to execute first;
//       the point of the marker is to move that to the build.
//       The marker is also what SELECTS the check: exactly one assembly in a graph carries it, and a mid-tier
//       assembly with the same [UsesMap] and no marker must stay silent (see CLEAN_UsesMapWithoutRoot).
// EXPECT: DWARF061
// EXPECT-MESSAGE DWARF061: is consumed through IDwarfMapper but no referenced assembly provides it
// EXPECT-MESSAGE DWARF061: Declare [GenerateMap

using DwarfMapper;

[assembly: DwarfMapperValidationRoot]

// The consumption stated by hand, which is what [UsesMap] is for: at a call site that handles the source as a
// base type or as `object`, only the destination is static and the auto-detection has nothing to key on.
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
