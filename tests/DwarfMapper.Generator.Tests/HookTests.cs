// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class HookTests
{
    [Fact]
    public void BeforeMap_is_called_with_source()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [BeforeMap] private static void Check(Src s) { }
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("Check(s);", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterMap_two_param_is_called_with_source_and_target()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [AfterMap] private static void Finish(Src s, Dst d) { }
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("__dwarf_target", gen, StringComparison.Ordinal);
        Assert.Contains("Finish(s, __dwarf_target);", gen, StringComparison.Ordinal);
        Assert.Contains("return __dwarf_target;", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterMap_one_param_is_called_with_target()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [AfterMap] private static void Touch(Dst d) { }
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("Touch(__dwarf_target);", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_hook_signature_reports_DWARF018()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [BeforeMap] private static int Bad(Src s) => 0;   // non-void
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF018");
    }

    [Fact]
    public void Hook_typed_object_applies_to_all()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [BeforeMap] private static void Log(object o) { }
            }
            """;
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("Log(s);", gen, StringComparison.Ordinal);
    }
}
