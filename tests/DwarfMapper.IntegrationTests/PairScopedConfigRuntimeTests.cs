// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public sealed class PsPerson { public string Name { get; set; } = ""; }
public sealed class PsPersonDto { public string FullName { get; set; } = ""; }
public sealed class PsPlace { public string Name { get; set; } = ""; public List<PsPerson> People { get; set; } = new(); }
public sealed class PsPlaceDto { public string Name { get; set; } = ""; public List<PsPersonDto> People { get; set; } = new(); }

// A fully attribute-only mapper: no partial methods. The Place -> PlaceDto pair is declared with [GenerateMap],
// and the nested Person.Name -> PersonDto.FullName rename is configured with the pair-scoped [MapProperty<,>].
[DwarfMapper]
[GenerateMap<PsPlace, PsPlaceDto>]
[MapProperty<PsPerson, PsPersonDto>(nameof(PsPerson.Name), nameof(PsPersonDto.FullName))]
public partial class PsMapper { }

public sealed class PairScopedConfigRuntimeTests
{
    [Fact]
    public void Nested_list_rename_is_applied_without_any_partial_method()
    {
        var moria = new PsPlace
        {
            Name = "Moria",
            People = new() { new PsPerson { Name = "Gimli" }, new PsPerson { Name = "Balin" } },
        };

        PsPlaceDto dto = new PsMapper().Map(moria);

        Assert.Equal("Moria", dto.Name);
        Assert.Equal(new[] { "Gimli", "Balin" }, dto.People.Select(p => p.FullName).ToArray());
    }
}
