// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// A real public mapper: the generator emits a [ModuleInitializer] that self-registers AmbientSrc -> AmbientDst
// into the ambient DwarfMapperRegistry when this test assembly loads, plus a [assembly: DwarfProvidesMap]
// manifest entry. These tests prove that end-to-end (Phase 2 / 3 of the ambient registry).
public sealed class AmbientSrc
{
    public int V { get; set; }
}

public sealed class AmbientDst
{
    public int V { get; set; }
}

[DwarfMapper]
[GenerateMap<AmbientSrc, AmbientDst>]
public partial class AmbientSampleMapper
{
    // A collection-conversion map (List<Src> -> IReadOnlyList<Dst>) — uses the library's internal collection
    // handling. The ambient registry registers it just like the plain map, so it resolves cross-assembly too.
    public partial System.Collections.Generic.IReadOnlyList<AmbientDst> Map(System.Collections.Generic.List<AmbientSrc> source);

    // An async-stream map — IAsyncEnumerable boxes to object, so it is ambient-registerable as well.
    public partial System.Collections.Generic.IAsyncEnumerable<AmbientDst> Map(System.Collections.Generic.IAsyncEnumerable<AmbientSrc> source);
}

public sealed class AmbientRegistrationRuntimeTests
{
    [Fact]
    public void Generated_module_initializer_self_registers_the_map()
    {
        // The module initializer ran at assembly load, before this test.
        Assert.True(DwarfMapperRegistry.IsProvided(typeof(AmbientSrc), typeof(AmbientDst)));
    }

    [Fact]
    public void Map_resolves_through_the_ambient_facade()
    {
        var dst = DwarfMapperFacade.Instance.Map<AmbientDst>(new AmbientSrc { V = 5 });
        Assert.Equal(5, dst.V);

        // ...and the direct concrete mapper still works (in-assembly path is unchanged).
        var direct = new AmbientSampleMapper().Map(new AmbientSrc { V = 9 });
        Assert.Equal(9, direct.V);
    }

    [Fact]
    public void Collection_conversion_map_resolves_through_the_ambient_facade()
    {
        // Broadened ambient registry: a List<Src> -> IReadOnlyList<Dst> map self-registers and resolves via
        // the facade, using the library's internal collection handling.
        Assert.True(DwarfMapperRegistry.IsProvided(typeof(System.Collections.Generic.List<AmbientSrc>),
            typeof(System.Collections.Generic.IReadOnlyList<AmbientDst>)));

        var result = DwarfMapperFacade.Instance.Map<System.Collections.Generic.IReadOnlyList<AmbientDst>>(
            new System.Collections.Generic.List<AmbientSrc> { new() { V = 1 }, new() { V = 2 } });

        Assert.Equal(new[] { 1, 2 }, result.Select(x => x.V));
    }

    [Fact]
    public void Async_stream_map_is_ambient_registered()
    {
        Assert.True(DwarfMapperRegistry.IsProvided(
            typeof(System.Collections.Generic.IAsyncEnumerable<AmbientSrc>),
            typeof(System.Collections.Generic.IAsyncEnumerable<AmbientDst>)));
    }

    [Fact]
    public void Assembly_carries_the_ProvidesMap_manifest()
    {
        var provides = typeof(AmbientRegistrationRuntimeTests).Assembly
            .GetCustomAttributes(typeof(DwarfProvidesMapAttribute), false)
            .Cast<DwarfProvidesMapAttribute>();

        Assert.Contains(provides, p => p.Source == typeof(AmbientSrc) && p.Destination == typeof(AmbientDst));
    }
}
