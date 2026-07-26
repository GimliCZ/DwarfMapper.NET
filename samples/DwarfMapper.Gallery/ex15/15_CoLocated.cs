// SPDX-License-Identifier: GPL-2.0-only

// 15 — Co-located mapping: the pair + config live ON the DTO class (dtos/PersonDto.cs), not on a separate
// mapper. The mapping reads where the type is (fewer jumps), and deleting the DTO deletes its mapping with it.
// There is NO central [DwarfMapper] mapper class in this example — just the model, the DTO (which carries its
// own mapping), and this caller. It still works through the generated extension / AddDwarfMappers() DI.

using DwarfMapper.Extensions;
using DwarfMapper.Gallery.Ex15.Models;
// surfaces the generated model.ToPersonDto() extension
// Person

// PersonDto (carries its own mapping)

namespace DwarfMapper.Gallery.Ex15;

[DocExample(15, Tier.FrontDoors, "Co-located on the DTO",
    Shows = "`[GenerateMap]` on a plain `sealed` DTO — no `partial`, no `[DwarfMapper]`")]
public static class Example
{
    public static void Run()
    {
        // <snippet: co-located-call>
        var model = new Person { Name = "John Doe", Age = 100 };

        // No mapper class exists in this example — the generated extension is the whole call site.
        var dto = model.ToPersonDto();
        // </snippet>

        Console.WriteLine($"15 Co-located on DTO -> {dto.FullName}: Age {dto.Age}");
    }
}
