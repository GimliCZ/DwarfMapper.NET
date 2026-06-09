// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class NullHandlingTests
{
    [Fact]
    public void NullableValue_to_value_throws_by_default()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("?? throw", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableValue_to_value_uses_default_when_configured()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
            public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("GetValueOrDefault()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_to_nullable_assigns_directly()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int? N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.DoesNotContain("?? throw", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GetValueOrDefault", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void NullableValue_to_widening_value_works()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public long N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void NullableEnum_to_enum_composes()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public enum E1 { A, B } public enum E2 { A, B }
            public class A { public E1? S { get; set; } }
            public class B { public E2 S { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void NullableEnum_to_int_composes()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public enum E1 { A, B }
            public class A { public E1? S { get; set; } }
            public class B { public int S { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }
}
