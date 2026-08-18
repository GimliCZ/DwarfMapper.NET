// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace RegistryValueSource;

// [MapTo] on a value type, compiled by the real generator in this project's own build and then CALLED.
//
// The defect this file exists for (A11-F1) was that the registry wrote `if (source is null) throw …` into
// every extension method it emitted, so a struct source produced CS0037 and did not compile at all. That is
// only half of what a caller wants back, though: "it compiles now" and "it maps" are different claims, and a
// test that asserted the first would have left exactly the gap the surface matrix reports as Honoured —
// "output changed", not "output is right". So these assert VALUES, through both shapes of the emitted API.

public class RuneStampDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Weight { get; set; }
}

public class RuneStampSummary
{
    public string Name { get; set; } = "";
    public decimal Weight { get; set; }
}

[MapTo(typeof(RuneStampDto), typeof(RuneStampSummary))]
public struct RuneStampSource
{
    public int Id { get; set; }

    public string Name { get; set; }

    public decimal Weight { get; set; }

    // Positional, exactly as for a class source: attribute 0 → target 0, attribute 1 → target 1. Present so
    // the struct case is shown carrying member directives too, not merely surviving the null-guard fix.
    [MapIgnore]
    [MapIgnore]
    public string Scratch { get; set; }
}

// A record struct reaches the same emitter down the same path (TypeKind.Struct), and is the shape a caller
// is most likely to reach for. Its positional members are get-only, so the mapping is declared the other way
// round — the record struct is the DESTINATION's source through ordinary settable properties.
[MapTo(typeof(RuneStampDto))]
public record struct RuneTally
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Weight { get; set; }
}

// A nested value-type member: the synthesized-helper path, which discriminated on nullability before the
// extension methods did. Mapped through, not merely emitted.
public struct Facet
{
    public int Depth { get; set; }
}

public class FacetDto
{
    public int Depth { get; set; }
}

public class GemDto
{
    public int Id { get; set; }
    public FacetDto Cut { get; set; } = new();
}

[MapTo(typeof(GemDto))]
public struct Gem
{
    public int Id { get; set; }
    public Facet Cut { get; set; }
}

public class RegistryMapToValueTypeSourceRuntimeTests
{
    [Fact]
    public void A_struct_source_maps_through_the_named_extension()
    {
        var src = new RuneStampSource { Id = 4, Name = "Angi", Weight = 2.5m, Scratch = "ignored" };

        var dto = src.ToRuneStampDto();

        Assert.Equal(4, dto.Id);
        Assert.Equal("Angi", dto.Name);
        Assert.Equal(2.5m, dto.Weight);
    }

    [Fact]
    public void A_struct_source_maps_through_generic_dispatch_to_every_target()
    {
        var src = new RuneStampSource { Id = 9, Name = "Ansuz", Weight = 11.25m };

        var dto = src.MapTo<RuneStampDto>();
        var summary = src.MapTo<RuneStampSummary>();

        Assert.Equal(9, dto.Id);
        Assert.Equal("Ansuz", dto.Name);
        Assert.Equal(11.25m, dto.Weight);
        Assert.Equal("Ansuz", summary.Name);
        Assert.Equal(11.25m, summary.Weight);
    }

    [Fact]
    public void A_struct_source_is_copied_not_aliased()
    {
        // The value-type half of the contract: the extension takes its source by value, so mutating the
        // struct afterwards must not be visible in a destination already produced. Cheap to assert and the
        // one behaviour a reference-type test could never have covered.
        var src = new RuneStampSource { Id = 1, Name = "before", Weight = 1m };
        var dto = src.ToRuneStampDto();

        src.Name = "after";

        Assert.Equal("before", dto.Name);
    }

    [Fact]
    public void A_record_struct_source_maps()
    {
        var tally = new RuneTally { Id = 7, Name = "Thurisaz", Weight = 3m };

        Assert.Equal(7, tally.MapTo<RuneStampDto>().Id);
        Assert.Equal("Thurisaz", tally.ToRuneStampDto().Name);
    }

    [Fact]
    public void A_nested_value_type_member_maps_through_the_synthesized_helper()
    {
        var gem = new Gem { Id = 3, Cut = new Facet { Depth = 58 } };

        var dto = gem.ToGemDto();

        Assert.Equal(3, dto.Id);
        Assert.Equal(58, dto.Cut.Depth);
    }
}
