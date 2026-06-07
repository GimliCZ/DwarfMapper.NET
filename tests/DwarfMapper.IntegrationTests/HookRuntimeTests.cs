// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class HSrc { public int Age { get; set; } }
public class HDst { public int Age { get; set; } public int Doubled { get; set; } }

[DwarfMapper]
public partial class HookMapper
{
    [MapIgnore("Doubled")]
    public partial HDst Map(HSrc s);

    [AfterMap]
    private static void Compute(HSrc s, HDst d) => d.Doubled = s.Age * 2;
}

public class HookRuntimeTests
{
    [Fact]
    public void AfterMap_post_processes_target()
    {
        var d = new HookMapper().Map(new HSrc { Age = 21 });
        Assert.Equal(21, d.Age);
        Assert.Equal(42, d.Doubled);  // set by the AfterMap hook
    }
}
