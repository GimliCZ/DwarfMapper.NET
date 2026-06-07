// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class FlattenTests
{
    [Fact]
    public void Flattens_nested_member_to_top_level()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Address { public string City { get; set; } = ""; }
            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public string City { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [Flatten("Address")]
                public partial CustomerDto ToDto(Customer c);
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("City = c.Address.City", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_flatten_root_reports_DWARF016()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Customer { public string Name { get; set; } = ""; }
            public class CustomerDto { public string City { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [Flatten("Nope")]
                public partial CustomerDto ToDto(Customer c);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF016" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Ambiguous_flatten_reports_DWARF017()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string City { get; set; } = ""; }
            public class B { public string City { get; set; } = ""; }
            public class Customer { public A Home { get; set; } = new(); public B Work { get; set; } = new(); }
            public class CustomerDto { public string City { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [Flatten("Home")]
                [Flatten("Work")]
                public partial CustomerDto ToDto(Customer c);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF017" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("City", StringComparison.Ordinal));
    }

    [Fact]
    public void Flattened_leaf_uses_conversion()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green }
            public class Inner { public Color Shade { get; set; } }
            public class Src { public Inner Inner { get; set; } = new(); }
            public class Dst { public int Shade { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [Flatten("Inner")]
                public partial Dst ToDto(Src s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));  // enum->int on the flattened leaf
    }
}
