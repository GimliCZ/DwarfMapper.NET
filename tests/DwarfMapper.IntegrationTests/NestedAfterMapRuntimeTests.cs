// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class NafInnerSrc { public int Val { get; set; } }
public class NafInnerDst { public int Val { get; set; } public int Computed { get; set; } }

public class NafOuterSrc { public NafInnerSrc Child { get; set; } = new(); }
public class NafOuterDst { public NafInnerDst Child { get; set; } = new(); }

// Attribute-declared pairs (as document/DTO mappers typically do): the generator emits a public Map AND a
// private nested helper for the inner pair. The [AfterMap] must run in BOTH, otherwise a target produced as
// a nested member silently loses the post-processing.
[DwarfMapper]
[GenerateMap<NafInnerSrc, NafInnerDst>]
[MapIgnore<NafInnerDst>(nameof(NafInnerDst.Computed))]
[GenerateMap<NafOuterSrc, NafOuterDst>]
public partial class NestedAfterMapMapper
{
    [AfterMap]
    private static void Build(NafInnerSrc s, NafInnerDst d) => d.Computed = s.Val * 10;
}

public class NestedAfterMapRuntimeTests
{
    [Fact]
    public void AfterMap_runs_for_top_level_map()
    {
        var d = new NestedAfterMapMapper().Map(new NafInnerSrc { Val = 5 });
        Assert.Equal(5, d.Val);
        Assert.Equal(50, d.Computed); // AfterMap ran
    }

    [Fact]
    public void AfterMap_runs_when_map_is_invoked_as_a_nested_member()
    {
        var d = new NestedAfterMapMapper().Map(new NafOuterSrc { Child = new NafInnerSrc { Val = 5 } });
        Assert.Equal(5, d.Child.Val);
        Assert.Equal(50, d.Child.Computed); // AfterMap must run for the nested Child map too
    }
}
