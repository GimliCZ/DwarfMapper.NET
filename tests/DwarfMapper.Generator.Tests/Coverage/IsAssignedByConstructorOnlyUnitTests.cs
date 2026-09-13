// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.IsAssignedByConstructorOnly, extracted from ResolveExplicitMaps' and
// ResolveAutoMatchedMembers' identical inline tests (per-branch rule: extract and test directly). Every caller passes
// the consumed-parameter set and the required-member set together, both built or both null, so "consumed parameters
// with no required-member set" never arrived through a mapper. It is pinned here with the answers a mapper does produce.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class IsAssignedByConstructorOnlyUnitTests
    {
        private static HashSet<string> Set(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void A_consumed_parameter_is_assigned_by_the_constructor_alone()
        {
            Assert.True(MapperExtractor.IsAssignedByConstructorOnly(Set("A", "R"), Set("R"), "A"));
        }

        [Fact]
        public void A_required_member_the_constructor_does_not_mark_set_still_needs_the_initializer()
        {
            Assert.False(MapperExtractor.IsAssignedByConstructorOnly(Set("A", "R"), Set("R"), "R"));
        }

        [Fact]
        public void A_member_no_constructor_parameter_consumed_is_not()
        {
            Assert.False(MapperExtractor.IsAssignedByConstructorOnly(Set("A"), Set(), "X"));
            Assert.False(MapperExtractor.IsAssignedByConstructorOnly(null, null, "A"));
        }

        [Fact]
        public void Without_a_required_member_set_a_consumed_parameter_is_the_constructors()
        {
            Assert.True(MapperExtractor.IsAssignedByConstructorOnly(Set("A"), null, "A"));
        }
    }
}
