// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapProperty(When = …, KeepDefault = true)]</c> - the per-member statement that a When guard leaving a
    ///     non-nullable member at its default is intended, so <c>DWARF066</c> is not reported for it. Owner ruling
    ///     2026-09-27: no DWARF diagnostic may need a project-wide <c>NoWarn</c>, and DWARF066's only answer for an
    ///     intended default was "confirm the unset default is intended", with nothing to confirm it with. The statement
    ///     changes nothing that is generated; one with nothing to accept is <c>DWARF116</c>.
    /// </summary>
    public class KeepDefaultIntentTests
    {
        private const string Types = """
                                     public class A { public string Note { get; set; } = ""; public bool Ready { get; set; } }
                                     public class B { public string Note { get; set; } = null!; }
                                     """;

        private static string Mapper(string attribute, string method = "public partial B Map(A a);", string types = Types)
        {
            return "using DwarfMapper;\nusing System.Linq;\nnamespace Demo;\n" + types + "\n" +
                   "[DwarfMapper] public partial class M\n{\n" +
                   "    private static bool IsReady(A a) => a.Ready;\n" +
                   "    " + attribute + " " + method + "\n}\n";
        }

        private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string source)
        {
            return GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
        }

        private static string Message(Diagnostic d)
        {
            return d.GetMessage(CultureInfo.InvariantCulture);
        }

        [Fact]
        public void Without_KeepDefault_the_guard_reports_DWARF066_naming_the_statement()
        {
            var d = Assert.Single(Run(Mapper("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady))]")).Diagnostics,
                x => x.Id == "DWARF066");

            Assert.Contains("KeepDefault = true", Message(d), StringComparison.Ordinal);
        }

        [Fact]
        public void KeepDefault_drops_DWARF066_and_changes_no_generated_byte()
        {
            var (withDiagnostics, withGenerated) = Run(Mapper("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady), KeepDefault = true)]"));
            var (_, withoutGenerated) = Run(Mapper("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady))]"));

            Assert.DoesNotContain(withDiagnostics, d => d.Id is "DWARF066" or "DWARF116");
            Assert.False(string.IsNullOrWhiteSpace(withGenerated));
            Assert.Equal(withoutGenerated, withGenerated);
        }

        [Fact]
        public void A_pair_scoped_KeepDefault_reaches_a_GenerateMap_pair()
        {
            const string template = """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class A { public string Note { get; set; } = ""; public bool Ready { get; set; } }
                                    public class B { public string Note { get; set; } = null!; }
                                    [DwarfMapper]
                                    [GenerateMap<A, B>]
                                    ATTR
                                    public partial class M { private static bool IsReady(A a) => a.Ready; }
                                    """;

            var without = Run(template.Replace("ATTR", "[MapProperty<A, B>(\"Note\", \"Note\", When = nameof(M.IsReady))]", StringComparison.Ordinal));
            var with = Run(template.Replace("ATTR", "[MapProperty<A, B>(\"Note\", \"Note\", When = nameof(M.IsReady), KeepDefault = true)]", StringComparison.Ordinal));

            Assert.Single(without.Diagnostics, d => d.Id == "DWARF066");
            Assert.DoesNotContain(with.Diagnostics, d => d.Id is "DWARF066" or "DWARF116");
            Assert.Equal(without.Generated, with.Generated);
        }

        [Fact]
        public void A_co_located_host_member_KeepDefault_is_read_off_the_destination_member()
        {
            // The member-placement form on a [GenerateMap<S,T>] host reaches the extras through MemberDirective - the
            // third reader, and the one a pair-scoped test does not exercise.
            const string template = """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class A { public string Note { get; set; } = ""; public bool Ready { get; set; } }
                                    [GenerateMap<A, B>]
                                    public sealed partial class B
                                    {
                                        ATTR public string Note { get; set; } = null!;
                                        private static bool IsReady(A a) => a.Ready;
                                    }
                                    """;

            var without = Run(template.Replace("ATTR", "[MapProperty(\"Note\", When = nameof(IsReady))]", StringComparison.Ordinal));
            var with = Run(template.Replace("ATTR", "[MapProperty(\"Note\", When = nameof(IsReady), KeepDefault = true)]", StringComparison.Ordinal));

            Assert.Single(without.Diagnostics, d => d.Id == "DWARF066");
            Assert.DoesNotContain(with.Diagnostics, d => d.Id is "DWARF066" or "DWARF116");
            Assert.Equal(without.Generated, with.Generated);
        }

        [Theory]
        [InlineData("[MapProperty(\"Note\", \"Note\", KeepDefault = true)]", Types, "there is no When guard on it, so nothing leaves it unassigned")]
        [InlineData("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady), KeepDefault = true)]",
            "public class A { public string Note { get; set; } = \"\"; public bool Ready { get; set; } }\npublic class B { public string? Note { get; set; } }",
            "its destination accepts null")]
        [InlineData("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady), KeepDefault = true)]",
            "public class A { public string Note { get; set; } = \"\"; public bool Ready { get; set; } }\npublic class B { public string Note { get; set; } = \"(none)\"; }",
            "it has its own non-null default")]
        [InlineData("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady), KeepDefault = true)]",
            "public class A { public int Note { get; set; } public bool Ready { get; set; } }\npublic class B { public int Note { get; set; } }",
            "its destination is a value type, whose default is not a null")]
        public void A_stale_KeepDefault_is_reported_as_DWARF116_with_its_reason(string attribute, string types, string reason)
        {
            var d = Assert.Single(Run(Mapper(attribute, types: types)).Diagnostics, x => x.Id == "DWARF116");

            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal(
                "[MapProperty(KeepDefault = true)] on 'Note' accepts an unset default that is not there: " + reason + ". " +
                "Remove KeepDefault - left in place, it would silently accept a null default introduced here later, one " +
                "nobody has reviewed.",
                Message(d));
        }

        [Fact]
        public void KeepDefault_on_an_update_into_is_DWARF116_because_it_keeps_the_current_value()
        {
            var diagnostics = Run(Mapper("[MapProperty(\"Note\", \"Note\", When = nameof(IsReady), KeepDefault = true)]",
                "public partial void Patch(A a, B b);")).Diagnostics;

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF116");
            Assert.Contains("this is an update-into", Message(d), StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARF066");
        }

        [Fact]
        public void A_lone_KeepDefault_on_a_projected_member_is_DWARF116()
        {
            var d = Assert.Single(
                Run(Mapper("[MapProperty(\"Note\", \"Note\", KeepDefault = true)]",
                    "public partial IQueryable<B> Project(IQueryable<A> q);")).Diagnostics,
                x => x.Id == "DWARF116");

            Assert.Contains("there is no When guard on it", Message(d), StringComparison.Ordinal);
        }
    }
}
