// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;

namespace DwarfMapper.Generator.Tests;

public class DiagnosticTests
{
    [Fact]
    public void NonPartial_mapper_reports_DWARF002()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } }
            [DwarfMapper]
            public class PersonMapper            // not partial
            {
                public PersonDto ToDto(Person p) => new();
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF002");
    }

    [Fact]
    public void Unmapped_destination_member_reports_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF001" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Name", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Ignored_member_suppresses_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper
            {
                [MapIgnore("Name")]
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
    }

    [Fact]
    public void Void_partial_method_reports_DWARF003()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial void ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF003");
    }

    [Fact]
    public void No_implicit_conversion_reports_DWARF005()
    {
        // string→int now auto-resolves via IParsable<int>; use truly incompatible types
        // (a custom class that is not implicitly convertible and not IParsable/IFormattable).
        const string src = """
            using DwarfMapper;
            public class Widget { public int X { get; set; } }
            public class PersonDto { public Widget Age { get; set; } = new(); }
            public class Person    { public int   Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void Destination_with_ctor_only_and_unmapped_param_reports_DWARF024()
    {
        // PersonDto has a single non-parameterless ctor with param 'x', but source has 'Age' (no 'x').
        // Previously DWARF006 (no parameterless ctor); now DWARF024 (ctor param unmapped).
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public PersonDto(int x) { Age = x; } public int Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF024");
    }

    [Fact]
    public void ReadOnly_destination_with_matching_source_reports_DWARF007()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; } }   // read-only, no setter
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF007" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Age", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ReadOnly_destination_without_matching_source_is_silent()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public int Doubled => Age * 2; } // computed, no source
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReadOnly_destination_can_be_silenced_with_MapIgnore()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; } }
            [DwarfMapper]
            public partial class PersonMapper
            {
                [MapIgnore("Age")]
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
    }

    [Fact]
    public void InitOnly_destination_is_mapped_not_flagged()
    {
        const string src = """
            using DwarfMapper;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; init; } }
            [DwarfMapper]
            public partial class PersonMapper
            {
                public partial PersonDto ToDto(Person p);
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains("Age = p.Age", generated, System.StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
