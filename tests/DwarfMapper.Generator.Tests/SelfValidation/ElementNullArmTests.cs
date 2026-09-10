// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Which null arm a COLLECTION ELEMENT takes when its map is a method the user declared.</b>
    ///     <para>
    ///         <c>UserConverterNullGuard</c> (<c>MapperExtractor.Conversions.cs</c>) is one decision read by five
    ///         sites — member, collection element, dictionary value, flatten leaf, constructor argument — and the
    ///         golden corpus pins it for a NESTED pair (<c>feat:NestedViaDeclaredMap</c>). It was not pinned at the
    ///         ELEMENT site, and that is the site where it was misread: on 2026-09-09 the emitted
    ///         <c>__item is null ? null! : MapFlat(__item)</c> was filed as an inconsistency against the
    ///         synthesized path, on the strength of reading one side of the pair and not the other. The
    ///         synthesized helper carries the same decision INSIDE its body (<c>if (s is null) return null!;</c>),
    ///         and <c>6fa7308</c> had already stated the ruling. See
    ///         <c>Issues/round30/FINDING-array-null-ternary.md</c> for the withdrawal.
    ///     </para>
    ///     <para>
    ///         So this pins the emitted expression for every cell, which is documentation a reader can run. Each
    ///         assertion below was written FROM the generator's actual output rather than from the doc-comment —
    ///         the fourth cell does not follow from the annotation rules alone and would have been asserted wrongly
    ///         if predicted.
    ///     </para>
    /// </summary>
    public class ElementNullArmTests
    {
        private static string Source(string srcElem, string dstElem)
        {
            return $$"""
                using DwarfMapper;
                namespace T
                {
                    public class Child { public int V { get; set; } }
                    public class ChildDto { public int V { get; set; } }
                    public class Src { public {{srcElem}}[] Items { get; set; } = new {{srcElem}}[0]; }
                    public class Dst { public {{dstElem}}[] Items { get; set; } = new {{dstElem}}[0]; }
                    [DwarfMapper] public partial class M
                    {
                        public partial Dst Map(Src s);
                        public partial ChildDto MapChild(Child c);
                    }
                }
                """;
        }

        /// <summary>
        ///     Both ends non-nullable-annotated: the lift fires WITH the null-forgiving operator.
        ///     <c>NullHandling.NullableProjectRefForgiving</c>. Both annotations claim the element cannot be null;
        ///     a <c>Result&lt;T&gt;</c> whose <c>Fail</c> parks <c>default!</c> makes one of them false at run
        ///     time, and mapping a failed result must not throw. The <c>!</c> is what keeps CS8601 out of a
        ///     generated file no consumer <c>#pragma</c> can reach.
        /// </summary>
        [Fact]
        public void Non_nullable_source_and_destination_lift_the_null_and_forgive_it()
        {
            var g = GeneratorAssert.CompilesClean(Source("Child", "ChildDto"), NullableContextOptions.Enable);

            Assert.Contains("__r[__i] = (__item is null ? null! : (global::T.ChildDto)MapChild(__item));", g, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Source nullable, destination not: <c>NullHandling.None</c>, UNCHANGED, and the ARGUMENT is forgiven
        ///     instead. That is the shape <c>DWARF070</c> names against the consumer's own DTO; lifting it here
        ///     would swallow the one case the mapper is supposed to be loud about.
        /// </summary>
        [Fact]
        public void A_nullable_source_element_into_a_non_nullable_destination_is_left_for_DWARF070()
        {
            var g = GeneratorAssert.CompilesClean(Source("Child?", "ChildDto"), NullableContextOptions.Enable);

            Assert.Contains("__r[__i] = MapChild(src[__i]!);", g, StringComparison.Ordinal);
            Assert.DoesNotContain("is null ? null", g, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Destination CAN hold null: <c>NullHandling.NullableProjectRef</c> — the same lift with no
        ///     forgiveness, because the destination's own annotation permits the null.
        /// </summary>
        [Fact]
        public void A_nullable_destination_element_lifts_the_null_without_forgiving_it()
        {
            var g = GeneratorAssert.CompilesClean(Source("Child", "ChildDto?"), NullableContextOptions.Enable);

            Assert.Contains("__r[__i] = (__item is null ? null : (global::T.ChildDto?)MapChild(__item));", g, StringComparison.Ordinal);
            Assert.DoesNotContain("null!", g, StringComparison.Ordinal);
        }

        /// <summary>
        ///     <b>The cell that does not follow from the annotation rules, measured rather than predicted.</b>
        ///     <para>
        ///         Under <c>#nullable disable</c> every annotation is <c>None</c>, so by the three arms above this
        ///         should read like the first case and emit the forgiving lift — <c>None</c> is not
        ///         <c>Annotated</c> at either end. It does not. The guard never runs, because its gate asks
        ///         <c>ConverterParamIsNonNullableRef</c> and a converter parameter in an OBLIVIOUS context is not
        ///         non-nullable either. The element is handed over bare.
        ///     </para>
        ///     <para>
        ///         <b>The consequence is worth stating plainly rather than only pinning:</b> in a
        ///         <c>&lt;Nullable&gt;disable&lt;/Nullable&gt;</c> consumer project, a null element does NOT map to
        ///         null — it reaches the user's own method and hits its <c>ArgumentNullException.ThrowIfNull</c>.
        ///         The <c>Result&lt;T&gt;</c>-Fail protection that arm one exists to provide is therefore absent
        ///         exactly where the consumer made no nullability claims. That may well be the right answer — no
        ///         annotations, no promises — but it is a behavioural difference between two project settings that
        ///         no document currently states, and it is pinned here so that a change to it is deliberate.
        ///     </para>
        /// </summary>
        [Fact]
        public void In_an_oblivious_context_the_element_gets_no_null_test_at_all()
        {
            var g = GeneratorAssert.CompilesClean(Source("Child", "ChildDto"), NullableContextOptions.Disable);

            Assert.Contains("__r[__i] = MapChild(src[__i]);", g, StringComparison.Ordinal);
            Assert.DoesNotContain("is null ?", g, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The non-vacuity control. Every assertion above is a <c>Contains</c> over generated text, and a
        ///     harness that returned an empty string, or a source that silently produced no collection helper,
        ///     would fail them all in a way that reads like a regression in the generator. This proves the corpus
        ///     these four cells are built from actually emits an element loop.
        /// </summary>
        [Fact]
        public void The_probe_source_really_emits_an_element_loop()
        {
            var g = GeneratorAssert.CompilesClean(Source("Child", "ChildDto"), NullableContextOptions.Enable);

            Assert.Contains("for (int __i = 0; __i < src.Length; __i++)", g, StringComparison.Ordinal);
            Assert.Contains("MapChild", g, StringComparison.Ordinal);
        }
    }
}
