// SPDX-License-Identifier: GPL-2.0-only
// CASE: OnCycle = SetNull is set on a pair whose destination reaches a value-type (struct) target
//       through a recursion-capable edge (here, a List<Node> self-reference).
// WHY:  Round 30 coverage sweep. EmitSetNullGuardedBody's on-stack guard breaks a cycle with an
//       unconditional `return null!;` — CS0037 against a non-nullable struct return, because a value
//       type cannot hold null. No fixture had ever exercised a struct destination under SetNull, so
//       the defect shipped undetected for as long as the feature has existed. The generator now
//       detects it and falls back to the plain depth-guarded body for the affected pair instead of
//       emitting code that fails to compile: an acyclic source still maps correctly, and a genuinely
//       cyclic source throws DwarfMappingDepthException instead of terminating early by nulling the
//       back-edge. This row pins the id and the two remedies the message offers.
// EXPECT: DWARF108
// EXPECT-MESSAGE DWARF108: OnCycle = SetNull applies to 'Map'
// EXPECT-MESSAGE DWARF108: is a value type and cannot hold null
// EXPECT-MESSAGE DWARF108: throws DwarfMappingDepthException instead of terminating early
// EXPECT-MESSAGE DWARF108: dotnet_diagnostic.DWARF108.severity = none
// EXPECT-CS:
// NOTE: No CS at all — that is the fix. The fallback body (the same shape a plain OnCycle = Throw
//       pair gets) compiles clean; before this fix, this exact fixture emitted CS0037 in a .g.cs no
//       consumer can edit.

#nullable enable

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public class Node
{
    public int V { get; set; }

    public List<Node>? Children { get; set; } = new();
}

public struct NodeDto
{
    public int V { get; set; }

    public List<NodeDto>? Children { get; set; }
}

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class M
{
    public partial NodeDto Map(Node n);
}
