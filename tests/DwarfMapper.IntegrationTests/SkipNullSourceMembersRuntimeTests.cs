// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

public sealed class SnsSrc
{
    public string? Name { get; set; }
    public string? Bio { get; set; }
    public int? Count { get; set; }
}

public sealed class SnsDst
{
    public string? Name { get; set; } = "keep-name";
    public string? Bio { get; set; } = "keep-bio";
    public int Count { get; set; } = 99;
}

// SkipNullSourceMembers: a null source member keeps the destination default.
[DwarfMapper(SkipNullSourceMembers = true)]
[GenerateMap<SnsSrc, SnsDst>]
public partial class SnsMapper
{
}

// Control: the default behaviour overwrites with null/default.
[DwarfMapper]
[GenerateMap<SnsSrc, SnsDst>]
public partial class SnsControlMapper
{
}

public sealed class SkipNullSourceMembersRuntimeTests
{
    [Fact]
    public void Null_source_members_keep_destination_defaults_when_enabled()
    {
        var dst = new SnsMapper().Map(new SnsSrc { Name = "new", Bio = null, Count = null });
        Assert.Equal("new", dst.Name);     // non-null source still maps
        Assert.Equal("keep-bio", dst.Bio); // null source preserved the default
        Assert.Equal(99, dst.Count);       // null int? preserved the default
    }

    [Fact]
    public void Non_null_source_members_still_map_when_enabled()
    {
        var dst = new SnsMapper().Map(new SnsSrc { Name = "a", Bio = "b", Count = 5 });
        Assert.Equal("a", dst.Name);
        Assert.Equal("b", dst.Bio);
        Assert.Equal(5, dst.Count);
    }

    [Fact]
    public void Default_behaviour_overwrites_with_null()
    {
        var dst = new SnsControlMapper().Map(new SnsSrc { Name = "new", Bio = null, Count = 5 });
        Assert.Equal("new", dst.Name);
        Assert.Null(dst.Bio);   // overwritten with null (default behaviour)
        Assert.Equal(5, dst.Count);
    }
}
