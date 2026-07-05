// SPDX-License-Identifier: GPL-2.0-only

// 07 — Flattening a nested object up to top-level members.
// `[Flatten("Address")]` pulls Address.City -> City and Address.Zip -> Zip onto the destination, by name.
// (DwarfMapper makes flattening *explicit* — there's no implicit name-splitting convention, which avoids
// silent mislinking.) The flattened leaves go through the normal conversion rules.

namespace DwarfMapper.Gallery.Ex07;

public sealed class Address
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public sealed class Person
{
    public string Name { get; set; } = "";
    public Address Address { get; set; } = new();
}

public sealed class PersonDto
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

[DwarfMapper]
public partial class Mapper
{
    [Flatten(nameof(Person.Address))] // Address.City -> City, Address.Zip -> Zip
    public partial PersonDto ToDto(Person p);
}

public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Person
            { Name = "Óin", Address = new Address { City = "Ered Luin", Zip = "0001" } });
        Console.WriteLine($"07 Flatten            -> {dto.Name} @ {dto.City} {dto.Zip}");
    }
}
