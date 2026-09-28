// SPDX-License-Identifier: GPL-2.0-only
// CASE: a [MapDerivedType] dispatch over a C# 15 closed source type with no arm for one direct descendant.
// WHY:  Round 31 T25 / research C2. A closed class can be derived from only inside its own assembly, so its direct
//       descendants are a complete set; a missing arm used to be the dispatch switch's run-time fallback, which throws
//       for an instance of the missing type. The C# 15 compiler marks a closed class IsClosedTypeAttribute — written by
//       hand here, which is what a referenced assembly's metadata shows the generator.
// EXPECT: DWARF114, DWARF078
// EXPECT-MESSAGE DWARF114: 'Map' dispatches over the closed type 'Demo.Shape'
// EXPECT-MESSAGE DWARF114: 'Demo.Square'
// EXPECT-MESSAGE DWARF114: have no [MapDerivedType] arm
// EXPECT-CS: CS8795
// NOTE: DWARF078 and CS8795 are the documented refusal cascade: an Error here means the mapper is not generated.

#nullable enable

using DwarfMapper;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class IsClosedTypeAttribute : Attribute
    {
    }
}

namespace Demo
{
    [System.Runtime.CompilerServices.IsClosedType]
    public abstract class Shape
    {
        public int Id { get; set; }
    }

    public sealed class Circle : Shape
    {
        public double R { get; set; }
    }

    public sealed class Square : Shape
    {
        public double Side { get; set; }
    }

    public abstract class ShapeDto
    {
        public int Id { get; set; }
    }

    public sealed class CircleDto : ShapeDto
    {
        public double R { get; set; }
    }

    [DwarfMapper]
    public partial class M
    {
        [MapDerivedType<Circle, CircleDto>]
        public partial ShapeDto Map(Shape s);
    }
}
