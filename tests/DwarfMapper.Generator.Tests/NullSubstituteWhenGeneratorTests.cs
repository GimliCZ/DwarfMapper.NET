// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     [MapProperty] NullSubstitute / When (Phase 8). NullSubstitute coalesces a null source to a constant
    ///     (`source ?? value`, direct members only; type-checked → DWARF049). When guards the assignment with a
    ///     bool predicate taking the source (`if (Pred(src)) target = …;`, member keeps its default otherwise;
    ///     invalid predicate → DWARF050).
    /// </summary>
    public class NullSubstituteWhenGeneratorTests
    {
        private static Diagnostic? Find(IEnumerable<Diagnostic> diags, string id)
        {
            return diags.FirstOrDefault(d => d.Id == id);
        }

        private static int Count(string h, string n)
        {
            int c = 0, i = 0;
            while ((i = h.IndexOf(n, i, StringComparison.Ordinal)) >= 0)
            {
                c++;
                i += n.Length;
            }

            return c;
        }

        [Fact]
        public void NullSubstitute_emits_coalesce_and_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class S { public string? Name { get; set; } }
                               public class D { public string Name { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty(nameof(S.Name), nameof(D.Name), NullSubstitute = "(none)")]
                                   public partial D Map(S s);
                               }
                               """;
            var gen = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("s.Name ?? \"(none)\"", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void When_emits_guarded_assignment_not_in_initializer()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class S { public int Tier { get; set; } public int Bonus { get; set; } }
                               public class D { public int Tier { get; set; } public int Bonus { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty(nameof(S.Bonus), nameof(D.Bonus), When = nameof(Eligible))]
                                   public partial D Map(S s);
                                   private static bool Eligible(S s) => s.Tier > 0;
                               }
                               """;
            var gen = GeneratorAssert.CompilesClean(src);
            Assert.Contains("if (Eligible(s)) __dwarf_target.Bonus = s.Bonus", gen, StringComparison.Ordinal);
            // The guarded member must NOT also be assigned unconditionally in the initializer.
            Assert.Equal(1, Count(gen, "Bonus = s.Bonus"));
        }

        [Fact]
        public void NullSubstitute_type_mismatch_reports_DWARF049()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class S { public string? Name { get; set; } }
                               public class D { public int Name { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty(nameof(S.Name), nameof(D.Name), NullSubstitute = "x")]
                                   public partial D Map(S s);
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            Assert.NotNull(Find(diags, "DWARF049"));
        }

        [Fact]
        public void NullSubstitute_with_converter_reports_DWARF049()
        {
            const string src = """
                               using DwarfMapper;
                               #nullable enable
                               namespace Demo;
                               public class S { public string? Code { get; set; } }
                               public class D { public int Code { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [MapProperty(nameof(S.Code), nameof(D.Code), Use = nameof(Conv), NullSubstitute = 0)]
                                   public partial D Map(S s);
                                   private static int Conv(string? v) => v is null ? 0 : v.Length;
                               }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            var d = Find(diags, "DWARF049");
            Assert.NotNull(d);
            // Two refusals share DWARF049 (this combination, and a substitute that does not convert); the
            // format is a bare "{0}", so the text is the only thing that says which one fired and for whom.
            Assert.Contains("for 'Code' is not supported together with a converter (Use=)",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("private static int Pred(S s) => 1;")] // non-bool return
        [InlineData("private static bool Pred(int x) => true;")] // param not source-assignable
        [InlineData("")] // missing method
        public void Invalid_when_predicate_reports_DWARF050(string member)
        {
            var src = $$"""
                        using DwarfMapper;
                        namespace Demo;
                        public class S { public int Bonus { get; set; } }
                        public class D { public int Bonus { get; set; } }
                        [DwarfMapper] public partial class M
                        {
                            [MapProperty(nameof(S.Bonus), nameof(D.Bonus), When = "Pred")]
                            public partial D Map(S s);
                            {{member}}
                        }
                        """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            var d = Find(diags, "DWARF050");
            Assert.NotNull(d);
            // Every row is the same refusal from the caller's side — the predicate they named is not usable —
            // so every row must say which predicate, which member, and what shape would have been accepted.
            Assert.Contains("[MapProperty(When = \"Pred\")] for 'Bonus' must name a bool-returning method that takes the source",
                d.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     DWARF049's arm for a substitute constant that does not CONVERT to the destination type — distinct
        ///     from the Use= combination arm, which the tests above already cover.
        /// </summary>
        /// <remarks>
        ///     Found uncovered by a round-27 measurement. The shape is the obvious authoring slip: a string
        ///     written where the destination is numeric. Without the check the constant would be emitted straight
        ///     into the coalesce and the GENERATED file would fail to compile, which is the failure this
        ///     repository refuses to hand a consumer.
        /// </remarks>
        [Fact]
        public void NullSubstitute_constant_that_does_not_convert_to_the_destination_reports_DWARF049()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int? A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty(nameof(Src.A), nameof(Dst.A), NullSubstitute = "not-an-int")]
                                   public partial Dst Map(Src s);
                               }
                               """;

            var (diags, _) = GeneratorTestHarness.Run(src);
            Assert.NotNull(Find(diags, "DWARF049"));
        }
    }
}
