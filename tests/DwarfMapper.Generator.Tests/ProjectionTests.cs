// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class ProjectionTests
{
    [Fact]
    public void DWARF028_descriptor_exists_and_is_error()
    {
        var d = DiagnosticDescriptors.ProjectionNotTranslatable;
        Assert.Equal("DWARF028", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.DefaultSeverity);
    }

    // ISSUE-002 — under CaseInsensitive the projection resolvers grouped source members by name and took
    // g.First(), silently binding one of two case-only-distinct members. The runtime map path reports DWARF010
    // for the identical input, so the same source gave a loud error via .Map(...) and a silent (and, for a
    // partial source type, build-order-dependent) answer via projection. All three resolvers now share one
    // lookup that reports DWARF010 and binds nothing.
    private const string CaseCollisionSource = """
                                               using DwarfMapper;
                                               using System.Linq;
                                               namespace Demo;
                                               public class Src { public int Foo { get; set; } public int foo { get; set; } }
                                               public class Dst { public int Foo { get; set; } }
                                               """;

    [Fact]
    public void Projection_case_insensitive_member_collision_is_ambiguous_not_silent()
    {
        const string s = CaseCollisionSource + """
                                               [DwarfMapper(CaseInsensitive = true)]
                                               public partial class M
                                               {
                                                   public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                               }
                                               """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF010");
    }

    // (A "both APIs on one mapper" test was considered and deliberately NOT added: the runtime path already
    // emitted DWARF010, so such a test passes identically before and after this fix — it would look like a
    // regression gate while proving nothing. The projection-only test above is the real gate.)

    [Fact]
    public void Projection_without_a_collision_still_binds_normally()
    {
        // Guards the fix against over-reach: a non-colliding case-insensitive match must still project.
        const string s = """
                         using DwarfMapper;
                         using System.Linq;
                         namespace Demo;
                         public class Src { public int Foo { get; set; } }
                         public class Dst { public int foo { get; set; } }
                         [DwarfMapper(CaseInsensitive = true)]
                         public partial class M
                         {
                             public partial IQueryable<Dst> Project(IQueryable<Src> q);
                         }
                         """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF010");
        Assert.Contains("foo =", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_duplicate_MapProperty_reports_DWARF011_and_compiles()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Linq;
                         namespace Demo;
                         public class Person { public string A { get; set; } = ""; public string B { get; set; } = ""; }
                         public class PersonDto { public string Name { get; set; } = ""; }
                         [DwarfMapper]
                         public partial class M
                         {
                             [MapProperty("A", "Name")]
                             [MapProperty("B", "Name")]
                             public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
                         }
                         """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF011");
        Assert.DoesNotContain(GeneratorTestHarness.RunAndGetCompilationErrors(s), d => d.Id == "CS1912");
    }

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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
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
    public void Projection_non_assignable_member_reports_DWARF028()
    {
        // Plan 19D: string→int in a projection is an untranslatable string-parse → DWARF028.
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
        Assert.Contains(diagnostics, d => d.Id == "DWARF028");
    }
}
