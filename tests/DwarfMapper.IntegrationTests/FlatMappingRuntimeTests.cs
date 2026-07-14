// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public class Person
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

public class PersonDto
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class PersonMapper
{
    public partial PersonDto ToDto(Person p);
}

public class FlatMappingRuntimeTests
{
    [Fact]
    public void Maps_all_flat_members()
    {
        var mapper = new PersonMapper();
        var dto = mapper.ToDto(new Person { Age = 41, Name = "Durin" });
        Assert.Equal(41, dto.Age);
        Assert.Equal("Durin", dto.Name);
    }

    [Fact]
    public void Null_source_throws_ArgumentNullException()
    {
        var mapper = new PersonMapper();
        Assert.Throws<ArgumentNullException>(() => mapper.ToDto(null!));
    }
}
