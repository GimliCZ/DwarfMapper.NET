// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="GeneratedNames.IsUserConv" /> answers a question the emitter asks about a converter's
    ///     NAME: is this one of ours, or the consumer's?
    ///     <para>
    ///         The round-29 Codecov report flagged its null branch as never taken. Every call site passes a name
    ///         it has already resolved, so <c>null</c> only arrives when a converter was not resolved at all —
    ///         which is precisely the path where an unguarded <c>StartsWith</c> would throw a
    ///         <see cref="NullReferenceException" /> out of a source generator, and a generator that throws
    ///         fails the consumer's build with no diagnostic to read.
    ///     </para>
    ///     <para>
    ///         Cheap to pin and worth pinning for that reason: the guard's whole value is on the input nothing
    ///         currently passes.
    ///     </para>
    /// </summary>
    public class GeneratedNamesPredicateTests
    {
        [Fact]
        public void A_null_name_is_not_a_user_converter_and_does_not_throw()
        {
            Assert.False(GeneratedNames.IsUserConv(null));
        }

        [Fact]
        public void An_empty_name_is_not_a_user_converter()
        {
            Assert.False(GeneratedNames.IsUserConv(string.Empty));
        }

        /// <summary>
        ///     The positive case, so the two refusals above are attributable to the INPUT rather than to a
        ///     predicate that answers false to everything. Built from the same prefix the production code uses,
        ///     rather than a literal copied here — a copy would keep passing after a rename that broke the
        ///     predicate's real callers.
        /// </summary>
        [Fact]
        public void A_name_carrying_the_user_converter_prefix_is_recognised()
        {
            var generated = GeneratedNames.UserConv + "SomeConverter";

            Assert.True(GeneratedNames.IsUserConv(generated));
        }

        [Fact]
        public void An_unrelated_name_is_not_a_user_converter()
        {
            Assert.False(GeneratedNames.IsUserConv("MapFlat"));
        }
    }
}
