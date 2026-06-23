// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper.Registry;
using Xunit;

// EXPERIMENTAL (v23). Full-scale conversion coverage for the [MapTo] registry: numeric narrowing,
// string parse/format, enum, nested objects, and List/array collections (incl. collections of nested).
// Not under DwarfMapper.* (see the shadowing note in the prototype test file).
namespace RegistryProto.Conv;

public enum Color { Red, Green, Blue }
public enum Hue { Red, Green, Blue }

public class Address { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
public class AddressDto { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }

public class PersonDto
{
    public long Id { get; set; }            // ← int   (implicit widening)
    public short Small { get; set; }        // ← int   (checked narrowing)
    public int Age { get; set; }            // ← string (parse)
    public string Score { get; set; } = ""; // ← double (format)
    public Hue Favorite { get; set; }       // ← Color  (enum by name)
    public AddressDto Home { get; set; } = new();           // nested object
    public List<long> Marks { get; set; } = new();          // ← List<int>   (element widen)
    public List<AddressDto> Contacts { get; set; } = new(); // ← List<Address> (element nested)
}

[MapTo(typeof(PersonDto))]
public class Person
{
    public int Id { get; set; }
    public int Small { get; set; }
    public string Age { get; set; } = "";
    public double Score { get; set; }
    public Color Favorite { get; set; }
    public Address Home { get; set; } = new();
    public List<int> Marks { get; set; } = new();
    public List<Address> Contacts { get; set; } = new();
}

public class RegistryMapToConversionsRuntimeTests
{
    [Fact]
    public void Maps_scalars_nested_and_collections()
    {
        var p = new Person
        {
            Id = 7,
            Small = 300,
            Age = "42",
            Score = 3.5,
            Favorite = Color.Green,
            Home = new Address { City = "Prague", Zip = "11000" },
            Marks = new List<int> { 1, 2, 3 },
            Contacts = new List<Address>
            {
                new() { City = "Brno", Zip = "60200" },
                new() { City = "Ostrava", Zip = "70030" },
            },
        };

        PersonDto dto = p.MapTo<PersonDto>();

        Assert.Equal(7L, dto.Id);                       // implicit widen
        Assert.Equal((short)300, dto.Small);            // checked narrow
        Assert.Equal(42, dto.Age);                      // parse
        Assert.Equal("3.5", dto.Score);                 // format (invariant)
        Assert.Equal(Hue.Green, dto.Favorite);          // enum by name
        Assert.Equal("Prague", dto.Home.City);          // nested
        Assert.Equal("11000", dto.Home.Zip);
        Assert.Equal(new List<long> { 1, 2, 3 }, dto.Marks);            // collection element widen
        Assert.Equal(2, dto.Contacts.Count);                           // collection of nested
        Assert.Equal("Brno", dto.Contacts[0].City);
        Assert.Equal("Ostrava", dto.Contacts[1].City);
    }

    [Fact]
    public void Checked_narrowing_throws_on_overflow()
    {
        var p = new Person { Age = "0", Small = 70000 /* > short.MaxValue */ };
        Assert.Throws<System.OverflowException>(() => p.MapTo<PersonDto>());
    }
}
