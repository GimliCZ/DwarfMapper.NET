// SPDX-License-Identifier: GPL-2.0-only

// 13 — Configuring a NESTED / collection-element mapping, all inside the one mapper class.
// A Place (Moria) holds a List<Person>; we map it to a PlaceDto holding a List<PersonDto>, and we want each
// Person.Name to become PersonDto.FullName. That rename does NOT belong on the top-level Place -> PlaceDto
// map — it belongs to the nested Person -> PersonDto pair. So declare that pair's method and hang the
// [MapProperty] on it: the parent's List<Person> -> List<PersonDto> automatically routes every element
// through it. There is no separate Profile/config object — every rule, including for nested element types,
// lives on this single [DwarfMapper] class.

namespace DwarfMapper.Gallery.Ex13;

public sealed class Person
{
    public string Name { get; set; } = "";
}

public sealed class Place
{
    public string Name { get; set; } = "";
    public List<Person> People { get; set; } = new();
}

public sealed class PersonDto
{
    public string FullName { get; set; } = "";
}

public sealed class PlaceDto
{
    public string Name { get; set; } = "";
    public List<PersonDto> People { get; set; } = new();
}

[DwarfMapper]
public partial class Mapper
{
    // Top-level map. People (List<Person> -> List<PersonDto>) is routed through the nested method below.
    public partial PlaceDto ToDto(Place place);

    // The nested element mapping, configured right here: Person.Name -> PersonDto.FullName.
    [MapProperty(nameof(Person.Name), nameof(PersonDto.FullName))]
    public partial PersonDto ToDto(Person person);
}

public static class Example
{
    public static void Run()
    {
        var moria = new Place
        {
            Name = "Moria",
            People = new List<Person> { new() { Name = "Gimli" }, new() { Name = "Balin" } }
        };

        var dto = new Mapper().ToDto(moria);

        // dto.People[0].FullName == "Gimli" — the nested rename was applied to every list element.
        Console.WriteLine(
            $"13 Nested list config -> {dto.Name}: [{string.Join(", ", dto.People.Select(p => p.FullName))}]");
    }
}
