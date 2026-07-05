// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Update-into-existing mapping: a partial method `void Map(S src, T dest)` (or `T Map(S, T)`)
// maps onto an EXISTING target instance instead of constructing a new one. Settable members are
// assigned from the source; the target identity is preserved.

public class MieSrc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Score { get; set; }
}

public class MieDst
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Score { get; set; }
}

[DwarfMapper]
public partial class MieVoidMapper
{
    // void form: mutates dest in place.
    public partial void Update(MieSrc src, MieDst dest);
}

[DwarfMapper]
public partial class MieReturnMapper
{
    // return form: returns the same dest instance (fluent).
    public partial MieDst Update(MieSrc src, MieDst dest);
}

public class MapIntoExistingRuntimeTests
{
    [Fact]
    public void Void_form_updates_existing_instance_in_place()
    {
        var dest = new MieDst { Id = 99, Name = "old", Score = -1 };
        var src = new MieSrc { Id = 7, Name = "new", Score = 42L };

        new MieVoidMapper().Update(src, dest);

        Assert.Equal(7, dest.Id);
        Assert.Equal("new", dest.Name);
        Assert.Equal(42, dest.Score); // long → int CreateChecked applies
    }

    [Fact]
    public void Return_form_returns_same_instance()
    {
        var dest = new MieDst { Id = 1, Name = "old", Score = 0 };
        var src = new MieSrc { Id = 5, Name = "fresh", Score = 100L };

        var result = new MieReturnMapper().Update(src, dest);

        Assert.Same(dest, result); // identity preserved — not a new instance
        Assert.Equal(5, result.Id);
        Assert.Equal("fresh", result.Name);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public void Null_source_throws_ArgumentNullException()
    {
        var dest = new MieDst();
        Assert.Throws<ArgumentNullException>(() => new MieVoidMapper().Update(null!, dest));
    }

    [Fact]
    public void Null_dest_throws_ArgumentNullException()
    {
        var src = new MieSrc();
        Assert.Throws<ArgumentNullException>(() => new MieVoidMapper().Update(src, null!));
    }

    [Fact]
    public void Conversion_overflow_still_loud()
    {
        var dest = new MieDst();
        var src = new MieSrc { Score = int.MaxValue + 1L };
        Assert.Throws<OverflowException>(() => new MieVoidMapper().Update(src, dest));
    }

    // ── Nested object member: replaced (constructed fresh) on update ────────────
    [Fact]
    public void Nested_and_collection_members_are_replaced()
    {
        var dest = new MieOuterDst
        {
            Inner = new MieInnerDst { X = -1 },
            Tags = new List<string> { "stale" }
        };
        var src = new MieOuterSrc
        {
            Inner = new MieInnerSrc { X = 5 },
            Tags = new List<string> { "a", "b" }
        };

        new MieOuterMapper().Update(src, dest);

        Assert.Equal(5, dest.Inner.X); // nested mapped
        Assert.Equal(new[] { "a", "b" }, dest.Tags); // collection replaced
    }
}

public class MieInnerSrc
{
    public int X { get; set; }
}

public class MieInnerDst
{
    public int X { get; set; }
}

public class MieOuterSrc
{
    public MieInnerSrc Inner { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public class MieOuterDst
{
    public MieInnerDst Inner { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

[DwarfMapper]
public partial class MieOuterMapper
{
    public partial void Update(MieOuterSrc src, MieOuterDst dest);
    public partial MieInnerDst ToInner(MieInnerSrc s); // nested mapper (found by signature)
}
