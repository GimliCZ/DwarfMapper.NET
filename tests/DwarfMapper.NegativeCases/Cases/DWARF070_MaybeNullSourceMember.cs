// SPDX-License-Identifier: GPL-2.0-only
// CASE: A source member annotated non-nullable but marked [MaybeNull], mapped into a non-nullable destination.
// WHY:  Research A6 / round 31 T23. The generator judged null-flow by the type's ANNOTATION alone, and a
//       [MaybeNull] string is annotated non-nullable — so DWARF070 said nothing, while the compiler, which does read
//       the attribute, raised CS8601 on the raw assignment INSIDE the .g.cs, where the consumer cannot suppress it.
//       MemberFacts now folds the attribute into the member's type, so the ordinary DWARF070 path sees it.
// EXPECT: DWARF070
// EXPECT-MESSAGE DWARF070: is a nullable reference but its destination is non-nullable
// EXPECT-CS:
// NOTE: No CS is the other half of the case: the '!' DWARF070 pairs with keeps CS8601 out of the generated file.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using DwarfMapper;

namespace Demo;

public class Src
{
    [MaybeNull] public string Name { get; set; } = "";
}

public class Dst
{
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class M
{
    public partial Dst Map(Src s);
}
