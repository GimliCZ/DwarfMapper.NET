// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// A <c>[DwarfMapper]</c> partial class declared INSIDE another type (a nested mapper).
/// <para>
/// Found while writing an integration fixture: nesting the mapper in the test class made the generated file
/// declare the partial at the wrong scope, so the compiler reported CS0759 ("no defining declaration for the
/// implementing partial method") and CS8795 — <b>out of generated code, with no DwarfMapper diagnostic</b>.
/// </para>
/// <para>
/// Nesting a mapper inside its owning service or inside a test class is an ordinary thing to write, and every
/// existing test happened to declare mappers at namespace scope, so nothing covered it. Whatever the answer
/// is, it must be one of the two the project already commits to everywhere else: <b>support it</b> (and emit
/// code that compiles), or <b>refuse it</b> with a DWARF diagnostic. Silently emitting broken code is the one
/// outcome that is not allowed.
/// </para>
/// </summary>
public class NestedMapperClassTests
{
    private const string NestedMapper = """
        using DwarfMapper;
        namespace Demo;
        public partial class Outer
        {
            public class A { public int X { get; set; } }
            public class B { public int X { get; set; } }

            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
        }
        """;

    private const string DoublyNestedMapper = """
        using DwarfMapper;
        namespace Demo;
        public partial class Outer
        {
            public partial class Middle
            {
                public class A { public int X { get; set; } }
                public class B { public int X { get; set; } }

                [DwarfMapper]
                public partial class M { public partial B Map(A a); }
            }
        }
        """;

    // The containing type is NOT partial. C# cannot complete a partial type inside a non-partial one, so this
    // must be refused with DWARF002 ("mapper type must be partial") — pointed at the OUTER type, which is what
    // the user has to change. Without this the compiler would instead spit CS0759/CS8795 out of generated code.
    private const string NonPartialOuter = """
        using DwarfMapper;
        namespace Demo;
        public class Outer
        {
            public class A { public int X { get; set; } }
            public class B { public int X { get; set; } }

            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
        }
        """;

    // ISSUE-006 — the re-declared partial must use the SAME type keyword as the outer type. The keyword was
    // computed as `struct` or `class`, collapsing every non-struct to "class", so a mapper nested in a RECORD
    // (or an interface, or a record struct) emitted `partial class Outer` over a `record Outer` → CS0261, again
    // a raw compiler error out of generated code with no DwarfMapper diagnostic.
    private static string OuterOfKind(string declaration)
    {
        return $$"""
                 using DwarfMapper;
                 namespace Demo;
                 public partial {{declaration}} Outer
                 {
                     public class A { public int X { get; set; } }
                     public class B { public int X { get; set; } }

                     [DwarfMapper]
                     public partial class M { public partial B Map(A a); }
                 }
                 """;
    }

    [Theory]
    [InlineData("record")]
    [InlineData("record struct")]
    [InlineData("struct")]
    [InlineData("interface")]
    [InlineData("class")]
    public void Mapper_nested_in_any_type_kind_emits_code_that_compiles(string kind)
    {
        var source = OuterOfKind(kind);

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();
        Assert.True(
            compileErrors.Count == 0,
            $"A [DwarfMapper] class nested inside a '{kind}' was accepted, but the emitted code does not "
            + "compile:\n  "
            + string.Join("\n  ",
                compileErrors.Select(e => $"{e.Id}: {e.GetMessage(CultureInfo.InvariantCulture)}"))
            + "\n\nThe re-declared partial must repeat the outer type's own keyword.\n\nGenerated:\n" + generated);
    }

    [Theory]
    [InlineData(nameof(NestedMapper))]
    [InlineData(nameof(DoublyNestedMapper))]
    public void Nested_mapper_emits_code_that_compiles(string which)
    {
        var source = which == nameof(NestedMapper) ? NestedMapper : DoublyNestedMapper;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();

        Assert.True(
            compileErrors.Count == 0,
            "A [DwarfMapper] class nested inside another type was accepted by the generator, but the emitted "
            + "code does not compile:\n  "
            + string.Join("\n  ",
                compileErrors.Select(e => $"{e.Id}: {e.GetMessage(CultureInfo.InvariantCulture)}"))
            + "\n\nThe generated partial must be re-declared inside the SAME containing type(s), not at "
            + "namespace scope.\n\nGenerated:\n" + generated);
    }

    [Fact]
    public void Nested_mapper_generated_half_is_declared_inside_its_containing_type()
    {
        var (_, generated) = GeneratorTestHarness.Run(NestedMapper);

        Assert.Contains("partial class Outer", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_partial_containing_type_is_refused_with_DWARF002_not_a_raw_compiler_error()
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(NonPartialOuter);

        var d = Assert.Single(diagnostics.Where(x => x.Id == "DWARF002"));
        Assert.Contains("Outer", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_and_namespace_level_mappers_of_the_same_name_do_not_collide()
    {
        // Both are named M. If the hint name ignored the containing type they would map to the same generated
        // file name, and AddSource throws on a duplicate hint — taking the whole generator down.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int X { get; set; } }
            public class B { public int X { get; set; } }

            [DwarfMapper]
            public partial class M { public partial B Map(A a); }

            public partial class Outer
            {
                [DwarfMapper]
                public partial class M { public partial B Map(A a); }
            }
            """;

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();

        Assert.True(compileErrors.Count == 0,
            "A nested mapper and a namespace-level mapper of the same name broke the generator:\n  "
            + string.Join("\n  ",
                compileErrors.Select(e => $"{e.Id}: {e.GetMessage(CultureInfo.InvariantCulture)}")));
    }
}
