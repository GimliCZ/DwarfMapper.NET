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
    public void Assembly_carries_the_ProvidesMap_manifest()
    {
        var provides = typeof(AmbientRegistrationRuntimeTests).Assembly
            .GetCustomAttributes(typeof(DwarfProvidesMapAttribute), false)
            .Cast<DwarfProvidesMapAttribute>();

        Assert.Contains(provides, p => p.Source == typeof(AmbientSrc) && p.Destination == typeof(AmbientDst));
    }
}
