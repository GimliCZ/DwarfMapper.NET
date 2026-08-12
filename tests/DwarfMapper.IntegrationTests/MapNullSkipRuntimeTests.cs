// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public sealed class SettingsPatchDto
{
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public int? Volume { get; set; }
}

public sealed class UserSettings
{
    public string DisplayName { get; set; } = "";
    public string Bio { get; set; } = "";
    public int Volume { get; set; }
}

public sealed class RankPatchDto
{
    public string? Title { get; set; }
}

public sealed class UserRank
{
    public string Title { get; set; } = "";
}

/// <summary>
///     One mapper carrying <b>both</b> semantics — the shape that forced a class split before
///     <c>[MapNullSkip]</c> existed.
/// </summary>
// NullStrategy = SetDefault so that "replace" has defined behaviour for the nullable VALUE member: with the
// default NullStrategy.Throw, a null int? into a non-nullable int throws rather than clearing. The two
// options are orthogonal and compose — SetDefault decides what a null BECOMES, [MapNullSkip] decides whether
// the assignment happens at all.
[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
public partial class SettingsMappers
{
    /// <summary>A null in the DTO means "clear this".</summary>
    public partial void Replace(SettingsPatchDto src, UserSettings dst);

    /// <summary>A null in the DTO means "leave this alone".</summary>
    [MapNullSkip]
    public partial void Patch(SettingsPatchDto src, UserSettings dst);
}

/// <summary>The inverse: a class that patches by default, with one map carved out.</summary>
[DwarfMapper(SkipNullSourceMembers = true, NullStrategy = NullStrategy.SetDefault)]
public partial class PatchByDefaultMappers
{
    public partial void Patch(SettingsPatchDto src, UserSettings dst);

    [MapNullSkip(false)]
    public partial void Replace(SettingsPatchDto src, UserSettings dst);
}

/// <summary>Pair-scoped form, for a mapper that declares its pairs as attributes rather than methods.</summary>
[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
[GenerateMap<SettingsPatchDto, UserSettings>]
[GenerateMap<RankPatchDto, UserRank>]
[MapNullSkip<RankPatchDto, UserRank>]
public partial class PairScopedNullSkipMappers
{
    public partial void UpdateSettings(SettingsPatchDto src, UserSettings dst);

    [MapNullSkip]
    public partial void PatchRank(RankPatchDto src, UserRank dst);
}

/// <summary>
///     Runtime behaviour of <c>[MapNullSkip]</c> — the pair/method scope of <c>SkipNullSourceMembers</c>.
/// </summary>
/// <remarks>
///     AutoMapper's <c>ForAllMembers(o =&gt; o.Condition((_,_,src) =&gt; src != null))</c> was per-map, so a
///     profile mixing patch-merge with ordinary maps could not translate to one class-level boolean. The
///     Round-18 migration split such profiles across extra mapper classes purely to carry it — and that split
///     then synthesized shared nested pairs twice with opposite null semantics, silently. See
///     <c>Issues/Rount18/</c>.
/// </remarks>
public sealed class MapNullSkipRuntimeTests
{
    private static UserSettings Populated() => new()
    {
        DisplayName = "existing name",
        Bio = "existing bio",
        Volume = 11
    };

    [Fact]
    public void Patch_leaves_the_destination_alone_where_the_source_is_null()
    {
        var dst = Populated();

        new SettingsMappers().Patch(new SettingsPatchDto { DisplayName = "new name" }, dst);

        Assert.Equal("new name", dst.DisplayName);   // supplied — overwritten
        Assert.Equal("existing bio", dst.Bio);       // null — untouched
        Assert.Equal(11, dst.Volume);                // null — untouched
    }

    [Fact]
    public void Replace_on_the_same_mapper_still_clears_where_the_source_is_null()
    {
        // The point of the whole feature: both behaviours on ONE mapper.
        var dst = Populated();

        new SettingsMappers().Replace(new SettingsPatchDto { DisplayName = "new name" }, dst);

        Assert.Equal("new name", dst.DisplayName);

        // "Cleared" means DEFAULT, and default(string) is null — not "". Pinned as-is rather than softened:
        // this is the value a replace-map actually writes, and a reader deciding between the two behaviours
        // needs to know that the non-skip path can put a null into a non-nullable member (DWARF070 says so
        // at build time).
        Assert.Null(dst.Bio);
        Assert.Equal(0, dst.Volume);
    }

    [Fact]
    public void MapNullSkip_false_carves_one_map_out_of_a_patching_class()
    {
        var patched = Populated();
        var replaced = Populated();
        var mappers = new PatchByDefaultMappers();
        var src = new SettingsPatchDto { DisplayName = "new name" };

        mappers.Patch(src, patched);
        mappers.Replace(src, replaced);

        Assert.Equal("existing bio", patched.Bio);
        Assert.Null(replaced.Bio);
    }

    [Fact]
    public void Pair_scoped_MapNullSkip_guards_only_the_pair_it_names()
    {
        var settings = Populated();
        var rank = new UserRank { Title = "existing title" };
        var mappers = new PairScopedNullSkipMappers();

        // RankPatchDto -> UserRank is null-skipped by the pair-scoped attribute...
        mappers.PatchRank(new RankPatchDto(), rank);
        Assert.Equal("existing title", rank.Title);

        // ...and the un-named SettingsPatchDto -> UserSettings pair is not.
        mappers.UpdateSettings(new SettingsPatchDto(), settings);
        Assert.Null(settings.Bio);
    }

    [Fact]
    public void A_non_nullable_source_value_is_written_even_under_null_skip()
    {
        // The guard is on NULLABILITY, not on emptiness. A source member that is present but default still
        // overwrites — worth pinning, because "skip nulls" is easy to misread as "skip falsy".
        var dst = Populated();

        new SettingsMappers().Patch(new SettingsPatchDto { DisplayName = "", Volume = 0 }, dst);

        Assert.Equal("", dst.DisplayName);
        Assert.Equal(0, dst.Volume);
    }
}
