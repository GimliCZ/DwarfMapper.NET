// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     Runtime construction of consumer attributes. The generator reads attributes from syntax and symbols and
    ///     never instantiates them, so their constructors run only when something asks the CLR for the attribute
    ///     object — reflection-based tooling, or a test like this. Their own documentation makes promises about that
    ///     path which nothing checked.
    /// </summary>
    public sealed class RuntimeAttributeConstructionTests
    {
        [Fact]
        public void MapToAttribute_keeps_the_declared_targets_in_order()
        {
            var attribute = new MapToAttribute(typeof(string), typeof(int));

            Assert.Equal([typeof(string), typeof(int)], attribute.Targets);
        }

        [Fact]
        public void MapToAttribute_given_a_null_array_has_no_targets_rather_than_throwing()
        {
            // The constructor's own comment: a nullable-oblivious `[MapTo(null)]` reaches it as a null array, and an
            // attribute must never throw in its constructor. `null!` IS that oblivious caller.
            var attribute = new MapToAttribute(null!);

            Assert.Empty(attribute.Targets);
        }
    }
}
