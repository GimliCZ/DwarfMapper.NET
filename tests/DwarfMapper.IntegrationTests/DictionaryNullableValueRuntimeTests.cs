// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // A dictionary whose value type differs from the destination's only by a nullable annotation. Until round 32
    // the value was copied bare: CS8601 inside the generated helper and, at run time, a null STORED in a
    // Dictionary<string, string> - the behaviour this file pins against is the collection arm's for an element
    // (#100), which docs/diagnostics.md's runtime table already promised for "a null dictionary entry".

    public class DictNullableValueSrc
    {
        public Dictionary<string, string?> Labels { get; set; } = new();
    }

    public class DictNullableValueDst
    {
        public Dictionary<string, string> Labels { get; set; } = new();
    }

    [DwarfMapper]
    public partial class DictNullableValueMapper
    {
        public partial DictNullableValueDst Map(DictNullableValueSrc s);
    }

    public class DictionaryNullableValueRuntimeTests
    {
        [Fact]
        public void Non_null_values_map_unchanged()
        {
            var d = new DictNullableValueMapper().Map(new DictNullableValueSrc
            {
                Labels = new Dictionary<string, string?>
                {
                    ["a"] = "x",
                    ["b"] = "y"
                }
            });

            Assert.Equal(2, d.Labels.Count);
            Assert.Equal("x", d.Labels["a"]);
            Assert.Equal("y", d.Labels["b"]);
        }

        [Fact]
        public void A_null_value_throws_rather_than_being_stored_where_the_type_forbids_it()
        {
            var src = new DictNullableValueSrc
            {
                Labels = new Dictionary<string, string?>
                {
                    ["a"] = "x",
                    ["b"] = null
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => new DictNullableValueMapper().Map(src));
            Assert.Equal("Dictionary entry was null", ex.Message);
        }
    }
}
