// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Update-into-existing mapping: void/T Map(S src, T dest). Member resolution (and the completeness
/// gate) is identical to normal mapping; only the emission differs (dest.Member = … vs new T { … }).
/// </summary>
public class MapIntoExistingGeneratorTests
{
    [Fact]
    public void Void_update_method_compiles_and_assigns_onto_dest()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class D { public int Id { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial void Update(S src, D dest); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Assigns onto the existing dest instance (no `new D`).
        Assert.Contains("dest.Id = ", generated, StringComparison.Ordinal);
        Assert.Contains("dest.Name = ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("new global::Demo.D", generated, StringComparison.Ordinal);
        // Both parameters null-guarded via the BCL throw-helper.
        Assert.Contains("ArgumentNullException.ThrowIfNull(src)", generated, StringComparison.Ordinal);
        Assert.Contains("ArgumentNullException.ThrowIfNull(dest)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Return_form_returns_dest()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Update(S src, D dest); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("return dest;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmapped_dest_member_still_triggers_completeness_DWARF001()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public string Extra { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { public partial void Update(S src, D dest); }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        // Extra has no source → completeness gate fires (build error), same as normal mapping.
        Assert.Contains(diags, d => d.Id == "DWARF001");
    }

    [Fact]
    public void MapIgnore_and_MapProperty_apply_to_update_methods()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } public string Full { get; set; } = ""; }
            public class D { public int Id { get; set; } public string Name { get; set; } = ""; public string Computed { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(S.Full), nameof(D.Name))]
                [MapIgnore(nameof(D.Computed))]
                public partial void Update(S src, D dest);
            }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("dest.Name = ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("dest.Computed", generated, StringComparison.Ordinal); // ignored
    }
}
