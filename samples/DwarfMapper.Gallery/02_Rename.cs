// SPDX-License-Identifier: GPL-2.0-only

// 02 — Renaming a member.
// Where AutoMapper writes `.ForMember(d => d.Name, o => o.MapFrom(s => s.FullName))`, DwarfMapper names both
// members with `nameof` in a [MapProperty] attribute. Use a named `partial` method when you want a specific
// method name (here ToDto). Everything else (Age) auto-matches.
using System;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex02;

public sealed class Customer { public string FullName { get; set; } = ""; public int Age { get; set; } }
public sealed class CustomerDto { public string Name { get; set; } = ""; public int Age { get; set; } }

[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]
    public partial CustomerDto ToDto(Customer c);
}

public static class Example
{
    public static void Run()
    {
        CustomerDto dto = new Mapper().ToDto(new Customer { FullName = "Gimli son of Glóin", Age = 62 });
        Console.WriteLine($"02 Rename             -> {dto.Name}, age {dto.Age}");
    }
}
