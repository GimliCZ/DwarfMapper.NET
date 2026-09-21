// SPDX-License-Identifier: GPL-2.0-only
// CASE: A nullable-annotated REFERENCE element flowing through a map method the user declared, into a
//       destination element type that forbids null.
// WHY:  DWARF070 was a statement about a source MEMBER (and, since round 29 task 2.7, a mapping PARAMETER).
//       Task 2.9 added the four element edges — collection, dictionary value, span, async stream — because
//       they were forgiving the same null with the same '!' and saying NOTHING: CollectionConverter.ElementExpr
//       and DictionaryConverter.Expr each kept their own IsSynthesized proxy, blind to a user-declared
//       converter, so the element was left un-forgiven (CS8604 in a .g.cs) and, once forgiven, unreported.
//       A shared message has to be true of all four kinds, and the element remedy is NOT the member one:
//       neither [MapProperty(NullSubstitute = …)] nor SkipNullSourceMembers can reach an element type. This
//       row pins the element noun and the element remedy so a future rewording cannot quietly drop them.
// EXPECT: DWARF070, DWARF103
// EXPECT-MESSAGE DWARF070: The source element mapped into 'Items'
// EXPECT-MESSAGE DWARF070: is a nullable reference but its destination is non-nullable
// EXPECT-MESSAGE DWARF070: For a collection ELEMENT or a dictionary VALUE neither attribute reaches it either
// EXPECT-MESSAGE DWARF070: make the destination element type nullable
// EXPECT-MESSAGE DWARF070: dotnet_diagnostic.DWARF070.severity = none
// EXPECT-CS:
// NOTE: DWARF103 is declared beside it because the element pair here is small and transfer-model shaped, so
//       the round 29 struct-DTO suggestion fires too. Padding the types until it stops firing would make the
//       case a hostage to DWARF103's size threshold; declaring it is the honest spelling.
// NOTE: No CS at all is the other half of the case, exactly as in DWARF070_NullableMappingParameter: the
//       element argument is null-forgiven so the unsuppressible CS8604 never reaches the consumer's .g.cs,
//       and this suppressible warning against code they own carries the signal instead.

#nullable enable

using System.Collections.Generic;
using DwarfMapper;

namespace Demo;

public class Child
{
    public int V { get; set; }

}

public class ChildDto
{
    public int V { get; set; }
}

public class Src
{
    public List<Child?> Items { get; set; } = new();
}

public class Dst
{
    public List<ChildDto> Items { get; set; } = new();
}

[DwarfMapper]
public partial class M
{
    public partial Dst Map(Src s);

    public partial ChildDto ToDto(Child c);
}
