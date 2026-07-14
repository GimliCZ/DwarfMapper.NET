// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Testing.Tests;

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
public partial class PersonRoundTripMapper
{
    public partial PersonDto ToDto(Person p);
    public partial Person FromDto(PersonDto d);
}

// A deliberately lossy mapper to prove failures are caught.
public class LossyDto
{
    public int Age { get; set; }
    public string Name { get; set; } = "";
}

public class RoundTripTests
{
    [Fact]
    public void Fuzzer_yields_requested_count()
    {
        var items = new List<Person>(Fuzzer.Generate<Person>(5, 3));
        Assert.Equal(5, items.Count);
    }

    [Fact]
    public void Lossless_roundtrip_passes()
    {
        var m = new PersonRoundTripMapper();
        // Assert.Null(exception) makes the assertion explicit: the verifier must not throw.
        var ex = Record.Exception(() =>
            RoundTrip.Verify<Person, PersonDto>(m.ToDto, m.FromDto, 7, 50));
        Assert.Null(ex);
    }

    [Fact]
    public void Lossy_roundtrip_throws_with_informed_dump()
    {
        // forward drops Name; backward cannot restore it -> round-trip mismatch.
        Func<Person, LossyDto> forward = p => new LossyDto { Age = p.Age, Name = "" };
        Func<LossyDto, Person> backward = d => new Person { Age = d.Age, Name = d.Name };
        var ex = Assert.Throws<RoundTripException>(() =>
            RoundTrip.Verify<Person, LossyDto>(forward, backward, 1, 20));
        Assert.Contains("Name", ex.Message, StringComparison.Ordinal);
    }
}
