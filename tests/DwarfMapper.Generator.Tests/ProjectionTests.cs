// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class ProjectionTests
{
    [Fact]
    public void Projects_flat_members_via_select()
    {
        const string s = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("global::System.Linq.Queryable.Select(", gen, StringComparison.Ordinal);
        Assert.Contains("Age = __s.Age", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_rename_via_MapProperty()
    {
        const string s = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public string FullName { get; set; } = ""; }
            public class PersonDto { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("FullName", "Name")]
                public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Name = __s.FullName", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_unmapped_member_reports_DWARF001()
    {
        const string s = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF001");
    }

    [Fact]
    public void Projection_non_assignable_member_reports_DWARF019()
    {
        const string s = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person { public string Age { get; set; } = ""; }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M { public partial IQueryable<PersonDto> Project(IQueryable<Person> src); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF019");
    }
}
