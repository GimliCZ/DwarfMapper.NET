// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public class HsSrc
{
    public HashSet<int> Tags { get; set; } = new();
}

public class HsDst
{
    public HashSet<long> Tags { get; set; } = new();
}

[DwarfMapper]
public partial class HashSetMapper
{
    public partial HsDst Map(HsSrc s);
}

public class HashSetRuntimeTests
{
    [Fact]
    public void Maps_hashset_with_element_widening()
    {
        var d = new HashSetMapper().Map(new HsSrc { Tags = new HashSet<int> { 1, 2, 3 } });
        Assert.Equal(3, d.Tags.Count);
        Assert.Contains(1L, d.Tags);
        Assert.Contains(3L, d.Tags);
    }
}
