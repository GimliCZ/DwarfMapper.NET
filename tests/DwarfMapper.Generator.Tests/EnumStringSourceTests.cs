// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>EnumStringSource</c> — the one-line answer to <c>DWARF083</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For enum↔string a member's <c>[EnumMember(Value=…)]</c> wins, then <c>[Description(…)]</c>, then the
    ///         identifier. That precedence is a feature: <c>InProgress</c> serializes as <c>"in_progress"</c> with
    ///         no converter. It is also a hazard when migrating, because <c>[Description]</c> is overwhelmingly a
    ///         <b>display</b> annotation — Round 18 came within one code review of writing <c>"Next-Day"</c> into a
    ///         store full of <c>"NextDay"</c>.
    ///     </para>
    ///     <para>
    ///         <c>EnumStringSource = Identifier</c> says: the annotations on this enum are for display, the
    ///         persisted form is the member name. One line on the mapper (or on
    ///         <c>[assembly: DwarfMapperDefaults]</c>) instead of a hand-written converter per enum.
    ///     </para>
    /// </remarks>
    public class EnumStringSourceTests
    {
        private const string Enum = """
                                    using System.ComponentModel;
                                    using DwarfMapper;
                                    namespace Demo;

                                    public enum DispatchChannel
                                    {
                                        [Description("Next-Day")] NextDay,
                                        Standard
                                    }

                                    public class Dispatch { public DispatchChannel Source { get; set; } }
                                    public class DispatchDoc { public string Source { get; set; } = ""; }
                                    public class DispatchRead { public string Source { get; set; } = ""; }
                                    public class DispatchBack { public DispatchChannel Source { get; set; } }
                                    """;

        [Fact]
        public void Attribute_is_the_default_and_writes_the_annotated_text()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Enum +
                                                                """

                                                                [DwarfMapper]
                                                                [GenerateMap<Dispatch, DispatchDoc>]
                                                                public partial class M;
                                                                """);

            Assert.Contains("=> \"Next-Day\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Identifier_writes_the_member_name_instead()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Enum +
                                                                """

                                                                [DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
                                                                [GenerateMap<Dispatch, DispatchDoc>]
                                                                public partial class M;
                                                                """);

            Assert.Contains("=> \"NextDay\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Next-Day", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Identifier_reads_the_member_name_too()
        {
            // Both directions or neither: a store written with identifiers has to be readable, and a parse switch
            // built from the annotations would throw on every row.
            var generated = GeneratorAssert.EmitsCompilableCode(Enum +
                                                                """

                                                                [DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
                                                                [GenerateMap<DispatchRead, DispatchBack>]
                                                                public partial class M;
                                                                """);

            Assert.Contains("\"NextDay\" =>", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Next-Day", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Identifier_silences_DWARF083_because_it_IS_the_remedy()
        {
            GeneratorAssert.DoesNotReport(Enum +
                                          """

                                          [DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
                                          [GenerateMap<Dispatch, DispatchDoc>]
                                          public partial class M;
                                          """,
                "DWARF083");
        }

        [Fact]
        public void The_assembly_default_applies_when_the_mapper_says_nothing()
        {
            var generated = GeneratorAssert.EmitsCompilableCode("""
                                                                using System.ComponentModel;
                                                                using DwarfMapper;

                                                                [assembly: DwarfMapperDefaults(EnumStringSource = EnumStringSource.Identifier)]

                                                                namespace Demo;

                                                                public enum DispatchChannel
                                                                {
                                                                    [Description("Next-Day")] NextDay,
                                                                    Standard
                                                                }

                                                                public class Dispatch { public DispatchChannel Source { get; set; } }
                                                                public class DispatchDoc { public string Source { get; set; } = ""; }

                                                                [DwarfMapper]
                                                                [GenerateMap<Dispatch, DispatchDoc>]
                                                                public partial class M;
                                                                """);

            Assert.Contains("=> \"NextDay\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_mapper_overrides_the_assembly_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode("""
                                                                using System.ComponentModel;
                                                                using DwarfMapper;

                                                                [assembly: DwarfMapperDefaults(EnumStringSource = EnumStringSource.Identifier)]

                                                                namespace Demo;

                                                                public enum DispatchChannel
                                                                {
                                                                    [Description("Next-Day")] NextDay,
                                                                    Standard
                                                                }

                                                                public class Dispatch { public DispatchChannel Source { get; set; } }
                                                                public class DispatchDoc { public string Source { get; set; } = ""; }

                                                                [DwarfMapper(EnumStringSource = EnumStringSource.Attribute)]
                                                                [GenerateMap<Dispatch, DispatchDoc>]
                                                                public partial class M;
                                                                """);

            Assert.Contains("=> \"Next-Day\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_mappers_choosing_differently_get_two_helpers()
        {
            // The design hazard, and the reason the strategy is folded into the helper's name hash. The helper was
            // keyed by TYPE alone, so both mappers would have shared one — and whichever was synthesized first
            // would silently have decided the persisted format for the other. That is the same bug class this
            // round already fixed twice (Use= auto-adoption, [MapConstructor] as an element converter), and here
            // it would have been introduced BY the fix for it.
            const string src = """
                               using System.ComponentModel;
                               using DwarfMapper;
                               namespace Demo;

                               public enum DispatchChannel
                               {
                                   [Description("Next-Day")] NextDay,
                                   Standard
                               }

                               public class Dispatch { public DispatchChannel Source { get; set; } }
                               public class DispatchDoc { public string Source { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Dispatch, DispatchDoc>]
                               public partial class Annotated;

                               [DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
                               [GenerateMap<Dispatch, DispatchDoc>]
                               public partial class Plain;
                               """;

            var (_, generated) = GeneratorTestHarness.RunAll(src);

            Assert.Contains("=> \"Next-Day\"", generated, StringComparison.Ordinal);
            Assert.Contains("=> \"NextDay\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_Flags_enum_is_unaffected_by_either_setting()
        {
            // A [Flags] enum's string form is the comma-joined list Enum.ToString builds from identifiers, so the
            // two settings describe the same mapping and the annotations never applied in the first place.
            const string flags = """
                                 using System;
                                 using System.ComponentModel;
                                 using DwarfMapper;
                                 namespace Demo;

                                 [Flags]
                                 public enum Perm { [Description("R")] Read = 1, [Description("W")] Write = 2 }

                                 public class Src { public Perm Perm { get; set; } }
                                 public class Dst { public string Perm { get; set; } = ""; }
                                 """;

            foreach (var setting in new[]
                     {
                         "Attribute", "Identifier"
                     })
            {
                var generated = GeneratorAssert.EmitsCompilableCode(flags +
                                                                    $$"""

                                                                      [DwarfMapper(EnumStringSource = EnumStringSource.{{setting}})]
                                                                      [GenerateMap<Src, Dst>]
                                                                      public partial class M;
                                                                      """);

                Assert.Contains("=> \"Read\"", generated, StringComparison.Ordinal);
            }
        }
    }
}
