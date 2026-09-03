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
        ///     element, never an unchecked <c>.Value</c> and never a silently unmapped null. Round 29 T0.2b
        ///     review fix round 1: the MESSAGE now names the index too — <c>__i</c> is always in scope in this
        ///     inline loop, so this caller opts into <c>CollectionConverter.ElementExpr</c>'s <c>indexExpr</c>
        ///     parameter; the array/list arm's OWN message stays the pre-existing generic text (several of its
        ///     target shapes have no loop counter to name), so this is a locatable SUPERSET of that behaviour,
        ///     not a divergent one.
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
            Assert.Contains(
                "throw new global::System.InvalidOperationException(\"Element at index \" + __i + \" was null, and the destination element type does not admit null.\")",
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

        /// <summary>
        ///     Round 29 T0.2b review fix round 1, gap (i): a struct source lifted into a CLASS target —
        ///     <c>NullableProject</c>'s <c>elemFq</c> cast is now the nullable-aware REFERENCE format
        ///     (<c>global::T.D?</c>, not the bare <c>global::T.D</c> a plain <c>FullyQualifiedFormat</c> would
        ///     have produced before this task's signature fix), so this pins that the two fixes compose: the
        ///     cast target type itself carries the '?' the signature fix taught <c>SpanTargetElementFullName</c>
        ///     to keep.
        /// </summary>
        [Fact]
        public void Nullable_struct_source_lifted_into_a_nullable_class_target()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct P { public int V; }
                    public class D { public int V; }
                    [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<P?> src, Span<D?> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(src));
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
            Assert.Contains(".HasValue ?", generated, StringComparison.Ordinal);
            // The nullable-aware cast: (global::T.D?), not the bare (global::T.D) the pre-fix format gave.
            Assert.Contains("(global::T.D?)", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Round 29 T0.2b review fix round 1, gap (ii): a nullable element whose converter is
        ///     context-threaded — <c>ctxDepthArgs</c> (<c>", __dwarf_ctx, 0"</c>) combined with <c>needsLocal</c>
        ///     (the <c>var __item = src[__i];</c> binding <c>NullableProject</c> requires). <c>Preserve</c>
        ///     forces EVERY object-map element to thread <c>(ctx, depth)</c> — not only a genuinely cyclic one —
        ///     because Preserve must register every nested reference for potential future sharing, whether or
        ///     not this particular type graph happens to cycle; empirically confirmed the simplest way to reach
        ///     it is a plain, non-recursive struct→class element pair under
        ///     <c>ReferenceHandlingStrategy.Preserve</c>.
        /// </summary>
        [Fact]
        public void Nullable_struct_element_with_a_context_threaded_converter()
        {
            const string src = """
                using System;
                using DwarfMapper;
                namespace T
                {
                    public struct P { public int V; }
                    public class D { public int V; }
                    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                    public partial class M { public partial void Map(ReadOnlySpan<P?> src, Span<D?> dst); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Empty(GeneratorTestHarness.GeneratedCodeWarnings(src));
            Assert.Contains("for (int __i", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast", generated, StringComparison.Ordinal);
            Assert.Contains(".HasValue ?", generated, StringComparison.Ordinal);
            // needsLocal: the local binding NullableProject requires so the span isn't indexed twice (CS8629).
            Assert.Contains("var __item = src[__i];", generated, StringComparison.Ordinal);
            // ctxDepthArgs: the (ctx, depth) tail spelled with the caller's own local names, not the
            // synthesized-helper-body "ctx, depth + 1" ElementExpr defaults to.
            Assert.Contains(", __dwarf_ctx, 0)", generated, StringComparison.Ordinal);
        }
    }
}
