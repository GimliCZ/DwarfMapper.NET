// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public sealed class Envelope<T>
{
    public T Payload { get; set; } = default!;
    public int Status { get; set; }
    public string CorrelationId { get; set; } = "";
}

public sealed class WmUser { public int Id { get; set; } public string Name { get; set; } = ""; }
public sealed class WmUserDto { public int Id { get; set; } public string Name { get; set; } = ""; }

public sealed class WmOrder { public long Total { get; set; } }
public sealed class WmOrderDto { public long Total { get; set; } }

[DwarfMapper]
[GenerateMap<WmUser, WmUserDto>]
[GenerateMap<WmOrder, WmOrderDto>]
[GenerateWrapperMap(typeof(Envelope<>))] // also synthesizes Envelope<WmUser>->Envelope<WmUserDto> and Envelope<WmOrder>->Envelope<WmOrderDto>
public partial class WrapperMappers { }

public class WrapperMapRuntimeTests
{
    [Fact]
    public void Wrapper_map_is_synthesized_for_each_declared_payload_pair()
    {
        var m = new WrapperMappers();

        var userEnv = new Envelope<WmUser>
        {
            Payload = new WmUser { Id = 7, Name = "ada" },
            Status = 200,
            CorrelationId = "abc",
        };
        Envelope<WmUserDto> dto = m.Map(userEnv);

        Assert.Equal(7, dto.Payload.Id);
        Assert.Equal("ada", dto.Payload.Name);
        Assert.Equal(200, dto.Status);
        Assert.Equal("abc", dto.CorrelationId);

        // The second declared payload pair is wrapped too.
        var orderEnv = new Envelope<WmOrder> { Payload = new WmOrder { Total = 99 }, Status = 201 };
        Envelope<WmOrderDto> orderDto = m.Map(orderEnv);
        Assert.Equal(99, orderDto.Payload.Total);
        Assert.Equal(201, orderDto.Status);

        // The inner (unwrapped) maps still work alongside the wrapper maps.
        Assert.Equal("ada", m.Map(new WmUser { Id = 1, Name = "ada" }).Name);
    }
}
