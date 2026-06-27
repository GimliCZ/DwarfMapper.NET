// SPDX-License-Identifier: GPL-2.0-only

// 01 — The simplest possible map.
// Same member names, same types. Declare the pair with one attribute line ([GenerateMap]); the generator
// emits `PersonDto Map(Person)`. No lambdas, no config, no per-DTO attributes.
using System;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex01;

public sealed class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
public sealed class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
[GenerateMap<Person, PersonDto>]
public partial class Mapper { }

public static class Example
{
    public static void Run()
    {
        PersonDto dto = new Mapper().Map(new Person { Age = 41, Name = "Durin" });
        Console.WriteLine($"01 Flat map           -> {dto.Name}, age {dto.Age}");
    }
}
