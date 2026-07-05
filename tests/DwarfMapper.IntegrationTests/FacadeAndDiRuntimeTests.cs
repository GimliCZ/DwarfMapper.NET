// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.IntegrationTests;

public sealed class FacadeOrder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class FacadeOrderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class FacadeOrderMapper
{
    public partial FacadeOrderDto ToDto(FacadeOrder o);
}

/// <summary>
///     Runtime tests for the generated convenience facade (<c>order.ToFacadeOrderDto()</c>) and the
///     <c>IServiceCollection.AddDwarfMappers()</c> registration extension.
/// </summary>
public sealed class FacadeAndDiRuntimeTests
{
    [Fact]
    public void Extension_method_maps_identically_to_the_instance_method()
    {
        var order = new FacadeOrder { Id = 7, Name = "vein" };

        var viaExtension = order.ToFacadeOrderDto();
        var viaInstance = new FacadeOrderMapper().ToDto(order);

        Assert.Equal(7, viaExtension.Id);
        Assert.Equal("vein", viaExtension.Name);
        Assert.Equal(viaInstance.Id, viaExtension.Id);
        Assert.Equal(viaInstance.Name, viaExtension.Name);
    }

    [Fact]
    public void AddDwarfMappers_registers_mappers_as_resolvable_singletons()
    {
        using var provider = new ServiceCollection().AddDwarfMappers().BuildServiceProvider();

        var mapper = provider.GetRequiredService<FacadeOrderMapper>();
        Assert.NotNull(mapper);
        Assert.Same(mapper, provider.GetRequiredService<FacadeOrderMapper>()); // singleton lifetime

        var dto = mapper.ToDto(new FacadeOrder { Id = 3, Name = "ore" });
        Assert.Equal(3, dto.Id);
        Assert.Equal("ore", dto.Name);
    }
}
