// SPDX-License-Identifier: GPL-2.0-only

// 05 - The empty union. A union struct can be empty - default(Shape), its Value null - and an empty source maps to
// an empty target. A nullable union member keeps "no value" apart from "an empty union": null stays null, and an
// empty union arrives as a present but empty one.

namespace DwarfMapper.Unions.Ex05
{
    public record class Circle(double Radius);

    public record class Square(double Side);

    public record class CircleDto(double Radius);

    public record class SquareDto(double Side);

    public union Shape(Circle, Square);

    public union ShapeDto(CircleDto, SquareDto);

    public class Sketch
    {
        public Shape? Outline { get; set; }
    }

    public class SketchDto
    {
        public ShapeDto? Outline { get; set; }
    }

// <snippet: union-empty>
    [DwarfMapper]
    public partial class ShapeMapper
    {
        public partial ShapeDto ToDto(Shape shape); // default(Shape) -> default(ShapeDto)

        public partial SketchDto ToDto(Sketch sketch); // Outline: null -> null, empty -> empty

        public partial CircleDto ToDto(Circle circle);

        public partial SquareDto ToDto(Square square);
    }
// </snippet>

    public static class Example
    {
        public static string Run()
        {
            var mapper = new ShapeMapper();
            var empty = mapper.ToDto(default(Shape)).Value is null ? "empty" : "not empty";
            var none = mapper.ToDto(new Sketch { Outline = null }).Outline.HasValue ? "a value" : "null";
            var hollow = mapper.ToDto(new Sketch { Outline = default(Shape) }).Outline;
            var present = hollow.HasValue && hollow.Value.Value is null ? "present, empty" : "something else";
            return empty + " | " + none + " | " + present;
        }

        public const string Expected = "empty | null | present, empty";
    }
}
