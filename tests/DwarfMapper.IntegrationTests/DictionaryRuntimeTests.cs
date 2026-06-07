// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class DSrc { public Dictionary<string, int> Scores { get; set; } = new(); }
public class DDst { public Dictionary<string, long> Scores { get; set; } = new(); }

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
        var d = new DictMapper().Map(new DSrc { Scores = new() { ["a"] = 1, ["b"] = 2 } });
        Assert.Equal(2, d.Scores.Count);
        Assert.Equal(1L, d.Scores["a"]);
        Assert.Equal(2L, d.Scores["b"]);
    }
}
