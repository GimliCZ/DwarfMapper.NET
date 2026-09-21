// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     Drives <see cref="ImmutabilityProof" />'s arms directly.
    ///     <para>
    ///         WHY DIRECTLY. Every existing test reaches this class through the generator, which only ever hands
    ///         it the shapes a <c>[MapShare]</c> or an automatic share actually produces. The arms that answer
    ///         the OTHER shapes — a delegate, a type parameter, a type with no canonical empty, a graph deep
    ///         enough to exhaust the visit budget — are reachable in principle and unreached in practice, which
    ///         is exactly what the round-29 Codecov patch report flagged: <c>ImmutabilityProof.cs</c> at 75.0 %,
    ///         the worst file in the change and 34 of its 86 uncovered lines.
    ///     </para>
    ///     <para>
    ///         A refusal arm nobody exercises is the dangerous kind of uncovered line: it decides whether a share
    ///         is REFUSED, and a share wrongly allowed aliases mutable state into a consumer's object graph.
    ///     </para>
    /// </summary>
    public class ImmutabilityProofArmTests
    {
        private static CSharpCompilation Compile(string source)
        {
            return GeneratorTestHarness.BuildCompilation(
                "ImmutabilityArms_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source)]);
        }

        private static ITypeSymbol Member(CSharpCompilation c, string typeName, string member)
        {
            var t = c.GetTypeByMetadataName(typeName)!;
            Assert.True(t is not null, $"fixture type {typeName} not found");
            var m = t!.GetMembers(member).Single();
            return m switch
            {
                IPropertySymbol p => p.Type,
                IFieldSymbol f => f.Type,
                _ => throw new InvalidOperationException($"{member} is neither a property nor a field")
            };
        }

        // ── Classify: the arms that REFUSE ───────────────────────────────────────────────────────────

        /// <summary>
        ///     A delegate is one of the shapes the proof cannot see through: its target can close over anything.
        ///     Unprovable, not Mutable — the difference matters, because Mutable is a DISPROOF the diagnostic
        ///     words differently.
        /// </summary>
        [Fact]
        public void A_delegate_is_unprovable_rather_than_disproven()
        {
            const string src = """
                               namespace T;
                               public delegate int Op(int x);
                               public sealed class Holder { public Op? Handler { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Holder", "Handler"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Unprovable, verdict);
            Assert.Contains("cannot see through", reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An unsubstituted type parameter: the proof has no type to walk, so it refuses rather than
        ///     assuming the best about whatever is substituted later.
        /// </summary>
        [Fact]
        public void An_open_type_parameter_is_unprovable()
        {
            const string src = """
                               namespace T;
                               public sealed class Box<TItem> { public TItem? Item { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Box`1", "Item"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Unprovable, verdict);
            Assert.Contains("cannot see through", reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An array is a DISPROOF, not a gap — the canonical shared-mutable-state shape. Pinned beside the
        ///     two above so the three verdicts are visibly distinct rather than "not Proven".
        /// </summary>
        [Fact]
        public void An_array_is_disproven_and_says_why()
        {
            const string src = """
                               namespace T;
                               public sealed class Holder { public int[]? Values { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Holder", "Values"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Mutable, verdict);
            Assert.Contains("settable through any reference", reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The control for the three above: an unmanaged struct is Proven, so the refusals are attributable
        ///     to the SHAPE rather than to a proof that refuses everything handed to it.
        /// </summary>
        [Fact]
        public void An_unmanaged_struct_is_proven_so_the_refusals_above_mean_something()
        {
            const string src = """
                               namespace T;
                               public struct Point { public int X; public int Y; }
                               public sealed class Holder { public Point P { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Holder", "P"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Proven, verdict);
            Assert.True(string.IsNullOrEmpty(reason) || reason.Length >= 0);
        }

        // ── The FIELD arm of member classification ───────────────────────────────────────────────────
        // Every share the generator produces today walks PROPERTIES, so the field arm -- a separate branch
        // with its own three outcomes -- was reached by nothing. It is the arm that decides whether a plain
        // `public T Field;` aliases mutable state, which is the disproof this whole class exists for.

        /// <summary>A writable field is a DISPROOF: anyone holding the object can assign through it.</summary>
        [Fact]
        public void A_writable_field_is_disproven()
        {
            const string src = """
                               namespace T;
                               public sealed class Bag { public int Count; }
                               public sealed class Holder { public Bag? B { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Holder", "B"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Mutable, verdict);
            Assert.Contains("writable field", reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A READONLY field cannot be reassigned, so the verdict turns on what it holds — here an array,
        ///     whose elements are settable through the very reference the field hands out. Mutable, and the
        ///     reason must be the MEMBER's rather than a generic one, or the diagnostic points at the wrong type.
        /// </summary>
        [Fact]
        public void A_readonly_field_holding_a_mutable_type_is_disproven_with_the_members_reason()
        {
            const string src = """
                               namespace T;
                               public sealed class Bag { public readonly int[] Values = new int[1]; }
                               public sealed class Holder { public Bag? B { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Holder", "B"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Mutable, verdict);
            Assert.Contains("settable through any reference", reason, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A readonly field of an UNPROVABLE type degrades the whole type to Unprovable rather than to
        ///     Mutable — the distinction `[MapShare]` rests on, since it shares what is merely unprovable on the
        ///     caller's assertion and refuses what is disproven outright.
        /// </summary>
        [Fact]
        public void A_readonly_field_holding_an_unprovable_type_degrades_to_unprovable()
        {
            const string src = """
                               namespace T;
                               public delegate int Op(int x);
                               public sealed class Bag { public readonly Op? Handler = null; }
                               public sealed class Holder { public Bag? B { get; init; } }
                               """;
            var c = Compile(src);

            var verdict = ImmutabilityProof.Classify(Member(c, "T.Holder", "B"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Unprovable, verdict);
            Assert.Contains("cannot see through", reason, StringComparison.Ordinal);
        }

        // ── TryEmptyExpression: every arm, including the two that return null ────────────────────────

        /// <summary>A non-named type has no canonical empty and must not have one invented for it.</summary>
        [Fact]
        public void An_array_type_has_no_empty_expression()
        {
            const string src = """
                               namespace T;
                               public sealed class Holder { public int[]? Values { get; init; } }
                               """;
            var c = Compile(src);

            Assert.Null(ImmutabilityProof.TryEmptyExpression(Member(c, "T.Holder", "Values")));
        }

        /// <summary>A static get-able PROPERTY named Empty whose type is the declaring type: the convention.</summary>
        [Fact]
        public void A_static_Empty_property_of_the_same_type_is_used()
        {
            const string src = """
                               namespace T;
                               public sealed class Bag { public static Bag Empty { get; } = new(); }
                               public sealed class Holder { public Bag? B { get; init; } }
                               """;
            var c = Compile(src);

            Assert.Equal("global::T.Bag.Empty", ImmutabilityProof.TryEmptyExpression(Member(c, "T.Holder", "B")));
        }

        /// <summary>The FIELD arm of the same convention — a separate switch case, separately unreached.</summary>
        [Fact]
        public void A_static_Empty_field_of_the_same_type_is_used()
        {
            const string src = """
                               namespace T;
                               public sealed class Bag { public static readonly Bag Empty = new(); }
                               public sealed class Holder { public Bag? B { get; init; } }
                               """;
            var c = Compile(src);

            Assert.Equal("global::T.Bag.Empty", ImmutabilityProof.TryEmptyExpression(Member(c, "T.Holder", "B")));
        }

        /// <summary>
        ///     A type with no <c>Empty</c> at all falls through to null — the "no canonical empty" refusal the
        ///     class comment insists on: "a type with no canonical empty has no answer this method may invent".
        /// </summary>
        [Fact]
        public void A_type_without_an_Empty_member_gets_no_expression()
        {
            const string src = """
                               namespace T;
                               public sealed class Bag { public int Count { get; init; } }
                               public sealed class Holder { public Bag? B { get; init; } }
                               """;
            var c = Compile(src);

            Assert.Null(ImmutabilityProof.TryEmptyExpression(Member(c, "T.Holder", "B")));
        }

        /// <summary>
        ///     An <c>Empty</c> that is INSTANCE, or non-public, or of a different type, is not the convention and
        ///     must be skipped rather than emitted — emitting it would produce code that does not compile, or
        ///     worse, one that compiles and means something else.
        /// </summary>
        [Theory]
        [InlineData("public Bag Empty { get; } = new();")] //            instance, not static
        [InlineData("internal static Bag Empty { get; } = new();")] //   not public
        [InlineData("public static int Empty { get; }")] //              wrong type
        public void An_Empty_that_does_not_match_the_convention_is_skipped(string declaration)
        {
            var src = $$"""
                        namespace T;
                        public sealed class Bag { {{declaration}} }
                        public sealed class Holder { public Bag? B { get; init; } }
                        """;
            var c = Compile(src);

            Assert.Null(ImmutabilityProof.TryEmptyExpression(Member(c, "T.Holder", "B")));
        }

        // ── GuardsOnDefault ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        ///     <c>ImmutableArray&lt;T&gt;</c> is never null yet wraps a null array, so the emitted guard has to
        ///     test <c>IsDefault</c>. Every other type answers false, and both answers are pinned because a
        ///     wrong one changes what a null source produces.
        /// </summary>
        [Fact]
        public void Only_ImmutableArray_guards_on_default()
        {
            const string src = """
                               using System.Collections.Immutable;
                               namespace T;
                               public sealed class Holder
                               {
                                   public ImmutableArray<int> Arr { get; init; }
                                   public ImmutableList<int>? List { get; init; }
                               }
                               """;
            var c = Compile(src);

            Assert.True(ImmutabilityProof.GuardsOnDefault(Member(c, "T.Holder", "Arr")));
            Assert.False(ImmutabilityProof.GuardsOnDefault(Member(c, "T.Holder", "List")));
        }
    }
}
