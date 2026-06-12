// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Flat projection via Queryable.Select ──────────────────────────────────
    [Fact]
    public Task Snap_Projection_Flat()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person    { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Projection with MapProperty rename ────────────────────────────────────
    [Fact]
    public Task Snap_Projection_Rename()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person    { public string FullName { get; set; } = ""; }
            public class PersonDto { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName","Name")]
                public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Nested member-init projection ─────────────────────────────────────────
    [Fact]
    public Task Snap_Projection_Nested_MemberInit()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Addr   { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); public string Name { get; set; } = ""; }
            public class AddrDto   { public string City { get; set; } = ""; }
            public class PersonDto { public AddrDto Home { get; set; } = new(); public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Constructor projection (record target) ────────────────────────────────
    [Fact]
    public Task Snap_Projection_Constructor()
    {
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            public record PersonDto(int Age, string Name);
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
