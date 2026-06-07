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
    public void Missing_nested_mapper_reports_DWARF005()
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
