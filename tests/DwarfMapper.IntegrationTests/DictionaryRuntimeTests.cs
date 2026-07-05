// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public class DSrc
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

public class DDst
{
    public Dictionary<string, long> Scores { get; set; } = new();
}

[DwarfMapper]
public partial class DictMapper
{
    public partial DDst Map(DSrc s);
}

public class DictionaryRuntimeTests
{
    [Fact]
    public void Maps_dictionary_with_value_widening()
    {
        var d = new DictMapper().Map(new DSrc { Scores = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 } });
        Assert.Equal(2, d.Scores.Count);
        Assert.Equal(1L, d.Scores["a"]);
        Assert.Equal(2L, d.Scores["b"]);
    }

    [Fact]
    public void Null_dictionary_source_maps_to_empty_not_throw()
    {
        var d = new DictMapper().Map(new DSrc { Scores = null! });
        Assert.NotNull(d.Scores);
        Assert.Empty(d.Scores);
    }
}
