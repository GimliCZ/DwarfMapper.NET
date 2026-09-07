// SPDX-License-Identifier: GPL-2.0-only
// CASE: [GenerateView(Name = "3 dogs")] — round 29, Phase 1, review round 1
// WHY:  The Name is the nested `readonly ref struct`'s type name, and it is written into generated C#
//       VERBATIM — into three positions: the struct declaration, its constructor, and the factory's
//       return type. A value that is not a usable type name therefore does not fail at the attribute; it
//       fails as CS1001/CS1514/CS1513 inside a .g.cs the consumer cannot edit and did not write, for
//       what is an ordinary typing mistake. That is the unsuppressible-diagnostic class round 29 exists
//       to clear, appearing in code this very round shipped.
//
//       Measured before the check was written, not assumed: `Name = "3 dogs"` emitted
//       `public readonly ref struct 3 dogs` with no generator diagnostic at all. Three neighbouring
//       shapes were probed at the same time — a reserved keyword ("class") produced the same CS1001, an
//       empty string silently discarded what the consumer wrote and took the default, and a name already
//       used by a type on the mapper produced CS0102 (that last one is DWARF102, not this id: the name is
//       valid, it is occupied, which is the collision DWARF102 already refuses between two views).
//
//       LOCATED ON THE ARGUMENT, not on the attribute and not on the class: the offending text is the
//       argument and nothing else on that line is wrong.
//
//       The refusal is deliberately BROADER than the compiler's minimum for contextual keywords — see
//       IsUsableTypeName, which records the measurement (5 of 29 actually break, and `scoped` parses into
//       the same tree shape as a good name, so an exact syntactic test is impossible).
// EXPECT: DWARF108
// EXPECT-MESSAGE DWARF108: is not a usable C# type name
// EXPECT-MESSAGE DWARF108: written into generated code exactly as given
// EXPECT-MESSAGE DWARF108: valid C# identifier that is not a reserved keyword
// EXPECT-MESSAGE DWARF108: omit Name to take the default 'DstView'

using DwarfMapper;

namespace Demo;

public sealed class Src
{
    public int Id { get; set; }
}

public sealed class Dst
{
    public int Id { get; set; }
}

[DwarfMapper]
[GenerateView<Src, Dst>(Name = "3 dogs")]
public partial class M
{
}
