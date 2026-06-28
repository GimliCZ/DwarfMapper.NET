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

    [Fact]
    public void UpdateInto_initOnly_target_reports_DWARF007_not_CS8852()
    {
        // An init-only target is writable in a CREATE map (object initializer) but NOT in update-into
        // (assignment post-construction). The generator must surface DWARF007, not emit an invalid
        // assignment that the C# compiler rejects with CS8852.
        const string src = """
            using DwarfMapper;
            public class S { public int Id { get; init; } public string Name { get; set; } = ""; }
            public class D { public int Id { get; init; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                public partial void Map(S s, D d);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF007"
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Id", System.StringComparison.Ordinal));
        Assert.DoesNotContain(GeneratorTestHarness.RunAndGetCompilationErrors(src), e => e.Id == "CS8852");
    }

    [Fact]
    public void UpdateInto_initOnly_silenced_with_MapIgnore_compiles_clean()
    {
        const string src = """
            using DwarfMapper;
            public class S { public int Id { get; init; } public string Name { get; set; } = ""; }
            public class D { public int Id { get; init; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapIgnore("Id")]
                public partial void Map(S s, D d);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF007");
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── Item 12: DWARF064 — [MapValue] shadows a same-named source member ──────────
    [Fact]
    public void MapValue_shadowing_source_member_reports_DWARF064()
    {
        const string src = """
            using DwarfMapper;
            public class S { public int A { get; set; } public string Name { get; set; } = ""; }
            public class D { public int A { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            [MapValue<D>("Name", "fixed")]   // shadows source S.Name
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF064"
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Name", System.StringComparison.Ordinal));
    }

    [Fact]
    public void MapValue_with_no_matching_source_member_does_not_report_DWARF064()
    {
        const string src = """
            using DwarfMapper;
            public class S { public int A { get; set; } }
            public class D { public int A { get; set; } public string Stamp { get; set; } = ""; }
            [DwarfMapper]
            [MapValue<D>("Stamp", "fixed")]  // no source member named Stamp → not a shadow
            public partial class M { public partial D Map(S s); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF064");
    }

    // ── Item 13: DWARF065 — update-into replaces a nested member ───────────────────
    [Fact]
    public void UpdateInto_nested_member_reports_DWARF065()
    {
        const string src = """
            using DwarfMapper;
            public class Inner { public int V { get; set; } }
            public class InnerDto { public int V { get; set; } }
            public class S { public Inner Child { get; set; } = new(); public int Top { get; set; } }
            public class D { public InnerDto Child { get; set; } = new(); public int Top { get; set; } }
            [DwarfMapper]
            public partial class M { public partial void Update(S s, D d); }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF065"
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Child", System.StringComparison.Ordinal));
        // A scalar member is a direct copy, not a replacement → no DWARF065 for it.
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF065"
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Top", System.StringComparison.Ordinal));
    }

    // ── Item 14: DWARF066 — [MapProperty(When=)] leaves a non-nullable member at default ──
    [Fact]
    public void When_guard_on_non_nullable_reference_reports_DWARF066()
    {
        const string src = """
            using DwarfMapper;
            #nullable enable
            public class S { public string Note { get; set; } = ""; }
            public class D { public string Note { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(S.Note), nameof(D.Note), When = nameof(Keep))]
                public partial D Map(S s);
                private static bool Keep(S s) => s.Note.Length > 0;
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src, Microsoft.CodeAnalysis.NullableContextOptions.Enable);
        Assert.Contains(diagnostics, d => d.Id == "DWARF066"
            && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Note", System.StringComparison.Ordinal));
    }

    [Fact]
    public void When_guard_on_nullable_reference_does_not_report_DWARF066()
    {
        const string src = """
            using DwarfMapper;
            #nullable enable
            public class S { public string? Note { get; set; } }
            public class D { public string? Note { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(S.Note), nameof(D.Note), When = nameof(Keep))]
                public partial D Map(S s);
                private static bool Keep(S s) => s.Note is not null;
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src, Microsoft.CodeAnalysis.NullableContextOptions.Enable);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF066");
    }
}
