// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public sealed class IctorSrc
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

// Factory-pattern DTO: the constructor is internal (not public), so it is reachable from within
// this assembly (or via [InternalsVisibleTo]) but not from arbitrary external code. DwarfMapper
// must be able to construct it — the same situation as a real DTO that hides its ctor behind a
// static factory. Before honoring accessibility-within, this surfaced DWARF026.
public sealed class IctorDst
{
    internal IctorDst()
    {
    }

    public string Name { get; set; } = "";
    public int Age { get; set; }
}

// A parameterized internal ctor must also be usable (constructor projection path).
public sealed class IctorParamDst
{
    internal IctorParamDst(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }
    public int Age { get; }
}

// AllowNonPublic opts in to using the internal ctors below; without it these would
// (correctly) surface DWARF026, since an internal ctor is non-public on purpose.
[DwarfMapper(AllowNonPublic = true)]
[GenerateMap<IctorSrc, IctorDst>]
public partial class IctorMapper
{
}

[DwarfMapper(CaseInsensitive = true, AllowNonPublic = true)]
[GenerateMap<IctorSrc, IctorParamDst>]
public partial class IctorParamMapper
{
}

public sealed class InternalCtorRuntimeTests
{
    [Fact]
    public void Internal_parameterless_ctor_is_used()
    {
        var dst = new IctorMapper().Map(new IctorSrc { Name = "Gimli", Age = 139 });
        Assert.Equal("Gimli", dst.Name);
        Assert.Equal(139, dst.Age);
    }

    [Fact]
    public void Internal_parameterized_ctor_is_used()
    {
        var dst = new IctorParamMapper().Map(new IctorSrc { Name = "Balin", Age = 178 });
        Assert.Equal("Balin", dst.Name);
        Assert.Equal(178, dst.Age);
    }
}
