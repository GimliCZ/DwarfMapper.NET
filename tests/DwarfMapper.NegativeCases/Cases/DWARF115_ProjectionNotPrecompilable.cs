// SPDX-License-Identifier: GPL-2.0-only
// CASE: a projection method in a project that publishes NativeAOT and references EF Core.
// WHY:  Round 31 T17. Measured on EF Core 10: `dotnet ef dbcontext optimize --precompile-queries` refuses a call to a
//       generated Project(db.Orders) as a dynamic query, and under NativeAOT a query that was not precompiled fails
//       when it first runs. The same tree composed at the call site, .Select(M.ProjectExpression), is precompiled.
//       Both gates are project facts, so the case declares them: PublishAot as a build property, EF Core as a
//       referenced assembly (an empty stand-in of that name - the check reads the reference, not a type).
// BUILD-PROPERTY: PublishAot=true
// REFERENCES-ASSEMBLY: Microsoft.EntityFrameworkCore
// EXPECT: DWARF115
// EXPECT-MESSAGE DWARF115: Projection 'Project' on 'M' cannot be precompiled by EF Core
// EXPECT-MESSAGE DWARF115: this project publishes NativeAOT
// EXPECT-MESSAGE DWARF115: Select(M.ProjectExpression)
// EXPECT-CS:
// NOTE: A warning: the projection is generated and works in a JIT app; only its EF precompilation is at stake.

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
}
