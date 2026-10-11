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

        [Fact]
        public void A_nullable_member_is_not_reported_at_all()
        {
            Assert.DoesNotContain(Run(Source("public string? Name { get; set; }")), d => d.Id == "DWARF066");
        }
    }
}
