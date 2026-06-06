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
}
