// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Three small value helpers whose edge answers no compilation produces:
// - EquatableArray hashes a default (null-backed) array and a null element; default and empty are one value;
// - MapperClassModel.EmitContainingTypes passes through a header with no modifiers (a bare type name);
// - GeneratedNames' prefix predicates answer false for a null name (a member with no converter).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ModelAndNamingEdgeUnitTests
    {
        [Fact]
        public void A_default_EquatableArray_hashes_like_an_empty_one()
        {
            Assert.Equal(EquatableArray.From(Array.Empty<string>()).GetHashCode(), default(EquatableArray<string>).GetHashCode());
        }

        [Fact]
        public void A_null_element_hashes_as_zero_and_arrays_with_it_stay_equal()
        {
            // The null element IS the case under test: EquatableArray<string> is declared over non-null strings, and
            // the hash has to survive one arriving anyway.
            var one = EquatableArray.From(new[] { "a", null! });
            var two = EquatableArray.From(new[] { "a", null! });

            Assert.Equal(one, two);
            Assert.Equal(one.GetHashCode(), two.GetHashCode());
            Assert.NotEqual(EquatableArray.From(new[] { "a", "b" }).GetHashCode(), one.GetHashCode());
        }

        [Fact]
        public void A_containing_type_header_without_modifiers_is_emitted_as_the_escaped_name()
        {
            var model = new MapperClassModel("Demo",
                "M",
                "public",
                default,
                default,
                default,
                default,
                ContainingTypes: EquatableArray.From(new[] { "class", "public partial class Outer" }));

            Assert.Equal(["@class", "public partial class Outer"], model.EmitContainingTypes.ToArray());
        }

        [Fact]
        public void A_null_name_is_no_synthesized_helper_of_any_family()
        {
            Assert.False(GeneratedNames.IsAnySynthesized(null));
            Assert.False(GeneratedNames.IsSynthesized(null));
            Assert.False(GeneratedNames.IsObjectMap(null));
        }
    }
}
