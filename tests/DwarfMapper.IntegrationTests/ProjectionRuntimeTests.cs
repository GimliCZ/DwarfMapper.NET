// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class PPerson { public int Age { get; set; } public string Name { get; set; } = ""; }
public class PPersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class ProjMapper
{
    public partial IQueryable<PPersonDto> Project(IQueryable<PPerson> src);
}

public class ProjectionRuntimeTests
{
    [Fact]
    public void Projects_over_queryable()
    {
        var source = new[]
        {
            new PPerson { Age = 30, Name = "Thorin" },
            new PPerson { Age = 40, Name = "Dwalin" },
        }.AsQueryable();

        var dtos = new ProjMapper().Project(source).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(30, dtos[0].Age);
        Assert.Equal("Thorin", dtos[0].Name);
        Assert.Equal("Dwalin", dtos[1].Name);
    }
}
