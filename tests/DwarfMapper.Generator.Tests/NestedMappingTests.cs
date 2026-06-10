// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class NestedMappingTests
{
    [Fact]
    public void Nested_member_uses_declared_mapper_method()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);
                public partial AddrDto ToDto(Addr a);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Home = ToDto(p.Home)", generated, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Missing_nested_mapper_with_AutoNest_false_reports_DWARF005()
    {
        // With AutoNest=false, nested pairs without a user-declared mapper fall through to DWARF005
        // (the original behavior before Plan 19 Part A).
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper(AutoNest = false)]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void Ambiguous_nested_mappers_report_DWARF013()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person { public Addr Home { get; set; } = new(); }
            public class PersonDto { public AddrDto Home { get; set; } = new(); }
            [DwarfMapper]
            public partial class M
            {
                public partial PersonDto ToDto(Person p);
                public partial AddrDto ToDto(Addr a);
                public partial AddrDto ToDtoAlt(Addr a);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF013" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Home", StringComparison.Ordinal));
    }
}
