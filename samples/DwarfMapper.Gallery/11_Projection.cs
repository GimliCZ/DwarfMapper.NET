// SPDX-License-Identifier: GPL-2.0-only

// 11 — IQueryable projection (the one place a lambda is actually generated).
// Declare `IQueryable<Dto> Project(IQueryable<Src>)` and the generator emits `src.Select(s => new Dto { ... })`
// — an expression tree your ORM translates to SQL. You never write the lambda; the generator does. Projection
// is deliberately minimal (direct members + renames + [MapIgnore]); a member needing a real conversion is
// DWARF028, by design — do that with a runtime Map method instead.
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex11;

public sealed class Order { public int Id { get; set; } public string Code { get; set; } = ""; }
public sealed class OrderDto { public int Id { get; set; } public string Code { get; set; } = ""; }

[DwarfMapper]
public partial class Mapper
{
    public partial IQueryable<OrderDto> Project(IQueryable<Order> src);
}

public static class Example
{
    public static void Run()
    {
        IQueryable<Order> orders = new List<Order>
        {
            new() { Id = 1, Code = "MITHRIL" },
            new() { Id = 2, Code = "GOLD" },
        }.AsQueryable();

        List<OrderDto> projected = new Mapper().Project(orders).ToList();
        Console.WriteLine($"11 Projection         -> {projected.Count} rows: {string.Join(", ", projected.Select(o => o.Code))}");
    }
}
