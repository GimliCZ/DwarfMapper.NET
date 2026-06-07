// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class EnumEdgeCaseTests
{
    [Fact]
    public void Aliased_enum_values_do_not_emit_unreachable_arms()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Src { A = 0, B = 0, C = 1 }
            public enum Dst { A = 0, B = 0, C = 1 }
            public class X { public Src V { get; set; } }
            public class Y { public Dst V { get; set; } }
            [DwarfMapper]
            public partial class M { public partial Y Map(X x); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.DoesNotContain(compileErrors, d => d.Id == "CS8510"); // no unreachable pattern
        Assert.Empty(compileErrors);
    }

    [Fact]
    public void Aliased_enum_to_string_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Src { A = 0, B = 0 }
            public class X { public Src V { get; set; } }
            public class Y { public string V { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial Y Map(X x); }
            """;
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.DoesNotContain(compileErrors, d => d.Id == "CS8510");
        Assert.Empty(compileErrors);
    }

    [Fact]
    public void Nested_and_namespaced_enums_do_not_collide()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Outer { public enum E { X, Y } }
            public enum Outer_E { X, Y }
            public class S { public Outer.E A { get; set; } public Outer_E B { get; set; } }
            public class T { public int A { get; set; } public int B { get; set; } }
            [DwarfMapper]
            public partial class M { public partial T Map(S s); }
            """;
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.DoesNotContain(compileErrors, d => d.Id == "CS1503");
        Assert.Empty(compileErrors);
    }
}
