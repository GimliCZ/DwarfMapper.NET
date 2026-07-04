// SPDX-License-Identifier: GPL-2.0-only

// 14 — The SAME nested-list scenario as 13, but with ZERO partial methods.
// The thing people dislike about 13 is the empty `partial` method that exists only to hang an attribute on —
// a "data holder". Pair-scoped, class-level attributes remove it entirely:
//   * Place -> PlaceDto is one [GenerateMap<>] line.
//   * the nested rename Person.Name -> PersonDto.FullName is [MapProperty<Person, PersonDto>(...)] on the class.
// There are NO methods at all. The pair-scoped linkage applies wherever Person -> PersonDto is mapped — here it
// is an auto-synthesized List<Person> element, and the rename still applies. Call it through the generated
// extension method, so there is no `new Mapper()` either.
// (An attribute that matches no mapped pair is DWARF056 — a misconfigured linkage is never a silent no-op.)
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;
using DwarfMapper.Extensions;   // surfaces the generated moria.ToPlaceDto() extension

namespace DwarfMapper.Gallery.Ex14;

public sealed class Person { public string Name { get; set; } = ""; }
public sealed class Place { public string Name { get; set; } = ""; public List<Person> People { get; set; } = new(); }

public sealed class PersonDto { public string FullName { get; set; } = ""; }
public sealed class PlaceDto { public string Name { get; set; } = ""; public List<PersonDto> People { get; set; } = new(); }

[DwarfMapper]
[GenerateMap<Place, PlaceDto>]
[MapProperty<Person, PersonDto>(nameof(Person.Name), nameof(PersonDto.FullName))]
public partial class Mapper { }   // no methods — the pair-scoped attribute carries the nested rename

public static class Example
{
    public static void Run()
    {
        Place moria = new Place
        {
            Name = "Moria",
            People = new List<Person> { new Person { Name = "Gimli" }, new Person { Name = "Balin" } },
        };

        // No `new Mapper()`, no methods on the mapper at all — just the generated extension:
        PlaceDto dto = moria.ToPlaceDto();

        Console.WriteLine($"14 Nested (ergonomic) -> {dto.Name}: [{string.Join(", ", dto.People.Select(p => p.FullName))}]");
    }
}
