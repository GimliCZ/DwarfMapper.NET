// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

/// <summary>
/// Phase-1 runtime tests for the ambient cross-assembly registry + <see cref="IDwarfMapper"/> facade.
/// The registry is process-wide static, so each test uses its OWN distinct source/destination types
/// (the key is the Type pair) to stay isolated without resetting global state.
/// </summary>
public sealed class AmbientRegistryTests
{
    // --- per-test type families (distinct so registrations never collide across tests) ---
    private sealed class S1 { public int V { get; set; } }
    private sealed class D1 { public int V { get; set; } }

    private sealed class S2 { public int V { get; set; } }
    private sealed class D2 { public int V { get; set; } }

    private class Base3 { public int V { get; set; } }
    private sealed class Derived3 : Base3 { }
    private sealed class D3 { public int V { get; set; } }

    private sealed class S4 { public int V { get; set; } }
    private sealed class D4a { public int V { get; set; } }
    private sealed class D4b { public int V { get; set; } }

    [Fact]
    public void Register_then_Map_resolves_by_runtime_type()
    {
        DwarfMapperRegistry.Register(typeof(S1), typeof(D1), s => new D1 { V = ((S1)s).V + 1 });

        var result = DwarfMapperRegistry.Map(new S1 { V = 41 }, typeof(D1));

        Assert.IsType<D1>(result);
        Assert.Equal(42, ((D1)result).V);
        Assert.True(DwarfMapperRegistry.IsProvided(typeof(S1), typeof(D1)));
        Assert.Contains((typeof(S1), typeof(D1)), DwarfMapperRegistry.Provided);
    }

    [Fact]
    public void Facade_Map_generic_destination_resolves()
    {
        DwarfMapperRegistry.Register(typeof(S2), typeof(D2), s => new D2 { V = ((S2)s).V * 2 });

        IDwarfMapper mapper = DwarfMapperFacade.Instance;
        var byDest = mapper.Map<D2>(new S2 { V = 5 });
        var byBoth = mapper.Map<S2, D2>(new S2 { V = 6 });

        Assert.Equal(10, byDest.V);
        Assert.Equal(12, byBoth.V);
    }

    [Fact]
    public void Map_resolves_derived_instance_via_base_registration()
    {
        DwarfMapperRegistry.Register(typeof(Base3), typeof(D3), s => new D3 { V = ((Base3)s).V });

        // A Derived3 instance has no exact registration; the base-type walk must find Base3 -> D3.
        var result = DwarfMapperRegistry.Map(new Derived3 { V = 7 }, typeof(D3));

        Assert.Equal(7, ((D3)result).V);
    }

    [Fact]
    public void Map_missing_throws_DwarfMapMissingException_with_types()
    {
        // Built-in types that are never registered → exercises the missing path without marker types.
        var ex = Assert.Throws<DwarfMapMissingException>(
            () => DwarfMapperRegistry.Map(Guid.NewGuid(), typeof(Uri)));

        Assert.Equal(typeof(Guid), ex.SourceType);
        Assert.Equal(typeof(Uri), ex.DestinationType);
        Assert.Contains("GenerateMap", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_distinct_provider_is_flagged_ambiguous_first_wins()
    {
        DwarfMapperRegistry.Register(typeof(S4), typeof(D4a), s => new D4a { V = ((S4)s).V });
        Assert.False(DwarfMapperRegistry.IsAmbiguous(typeof(S4), typeof(D4a)));

        DwarfMapperRegistry.Register(typeof(S4), typeof(D4a), _ => new D4a { V = -1 }); // second provider
        Assert.True(DwarfMapperRegistry.IsAmbiguous(typeof(S4), typeof(D4a)));

        // First registration wins.
        var result = (D4a)DwarfMapperRegistry.Map(new S4 { V = 9 }, typeof(D4a));
        Assert.Equal(9, result.V);

        // A different destination from the same source is NOT ambiguous.
        DwarfMapperRegistry.Register(typeof(S4), typeof(D4b), s => new D4b { V = ((S4)s).V });
        Assert.False(DwarfMapperRegistry.IsAmbiguous(typeof(S4), typeof(D4b)));
    }

    [Fact]
    public void TryGet_returns_false_when_absent_true_when_present()
    {
        Assert.False(DwarfMapperRegistry.TryGet(typeof(string), typeof(Uri), out _));

        DwarfMapperRegistry.Register(typeof(S1), typeof(D2), s => new D2 { V = ((S1)s).V });
        Assert.True(DwarfMapperRegistry.TryGet(typeof(S1), typeof(D2), out var map));
        Assert.NotNull(map);
    }

    [Fact]
    public void Register_null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => DwarfMapperRegistry.Register(null!, typeof(D1), _ => new D1()));
        Assert.Throws<ArgumentNullException>(() => DwarfMapperRegistry.Register(typeof(S1), null!, _ => new D1()));
        Assert.Throws<ArgumentNullException>(() => DwarfMapperRegistry.Register(typeof(S1), typeof(D1), null!));
    }
}
