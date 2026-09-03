// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Round 29 T0.2b: closes the corpus hole <c>SpanMapBlitTests.Nullable_element_pair_is_not_a_reinterpret</c>
    ///     documented (its remarks previously said the loop itself does not compile for a <c>Nullable&lt;T&gt;</c>
    ///     element pair — CS1503, bare <c>src[__i]</c> passed to the synthesized helper's non-nullable parameter).
    ///     The span map's per-element expression now goes through the SAME rule the collection converter's
    ///     <c>ElementExpr</c> applies to array/list elements — <see cref="DwarfMapper.Generator.Pipeline.MapEmitter" />
    ///     no longer writes a second, ad-hoc "just call the converter" expression.
    /// </summary>
    public class SpanMapNullableElementTests
    {
        /// <summary>
        ///     (a) <c>ReadOnlySpan&lt;P?&gt; → Span&lt;Q?&gt;</c>, struct elements routed through a synthesized
        ///     object helper: null stays null, a value is lifted through the helper and re-wrapped.
        /// </summary>
        [Fact]
        public void Nullable_struct_elements_are_lifted_null_preserving()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct P { public int V; }
                    public struct Q { public int V; }
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<P?> src, Span<Q?> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(src));
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
            Assert.Contains(".HasValue ?", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     (b) <c>ReadOnlySpan&lt;C?&gt; → Span&lt;D?&gt;</c>, class (reference-type) elements. Two DISTINCT
        ///     defects were RED here, not one: (1) the method's own declared signature dropped the nullable
        ///     annotation on the span's type argument (<c>Span&lt;D?&gt;</c> rendered as <c>Span&lt;D&gt;</c>),
        ///     which mismatched the user's own partial declaration — CS8611, on the SIGNATURE, before element
        ///     resolution is even reached; fixed by giving the span parameter/return types the same
        ///     nullable-aware format <c>CollectionConverter</c> already used for its own helper signatures. That
        ///     fix alone would have UNMASKED a second one it was hiding: once the declared element type is
        ///     really <c>D?</c>, the call site needs the same null-forgiving <c>!</c> the collection converter's
        ///     F24 rule applies to a nullable-reference element into a synthesized helper's non-nullable
        ///     parameter (the helper null-guards internally: null in, null out) — same as <c>C?[] → D?[]</c>.
        /// </summary>
        [Fact]
        public void Nullable_reference_elements_compile_warning_free()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public class C { public int V; }
                    public class D { public int V; }
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<C?> src, Span<D?> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(src));
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
            // The signature fix: the declared partial keeps the '?' on both type arguments.
            Assert.Contains(
                "Map(global::System.ReadOnlySpan<global::T.C?> src, global::System.Span<global::T.D?> dst)",
                generated, StringComparison.Ordinal);
            // The forgive fix: the element call site null-forgives into the synthesized helper's non-nullable
            // parameter — the F24 rule, applied here for the first time to a span element.
            Assert.Contains("(src[__i]!)", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     (c) <c>ReadOnlySpan&lt;P?&gt; → Span&lt;Q&gt;</c>: the target element cannot hold null, so the pair
        ///     resolves EXACTLY the way the array/list arm resolves <c>P?[] → Q[]</c> — through the same
        ///     <c>TryResolveConversion</c> nullable-value-source arm, under the default
        ///     <see cref="DwarfMapper.NullStrategy.Throw" />: a runtime <c>InvalidOperationException</c> per null
        ///     element, never an unchecked <c>.Value</c> and never a silently unmapped null.
        /// </summary>
        [Fact]
        public void Nullable_struct_source_into_non_nullable_target_throws_on_null_like_the_array_arm()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct P { public int V; }
                    public struct Q { public int V; }
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<P?> src, Span<Q> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(src));
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.Contains("throw new global::System.InvalidOperationException(\"Collection element was null\")",
                generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     (d) <c>ReadOnlySpan&lt;int?&gt; → Span&lt;int?&gt;</c>: identity nullable primitives never needed a
        ///     converter and were never routed through the broken bare-call path — pinned so a future change to
        ///     the shared rule cannot regress the already-working case.
        /// </summary>
        [Fact]
        public void Identity_nullable_primitive_elements_already_worked_and_still_do()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<int?> src, Span<int?> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(src));
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.Contains("dst[__i] = src[__i];", generated, StringComparison.Ordinal);
        }
    }
}
