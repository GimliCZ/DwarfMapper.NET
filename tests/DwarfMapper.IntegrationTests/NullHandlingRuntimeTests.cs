// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public class NSrc
{
    public int? N { get; set; }
}

public class NDst
{
    public int N { get; set; }
}

[DwarfMapper]
public partial class ThrowMapper
{
    public partial NDst Map(NSrc s);
}

[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
public partial class DefaultMapper
{
    public partial NDst Map(NSrc s);
}

public class NullHandlingRuntimeTests
{
    [Fact]
    public void Throw_strategy_maps_value_and_throws_on_null()
    {
        Assert.Equal(5, new ThrowMapper().Map(new NSrc { N = 5 }).N);
        Assert.Throws<InvalidOperationException>(() => new ThrowMapper().Map(new NSrc { N = null }));
    }

    [Fact]
    public void SetDefault_strategy_uses_zero_on_null()
    {
        Assert.Equal(0, new DefaultMapper().Map(new NSrc { N = null }).N);
        Assert.Equal(7, new DefaultMapper().Map(new NSrc { N = 7 }).N);
    }
}
