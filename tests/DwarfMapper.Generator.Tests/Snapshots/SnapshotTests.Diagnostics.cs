// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── DWARF001: unmapped destination member ─────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF001_UnmappedMember()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF001")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }

    // ── DWARF005: no implicit conversion ──────────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF005_NoImplicitConversion()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Widget    { public int X { get; set; } }
            public class PersonDto { public Widget Age { get; set; } = new(); }
            public class Person    { public int   Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF005")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }

    // ── DWARF024: ctor param unmapped ─────────────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF024_CtorParamUnmapped()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Person { public int Age { get; set; } }
            public class PersonDto { public PersonDto(int x) { Age = x; } public int Age { get; set; } }
            [DwarfMapper]
            public partial class PersonMapper { public partial PersonDto ToDto(Person p); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF024")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }

    // ── DWARF028: projection not translatable ─────────────────────────────────
    [Fact]
    public Task Snap_Diag_DWARF028_ProjectionNotTranslatable()
    {
        // Use a converter (Use=) which cannot be translated to an expression tree
        const string src = """
            using DwarfMapper;
            using System.Linq;
            namespace Demo;
            public class Person    { public string Amount { get; set; } = ""; }
            public class PersonDto { public int Amount { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty("Amount","Amount", Use = nameof(ParseInt))]
                public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
                private static int ParseInt(string v) => int.Parse(v);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        var snapshot = string.Join("\n",
            diagnostics
                .Where(d => d.Id == "DWARF028")
                .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage(CultureInfo.InvariantCulture)}")
                .OrderBy(s => s));
        return Verifier.Verify(snapshot);
    }
}
