// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DwarfMapper.Generator.Tests.Fuzzing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generated code must not merely COMPILE — it must compile <b>without warnings</b>, in a nullable-annotated
/// context, because that is the build every real consumer has.
/// <para>
/// The suite already asserted that emitted code compiles, but "compiles" was defined as
/// <see cref="DiagnosticSeverity.Error" />. A generated file that only WARNS slipped through every tier — and
/// a warning out of generated code is strictly worse than one out of hand-written code: the consumer cannot
/// edit the file to fix it, cannot annotate it, and with <c>TreatWarningsAsErrors</c> (this repo's own default,
/// and a very common one) it is a hard build break with no remedy. DwarfMapper leaked exactly such a CS8601 for
/// every nullable-reference member mapped to a non-nullable one, and nothing caught it.
/// </para>
/// <para>
/// So the bar is: whatever DwarfMapper emits is clean. If a mapping is genuinely risky, DwarfMapper says so in
/// its OWN diagnostic (DWARF070 et al.) — actionable, pointing at the user's DTO, suppressible per-rule — and
/// leaves the compiler with nothing to complain about. Diagnostics arising in the user's own source are
/// excluded; only <c>.g.cs</c> is held to this standard.
/// </para>
/// </summary>
public class GeneratedCodeIsWarningFreeTests
{
    public static IEnumerable<object[]> AllCells() =>
        CombinatorialSchema.DepthOneMatrix()
            .Concat(CombinatorialSchema.DepthTwoMatrix())
            .Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllCells))]
    public void Combinatorial_cell_emits_warning_free_code(MatrixCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        AssertClean(cell.Source, $"combinatorial cell [{cell.BasicType} / {cell.ShapeName} / {cell.Variant}]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void Fuzz_seed_emits_warning_free_code(int seed)
    {
        AssertClean(SyntheticSchema.Generate(seed), $"fuzz seed {seed} (compile schema)");
        AssertClean(SyntheticSchema.GenerateBehavioral(seed), $"fuzz seed {seed} (behavioural schema)");
    }

    /// <summary>
    ///     Feature-driven cases that neither schema generates. The two schemas above vary TYPES and SHAPES
    ///     (basic type × collection shape × nullability), so anything reached only by an ATTRIBUTE was outside
    ///     the warning-free bar entirely — each of these emits a distinct code path, and none of them was ever
    ///     checked for warnings. Kept as explicit sources rather than folded into the schemas so a failure names
    ///     the feature that broke.
    /// </summary>
    public static IEnumerable<object[]> FeatureCases()
    {
        // [MapValue] — constant and computed assignment into a source-less member.
        yield return
        [
            "MapValue", """
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public int Id { get; set; } }
                public sealed class Dst { public int Id { get; set; } public string Tag { get; set; } = ""; public int Stamp { get; set; } }
                [DwarfMapper]
                public partial class M
                {
                    [MapValue(nameof(Dst.Tag), "api-v2")]
                    [MapValue(nameof(Dst.Stamp), Use = nameof(Next))]
                    public partial Dst Map(Src s);
                    private static int Next() => 42;
                }
                """
        ];

        // [Reinterpret] — the forced blittable bulk-copy path (unsafe-adjacent emission).
        yield return
        [
            "Reinterpret", """
                using DwarfMapper;
                namespace Demo;
                public struct A { public int X; public int Y; }
                public struct B { public int X; public int Y; }
                public sealed class Src { public A[] V { get; set; } = System.Array.Empty<A>(); }
                public sealed class Dst { public B[] V { get; set; } = System.Array.Empty<B>(); }
                [DwarfMapper]
                public partial class M
                {
                    [Reinterpret(nameof(Src.V))]
                    public partial Dst Map(Src s);
                }
                """
        ];

        // Nullable reference member + NullSubstitute — the DWARF070 resolution path, on the hot assignment.
        yield return
        [
            "NullSubstitute", """
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public string? Name { get; set; } }
                public sealed class Dst { public string Name { get; set; } = ""; }
                [DwarfMapper]
                public partial class M
                {
                    [MapProperty(nameof(Src.Name), nameof(Dst.Name), NullSubstitute = "<none>")]
                    public partial Dst Map(Src s);
                }
                """
        ];

        // Queue/Stack targets — emitters added after this suite was written.
        yield return
        [
            "StackQueue", """
                using System.Collections.Generic;
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public List<int> A { get; set; } = new(); public List<int> B { get; set; } = new(); }
                public sealed class Dst { public Queue<int> A { get; set; } = new(); public Stack<int> B { get; set; } = new(); }
                [DwarfMapper]
                public partial class M { public partial Dst Map(Src s); }
                """
        ];

        // Value-type SOURCE collection — the nullable-struct parameter that emitted uncompilable code
        // (CS0411/CS1061). Compiling is now covered elsewhere; this holds it to the warning bar too.
        yield return
        [
            "ImmutableArraySource", """
                using System.Collections.Generic;
                using System.Collections.Immutable;
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public ImmutableArray<int> V { get; set; } }
                public sealed class Dst { public List<int> V { get; set; } = new(); }
                [DwarfMapper]
                public partial class M { public partial Dst Map(Src s); }
                """
        ];

        // Flexible naming — exercised on BOTH the runtime and projection emitters.
        yield return
        [
            "FlexibleProjection", """
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public int user_id { get; set; } public string? user_name { get; set; } }
                public sealed class Dst { public int UserId { get; set; } public string? UserName { get; set; } }
                [DwarfMapper(NameConvention = NameConvention.Flexible)]
                public partial class M
                {
                    public partial Dst Map(Src s);
                    public partial IQueryable<Dst> Project(IQueryable<Src> q);
                }
                """
        ];
    }

    [Theory]
    [MemberData(nameof(FeatureCases))]
    public void Feature_case_emits_warning_free_code(string feature, string source)
    {
        AssertClean(source, $"feature case [{feature}]");
    }

    private static void AssertClean(string source, string what)
    {
        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source, NullableContextOptions.Enable);

        Assert.True(
            warnings.Length == 0,
            $"The generator emitted code that WARNS in {what}. The consumer cannot edit generated code to "
            + "silence this, and under TreatWarningsAsErrors it is an unfixable build break. Either emit clean "
            + "code, or — if the mapping is genuinely risky — suppress the compiler diagnostic and report a "
            + "DWARF diagnostic against the user's own source instead (as DWARF070 does).\n  "
            + string.Join("\n  ", warnings
                .Select(d => $"{d.Id} at {d.Location.SourceTree?.FilePath}: "
                             + d.GetMessage(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)));
    }
}
