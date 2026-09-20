// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     The doc pipeline's one failure type. Its three constructors were never executed by any test, so
    ///     nothing held it to the two properties the pipeline relies on: the message reaches the reader intact,
    ///     because during a doc-test run the exception text is the ONLY diagnostic anyone sees, and the cause is
    ///     carried rather than swallowed.
    /// </summary>
    public class DocToolingExceptionTests
    {
        [Fact]
        public void Carries_the_message_it_was_given()
        {
            var ex = new DocToolingException("README.md:12 is stale");

            Assert.Equal("README.md:12 is stale", ex.Message);
            Assert.Null(ex.InnerException);
        }

        [Fact]
        public void Carries_the_message_and_the_cause_together()
        {
            var cause = new IOException("locked");

            var ex = new DocToolingException("docs/api.md could not be written", cause);

            Assert.Equal("docs/api.md could not be written", ex.Message);
            Assert.Same(cause, ex.InnerException);
        }

        /// <summary>
        ///     The parameterless constructor exists because CA1032 requires the full set on a public exception.
        ///     Nothing in the pipeline throws it - every real failure names a file:line - so what is pinned here
        ///     is only that it produces the framework's own message rather than an empty one.
        /// </summary>
        [Fact]
        public void Falls_back_to_the_framework_message_when_given_none()
        {
            var ex = new DocToolingException();

            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            Assert.Null(ex.InnerException);
        }

        /// <summary>
        ///     The base type is load-bearing, not incidental: RepoLayout.Root reports an unusable working tree
        ///     from a PROPERTY GETTER, and only an InvalidOperationException may be thrown from one without
        ///     tripping CA1065.
        /// </summary>
        [Fact]
        public void Is_an_invalid_operation_so_a_property_getter_may_throw_it()
        {
            Assert.IsAssignableFrom<InvalidOperationException>(new DocToolingException("any"));
        }
    }
}
