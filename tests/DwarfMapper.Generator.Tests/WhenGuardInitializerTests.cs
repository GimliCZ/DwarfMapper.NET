// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     DWARF066 - a <c>[MapProperty(When = …)]</c> guard on a non-nullable reference member, which keeps its default
    ///     when the predicate is false - fired whether or not the member HAD a non-null default. Its message offers "give
    ///     the member a default initializer" as the first remedy, and the code comment beside the report says "a member
    ///     with its own default initializer is fine" - but nothing ever looked for one, so the remedy changed nothing and
    ///     the reader concluded the advice was wrong.
    /// </summary>
    /// <remarks>
    ///     Found 2026-10-11 while designing <c>KeepDefault</c>, the per-member statement for DWARF066: its stale rule is
    ///     the complement of DWARF066's own condition, so the condition had to be read - and it was not the one its
    ///     message and comment describe. An initializer that is itself a null (<c>= null!</c>, <c>= default!</c>) is not
    ///     a non-null default and keeps the report.
    /// </remarks>
    public class WhenGuardInitializerTests
    {
        private static string Source(string targetMember)
        {
            return "using DwarfMapper;\nnamespace Demo;\n" +
                   "public class A { public string Name { get; set; } = \"\"; public bool Ready { get; set; } }\n" +
                   "public class B { " + targetMember + " }\n" +
                   "[DwarfMapper] public partial class M\n{\n" +
                   "    private static bool IsReady(A a) => a.Ready;\n" +
                   "    [MapProperty(\"Name\", \"Name\", When = nameof(IsReady))] public partial B Map(A a);\n}\n";
        }

        private static ImmutableArray<Diagnostic> Run(string source)
        {
            return GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics;
        }

        [Theory]
        [InlineData("public string Name { get; set; } = \"(none)\";")]
        [InlineData("public string Name { get; set; } = string.Empty;")]
        [InlineData("public string Name = \"(none)\";")]
        public void A_member_with_its_own_non_null_initializer_is_not_reported(string member)
        {
            Assert.DoesNotContain(Run(Source(member)), d => d.Id == "DWARF066");
        }

        [Theory]
        [InlineData("public string Name { get; set; } = null!;")]
        [InlineData("public string Name { get; set; } = default!;")]
        [InlineData("public required string Name { get; set; }")]
        public void A_member_whose_default_is_still_null_is_reported(string member)
        {
            var d = Assert.Single(Run(Source(member)), x => x.Id == "DWARF066");
            Assert.Contains("'Name'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        private static string Positional(string parameter)
        {
            var types = "public record BaseB(" + parameter + ");\npublic record B : BaseB { public B() : base(\"\") { } }\n";
            return Source("").Replace("public class B {  }\n", types, StringComparison.Ordinal);
        }

        // Round 35 T4. Declared by a record's parameter list, the member holds whatever the constructor was given,
        // and every constructor has to give it one: a false guard leaves that value, not a null. This was reported
        // from round 32 to round 34 as a known false positive ("no initializer to read") with KeepDefault as the way
        // round it - which is now the stale statement, since there is no report left for it to accept.
        [Fact]
        public void A_positional_record_member_is_not_reported_and_KeepDefault_on_it_accepts_nothing()
        {
            var source = Positional("string Name");

            Assert.DoesNotContain(Run(source), x => x.Id == "DWARF066");
            var kept = Run(source.Replace("When = nameof(IsReady))", "When = nameof(IsReady), KeepDefault = true)", StringComparison.Ordinal));
            Assert.DoesNotContain(kept, x => x.Id == "DWARF066");
            var stale = Assert.Single(kept, x => x.Id == "DWARF116");
            Assert.Contains("its own non-null default", stale.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        // The parameter's own default is read as an initializer is: a null in disguise is still a null, and a real
        // default is a default.
        [Theory]
        [InlineData("string Name = null!", true)]
        [InlineData("string Name = default!", true)]
        [InlineData("string Name = \"(none)\"", false)]
        public void A_positional_record_member_with_a_parameter_default_is_judged_by_that_default(string parameter, bool reported)
        {
            Assert.Equal(reported, Run(Positional(parameter)).Any(x => x.Id == "DWARF066"));
        }

        // The search for an initializer walks the base types by NAME, so it can meet a member that is no property,
        // field or record parameter at all - here a method the property hides. That has no default to offer; the
        // search goes on, finds none, and the member is reported.
        [Fact]
        public void A_member_hiding_a_base_method_of_its_name_finds_no_default_there_and_is_reported()
        {
            const string types = "public class BaseB { public string Name() => \"\"; }\npublic class B : BaseB { public new string Name { get; set; } }\n";
            var source = Source("").Replace("public class B {  }\n", types, StringComparison.Ordinal);

            Assert.Single(Run(source), x => x.Id == "DWARF066");
        }

        [Fact]
        public void A_nullable_member_is_not_reported_at_all()
        {
            Assert.DoesNotContain(Run(Source("public string? Name { get; set; }")), d => d.Id == "DWARF066");
        }

        [Fact]
        public void An_update_into_is_not_reported_because_a_false_guard_keeps_the_current_value_not_a_default()
        {
            // `if (IsReady(a)) b.Name = a.Name;` writes into an instance the caller already holds: a false predicate
            // leaves the member at whatever it is NOW, which is the point of a guarded update. DWARF066's premise - the
            // member "keeps its default (null)" - is about a NEW destination, and it fired here regardless (found
            // 2026-10-11, measured: the create map and the update-into of one pair both reported).
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class A { public string Name { get; set; } = ""; public bool Ready { get; set; } }
                                  public class B { public string Name { get; set; } = null!; }
                                  [DwarfMapper] public partial class M
                                  {
                                      private static bool IsReady(A a) => a.Ready;
                                      [MapProperty("Name", "Name", When = nameof(IsReady))] public partial void Patch(A a, B b);
                                      [MapProperty("Name", "Name", When = nameof(IsReady))] public partial B Create(A a);
                                  }
                                  """;

            // Line 8 (0-based) is Create: the create map keeps its report, the update-into on line 7 loses it.
            var d = Assert.Single(Run(source), x => x.Id == "DWARF066");
            Assert.Equal(8, d.Location.GetLineSpan().StartLinePosition.Line);
        }
    }
}
