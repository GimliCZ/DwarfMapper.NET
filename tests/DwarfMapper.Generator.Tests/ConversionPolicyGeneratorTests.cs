// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     ImplicitConversions policy (DWARF038): lossy basic-type conversions (narrowing, parse/format,
    ///     cross-category numeric) surface as a Warning by default (item 8), or a build ERROR when
    ///     [DwarfMapper(ImplicitConversions = false)]. Lossless same-category widening and identity are silent.
    /// </summary>
    public class ConversionPolicyGeneratorTests
    {
        private static Diagnostic? D038(IEnumerable<Diagnostic> diags)
        {
            return diags.FirstOrDefault(d => d.Id == "DWARF038");
        }

        [Fact]
        public void Parse_format_conversion_warns_and_names_the_runtime_exceptions()
        {
            // Item 15: a string -> int parse conversion can throw at runtime; the message must say so.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public string Score { get; set; } = ""; }
                               public class D { public int Score { get; set; } }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = D038(diags);
            Assert.NotNull(d);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity); // lossy → Warning (item 8)
            var msg = d.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("FormatException", msg, StringComparison.Ordinal);
            Assert.Contains("OverflowException", msg, StringComparison.Ordinal);
        }

        [Fact]
        public void Narrowing_in_permissive_mode_is_a_Warning_and_still_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public long Score { get; set; } }
                               public class D { public int Score { get; set; } }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = D038(diags);
            Assert.NotNull(d);
            // Item 8: a lossy (narrowing) implicit conversion is a Warning (data-losing behaviour), not a mere Info.
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(src); // still maps (CreateChecked)
        }

        [Fact]
        public void Narrowing_in_strict_mode_is_a_build_error()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public long Score { get; set; } }
                               public class D { public int Score { get; set; } }
                               [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = D038(diags);
            Assert.NotNull(d);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        }

        [Fact]
        public void Lossless_widening_is_silent()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int A { get; set; } public float B { get; set; } }
                               public class D { public long A { get; set; } public double B { get; set; } } // int→long, float→double: same-category widening
                               [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.Null(D038(diags)); // no suggestion, no error — even in strict mode
            Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void Cross_category_numeric_is_flagged()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int N { get; set; } }
                               public class D { public double N { get; set; } } // int→double: cross-category
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.NotNull(D038(diags));
        }

        /// <summary>
        ///     The option, asked once per <c>Nullable&lt;&gt;</c> permutation of the SAME pair. Three of the four
        ///     answered differently from the unwrapped pair, and all three were silences the option exists to
        ///     prevent (TASKS.md <c>I20</c>): <c>long? → double?</c> and <c>long → double?</c> reported nothing at
        ///     any severity, because the classifier read <c>SpecialType.System_Nullable_T</c> and said "not a
        ///     numeric type" while C# happily LIFTED the lossy conversion; <c>long? → double</c> took an
        ///     unwrap-then-assign arm that asked no lossiness question at all. And every recursion crossing a
        ///     <c>Nullable&lt;&gt;</c> wrapper dropped <c>implicitConversions</c> back to its permissive default,
        ///     so <c>long? → int?</c> and <c>string → int?</c> — which DID report — reported a Warning under
        ///     <c>ImplicitConversions = false</c> and the mapper was generated anyway. The strict setting was
        ///     silently off for the whole nullable half of the type space.
        ///     <para>
        ///         The widening rows are the DIAGONAL, not padding: the remedy widens a classifier, and the way to
        ///         get that wrong is to start flagging conversions that lose nothing. <c>int? → long?</c> must stay
        ///         as silent as <c>int → long</c>, at both severities.
        ///     </para>
        /// </summary>
        [Theory]
        // ── lossy: cross-category numeric, every Nullable<> permutation of one pair ──
        [InlineData("long", "double", true)] // the unwrapped control — this one always reported
        [InlineData("long?", "double?", true)] // I20: both wrapped
        [InlineData("long", "double?", true)] // I20: target wrapped
        [InlineData("long?", "double", true)] // I20: source wrapped
        // ── lossy: the two kinds that reported but lost their escalation under a wrapper ──
        [InlineData("long?", "int?", true)] // narrowing (CreateChecked)
        [InlineData("string", "int?", true)] // parse (IParsable)
        // ── the diagonal: same-category widening loses nothing and must stay silent, wrapped or not ──
        [InlineData("int", "long", false)]
        [InlineData("int?", "long?", false)]
        [InlineData("int?", "long", false)]
        [InlineData("int", "long?", false)]
        public void The_option_answers_the_same_across_every_Nullable_permutation_of_a_pair(
            string srcType,
            string tgtType,
            bool lossy)
        {
            foreach (var strict in new[]
                     {
                         false, true
                     })
            {
                var attribute = strict ? "[DwarfMapper(ImplicitConversions = false)]" : "[DwarfMapper]";
                var source = $$"""
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public {{srcType}} N { get; set; } }
                               public class D { public {{tgtType}} N { get; set; } }
                               {{attribute}} public partial class M { public partial D Map(S s); }
                               """;
                var (diags, _) = GeneratorTestHarness.Run(source);
                var d = D038(diags);
                var where = $"{srcType} -> {tgtType}, strict={strict}";

                if (!lossy)
                {
                    Assert.True(d is null,
                        $"{where}: the conversion loses nothing and must not be flagged, but DWARF038 fired " + $"as {d?.Severity}. A classifier widened too far is the failure mode this row guards.");
                    Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
                    continue;
                }

                Assert.True(d is not null,
                    $"{where}: no DWARF038 at all. The option promises to report this conversion, and a " + "Nullable<> wrapper is not a reason for it to go quiet (I20).");
                var expected = strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                Assert.True(d.Severity == expected,
                    $"{where}: DWARF038 is {d.Severity}, expected {expected}. Under " + "ImplicitConversions = false the build must BREAK — a Warning means the strict setting " + "was read as permissive somewhere on the way down (I20).");
            }
        }

        [Fact]
        public void Explicit_MapProperty_Use_silences_the_suggestion()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public long Score { get; set; } }
                               public class D { public int Score { get; set; } }
                               [DwarfMapper(ImplicitConversions = false)]
                               public partial class M
                               {
                                   [MapProperty(nameof(S.Score), nameof(D.Score), Use = nameof(Shrink))]
                                   public partial D Map(S s);
                                   private static int Shrink(long v) => (int)v;
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.Null(D038(diags)); // explicit conversion → no DWARF038, even in strict mode
            GeneratorAssert.EmitsCompilableCode(src);
        }
    }
}
