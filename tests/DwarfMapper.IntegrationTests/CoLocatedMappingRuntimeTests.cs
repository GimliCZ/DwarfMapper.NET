// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.IntegrationTests;

public sealed class ClPerson
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

// Co-located mapping with NO partial and NO [DwarfMapper] — just declarative attributes on a plain data class.
// The generator emits a separate ClPersonDtoMapper; this stays an ordinary sealed DTO.
[GenerateMap<ClPerson, ClPersonDto>]
[MapProperty<ClPerson, ClPersonDto>(nameof(ClPerson.Name), nameof(FullName))]
public sealed class ClPersonDto
{
    public string FullName { get; set; } = "";
    public int Age { get; set; }
}

public sealed class CoLocatedMappingRuntimeTests
{
    [Fact]
    public void Plain_dto_with_colocated_mapping_maps_via_the_generated_extension()
    {
        var person = new ClPerson { Name = "John Doe", Age = 100 };

        var dto = person.ToClPersonDto();

        Assert.Equal("John Doe", dto.FullName);
        Assert.Equal(100, dto.Age);
    }

    [Fact]
    public void The_separate_generated_mapper_is_registered_in_DI()
    {
        using var provider = new ServiceCollection().AddDwarfMappers().BuildServiceProvider();

        // The generated mapper type is <Host>Mapper, held by DI as a singleton.
        var mapper = provider.GetRequiredService<ClPersonDtoMapper>();
        Assert.NotNull(mapper);
        Assert.Same(mapper, provider.GetRequiredService<ClPersonDtoMapper>());
    }
}
