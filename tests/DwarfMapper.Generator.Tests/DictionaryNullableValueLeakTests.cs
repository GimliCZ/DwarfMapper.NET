// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A dictionary whose VALUE type differs from the destination's only by a nullable annotation -
    ///     <c>Dictionary&lt;string, string?&gt;</c> into <c>Dictionary&lt;string, string&gt;</c> - was copied value by value
    ///     bare: <c>CS8601</c> inside <c>__DwarfMapDict_*</c>, a file the consumer cannot edit, no DwarfMapper diagnostic,
    ///     and at run time the null STORED in a dictionary whose value type forbids it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Found 2026-10-11 by the same probe that found the registry's twin (DWARFR15). The collection arm fixed
    ///         this exact shape for ELEMENTS long ago (#100 in <c>MapperExtractor.Conversions.Arms</c>: a nullable
    ///         reference element into the same type non-nullable throws on a null element, because a non-null reference
    ///         has no default to substitute); the dictionary arm never got the twin, although
    ///         <c>DictionaryConverter.Expr</c> already emits <c>?? throw … "Dictionary entry was null"</c> for a value
    ///         whose null handling asks for it, and docs/diagnostics.md's runtime table already promised that "a null
    ///         dictionary entry" throws <c>InvalidOperationException</c>. The code, not the docs, was wrong.
    ///     </para>
    ///     <para>
    ///         It hid behind a corpus hole: the combinatorial matrix varies a dictionary's value TYPE and its
    ///         nullability together, and no cell holds the value type fixed while flipping only the annotation.
    ///     </para>
    /// </remarks>
    public class DictionaryNullableValueLeakTests
    {
        private static string Source(string sourceType, string targetType)
        {
            return "using System.Collections.Generic;\nusing DwarfMapper;\nnamespace Demo;\n" +
                   "public class A { public " + sourceType + " Lookup { get; set; } = new(); }\n" +
                   "public class B { public " + targetType + " Lookup { get; set; } = new(); }\n" +
                   "[DwarfMapper] public partial class M { public partial B Map(A a); }\n";
        }

        [Theory]
        [InlineData("Dictionary<string, string?>", "Dictionary<string, string>")]
        [InlineData("Dictionary<int, object?>", "Dictionary<int, object>")]
        public void A_nullable_reference_value_into_the_same_type_non_nullable_leaks_no_warning(string sourceType, string targetType)
        {
            var warnings = GeneratorTestHarness.GeneratedCodeWarnings(Source(sourceType, targetType));

            Assert.True(warnings.Length == 0,
                $"{sourceType} -> {targetType}: the generated dictionary helper warns, in a file the consumer cannot edit:\n  " +
                string.Join("\n  ", warnings.Select(w => w.Id + " " + w.GetMessage(CultureInfo.InvariantCulture))));
        }

        [Fact]
        public void A_null_value_throws_like_a_null_collection_element_instead_of_being_stored()
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(
                Source("Dictionary<string, string?>", "Dictionary<string, string>"),
                NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("?? throw new global::System.InvalidOperationException(\"Dictionary entry was null\")",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_destination_value_keeps_the_plain_copy()
        {
            var (_, generated) = GeneratorTestHarness.Run(
                Source("Dictionary<string, string?>", "Dictionary<string, string?>"),
                NullableContextOptions.Enable);

            Assert.DoesNotContain("Dictionary entry was null", generated, StringComparison.Ordinal);
        }
    }
}
