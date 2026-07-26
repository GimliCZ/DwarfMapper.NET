// SPDX-License-Identifier: GPL-2.0-only

// 24 — Polymorphic dispatch. Declare the map in terms of the BASE type and list the concrete pairs with
// [MapDerivedType<S,D>]; the generated method type-switches on the runtime type and produces the matching
// derived DTO. This is what you would otherwise write as a switch expression that someone forgets to extend.
//
// A source type you did not list is a runtime failure, not a silent downgrade to the base mapping — the
// same "no quiet data loss" stance the completeness gate takes at compile time.

namespace DwarfMapper.Gallery.Ex24;

public abstract class Tool
{
    public string Owner { get; set; } = "";
}

public sealed class Axe : Tool
{
    public int Weight { get; set; }
}

public sealed class Pick : Tool
{
    public int Reach { get; set; }
}

public abstract class ToolDto
{
    public string Owner { get; set; } = "";
}

public sealed class AxeDto : ToolDto
{
    public int Weight { get; set; }
}

public sealed class PickDto : ToolDto
{
    public int Reach { get; set; }
}

// <snippet: map-derived-type>
[DwarfMapper]
public partial class Mapper
{
    [MapDerivedType<Axe, AxeDto>]
    [MapDerivedType<Pick, PickDto>]
    public partial ToolDto ToDto(Tool tool);   // dispatches on the runtime type
}
// </snippet>

[DocExample(24, Tier.Advanced, "`[MapDerivedType]` — polymorphic dispatch",
    Shows = "one base-typed method that maps each concrete subtype to its own DTO")]
public static class Example
{
    public static void Run()
    {
        var mapper = new Mapper();
        var axe = mapper.ToDto(new Axe { Owner = "Gimli", Weight = 6 });
        var pick = mapper.ToDto(new Pick { Owner = "Balin", Reach = 2 });

        Console.WriteLine(
            $"24 [MapDerivedType]   -> {axe.GetType().Name}({((AxeDto)axe).Weight}), "
            + $"{pick.GetType().Name}({((PickDto)pick).Reach})");
    }
}
