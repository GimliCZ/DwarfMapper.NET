// SPDX-License-Identifier: GPL-2.0-only

// 22 — The opt-in escape hatch from example 21's automatic proof.
//
// The AUTOMATIC bulk copy needs matching field names as well as matching layout, so a struct whose fields
// were renamed falls back to per-element assignment — which is correct, just slower. [Reinterpret] says
// "I assert these fields correspond"; the generator still refuses unless the types are unmanaged and the
// sizes match, and still emits the runtime size guard. What you are overriding is the NAME proof, nothing
// else. Get the correspondence wrong and you get wrong data, quietly — so only reach for this with a layout
// you control and a test that checks the values.

namespace DwarfMapper.Gallery.Ex22;

public struct Coord
{
    public int A;
    public int B;
}

public struct CoordDto
{
    public int X;   // corresponds to Coord.A — a claim the NAMES cannot support
    public int Y;   // corresponds to Coord.B
}

public sealed class Survey
{
    public Coord[] Points { get; set; } = [];
}

public sealed class SurveyDto
{
    public CoordDto[] Points { get; set; } = [];
}

// <snippet: reinterpret>
[DwarfMapper]
public partial class Mapper
{
    [Reinterpret(nameof(SurveyDto.Points))]   // blit Coord[] -> CoordDto[]; I assert A->X, B->Y
    public partial SurveyDto ToDto(Survey s);
}
// </snippet>

[DocExample(22, Tier.Advanced, "`[Reinterpret]` — asserted blit",
    Shows = "bulk-copying layout-identical structs whose field NAMES differ")]
public static class Example
{
    public static void Run()
    {
        var dto = new Mapper().ToDto(new Survey
        {
            Points = [new Coord { A = 3, B = 4 }, new Coord { A = 7, B = 8 }]
        });

        Console.WriteLine(
            $"22 [Reinterpret]      -> ({dto.Points[0].X},{dto.Points[0].Y}) ({dto.Points[1].X},{dto.Points[1].Y})");
    }
}
