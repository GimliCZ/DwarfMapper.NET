// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.UntranslatableModifierReason, extracted from ResolveProjectionMembers' inline refusal of
// [MapProperty] modifiers (per-branch rule: a branch no compilation reaches is extracted and tested directly).
// ReadMapPropertyExtras records only a target that carries NullSubstitute or When, so the "recorded, but with neither"
// answer never arrives through a mapper; it is pinned here with the other three.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class UntranslatableModifierReasonUnitTests
    {
        private static readonly Dictionary<string, (bool HasNullSub, string? When)> Extras = new(StringComparer.Ordinal)
        {
            ["Sub"] = (true, null),
            ["Cond"] = (false, "IsReady"),
            ["Both"] = (true, "IsReady"),
            ["Neither"] = (false, null),
        };

        [Fact]
        public void A_target_with_no_recorded_modifier_has_no_reason()
        {
            Assert.Null(MapperExtractor.UntranslatableModifierReason(Extras, "Other"));
        }

        [Fact]
        public void A_recorded_entry_with_neither_modifier_has_no_reason()
        {
            Assert.Null(MapperExtractor.UntranslatableModifierReason(Extras, "Neither"));
        }

        [Theory]
        [InlineData("Sub", "NullSubstitute is not translatable in projection")]
        [InlineData("Cond", "When= is not translatable in projection")]
        [InlineData("Both", "NullSubstitute is not translatable in projection")]
        public void A_modifier_is_refused_with_its_own_reason_NullSubstitute_first(string target, string expectedStart)
        {
            Assert.StartsWith(expectedStart, MapperExtractor.UntranslatableModifierReason(Extras, target), StringComparison.Ordinal);
        }
    }
}
