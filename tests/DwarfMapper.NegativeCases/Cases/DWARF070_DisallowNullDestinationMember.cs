// SPDX-License-Identifier: GPL-2.0-only
// CASE: A nullable source member mapped into a destination annotated nullable but marked [DisallowNull].
// WHY:  Research A6 / round 31 T23, the write-side twin of DWARF070_MaybeNullSourceMember. A [DisallowNull]
//       string? is annotated nullable, so the generator saw no null-into-non-nullable edge; the compiler reads the
//       attribute and raised CS8601 inside the .g.cs. MemberFacts now treats the member as non-nullable for writes.
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
    public string? Name { get; set; }
}

public class Dst
{
    [DisallowNull] public string? Name { get; set; }
}

[DwarfMapper]
public partial class M
{
    public partial Dst Map(Src s);
}
