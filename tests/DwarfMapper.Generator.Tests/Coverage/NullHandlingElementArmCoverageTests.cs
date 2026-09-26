// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for the per-ENTRY null-handling arms shared by DictionaryConverter.Expr and
// CollectionConverter.ElementExpr. Both builders switch on NullHandling twice — once for an entry with no
// converter, once for an entry routed through one — and the ValueOrDefault / ThrowIfNull / NullableProject arms
// had only ever been reached for MEMBERS, never for a dictionary value or collection element.
//
// How the resolver picks each arm (MapperExtractor.Conversions.Arms.cs, the Nullable<T> arm):
//   nullHandling = NullStrategy == SetDefault ? ValueOrDefault : ThrowIfNull
//   - `int? -> int`   unwraps to a direct assign   → converter is null
//   - `E1? -> E2`     unwraps, then needs an enum converter → converter is non-null
//   - `E1? -> E2?`    both nullable, converter lifts → NullableProject
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class NullHandlingElementArmCoverageTests
    {
        private const string Enums = "public enum E1 { A, B } public enum E2 { A, B }";

        private static string Source(string srcMember, string dstMember, bool setDefault)
        {
            var options = setDefault ? "(NullStrategy = NullStrategy.SetDefault)" : "";
            return "using System.Collections.Generic;\nusing DwarfMapper;\nnamespace Demo;\n" + Enums + "\n" +
                   "public class S { public " + srcMember + " V { get; set; } = new(); }\n" +
                   "public class D { public " + dstMember + " V { get; set; } = new(); }\n" +
                   "[DwarfMapper" + options + "] public partial class M { public partial D Map(S s); }\n";
        }

        // ── DictionaryConverter.Expr ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Dictionary_nullable_value_without_converter_takes_the_default_under_SetDefault()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Source("Dictionary<string, int?>", "Dictionary<string, int>", setDefault: true));
            // No converter: the unwrap is the whole expression, closed by the statement, not by a call.
            Assert.Contains("__kv.Value.GetValueOrDefault(); }", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Dictionary_nullable_value_without_converter_throws_by_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Source("Dictionary<string, int?>", "Dictionary<string, int>", setDefault: false));
            Assert.Contains("= __kv.Value ?? throw new global::System.InvalidOperationException(\"Dictionary entry was null\")",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Dictionary_nullable_enum_value_through_a_converter_takes_the_default_under_SetDefault()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Source("Dictionary<string, E1?>", "Dictionary<string, E2>", setDefault: true));
            // The unwrap is the converter's ARGUMENT: the call closes right after it.
            Assert.Contains("(__kv.Value.GetValueOrDefault())", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Dictionary_nullable_enum_value_through_a_converter_throws_by_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Source("Dictionary<string, E1?>", "Dictionary<string, E2>", setDefault: false));
            Assert.Contains("(__kv.Value ?? throw new global::System.InvalidOperationException(\"Dictionary entry was null\"))",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Dictionary_nullable_enum_value_into_a_nullable_target_lifts_through_the_converter()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(
                Source("Dictionary<string, E1?>", "Dictionary<string, E2?>", setDefault: false));
            Assert.Contains("(__kv.Value.HasValue ? (", generated, StringComparison.Ordinal);
        }

        // ── CollectionConverter.ElementExpr ─────────────────────────────────────────────────────────────────

        [Fact]
        public void List_nullable_element_without_converter_takes_the_default_under_SetDefault()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source("List<int?>", "List<int>", setDefault: true));
            Assert.Contains(".GetValueOrDefault()", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void List_nullable_element_without_converter_throws_by_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source("List<int?>", "List<int>", setDefault: false));
            Assert.Contains("?? throw new global::System.InvalidOperationException(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void List_nullable_enum_element_through_a_converter_takes_the_default_under_SetDefault()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source("List<E1?>", "List<E2>", setDefault: true));
            Assert.Contains(".GetValueOrDefault())", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void List_nullable_enum_element_through_a_converter_throws_by_default()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source("List<E1?>", "List<E2>", setDefault: false));
            Assert.Contains("?? throw new global::System.InvalidOperationException(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void List_nullable_enum_element_into_a_nullable_target_lifts_through_the_converter()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source("List<E1?>", "List<E2?>", setDefault: false));
            Assert.Contains(".HasValue ? (", generated, StringComparison.Ordinal);
        }
    }
}
