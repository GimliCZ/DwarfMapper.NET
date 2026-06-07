// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class FieldSource { public int Count; public string Tag = ""; }
public class FieldTarget { public int Count; public string Tag = ""; }

[DwarfMapper]
public partial class FieldMapper
{
    public partial FieldTarget Map(FieldSource s);
}

public class RenameSource { public string FullName { get; set; } = ""; }
public class RenameTarget { public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class RenameMapper
{
    [MapProperty("FullName", "Name")]
    public partial RenameTarget Map(RenameSource s);
}

public class CiSource { public int count { get; set; } }
public class CiTarget { public int Count { get; set; } }

[DwarfMapper(CaseInsensitive = true)]
public partial class CiMapper
{
    public partial CiTarget Map(CiSource s);
}

public class MemberResolutionRuntimeTests
{
    [Fact]
    public void Maps_public_fields()
    {
        var t = new FieldMapper().Map(new FieldSource { Count = 3, Tag = "ore" });
        Assert.Equal(3, t.Count);
        Assert.Equal("ore", t.Tag);
    }

    [Fact]
    public void Maps_renamed_member()
    {
        var t = new RenameMapper().Map(new RenameSource { FullName = "Gimli" });
        Assert.Equal("Gimli", t.Name);
    }

    [Fact]
    public void Maps_case_insensitively()
    {
        var t = new CiMapper().Map(new CiSource { count = 9 });
        Assert.Equal(9, t.Count);
    }
}
