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

        [Fact]
        public void MapValueAttribute_constant_form_keeps_target_and_value_and_no_Use()
        {
            var attribute = new MapValueAttribute("Source", "api-v2");

            Assert.Equal("Source", attribute.Target);
            Assert.Equal("api-v2", attribute.Value);
            Assert.Null(attribute.Use);
        }

        [Fact]
        public void MapValueAttribute_computed_form_keeps_target_and_Use_and_no_value()
        {
            var attribute = new MapValueAttribute("CreatedAt") { Use = "Now" };

            Assert.Equal("CreatedAt", attribute.Target);
            Assert.Equal("Now", attribute.Use);
            Assert.Null(attribute.Value);
        }
    }
}
