// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class UioSrc { public int Id { get; init; } public string Name { get; set; } = ""; }
public class UioDst { public int Id { get; init; } public string Name { get; set; } = ""; }

// Update-into where the target has an init-only member (Id). The init-only member cannot be assigned
// post-construction, so update-into treats it as non-writable: the generator no longer emits an invalid
// assignment (CS8852) and instead surfaces DWARF007 (read-only destination), which the user silences with
// [MapIgnore]. Only the settable members are updated.
[DwarfMapper]
public partial class UpdateIntoInitOnlyMapper
{
    [MapIgnore("Id")]
    public partial void Map(UioSrc src, UioDst dest);
}

public class UpdateIntoInitOnlyRuntimeTests
{
    [Fact]
    public void UpdateInto_skips_init_only_member_and_updates_settable_ones()
    {
        var dest = new UioDst { Id = 7, Name = "old" };
        new UpdateIntoInitOnlyMapper().Map(new UioSrc { Id = 99, Name = "new" }, dest);

        Assert.Equal("new", dest.Name); // settable updated
        Assert.Equal(7, dest.Id);       // init-only preserved (not overwritten)
    }
}
