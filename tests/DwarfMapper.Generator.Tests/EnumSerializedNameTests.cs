// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Enum↔string honours <c>[EnumMember(Value=)]</c> / <c>[Description]</c> for the string form. These lock the
/// generator-visible behaviour: precedence, and — the tricky bit — that duplicate serialized names cannot emit
/// two identical <c>case</c> labels (CS0152) in the string→enum switch.
/// </summary>
public class EnumSerializedNameTests
{
    [Fact]
    public void Enum_to_string_emits_the_serialized_name_with_correct_precedence()
    {
        const string source = """
            using System.ComponentModel;
            using System.Runtime.Serialization;
            using DwarfMapper;
            namespace Demo;
            public enum E { [EnumMember(Value="em")] [Description("desc")] Both, [Description("d2")] Desc, Plain }
            public class Src { public E V { get; set; } }
            public class Dst { public string V { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { [MapProperty(nameof(Src.V), nameof(Dst.V))] public partial Dst Map(Src s); }
            """;

        var (_, generated) = GeneratorTestHarness.Run(source);

        Assert.Contains("=> \"em\"", generated, System.StringComparison.Ordinal);   // EnumMember beats Description
        Assert.Contains("=> \"d2\"", generated, System.StringComparison.Ordinal);   // Description
        Assert.Contains("=> \"Plain\"", generated, System.StringComparison.Ordinal); // identifier fallback
        Assert.DoesNotContain("=> \"desc\"", generated, System.StringComparison.Ordinal); // Description lost to EnumMember
    }

    [Fact]
    public void Duplicate_serialized_names_do_not_emit_duplicate_case_labels()
    {
        // Two members share a serialized name. Naively this emits two identical string→enum labels (CS0152, an
        // uncompilable-generated-code defect). De-dup keeps the first; the code must compile.
        const string source = """
            using System.Runtime.Serialization;
            using DwarfMapper;
            namespace Demo;
            public enum E { [EnumMember(Value="same")] A, [EnumMember(Value="same")] B, C }
            public class Src { public string V { get; set; } = ""; }
            public class Dst { public E V { get; set; } }
            [DwarfMapper]
            public partial class M { [MapProperty(nameof(Src.V), nameof(Dst.V))] public partial Dst Map(Src s); }
            """;

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source).ToList();

        Assert.True(compileErrors.Count == 0,
            "Duplicate [EnumMember] values produced uncompilable code:\n  "
            + string.Join("\n  ",
                compileErrors.Select(e => $"{e.Id}: {e.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")));
    }

    [Fact]
    public void A_flags_enum_keeps_member_names_not_serialized_names()
    {
        // Documented scope: [Flags] enum string form stays identifier-based (Enum.ToString semantics), so its
        // custom [EnumMember] names are NOT applied.
        const string source = """
            using System;
            using System.Runtime.Serialization;
            using DwarfMapper;
            namespace Demo;
            [Flags] public enum E { None = 0, [EnumMember(Value="a!")] A = 1, B = 2 }
            public class Src { public E V { get; set; } }
            public class Dst { public string V { get; set; } = ""; }
            [DwarfMapper]
            public partial class M { [MapProperty(nameof(Src.V), nameof(Dst.V))] public partial Dst Map(Src s); }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("a!", generated, System.StringComparison.Ordinal); // custom name NOT used for flags
    }
}
