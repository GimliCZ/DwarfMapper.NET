// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="Identifiers" /> is the one place a consumer-supplied name becomes text in a
    ///     <c>.g.cs</c>, and it has two sibling helpers rather than one because the two positions genuinely
    ///     differ. These tests pin that difference AS A MEASUREMENT against the real compiler, not as an
    ///     assertion about what the language is believed to allow.
    ///     <para>
    ///         The measurement first arrived with the round-29 view endpoint and outlived it: the endpoint was
    ///         withdrawn on 2026-09-07 (see <c>Issues/round29/WITHDRAWN-generated-views.md</c>), and
    ///         <see cref="Identifiers.EscapeTypeName" /> was kept because nothing about it is view-specific —
    ///         it is the correct helper for any name written in a type-DECLARATION position, and a mapper
    ///         class named <c>@record</c> is the same shape. It currently has no production call site; these
    ///         tests are what keep the measurement it encodes honest until one arrives.
    ///     </para>
    /// </summary>
    public class IdentifiersTests
    {
        /// <summary>
        ///     A type name is not written once. It is written where the type is DECLARED, where it is a
        ///     RETURN type, and where it is CONSTRUCTED — and the three do not fail together, which is why a
        ///     probe that only looked at the declaration would have called <c>record</c> and <c>partial</c>
        ///     safe. This shape carries all three.
        /// </summary>
        private static string TypeNamePositions(string name, string suffix = "")
        {
            return $"public class H{suffix} {{ public readonly ref struct {name} {{ }} " +
                   $"public {name} Make() => new {name}(); }}";
        }

        /// <summary>
        ///     The whole reason <see cref="Identifiers.EscapeTypeName" /> is a SIBLING of
        ///     <see cref="Identifiers.Escape" /> rather than a widening of it: a contextual keyword is fine as
        ///     a member name and is not fine as a type name. Widening <c>Escape</c> to cover both would churn
        ///     every golden file in the corpus for no gain.
        /// </summary>
        [Theory]
        [InlineData("record")]
        [InlineData("required")]
        [InlineData("file")]
        [InlineData("scoped")]
        [InlineData("partial")]
        [InlineData("extension")]
        public void A_contextual_keyword_is_escaped_in_type_position_and_left_alone_in_member_position(string name)
        {
            Assert.Equal(name, Identifiers.Escape(name));
            Assert.Equal("@" + name, Identifiers.EscapeTypeName(name));
        }

        /// <summary>
        ///     The measurement the two-helper split rests on, SWEPT rather than listed: every contextual
        ///     keyword Roslyn knows, written unescaped into the three type-name positions, and the set that
        ///     breaks pinned exactly.
        ///     <para>
        ///         Re-measured 2026-09-07 and it corrected the note it replaces twice over. That note said
        ///         "of 29 contextual keywords, five broke"; there are <b>46</b>, and <b>six</b> break — the
        ///         sixth is <c>extension</c>, which C# 14 added and a hand-written list could not know about.
        ///         That is the argument for sweeping in one sentence: a curated list of keywords goes stale
        ///         with the language, and this repository has already paid for that mistake once
        ///         (<c>9f937b8</c> replaced a curated keyword list with the escape itself).
        ///     </para>
        ///     <para>
        ///         The two halves of the set fail differently, which is the part a declaration-only probe
        ///         would miss: four are refused wherever the type is DECLARED, and two — <c>record</c> and
        ///         <c>partial</c> — declare perfectly well and break only where the name is READ as a modifier
        ///         of what follows it.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_contextual_keywords_that_break_a_type_name_are_exactly_these()
        {
            var contextual = ContextualKeywords();
            Assert.True(contextual.Count > 40,
                $"only {contextual.Count} contextual keywords found - the sweep stopped sweeping");

            var broke = new List<string>();
            var brokeTheDeclarationToo = new List<string>();
            foreach (var name in contextual)
            {
                if (ErrorsIn(TypeNamePositions(name)).Count == 0)
                {
                    continue;
                }

                broke.Add(name);
                if (ErrorsIn($"public struct {name} {{ }}").Count > 0)
                {
                    brokeTheDeclarationToo.Add(name);
                }

                // Whatever the mechanism, the escape answers it.
                Assert.Empty(ErrorsIn(TypeNamePositions(Identifiers.EscapeTypeName(name))));
            }

            Assert.Equal(["extension", "file", "partial", "record", "required", "scoped"], broke);
            Assert.Equal(["extension", "file", "required", "scoped"], brokeTheDeclarationToo);
        }

        /// <summary>
        ///     The per-keyword diagnostic ids behind the four that are refused outright, kept because "it is
        ///     refused" and "it is refused for THIS reason" are different claims and only the second one
        ///     survives a language change without quietly starting to mean something else.
        /// </summary>
        [Theory]
        [InlineData("required", "CS9029")]
        [InlineData("file", "CS9056")]
        [InlineData("scoped", "CS9062")]
        [InlineData("extension", "CS9306")]
        public void The_four_refused_as_a_bare_type_declaration_report_these_ids(string name, string id)
        {
            Assert.Contains(ErrorsIn($"public struct {name} {{ }}"), d => string.Equals(d.Id, id, StringComparison.Ordinal));
            Assert.Empty(ErrorsIn($"public struct {Identifiers.EscapeTypeName(name)} {{ }}"));
        }

        /// <summary>
        ///     And two of them are not refused there, which is the half that makes the sweep necessary:
        ///     <c>record</c> and <c>partial</c> declare a struct without complaint, so the defect they cause
        ///     appears somewhere other than where the name was introduced — for the withdrawn view endpoint
        ///     that was the factory's return type, <c>public record Make()</c> parsing as a positional record.
        /// </summary>
        [Theory]
        [InlineData("record")]
        [InlineData("partial")]
        public void Two_of_them_declare_cleanly_and_only_break_where_the_name_is_used(string name)
        {
            Assert.Empty(ErrorsIn($"public readonly ref struct {name} {{ }}"));
            Assert.NotEmpty(ErrorsIn(TypeNamePositions(name)));
        }

        /// <summary>
        ///     The other half of the same measurement: as a MEMBER name a contextual keyword needs nothing,
        ///     which is what makes <see cref="Identifiers.Escape" />'s narrower rule correct rather than lax.
        /// </summary>
        [Theory]
        [InlineData("record")]
        [InlineData("required")]
        [InlineData("file")]
        [InlineData("scoped")]
        [InlineData("partial")]
        [InlineData("extension")]
        public void A_contextual_keyword_is_a_legal_member_name_unescaped(string name)
        {
            Assert.Empty(ErrorsIn($"public class C {{ public int {name} {{ get; set; }} }}"));
        }

        /// <summary>A RESERVED keyword is escaped in both positions — the two helpers agree there.</summary>
        [Theory]
        [InlineData("class")]
        [InlineData("int")]
        [InlineData("event")]
        public void A_reserved_keyword_is_escaped_in_both_positions(string name)
        {
            Assert.Equal("@" + name, Identifiers.Escape(name));
            Assert.Equal("@" + name, Identifiers.EscapeTypeName(name));
        }

        /// <summary>
        ///     A name that merely RESEMBLES a keyword is untouched. The escape is case-sensitive because
        ///     <c>SyntaxFacts</c> is, and escaping what does not need it would move golden output.
        /// </summary>
        [Theory]
        [InlineData("Record")]
        [InlineData("Row")]
        [InlineData("Class")]
        [InlineData("Identity")]
        public void A_name_that_only_resembles_a_keyword_is_left_alone(string name)
        {
            Assert.Equal(name, Identifiers.Escape(name));
            Assert.Equal(name, Identifiers.EscapeTypeName(name));
        }

        /// <summary>
        ///     A name a consumer already escaped is left as they wrote it. <c>@@class</c> is not an
        ///     identifier, so double-escaping is a parse failure in the consumer's <c>.g.cs</c>, not a
        ///     cosmetic defect.
        /// </summary>
        [Theory]
        [InlineData("@class")]
        [InlineData("@record")]
        public void A_name_the_consumer_escaped_is_not_escaped_twice(string name)
        {
            Assert.Equal(name, Identifiers.Escape(name));
            Assert.Equal(name, Identifiers.EscapeTypeName(name));
        }

        /// <summary>The empty string is returned unchanged rather than becoming a bare <c>@</c>.</summary>
        [Fact]
        public void The_empty_string_is_returned_unchanged()
        {
            Assert.Equal(string.Empty, Identifiers.Escape(string.Empty));
            Assert.Equal(string.Empty, Identifiers.EscapeTypeName(string.Empty));
        }

        /// <summary>
        ///     The sweep the two-helper split rests on, kept as a test rather than a note: EVERY keyword
        ///     Roslyn knows — reserved and contextual, taken from <c>SyntaxFacts.GetKeywordKinds()</c> rather
        ///     than a hand-picked list that would go stale as the language grows — is escaped by
        ///     <see cref="Identifiers.EscapeTypeName" /> and names a type that declares, returns and
        ///     constructs clean in all three positions at once.
        /// </summary>
        [Fact]
        public void Every_keyword_roslyn_knows_names_a_type_that_compiles_once_escaped()
        {
            var keywords = SyntaxFacts.GetKeywordKinds().Select(SyntaxFacts.GetText)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(keywords.Count > 100, $"only {keywords.Count} keywords found - the sweep stopped sweeping");

            var declarations = new List<string>(keywords.Count);
            for (var i = 0; i < keywords.Count; i++)
            {
                var escaped = Identifiers.EscapeTypeName(keywords[i]);
                Assert.Equal("@" + keywords[i], escaped);
                declarations.Add(TypeNamePositions(escaped, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            // One compilation over all of them: 127 separate ones measure the same thing far more slowly, and
            // each holder class is numbered so a collision between two of them cannot pass for a clean run.
            Assert.Empty(ErrorsIn(string.Join("\n", declarations)));
        }

        private static List<string> ContextualKeywords()
        {
            return SyntaxFacts.GetContextualKeywordKinds().Select(SyntaxFacts.GetText)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }

        private static List<Diagnostic> ErrorsIn(string source)
        {
            return GeneratorTestHarness.BuildCompilation("IdentifiersProbe", source)
                .GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
        }
    }
}
