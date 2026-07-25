// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// DWARF072 / <c>[DwarfMapper(AutoMatchMembers = false)]</c> — the trust-boundary / anti-over-posting guard.
/// <para>
/// By-name auto-matching is exactly how mass assignment (OWASP API6) happens with a mapper: an
/// attacker-controlled <c>IsAdmin</c> that lines up by name with a protected entity field is copied silently,
/// and the completeness gate never notices because the field IS mapped. Proven directly: with auto-matching on,
/// <c>IsAdmin = input.IsAdmin</c> is emitted with zero diagnostics. Explicit-only mode refuses to auto-wire, so
/// the over-post becomes a build error the developer must resolve deliberately.
/// </para>
/// <para>
/// The negative cases matter as much as the positive one: explicit <c>[MapProperty]</c>, <c>[MapValue]</c>,
/// <c>[MapIgnore]</c> and constructor parameters must still work, or the mode is unusable and nobody adopts it.
/// </para>
/// </summary>
public class ExplicitOnlyTrustBoundaryTests
{
    private static string[] Ids(string source) =>
        GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();

    [Fact]
    public void Default_mapper_silently_auto_wires_a_same_named_field()
    {
        // The baseline this feature exists to fix: without the guard, a same-named field auto-wires with no
        // diagnostic. This is the mass-assignment amplifier, asserted so a regression that "helpfully" made
        // auto-match safe-by-default (changing everyone's behaviour) would show up here.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class UserInput { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
            public class UserEntity { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
            [DwarfMapper]
            public partial class M { public partial UserEntity Map(UserInput input); }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("IsAdmin = input.IsAdmin", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_only_refuses_to_auto_wire_a_same_named_field()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class UserInput { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
            public class UserEntity { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
            [DwarfMapper(AutoMatchMembers = false)]
            public partial class M { public partial UserEntity Map(UserInput input); }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        // Both Name and IsAdmin have same-named sources; both must be surfaced, not silently wired.
        var d072 = diagnostics.Where(x => x.Id == "DWARF072").ToList();
        Assert.Equal(2, d072.Count);
        Assert.All(d072, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        var messages = string.Join(" | ", d072.Select(d => d.GetMessage(CultureInfo.InvariantCulture)));
        Assert.Contains("IsAdmin", messages, StringComparison.Ordinal);
        Assert.Contains("Name", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_only_honours_MapProperty_and_MapIgnore_and_then_compiles()
    {
        // The intended usage: map what you allow, ignore what you protect — every field a deliberate decision.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class UserInput { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
            public class UserEntity { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
            [DwarfMapper(AutoMatchMembers = false)]
            [MapIgnore("IsAdmin")]
            public partial class M
            {
                [MapProperty(nameof(UserInput.Name), nameof(UserEntity.Name))]
                public partial UserEntity Map(UserInput input);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF072");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // Name is mapped (explicitly); IsAdmin is not assigned at all (ignored) — the protected field cannot
        // be over-posted.
        Assert.Contains("Name = input.Name", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAdmin = input.IsAdmin", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_only_still_reports_a_truly_unmapped_member_as_DWARF001_not_DWARF072()
    {
        // A destination member with NO source at all is the DWARF001 case, unchanged. DWARF072 is specifically
        // the "a source EXISTS but I won't auto-wire it" case — the distinction is the whole point.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class UserInput { public string Name { get; set; } = ""; }
            public class UserEntity { public string Name { get; set; } = ""; public int Orphan { get; set; } }
            [DwarfMapper(AutoMatchMembers = false)]
            public partial class M
            {
                [MapProperty(nameof(UserInput.Name), nameof(UserEntity.Name))]
                public partial UserEntity Map(UserInput input);
            }
            """;

        var ids = Ids(source);
        Assert.Contains("DWARF001", ids); // Orphan: no source
        Assert.DoesNotContain("DWARF072", ids); // Name is explicitly mapped; nothing auto-wireable remains
    }

    [Fact]
    public void Explicit_only_still_allows_MapValue_to_satisfy_a_member()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class UserInput { public string Name { get; set; } = ""; }
            public class UserEntity { public string Name { get; set; } = ""; public string Source { get; set; } = ""; }
            [DwarfMapper(AutoMatchMembers = false)]
            public partial class M
            {
                [MapProperty(nameof(UserInput.Name), nameof(UserEntity.Name))]
                [MapValue(nameof(UserEntity.Source), "api")]
                public partial UserEntity Map(UserInput input);
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Explicit_only_still_resolves_constructor_parameters()
    {
        // A ctor-only target (positional record) must remain mappable — ctor params are structurally required,
        // not an optional over-post. Otherwise the mode would be unusable for records.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
            public record Dst(int Id, string Name);
            [DwarfMapper(AutoMatchMembers = false)]
            public partial class M { public partial Dst Map(Src s); }
            """;

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();
        Assert.True(compileErrors.Count == 0,
            "Explicit-only broke constructor-parameter resolution:\n  "
            + string.Join("\n  ", compileErrors.Select(e => $"{e.Id}: {e.GetMessage(CultureInfo.InvariantCulture)}")));
    }

    [Fact]
    public void Explicit_only_leaves_a_nested_explicitly_mapped_object_mapping_normally()
    {
        // Explicit-only guards the TOP-LEVEL boundary. Once the developer explicitly maps a nested edge, the
        // nested contents map by auto-match (the developer opted into that whole object). Must compile.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Src { public string Name { get; set; } = ""; public Addr Home { get; set; } = new(); }
            public class Dst { public string Name { get; set; } = ""; public AddrDto Home { get; set; } = new(); }
            [DwarfMapper(AutoMatchMembers = false)]
            public partial class M
            {
                [MapProperty(nameof(Src.Name), nameof(Dst.Name))]
                [MapProperty(nameof(Src.Home), nameof(Dst.Home))]
                public partial Dst Map(Src s);
            }
            """;

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();
        Assert.True(compileErrors.Count == 0,
            "Explicit-only broke a nested explicitly-mapped object:\n  "
            + string.Join("\n  ", compileErrors.Select(e => $"{e.Id}: {e.GetMessage(CultureInfo.InvariantCulture)}")));
    }
}
