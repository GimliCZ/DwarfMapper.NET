// SPDX-License-Identifier: GPL-2.0-only
// CASE: a projection whose {Method}Expression property name is already taken by a member the mapper declares.
// WHY:  Round 31 T16 exposes each projection's tree as a static {Method}Expression. When the name is taken, emitting
//       it would be CS0102 (or CS0108 against a base member) inside the .g.cs, so the property is left out — and the
//       reader who looks for it is told why here rather than meeting CS0117 at a call site. Info, not Warning: the
//       property is a new convenience, and an existing mapper that happens to own the name must not fail a
//       warnings-as-errors build because of it.
// EXPECT: DWARF112
// EXPECT-MESSAGE DWARF112: The expression property 'ProjectExpression' was not generated for projection 'Project'
// EXPECT-MESSAGE DWARF112: 'M' already declares a member of that name
// EXPECT-MESSAGE DWARF112: Rename the member or the method to get it
// EXPECT-CS:
// NOTE: No CS: the projection itself is generated and works; only the convenience property is absent.

#nullable enable

using System.Linq;
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

[DwarfMapper]
public partial class M
{
    public partial IQueryable<Dst> Project(IQueryable<Src> q);

    public int ProjectExpression => 42;
}
