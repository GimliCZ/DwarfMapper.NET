// SPDX-License-Identifier: GPL-2.0-only

// 16 — The no-mapper-class front door. [MapTo] on the SOURCE type declares the pair and the generator emits
// the mapper plus a rune.ToRuneDto() extension. Nothing to instantiate, no partial class, no [DwarfMapper].
// One source can target several types: stack [MapTo(typeof(A), typeof(B))] and the per-member directives are
// read in source order, each aligned to the matching target.

using DwarfMapper.Extensions;

namespace DwarfMapper.Gallery.Ex16;

// <snippet: map-to-registry>
[MapTo(typeof(RuneDto))]
public sealed class Rune
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class RuneDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
// </snippet>

// <snippet: map-to-multi-target>
[MapTo(typeof(GateDto), typeof(GateSummary))]
public sealed class Gate
{
    public int Id { get; set; }

    [MapProperty("Name"), MapProperty("Title")]   // GateDto.Name ; GateSummary.Title
    public string Label { get; set; } = "";

    [MapProperty("Warden"), MapIgnore]            // mapped into GateDto ; ignored for GateSummary
    public string Keeper { get; set; } = "";
}
// </snippet>

public sealed class GateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Warden { get; set; } = "";
}

public sealed class GateSummary
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

[DocExample(16, Tier.FrontDoors, "The `[MapTo]` registry",
    Shows = "declaring the pair on the source type — no mapper class at all")]
public static class Example
{
    public static void Run()
    {
        var dto = new Rune { Id = 2, Name = "Angerthas" }.ToRuneDto();

        // One source, two targets: the stacked directives are read in source order, each aligned to the
        // matching [MapTo] type — so Label lands in two differently-named members and Keeper in only one.
        var gate = new Gate { Id = 7, Label = "West-gate", Keeper = "Durin" };

        Console.WriteLine(
            $"16 [MapTo] registry   -> #{dto.Id} {dto.Name}; "
            + $"{gate.ToGateDto().Name}/{gate.ToGateDto().Warden} vs {gate.ToGateSummary().Title}");
    }
}
