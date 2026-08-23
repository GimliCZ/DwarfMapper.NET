// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>DWARF079</c> — <c>[MapIgnore]</c> on a <c>required</c> destination member.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ignoring the member omits it from the generated object initializer, which C# refuses with
    ///         <c>CS9035</c> — an error reported against generated code, with nothing tying it back to the
    ///         attribute that caused it.
    ///     </para>
    ///     <para>
    ///         The single most-repeated friction point of the Round-18 migration: three separate conversions hit
    ///         it independently and each reinvented the same workaround. It is common specifically because
    ///         AutoMapper built destinations reflectively and so bypassed the rule — <c>.Ignore()</c> on a
    ///         <c>required</c> member simply left it null there. See <c>Issues/Rount18/</c>.
    ///     </para>
    /// </remarks>
    public class IgnoredRequiredMemberTests
    {
        private const string Id = "DWARF079";

        [Fact]
        public void Reports_when_a_required_member_is_ignored()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Name { get; set; } = ""; }
                               public class Doc { public required string Id { get; set; } public string Name { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore(nameof(Doc.Id))]
                                   public partial Doc Map(Src s);
                               }
                               """;

            Assert.NotEmpty(GeneratorAssert.Reports(src, Id));
        }

        [Fact]
        public void The_message_names_the_member_and_offers_MapValue()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Name { get; set; } = ""; }
                               public class Doc { public required string Id { get; set; } public string Name { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore(nameof(Doc.Id))]
                                   public partial Doc Map(Src s);
                               }
                               """;

            var message = GeneratorAssert.Reports(src, Id)[0].GetMessage(CultureInfo.InvariantCulture);

            Assert.Contains("'Id'", message, StringComparison.Ordinal);

            // Naming CS9035 is what connects this diagnostic to the compiler error the consumer would otherwise
            // see alone, and [MapValue] is the remedy three separate migrations each had to discover unaided.
            Assert.Contains("CS9035", message, StringComparison.Ordinal);
            Assert.Contains("[MapValue", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Carries_the_member_name_for_a_code_fix()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Name { get; set; } = ""; }
                               public class Doc { public required string Id { get; set; } public string Name { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore(nameof(Doc.Id))]
                                   public partial Doc Map(Src s);
                               }
                               """;

            var reported = GeneratorAssert.Reports(src, Id)[0];

            Assert.True(reported.Properties.TryGetValue("Member", out var member));
            Assert.Equal("Id", member);
        }

        [Fact]
        public void Is_silent_when_the_ignored_member_is_not_required()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Name { get; set; } = ""; }
                               public class Doc { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore(nameof(Doc.Id))]
                                   public partial Doc Map(Src s);
                               }
                               """;

            GeneratorAssert.CompilesClean(src);
            GeneratorAssert.DoesNotReport(src, Id);
        }

        [Fact]
        public void Is_silent_when_the_constructor_carries_SetsRequiredMembers()
        {
            // [SetsRequiredMembers] tells C# every required member is already satisfied, so omitting it from the
            // initializer is legal and ignoring it is a legitimate choice. Reporting here would be a false
            // positive on the one shape that makes the pattern safe.
            const string src = """
                               using System.Diagnostics.CodeAnalysis;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Name { get; set; } = ""; }
                               public class Doc
                               {
                                   [SetsRequiredMembers]
                                   public Doc() { Id = "generated"; }
                                   public required string Id { get; set; }
                                   public string Name { get; set; } = "";
                               }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore(nameof(Doc.Id))]
                                   public partial Doc Map(Src s);
                               }
                               """;

            GeneratorAssert.DoesNotReport(src, Id);
        }

        [Fact]
        public void Is_silent_when_the_required_member_is_supplied_as_a_constructor_argument()
        {
            // A positional record member is both a ctor parameter and a required-ish init property. The ctor
            // supplies it, so there is nothing for the initializer to omit.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
                               public record Doc(string Id) { public string Name { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial Doc Map(Src s);
                               }
                               """;

            GeneratorAssert.CompilesClean(src);
            GeneratorAssert.DoesNotReport(src, Id);
        }

        [Fact]
        public void Is_silent_for_update_into_which_has_no_object_initializer()
        {
            // Update-into writes into an instance the CALLER already constructed. There is no object initializer
            // to omit a member from, and `required` only ever constrains construction — so ignoring a required
            // member here is legitimate, not an error.
            //
            // This started as a false positive: the first cut of DWARF079 broke NonTrivialShapeRuntimeTests,
            // which does exactly this on purpose. Pinned so the exemption cannot be lost again.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string A { get; set; } = ""; public string B { get; set; } = ""; }
                               public class Dst { public required string A { get; set; } public required string B { get; set; } }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore(nameof(Dst.B))]
                                   public partial void Update(Src s, Dst d);
                               }
                               """;

            GeneratorAssert.CompilesClean(src);
            GeneratorAssert.DoesNotReport(src, Id);
        }

        [Fact]
        public void The_MapValue_remedy_it_suggests_actually_compiles()
        {
            // A diagnostic that recommends a fix which does not work is worse than no diagnostic. This pins the
            // suggested remedy end-to-end rather than trusting the message text.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public string Name { get; set; } = ""; }
                               public class Doc { public required string Id { get; set; } public string Name { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapValue(nameof(Doc.Id), "")]
                                   public partial Doc Map(Src s);
                               }
                               """;

            var generated = GeneratorAssert.CompilesClean(src);

            GeneratorAssert.DoesNotReport(src, Id);
            Assert.Contains("Id = ", generated, StringComparison.Ordinal);
        }
    }
}
