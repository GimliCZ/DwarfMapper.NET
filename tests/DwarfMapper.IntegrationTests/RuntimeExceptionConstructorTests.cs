// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     The conventional exception constructors of the runtime's public exceptions. The runtime never calls them —
    ///     it throws through the typed constructors — but they are public API a caller can reach (rethrowing,
    ///     wrapping, a test double), and nothing had ever executed them. Pinned here so a message or an inner
    ///     exception that stopped flowing through to <see cref="Exception" /> would be noticed.
    /// </summary>
    public sealed class RuntimeExceptionConstructorTests
    {
        private static readonly InvalidOperationException Inner = new("inner");

        [Fact]
        public void DwarfMapMissingException_message_constructor_keeps_the_message_and_no_pair()
        {
            var ex = new DwarfMapMissingException("custom");

            Assert.Equal("custom", ex.Message);
            Assert.Null(ex.InnerException);
            Assert.Null(ex.SourceType);
            Assert.Null(ex.DestinationType);
            Assert.Empty(ex.AmbiguousInterfaces);
        }

        [Fact]
        public void DwarfMapMissingException_inner_constructor_keeps_the_message_and_the_inner_exception()
        {
            var ex = new DwarfMapMissingException("custom", Inner);

            Assert.Equal("custom", ex.Message);
            Assert.Same(Inner, ex.InnerException);
            Assert.Null(ex.SourceType);
            Assert.Empty(ex.AmbiguousInterfaces);
        }

        [Fact]
        public void DwarfMapValidationException_inner_constructor_keeps_the_message_and_the_inner_exception()
        {
            var ex = new DwarfMapValidationException("custom", Inner);

            Assert.Equal("custom", ex.Message);
            Assert.Same(Inner, ex.InnerException);
        }

        [Fact]
        public void DwarfMappingDepthException_message_constructor_keeps_the_message_and_zero_depths()
        {
            var ex = new DwarfMappingDepthException("custom");

            Assert.Equal("custom", ex.Message);
            Assert.Null(ex.InnerException);
            Assert.Equal(0, ex.MaxDepth);
            Assert.Equal(0, ex.ActualDepth);
        }

        [Fact]
        public void DwarfMappingDepthException_inner_constructor_keeps_the_message_and_the_inner_exception()
        {
            var ex = new DwarfMappingDepthException("custom", Inner);

            Assert.Equal("custom", ex.Message);
            Assert.Same(Inner, ex.InnerException);
            Assert.Equal(0, ex.MaxDepth);
            Assert.Equal(0, ex.ActualDepth);
        }

        // ── The typed constructor's message, for a nullable-oblivious caller ─────────────────────────────────
        // Its Type parameters are non-nullable, but an assembly compiled without nullable annotations passes null
        // freely, and an exception constructor must not throw a NullReferenceException while composing its own
        // message. Every `?.` in FormatMessage exists for that caller; `null!` below IS that caller.

        [Fact]
        public void An_update_into_message_for_null_types_names_the_operation_without_throwing()
        {
            var ex = new DwarfMapMissingException(null!, null!, null, isUpdate: true);

            Assert.StartsWith("No DwarfMapper UPDATE-INTO map is registered for '' -> ''.", ex.Message, StringComparison.Ordinal);
            Assert.Contains("`public partial void Update( source,  destination);`", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_ambiguous_message_for_null_types_still_names_the_interfaces()
        {
            var ex = new DwarfMapMissingException(null!, null!, [typeof(IDisposable), typeof(ICloneable)]);

            Assert.StartsWith("Ambiguous DwarfMapper map for '' -> ''", ex.Message, StringComparison.Ordinal);
            Assert.Contains("(IDisposable, ICloneable)", ex.Message, StringComparison.Ordinal);
            Assert.Contains("[GenerateMap<, >]", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_missing_map_message_for_a_null_source_takes_the_declare_the_pair_remedy()
        {
            var ex = new DwarfMapMissingException(null!, typeof(FbDto));

            Assert.Contains("Declare [GenerateMap<, FbDto>]", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("LINQ iterator", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_LINQ_iterator_source_with_a_null_destination_takes_the_materialize_remedy()
        {
            var iterator = new[] { 1, 2 }.Where(static x => x > 0).GetType();

            var ex = new DwarfMapMissingException(iterator, null!);

            Assert.Contains("compiler-generated LINQ iterator", ex.Message, StringComparison.Ordinal);
            Assert.Contains("[GenerateMap<IEnumerable<T>, >]", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_private_nested_source_type_takes_the_materialize_remedy_too()
        {
            // No '<' in the name: it is classified an iterator by IsNestedPrivate alone, the arm a compiler-generated
            // type without a mangled name reaches.
            var ex = new DwarfMapMissingException(typeof(PrivateNestedSource), typeof(FbDto));

            Assert.Contains("'PrivateNestedSource' is a compiler-generated LINQ iterator", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_compiler_generated_iterator_source_is_recognised_by_its_mangled_name()
        {
            // A `yield` iterator's state machine is named like "<Numbers>d__0": the '<' arm of the iterator test,
            // which the LINQ case above does not take (its type is recognised by IsNestedPrivate instead).
            var iterator = Numbers().GetType();
            Assert.Contains('<', iterator.Name);

            var ex = new DwarfMapMissingException(iterator, typeof(FbDto));

            Assert.Contains("compiler-generated LINQ iterator", ex.Message, StringComparison.Ordinal);
            Assert.Contains("[GenerateMap<IEnumerable<T>, FbDto>]", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_missing_map_message_for_a_null_destination_takes_the_declare_the_pair_remedy()
        {
            var ex = new DwarfMapMissingException(typeof(FbDto), null!);

            Assert.Contains("Declare [GenerateMap<FbDto, >]", ex.Message, StringComparison.Ordinal);
        }

        private static IEnumerable<int> Numbers()
        {
            yield return 1;
        }

        private sealed class PrivateNestedSource
        {
        }
    }
}
