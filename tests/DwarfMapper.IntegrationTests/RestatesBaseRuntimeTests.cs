// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── The shape a real migration wrote 26 times across three projects ───────────
// A base pair and a derived pair, the derived one restating the base's configuration by hand. The agents doing
// it invented a MIRRORS BASE / END MIRRORS BASE comment convention to keep the restatements traceable, which
// is the tell: the problem was never the typing, it was that nothing could check the result. [RestatesBase]
// is that check. It emits nothing — these tests exist to prove that the guarded mapping still behaves.
public class RbCommand
{
    public string Raw { get; set; } = "";
}

public class RbAliasCommand : RbCommand
{
    public string Alias { get; set; } = "";
}

public class RbCommandDto
{
    public string Text { get; set; } = "";
}

public class RbAliasCommandDto : RbCommandDto
{
    public string Alias { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<RbCommand, RbCommandDto>]
[MapProperty<RbCommand, RbCommandDto>(nameof(RbCommand.Raw), nameof(RbCommandDto.Text), Use = nameof(Clean))]
[GenerateMap<RbAliasCommand, RbAliasCommandDto>]
[RestatesBase<RbAliasCommand, RbAliasCommandDto>]
[MapProperty<RbAliasCommand, RbAliasCommandDto>(nameof(RbCommand.Raw), nameof(RbCommandDto.Text),
    Use = nameof(Clean))]
public partial class RbMapper
{
    private static string Clean(string raw) => raw.Trim();
}

// The same shape with a DELIBERATE override, stated. Text is mapped raw here on purpose, and saying so is what
// keeps every other member guarded rather than switching the check off wholesale.
[DwarfMapper]
[GenerateMap<RbCommand, RbCommandDto>]
[MapProperty<RbCommand, RbCommandDto>(nameof(RbCommand.Raw), nameof(RbCommandDto.Text), Use = nameof(Clean))]
[GenerateMap<RbAliasCommand, RbAliasCommandDto>]
[RestatesBase<RbAliasCommand, RbAliasCommandDto>(Overrides = [nameof(RbCommandDto.Text)])]
[MapProperty<RbAliasCommand, RbAliasCommandDto>(nameof(RbCommand.Raw), nameof(RbCommandDto.Text))]
public partial class RbOverrideMapper
{
    private static string Clean(string raw) => raw.Trim();
}

public class RestatesBaseRuntimeTests
{
    [Fact]
    public void The_base_pair_maps_as_configured()
    {
        Assert.Equal("hi", new RbMapper().Map(new RbCommand { Raw = "  hi  " }).Text);
    }

    [Fact]
    public void The_restated_pair_maps_the_same_way()
    {
        // The point of the whole feature: agreement, checked at build time, observable here.
        var mapped = new RbMapper().Map(new RbAliasCommand { Raw = "  hi  ", Alias = "a" });

        Assert.Equal("hi", mapped.Text);
        Assert.Equal("a", mapped.Alias);
    }

    [Fact]
    public void A_declared_override_really_does_map_differently()
    {
        // Overrides is not a suppression that pretends nothing changed — the mapping genuinely differs, and
        // the attribute's job is that the difference was stated rather than discovered.
        var mapped = new RbOverrideMapper().Map(new RbAliasCommand { Raw = "  hi  ", Alias = "a" });

        Assert.Equal("  hi  ", mapped.Text);
        Assert.Equal("hi", new RbOverrideMapper().Map(new RbCommand { Raw = "  hi  " }).Text);
    }

    [Fact]
    public void The_attribute_carries_the_overrides_it_was_given()
    {
        // Reflected rather than mapped, because Overrides is compile-time-only data: the generator reads it
        // and emits nothing for it. This pins that the property survives as a usable part of the public API.
        var attribute = typeof(RbOverrideMapper)
            .GetCustomAttributes(typeof(RestatesBaseAttribute<RbAliasCommand, RbAliasCommandDto>), false)
            .Cast<RestatesBaseAttribute<RbAliasCommand, RbAliasCommandDto>>()
            .Single();

        Assert.Equal([nameof(RbCommandDto.Text)], attribute.Overrides);
    }
}
