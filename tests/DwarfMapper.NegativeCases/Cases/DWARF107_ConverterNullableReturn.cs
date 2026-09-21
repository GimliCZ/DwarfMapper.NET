// SPDX-License-Identifier: GPL-2.0-only
// CASE: A converter the user declared returns a nullable reference, and its result is written into a
//       destination member whose type forbids null.
// WHY:  Round 29 task 2.9. DWARF070 covers a nullable value going IN; this is a nullable value coming BACK
//       OUT, and the two are not the same diagnostic. DWARF070 opens "{0} is a nullable reference" and here
//       NOTHING on the source side is — `Child Inner` into `ChildDto Inner`, non-nullable end to end, and the
//       generated file warned anyway (CS8601). No noun phrase makes that sentence honest, the remedies are
//       disjoint ([MapProperty(NullSubstitute)], SkipNullSourceMembers and "declare the parameter
//       non-nullable" are all about the value going in), and a consumer who suppressed DWARF070 accepted
//       nullable SOURCES — not a converter that hands back null. This row pins the id, the two nouns the
//       message must carry, and the sentence that says the suppressions are not interchangeable.
// EXPECT: DWARF107
// EXPECT-MESSAGE DWARF107: 'ToDto' is declared to return a nullable reference
// EXPECT-MESSAGE DWARF107: destination member 'Inner'
// EXPECT-MESSAGE DWARF107: declaring 'ToDto' to return a non-nullable reference
// EXPECT-MESSAGE DWARF107: dotnet_diagnostic.DWARF107.severity = none
// EXPECT-MESSAGE DWARF107: DWARF070's suppression does NOT cover this
// EXPECT-CS:
// NOTE: No CS at all is the other half of the case, and here it is the whole trade. The call is null-forgiven,
//       so the CS8601 that used to land in a .g.cs no consumer can edit is gone — which means a null returned
//       at run time is now STORED rather than refused. This warning is all the consumer gets in exchange, so
//       it has to say so, and the wording above is what pins that.

#nullable enable

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
    public Child Inner { get; set; } = new();
}

public class Dst
{
    public ChildDto Inner { get; set; } = new();
}

[DwarfMapper]
public partial class M
{
    public partial Dst Map(Src s);

    public partial ChildDto? ToDto(Child c);
}
