// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     The cacheability battery, applied to EVERY registered generator. Before this, MapToGenerator had no
///     caching coverage at all: its model could stop being value-equatable, incrementality would silently die,
///     and every test would still pass.
/// </summary>
public class CacheBatteryTests
{
    private const string BothGeneratorsSource = """
                                                using DwarfMapper;
                                                using System.Collections.Generic;
                                                namespace Demo;
                                                public class Addr { public string City { get; set; } = ""; }
                                                public class A { public int X { get; set; } public Addr Address { get; set; } = new(); public List<int> N { get; set; } = new(); }
                                                public class B { public int X { get; set; } public string City { get; set; } = ""; public List<long> N { get; set; } = new(); }
                                                [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                [MapTo(typeof(B2))] public class Src2 { public int X { get; set; } }
                                                public class B2 { public int X { get; set; } }
                                                """;

    public static TheoryData<string> Generators()
    {
        var data = new TheoryData<string>();
        foreach (var g in GeneratorRegistry.All) data.Add(g.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void Battery_passes_for_every_registered_generator(string generatorName)
    {
        var g = GeneratorRegistry.All.Single(x => x.Name == generatorName);
        GeneratorCacheAssert.Battery(g, BothGeneratorsSource);
    }
}
