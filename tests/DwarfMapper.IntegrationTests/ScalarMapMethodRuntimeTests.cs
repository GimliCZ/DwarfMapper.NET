// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public enum SmmLevel
    {
        Low,
        Mid,
        High
    }

    public enum SmmLevelDto
    {
        // Declared in another order on purpose: a map by NAME and a cast disagree on every member but Mid.
        High,
        Mid,
        Low
    }

    [DwarfMapper]
    public partial class SmmMapper
    {
        public partial SmmLevelDto ToDto(SmmLevel level);

        public partial SmmLevel Parse(string name);

        public partial string Name(SmmLevel level);

        public partial int Number(SmmLevel level);

        public partial int Narrow(long value);

        public partial int ParseNumber(string text);
    }

    [DwarfMapper]
    [GenerateMap<SmmLevel, SmmLevelDto>]
    public partial class SmmAttributeMapper
    {
    }

    /// <summary>
    ///     <b>A declared mapping method between scalars converts its value</b> (round 35, T16). Every method of
    ///     <see cref="SmmMapper" /> used to be generated as <c>return new T { };</c> - the default, whatever it was
    ///     given - except <c>Name</c>, which did not build (DWARF024 for the parameters of <c>string</c>'s
    ///     constructors). The attribute form of the first one converted all along; it is the control.
    /// </summary>
    public class ScalarMapMethodRuntimeTests
    {
        [Theory]
        [InlineData(SmmLevel.Low, SmmLevelDto.Low)]
        [InlineData(SmmLevel.Mid, SmmLevelDto.Mid)]
        [InlineData(SmmLevel.High, SmmLevelDto.High)]
        public void An_enum_is_mapped_to_an_enum_by_name_as_the_attribute_form_maps_it(SmmLevel level, SmmLevelDto expected)
        {
            Assert.Equal(expected, new SmmMapper().ToDto(level));
            Assert.Equal(expected, new SmmAttributeMapper().Map(level));
        }

        [Fact]
        public void The_other_scalar_pairs_convert_their_value()
        {
            var mapper = new SmmMapper();

            Assert.Equal(SmmLevel.High, mapper.Parse("High"));
            Assert.Equal("Mid", mapper.Name(SmmLevel.Mid));
            Assert.Equal(2, mapper.Number(SmmLevel.High));
            Assert.Equal(41, mapper.Narrow(41L));
            Assert.Equal(1234, mapper.ParseNumber("1234"));
        }

        // The conversions are the checked ones a member of the same pair gets: nothing is truncated or guessed.
        [Fact]
        public void A_value_the_destination_cannot_hold_throws_as_it_does_for_a_member()
        {
            var mapper = new SmmMapper();

            Assert.Throws<OverflowException>(() => mapper.Narrow(long.MaxValue));
            Assert.Throws<FormatException>(() => mapper.ParseNumber("twelve"));
            Assert.ThrowsAny<Exception>(() => mapper.Parse("Nowhere"));
        }
    }
}
