// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System.ComponentModel;
using System.Runtime.Serialization;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Enum <-> string honours [EnumMember]/[Description]: an enum can expose a serialization/display name that
// differs from its C# identifier without a hand-written converter. [EnumMember] wins over [Description], which
// wins over the member name. Separate one-way mappers keep each direction's fixture clean.

public enum EsnStatus
{
    [EnumMember(Value = "in_progress")]
    InProgress,

    [Description("done!")]
    Complete,

    Cancelled, // no attribute — falls back to the identifier
}

public sealed class EsnEnumSrc { public EsnStatus Value { get; set; } }
public sealed class EsnStringDst { public string Value { get; set; } = ""; }
public sealed class EsnStringSrc { public string Value { get; set; } = ""; }
public sealed class EsnEnumDst { public EsnStatus Value { get; set; } }

[DwarfMapper]
public partial class EnumSerializedNameMapper
{
    public partial EsnStringDst ToText(EsnEnumSrc s);
    public partial EsnEnumDst FromText(EsnStringSrc s);
}

public class EnumSerializedNameRuntimeTests
{
    [Theory]
    [InlineData(EsnStatus.InProgress, "in_progress")]  // [EnumMember]
    [InlineData(EsnStatus.Complete, "done!")]          // [Description]
    [InlineData(EsnStatus.Cancelled, "Cancelled")]     // fallback to identifier
    public void Enum_to_string_uses_the_serialized_name(EsnStatus input, string expected)
    {
        var dto = new EnumSerializedNameMapper().ToText(new EsnEnumSrc { Value = input });
        Assert.Equal(expected, dto.Value);
    }

    [Theory]
    [InlineData("in_progress", EsnStatus.InProgress)]
    [InlineData("done!", EsnStatus.Complete)]
    [InlineData("Cancelled", EsnStatus.Cancelled)]
    public void String_to_enum_parses_the_serialized_name(string input, EsnStatus expected)
    {
        var dto = new EnumSerializedNameMapper().FromText(new EsnStringSrc { Value = input });
        Assert.Equal(expected, dto.Value);
    }

    [Fact]
    public void The_pair_round_trips_through_the_serialized_name()
    {
        var mapper = new EnumSerializedNameMapper();
        foreach (var s in new[] { EsnStatus.InProgress, EsnStatus.Complete, EsnStatus.Cancelled })
        {
            var text = mapper.ToText(new EsnEnumSrc { Value = s }).Value;
            var back = mapper.FromText(new EsnStringSrc { Value = text }).Value;
            Assert.Equal(s, back);
        }
    }
}
