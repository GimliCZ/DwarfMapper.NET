// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[RestatesBase]</c>, <c>DWARF084</c> and <c>DWARF085</c> — the answer to <c>IncludeBase</c> that is not
    ///     an inheritance primitive.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Round 18 restated base configuration by hand at 15 sites in one project, 5 in another and 6 in a
    ///         third, and the agents doing it independently invented a <c>MIRRORS BASE</c> / <c>END MIRRORS BASE</c>
    ///         comment convention to keep the restatements traceable. That convention is the tell: the problem was
    ///         never the typing, it was that nothing could check the result.
    ///     </para>
    ///     <para>
    ///         Decision R18-D4 kept restatement — every pair's configuration stays literally visible at its own
    ///         declaration — and closed the half that hurts. The cost splits into typing it, which is mechanical
    ///         and over once, and drifting from the base later, which is silent and only ever drifts toward wrong
    ///         data.
    ///     </para>
    ///     <para>
    ///         The check compares the RESOLVED mappings, which is strictly stronger than the marker comments it
    ///         replaces: a restatement that is present but no longer does the same thing — the base gained a
    ///         <c>Use=</c> converter, this pair kept mapping the raw value — is invisible to any convention based
    ///         on comments, and is exactly the failure worth catching.
    ///     </para>
    /// </remarks>
    public class RestatesBaseTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Command { public string Raw { get; set; } = ""; }
                                     public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                     public class CommandDto { public string Text { get; set; } = ""; }
                                     public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }
                                     """;

        private const string Converter = """
                                             private static string Clean(string raw) => raw.Trim();
                                         """;

        /// <summary>
        ///     The attribute this file is about, referenced by type so a rename cannot leave the suite silently
        ///     testing nothing. (The generator matches it by string — it is a netstandard2.0 analyzer and cannot
        ///     reference the runtime assembly — so the name is a literal on that side and unchecked by the
        ///     compiler. This is the check.)
        /// </summary>
        private static readonly Type Attribute = typeof(RestatesBaseAttribute<object, object>);

        [Fact]
        public void The_attribute_is_named_RestatesBaseAttribute()
        {
            Assert.Equal("RestatesBaseAttribute`2", Attribute.Name);
        }

        [Fact]
        public void A_faithful_restatement_is_silent_and_changes_nothing()
        {
            // The attribute must not alter emission at all — it exists so there is something to check.
            var generated = GeneratorAssert.CompilesClean(Types +
                                                          """

                                                          [DwarfMapper]
                                                          [GenerateMap<Command, CommandDto>]
                                                          [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                                                          [GenerateMap<AliasCommand, AliasCommandDto>]
                                                          [RestatesBase<AliasCommand, AliasCommandDto>]
                                                          [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                                                          public partial class M
                                                          {
                                                          """ +
                                                          Converter +
                                                          """
                                                          }
                                                          """);

            // Both pairs route through the converter — which is what "faithful" means here.
            Assert.Equal(2, generated.Split("Clean(").Length - 1);
        }

        [Fact]
        public void Reports_DWARF085_when_the_restatement_is_missing()
        {
            // The base converts Raw; the derived pair does not restate it and silently maps the raw value.
            var reported = GeneratorAssert.Reports(Types +
                                                   """

                                                   [DwarfMapper]
                                                   [GenerateMap<Command, CommandDto>]
                                                   [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                                                   [GenerateMap<AliasCommand, AliasCommandDto>]
                                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                                   [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                                   public partial class M
                                                   {
                                                   """ +
                                                   Converter +
                                                   """
                                                   }
                                                   """,
                "DWARF085");

            Assert.NotEmpty(reported);

            var message = reported[0].GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("Text", message, StringComparison.Ordinal);
            Assert.Contains("AliasCommandDto", message, StringComparison.Ordinal);

            // The base pair, so the reader knows what to compare against.
            Assert.Contains("Demo.CommandDto", message, StringComparison.Ordinal);

            // And both ways out.
            Assert.Contains("Restate", message, StringComparison.Ordinal);
            Assert.Contains("Overrides", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Reports_when_the_derived_pair_stops_mapping_a_member_the_base_maps()
        {
            // The other drift direction, and the quieter one: an [MapIgnore] the base does not have.
            var reported = GeneratorAssert.Reports(Types +
                                                   """

                                                   [DwarfMapper]
                                                   [GenerateMap<Command, CommandDto>]
                                                   [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                                   [GenerateMap<AliasCommand, AliasCommandDto>]
                                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                                   [MapIgnore<AliasCommandDto>(nameof(CommandDto.Text))]
                                                   public partial class M { }
                                                   """,
                "DWARF085");

            Assert.NotEmpty(reported);
            Assert.Contains("does not map",
                reported[0].GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void An_override_is_a_decision_and_silences_exactly_that_member()
        {
            // Naming the member is the statement "we meant this". Every OTHER member stays guarded, which is what
            // separates a deliberate override from a forgotten restatement.
            GeneratorAssert.DoesNotReport(Types +
                                          """

                                          [DwarfMapper]
                                          [GenerateMap<Command, CommandDto>]
                                          [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                                          [GenerateMap<AliasCommand, AliasCommandDto>]
                                          [RestatesBase<AliasCommand, AliasCommandDto>(Overrides = new[] { nameof(CommandDto.Text) })]
                                          [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                          public partial class M
                                          {
                                          """ +
                                          Converter +
                                          """
                                          }
                                          """,
                "DWARF085");
        }

        [Fact]
        public void An_override_does_not_silence_a_different_member()
        {
            // The guard the exemption must not become a blanket. Overriding Text leaves everything else checked.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Raw { get; set; } = ""; public string Note { get; set; } = ""; }
                               public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                               public class CommandDto { public string Text { get; set; } = ""; public string Note { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                               [MapProperty<Command, CommandDto>(nameof(Command.Note), nameof(CommandDto.Note), Use = nameof(Clean))]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>(Overrides = new[] { nameof(CommandDto.Text) })]
                               [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                               [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Note), nameof(CommandDto.Note))]
                               public partial class M
                               {
                                   private static string Clean(string raw) => raw.Trim();
                               }
                               """;

            var message = GeneratorAssert.Reports(src, "DWARF085")[0].GetMessage(CultureInfo.InvariantCulture);

            Assert.Contains("Note", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Reports_DWARF084_when_no_base_pair_is_declared()
        {
            // Refused rather than skipped: a check the author asked for and did not get is the drift risk itself.
            var reported = GeneratorAssert.Reports(Types +
                                                   """

                                                   [DwarfMapper]
                                                   [GenerateMap<AliasCommand, AliasCommandDto>]
                                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                                   public partial class M { }
                                                   """,
                "DWARF084");

            Assert.NotEmpty(reported);

            var message = reported[0].GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("no base pair", message, StringComparison.Ordinal);
            Assert.Contains("base class", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Reports_DWARF084_when_the_named_pair_is_not_declared_at_all()
        {
            var reported = GeneratorAssert.Reports(Types +
                                                   """

                                                   [DwarfMapper]
                                                   [GenerateMap<Command, CommandDto>]
                                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                                   public partial class M { }
                                                   """,
                "DWARF084");

            Assert.NotEmpty(reported);
            Assert.Contains("does not declare",
                reported[0].GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_redeclared_member_of_a_different_type_is_not_drift()
        {
            // `new` hides the base member: two different members wearing one name. Their mappings are SUPPOSED to
            // differ, and flagging that would be the check crying wolf on the one shape where divergence is the
            // point.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Command { public string Code { get; set; } = ""; }
                               public class AliasCommand : Command { public int Serial { get; set; } }
                               public class CommandDto { public string Code { get; set; } = ""; }
                               public class AliasCommandDto : CommandDto { public new int Code { get; set; } public int Serial { get; set; } }

                               [DwarfMapper]
                               [GenerateMap<Command, CommandDto>]
                               [GenerateMap<AliasCommand, AliasCommandDto>]
                               [RestatesBase<AliasCommand, AliasCommandDto>]
                               [MapProperty<AliasCommand, AliasCommandDto>(nameof(AliasCommand.Serial), "Code")]
                               public partial class M { }
                               """;

            GeneratorAssert.DoesNotReport(src, "DWARF085");
        }

        [Fact]
        public void The_attribute_is_inert_without_it_nothing_changes()
        {
            // Stated as a test because "it changes no emitted code whatsoever" is a promise, and a promise about
            // emission is checkable.
            const string withoutAttribute = """
                                            using DwarfMapper;
                                            namespace Demo;
                                            public class Command { public string Raw { get; set; } = ""; }
                                            public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                            public class CommandDto { public string Text { get; set; } = ""; }
                                            public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                                            [DwarfMapper]
                                            [GenerateMap<Command, CommandDto>]
                                            [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                            [GenerateMap<AliasCommand, AliasCommandDto>]
                                            [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                            public partial class M { }
                                            """;

            var withAttribute = withoutAttribute.Replace(
                "[GenerateMap<AliasCommand, AliasCommandDto>]",
                "[GenerateMap<AliasCommand, AliasCommandDto>]\n[RestatesBase<AliasCommand, AliasCommandDto>]",
                StringComparison.Ordinal);

            var (_, before) = GeneratorTestHarness.RunAll(withoutAttribute);
            var (_, after) = GeneratorTestHarness.RunAll(withAttribute);

            Assert.Equal(before, after);
        }

        [Fact]
        public void DWARF085_is_a_Warning_because_the_author_opted_into_the_check()
        {
            // Unlike DWARF081 (two mappers legitimately configured differently), declaring [RestatesBase] IS the
            // statement that these two are meant to agree. Breaking a warnings-as-errors build is then correct.
            var reported = GeneratorAssert.Reports(Types +
                                                   """

                                                   [DwarfMapper]
                                                   [GenerateMap<Command, CommandDto>]
                                                   [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                                                   [GenerateMap<AliasCommand, AliasCommandDto>]
                                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                                   [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                                   public partial class M
                                                   {
                                                   """ +
                                                   Converter +
                                                   """
                                                   }
                                                   """,
                "DWARF085");

            Assert.Equal(DiagnosticSeverity.Warning, reported[0].Severity);
        }
    }
}
