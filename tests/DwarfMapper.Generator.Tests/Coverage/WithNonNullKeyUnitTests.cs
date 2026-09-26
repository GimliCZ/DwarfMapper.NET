// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;

// Unit test for MapperExtractor.WithNonNullKey, extracted from ReadDerivedTypeAttributes' skip of an attribute with no
// class (per-branch rule: a branch no compilation reaches is extracted and tested directly). Roslyn gives every
// AttributeData a class, and one without cannot be constructed, so the skip is pinned here on plain values.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class WithNonNullKeyUnitTests
    {
        [Fact]
        public void Items_with_a_null_key_are_skipped_and_the_rest_keep_their_order_and_key()
        {
            var pairs = MapperExtractor.WithNonNullKey(new[] { "alpha", "", "gamma" }, s => s.Length == 0 ? null : s.ToUpperInvariant()).ToList();

            Assert.Equal(new[] { ("alpha", "ALPHA"), ("gamma", "GAMMA") }, pairs);
        }

        [Fact]
        public void No_items_yield_nothing()
        {
            Assert.Empty(MapperExtractor.WithNonNullKey(Array.Empty<string>(), s => s));
        }
    }
}
