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
    }
}
