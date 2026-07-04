// SPDX-License-Identifier: GPL-2.0-only

// 10 — Mapping into an immutable target (positional record).
// No parameterless constructor needed: DwarfMapper binds source members to constructor parameters by name and
// emits a named-argument `new PersonDto(Id: ..., Name: ...)`. Every ctor parameter is mandatory (a missing one
// is the build error DWARF024), so you can't silently leave an immutable field unset.
using System;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex10;

public sealed class Person { public int Id { get; set; } public string Name { get; set; } = ""; }
public record PersonDto(int Id, string Name);   // immutable, no parameterless ctor

[DwarfMapper]
public partial class Mapper
{
    public partial PersonDto ToDto(Person p);   // emits new PersonDto(Id: p.Id, Name: p.Name)
}

public static class Example
{
    public static void Run()
    {
        PersonDto dto = new Mapper().ToDto(new Person { Id = 3, Name = "Fíli" });
        Console.WriteLine($"10 Record/immutable   -> {dto} (immutable)");
    }
}
