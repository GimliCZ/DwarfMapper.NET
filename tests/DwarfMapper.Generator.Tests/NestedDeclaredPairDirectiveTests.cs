// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>The directives written on a declared partial apply wherever its pair is mapped</b> (round 35 T2).
    ///     <para>
    ///         Under <c>ReferenceHandling.Preserve</c> and <c>OnCycle.SetNull</c> a nested use of a declared pair cannot
    ///         call the public partial - it takes no tracking context - so a synthesized <c>__DwarfMap_Obj_*</c> helper
    ///         stands in (<c>PrefersSynthesizedObjectMap</c>). The helper was built from the class's pair-scoped
    ///         directives alone: a member the method <c>[MapIgnore]</c>s was mapped, with no diagnostic. The helper now
    ///         takes the method's directives, merged with the pair-scoped ones exactly as the declared path merges
    ///         them (the method's win, the pair's fill the gaps).
    ///     </para>
    ///     <para>
    ///         The runtime half is <c>NestedDeclaredPairDirectivesTests</c> and <c>ArrayBlitElementConfigTests</c> in
    ///         IntegrationTests; these read the helper itself, in the project whose coverage is measured.
    ///     </para>
    /// </summary>
    public class NestedDeclaredPairDirectiveTests
    {
        private const string Preserve = "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]";
        private const string SetNull = "[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]";

        /// <summary>A root with a nested member and a list of the pair; <paramref name="item" /> declares the pair.</summary>
        private static string Source(string mapperAttributes, string item, string itemDto = "")
        {
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public class Item    { public int Kept { get; set; } public int Dropped { get; set; } public int Old { get; set; } public string? Note { get; set; } }
                     public class ItemDto { public int Kept { get; set; } public int Dropped { get; set; } public int Old { get; set; } public string? Note { get; set; } {{itemDto}} }
                     public class Root    { public Item Child { get; set; } = new(); public List<Item> Kids { get; set; } = new(); }
                     public class RootDto { public ItemDto Child { get; set; } = new(); public List<ItemDto> Kids { get; set; } = new(); }
                     {{mapperAttributes}}
                     public partial class M
                     {
                         {{item}}
                         public partial RootDto Map(Root r);
                     }
                     """;
        }

        /// <summary>The body of the synthesized helper for Item → ItemDto, and the diagnostics of the run.</summary>
        private static (string Helper, IReadOnlyList<Diagnostic> Diagnostics) Helper(string source)
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);

            var start = generated.IndexOf("private global::Demo.ItemDto __DwarfMap_Obj_global__Demo_Item_global__Demo_ItemDto", StringComparison.Ordinal);
            Assert.True(start >= 0, "no synthesized helper for Item → ItemDto: the nested route did not go through one.\n" + generated);
            var end = generated.IndexOf("\n    }", start, StringComparison.Ordinal);
            return (generated[start..end], diagnostics);
        }

        [Theory]
        [InlineData(Preserve)]
        [InlineData(SetNull)]
        public void An_ignore_on_the_method_keeps_the_member_out_of_the_nested_helper(string mode)
        {
            var (helper, _) = Helper(Source(mode, """[MapIgnore("Dropped")] public partial ItemDto MapItem(Item i);"""));

            Assert.DoesNotContain("Dropped", helper, StringComparison.Ordinal);
            Assert.Contains("Kept = s.Kept", helper, StringComparison.Ordinal);
        }

        // The control: with nothing written on the method, the helper maps every member, as it always did.
        [Theory]
        [InlineData(Preserve)]
        [InlineData(SetNull)]
        public void A_bare_declared_partial_leaves_the_nested_helper_complete(string mode)
        {
            var (helper, _) = Helper(Source(mode, "public partial ItemDto MapItem(Item i);"));

            Assert.Contains("Dropped = s.Dropped", helper, StringComparison.Ordinal);
        }

        // Only the pair's own partial speaks for it: an update-into of the same pair (two parameters), a partial
        // from the same source to another type, and a non-partial helper are all passed over on the way to it.
        [Fact]
        public void Other_methods_of_the_class_are_not_taken_for_the_pairs_partial()
        {
            var (helper, _) = Helper(Source(Preserve,
                """
                public partial void Update(Item i, ItemDto d);
                public partial Summary Summarize(Item i);
                public static int Twice(int v) => v * 2;
                [MapIgnore("Dropped")] public partial ItemDto MapItem(Item i);
                """,
                "} public class Summary { public int Kept { get; set; }"));

            Assert.DoesNotContain("Dropped", helper, StringComparison.Ordinal);
            Assert.Contains("Kept = s.Kept", helper, StringComparison.Ordinal);
        }

        // A class-level [MapIgnore] applies to every method of the class, so it applies to a declared pair's helper
        // as it does to the pair's method. (It did not: the helper read the pair-scoped form alone.)
        [Theory]
        [InlineData(Preserve)]
        [InlineData(SetNull)]
        public void A_class_level_ignore_reaches_the_nested_helper_of_a_declared_pair(string mode)
        {
            var (helper, _) = Helper(Source(mode + " [MapIgnore(\"Dropped\")]", "public partial ItemDto MapItem(Item i);"));

            Assert.DoesNotContain("Dropped", helper, StringComparison.Ordinal);
            Assert.Contains("Kept = s.Kept", helper, StringComparison.Ordinal);
        }

        [Fact]
        public void A_rename_a_value_a_guard_and_a_format_on_the_method_reach_the_nested_helper()
        {
            var (helper, _) = Helper(Source(Preserve,
                """
                [MapProperty("Old", "Renamed")]
                [MapValue("Fixed", 7)]
                [MapProperty("Note", "Note", NullSubstitute = "none")]
                [MapProperty("Kept", "Text", StringFormat = "D3")]
                public partial ItemDto MapItem(Item i);
                """,
                "public int Renamed { get; set; } public int Fixed { get; set; } public string Text { get; set; } = \"\";"));

            Assert.Contains("Renamed = s.Old", helper, StringComparison.Ordinal);
            Assert.Contains("Fixed = 7", helper, StringComparison.Ordinal);
            Assert.Contains("\"none\"", helper, StringComparison.Ordinal);
            // The format is applied through its own small helper.
            Assert.Contains("Text = __DwarfMap_FmtStrF_", helper, StringComparison.Ordinal);
        }

        // The merge is the declared path's: for one destination member the method's directive wins over the
        // pair-scoped one, and a pair-scoped directive for a member the method says nothing about still applies.
        [Fact]
        public void The_methods_directives_win_and_the_pair_scoped_ones_fill_the_gaps()
        {
            var (helper, _) = Helper(Source(
                Preserve + """
                           [MapProperty<Item, ItemDto>("Dropped", "A")]
                           [MapProperty<Item, ItemDto>("Dropped", "B")]
                           [MapProperty<Item, ItemDto>("Note", "Note", NullSubstitute = "pair")]
                           [MapProperty<Item, ItemDto>("Note", "Other", NullSubstitute = "pair-only")]
                           [MapValue<ItemDto>("V", 1)]
                           [MapValue<ItemDto>("W", 2)]
                           """,
                """
                [MapProperty("Old", "A")]
                [MapProperty("Note", "Note", NullSubstitute = "method")]
                [MapValue("V", 9)]
                public partial ItemDto MapItem(Item i);
                """,
                "public int A { get; set; } public int B { get; set; } public string Other { get; set; } = \"\"; public int V { get; set; } public int W { get; set; }"));

            // explicit maps: A is named by both - the method's source wins; B only by the pair.
            Assert.Contains("A = s.Old", helper, StringComparison.Ordinal);
            Assert.Contains("B = s.Dropped", helper, StringComparison.Ordinal);
            // extras: Note by both, Other only by the pair.
            Assert.Contains("\"method\"", helper, StringComparison.Ordinal);
            Assert.DoesNotContain("\"pair\"", helper, StringComparison.Ordinal);
            Assert.Contains("\"pair-only\"", helper, StringComparison.Ordinal);
            // values: V by both, W only by the pair.
            Assert.Contains("V = 9", helper, StringComparison.Ordinal);
            Assert.Contains("W = 2", helper, StringComparison.Ordinal);
        }

        // Two partials for one pair never reach the question "whose directives?": a nested use of the pair is
        // refused as ambiguous first, in these modes as in any other.
        [Theory]
        [InlineData(Preserve)]
        [InlineData(SetNull)]
        public void Two_declared_partials_for_one_pair_are_ambiguous_before_any_helper_is_built(string mode)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Source(mode,
                """
                [MapIgnore("Dropped")] public partial ItemDto MapItem(Item i);
                public partial ItemDto MapItemWhole(Item i);
                """));

            Assert.Contains(diagnostics, d => d.Id == "DWARF013");
        }

        // The block-copy gate. An array of layout-identical structs is copied in one block unless something customizes
        // the element pair; a directive on the pair's declared partial is such a thing in every mode.
        [Theory]
        [InlineData(SetNull, "[MapIgnore(\"Dropped\")] ", false)]
        [InlineData(SetNull, "", true)]
        [InlineData("[DwarfMapper]", "", false)] // resolves to the declared method itself: the loop, as always
        public void An_array_of_structs_is_block_copied_only_past_a_bare_partial(string mode, string directive, bool blockCopied)
        {
            var source = $$"""
                           using DwarfMapper;
                           namespace Demo;
                           public struct C { public int Kept; public int Dropped; }
                           public struct D { public int Kept; public int Dropped; }
                           {{mode}}
                           public partial class M
                           {
                               {{directive}}public partial D MapCell(C c);
                               public partial D[] MapArray(C[] cells);
                           }
                           """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal(blockCopied, generated.Contains("__DwarfBlit_", StringComparison.Ordinal));
            GeneratorAssert.EmitsCompilableCode(source);
        }
    }
}
