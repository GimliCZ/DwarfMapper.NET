// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── AutoNest: 2-level nested class→record ─────────────────────────────────
    [Fact]
    public Task Snap_AutoNest_TwoLevel()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Addr   { public string City { get; set; } = ""; public int Zip { get; set; } }
                           public class Person { public Addr Home { get; set; } = new(); public string Name { get; set; } = ""; }
                           public record AddrDto(string City, int Zip);
                           public record PersonDto(AddrDto Home, string Name);
                           [DwarfMapper]
                           public partial class M { public partial PersonDto Map(Person p); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── AutoNest: 3-level graph ───────────────────────────────────────────────
    [Fact]
    public Task Snap_AutoNest_ThreeLevel()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class City   { public string Name { get; set; } = ""; }
                           public class Addr   { public City Location { get; set; } = new(); public int Zip { get; set; } }
                           public class Person { public Addr Home { get; set; } = new(); public string FullName { get; set; } = ""; }
                           public record CityDto(string Name);
                           public record AddrDto(CityDto Location, int Zip);
                           public record PersonDto(AddrDto Home, string FullName);
                           [DwarfMapper]
                           public partial class M { public partial PersonDto Map(Person p); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── AutoNest: conversion at depth (long→int, string→Guid) ─────────────────
    [Fact]
    public Task Snap_AutoNest_ConversionAtDepth()
    {
        const string src = """
                           using DwarfMapper;
                           using System;
                           namespace Demo;
                           public class Inner { public long Count { get; set; } public string Id { get; set; } = ""; }
                           public class Outer { public Inner Sub { get; set; } = new(); }
                           public record InnerDto(int Count, Guid Id);
                           public record OuterDto(InnerDto Sub);
                           [DwarfMapper]
                           public partial class M { public partial OuterDto Map(Outer o); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }
}
