// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// DWARF073 — <c>[MapProperty(StringFormat="…")]</c> validity. A format string is only meaningful for an
/// IFormattable source mapped to a string destination and not alongside a <c>Use=</c> converter; every other
/// use is refused loudly rather than silently ignored.
/// </summary>
public class StringFormatDiagnosticTests
{
    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string body) =>
        GeneratorTestHarness.Run($$"""
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public int N { get; set; } public DateTime D { get; set; } public string S { get; set; } = ""; }
            public class Dst { public string N { get; set; } = ""; public int NInt { get; set; } public string D { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                {{body}}
                public partial Dst Map(Src s);
            }
            """);

    [Fact]
    public void Valid_format_emits_a_ToString_with_the_format_and_invariant_culture()
    {
        var (diagnostics, generated) = Run(
            "[MapProperty(nameof(Src.N), nameof(Dst.N), StringFormat = \"N0\")] [MapIgnore(\"NInt\")] [MapIgnore(\"D\")]");

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(".ToString(\"N0\", global::System.Globalization.CultureInfo.InvariantCulture)",
            generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Non_string_destination_reports_DWARF073()
    {
        // Target NInt is an int, not a string — a format cannot apply.
        var (diagnostics, _) = Run(
            "[MapProperty(nameof(Src.N), nameof(Dst.NInt), StringFormat = \"N0\")] [MapIgnore(\"N\")] [MapIgnore(\"D\")]");

        Assert.Contains(diagnostics, d => d.Id == "DWARF073");
    }

    [Fact]
    public void StringFormat_with_Use_converter_reports_DWARF073()
    {
        // The converter is declared AFTER the mapping method so the [MapProperty]/[MapIgnore] attributes attach
        // to Map, not to the converter.
        const string source = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public int N { get; set; } }
            public class Dst { public string N { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(Src.N), nameof(Dst.N), Use = nameof(Conv), StringFormat = "N0")]
                public partial Dst Map(Src s);
                public static string Conv(int v) => v.ToString();
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        var d = Assert.Single(diagnostics.Where(x => x.Id == "DWARF073"));
        Assert.Contains("Use=", d.GetMessage(CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Different_formats_on_the_same_source_type_get_distinct_converters()
    {
        // Two members of the same source type (int) with different formats must not collide on one synthesized
        // method — the format is part of the converter's identity.
        const string source = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; set; } public int B { get; set; } }
            public class Dst { public string A { get; set; } = ""; public string B { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(Src.A), nameof(Dst.A), StringFormat = "N0")]
                [MapProperty(nameof(Src.B), nameof(Dst.B), StringFormat = "X8")]
                public partial Dst Map(Src s);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("\"N0\"", generated, System.StringComparison.Ordinal);
        Assert.Contains("\"X8\"", generated, System.StringComparison.Ordinal);
    }
}
