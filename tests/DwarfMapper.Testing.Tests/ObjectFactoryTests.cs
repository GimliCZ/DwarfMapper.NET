// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper.Testing;

namespace DwarfMapper.Testing.Tests;

public class Sample
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? Maybe { get; set; }
    public List<int> Nums { get; set; } = new();
    public Nested Child { get; set; } = new();
}

public class Nested { public string City { get; set; } = ""; }

public class ObjectFactoryTests
{
    [Fact]
    public void Populates_all_members()
    {
        var s = ObjectFactory.Create<Sample>(seed: 1);
        Assert.NotEqual(0, s.Id);
        Assert.NotEqual("", s.Name);
        Assert.NotNull(s.Maybe);
        Assert.NotEmpty(s.Nums);
        Assert.NotEqual("", s.Child.City);
    }

    [Fact]
    public void Same_seed_is_deterministic()
    {
        var a = ObjectFactory.Create<Sample>(seed: 42);
        var b = ObjectFactory.Create<Sample>(seed: 42);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Child.City, b.Child.City);
    }

    [Fact]
    public void Different_seeds_differ()
    {
        var a = ObjectFactory.Create<Sample>(seed: 1);
        var b = ObjectFactory.Create<Sample>(seed: 2);
        Assert.NotEqual(a.Id, b.Id);
    }
}
