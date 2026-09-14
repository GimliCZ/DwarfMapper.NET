// SPDX-License-Identifier: GPL-2.0-only
// CASE: A [DwarfMapper] class nested as private inside another type.
// WHY:  The generated convenience extensions, AddDwarfMappers() and the ambient registration are top-level
//       classes that hold a new() of every mapper, and none of them can name a type hidden inside another.
//       Round 30 found this emitting CS0122 in generated files and breaking the whole assembly's build; the fix
//       leaves such a mapper out of all three. Leaving it out silently would still surprise an author who
//       expects AddDwarfMappers() to register it, so DWARF110 says so, names the type that hides it, and gives
//       the accessibility change that includes it. This row pins the id and that wording.
// EXPECT: DWARF110
// EXPECT-MESSAGE DWARF110: Mapper 'Outer.M'
// EXPECT-MESSAGE DWARF110: ('Outer.M' is private)
// EXPECT-MESSAGE DWARF110: AddDwarfMappers() and the ambient registry
// EXPECT-MESSAGE DWARF110: make it and every type it is nested in internal, protected internal or public
// EXPECT-CS:
// NOTE: No CS at all — that is the round-30 fix. The mapper's own partial compiles inside Outer, and nothing
//       outside Outer refers to it any more.

using DwarfMapper;

namespace Demo;

public class Src
{
    public int Id { get; set; }
}

public class Dst
{
    public int Id { get; set; }
}

public partial class Outer
{
    [DwarfMapper]
    private partial class M
    {
        public partial Dst Map(Src s);
    }
}
