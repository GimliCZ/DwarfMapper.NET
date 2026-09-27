// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.Linq;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Research C2 / round 31 T25: a C# 15 <c>closed</c> class can be derived from only inside its own assembly, so
    ///     its direct descendants are a known, complete set, and a <c>[MapDerivedType]</c> dispatch over it can be checked
    ///     for a missing arm at compile time instead of throwing at run time. The C# 15 compiler marks a closed class with
    ///     <c>IsClosedTypeAttribute</c>; it is written by hand here, which is exactly what a referenced assembly's metadata
    ///     shows the generator.
    /// </summary>
    public sealed class ClosedHierarchyArmTests
    {
        private const string Shapes = """
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class IsClosedTypeAttribute : System.Attribute { }
            }
            namespace H
            {
                using DwarfMapper;
                [System.Runtime.CompilerServices.IsClosedType] public abstract class Shape { public int Id { get; set; } }
                public sealed class Circle : Shape { public double R { get; set; } }
                public sealed class Square : Shape { public double Side { get; set; } }
                public abstract class ShapeDto { public int Id { get; set; } }
                public sealed class CircleDto : ShapeDto { public double R { get; set; } }
                public sealed class SquareDto : ShapeDto { public double Side { get; set; } }

            """;

        private static string Src(string body)
        {
            return Shapes + body + "\n}\n";
        }

        [Fact]
        public void An_arm_for_every_direct_descendant_is_complete()
        {
            GeneratorAssert.CompilesClean(Src(
                "[DwarfMapper] public partial class M { [MapDerivedType<Circle, CircleDto>][MapDerivedType<Square, SquareDto>] public partial ShapeDto Map(Shape s); }"));
        }

        [Fact]
        public void A_direct_descendant_without_an_arm_is_DWARF114_naming_it()
        {
            var diagnostics = GeneratorTestHarness.Run(Src(
                "[DwarfMapper] public partial class M { [MapDerivedType<Circle, CircleDto>] public partial ShapeDto Map(Shape s); }")).Diagnostics;
            var hit = Assert.Single(diagnostics, d => string.Equals(d.Id, "DWARF114", StringComparison.Ordinal));
            var message = hit.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'H.Square'", message, StringComparison.Ordinal);
            Assert.DoesNotContain("'H.Circle'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_open_base_is_not_checked()
        {
            // The same arms over a base WITHOUT the closed marker: a descendant in another assembly is possible, so a
            // missing arm is the run-time fallback's job, exactly as before.
            var src = Src(
                "public abstract class Open { } public sealed class OpenA : Open { } public sealed class OpenB : Open { }" +
                "public abstract class OpenDto { } public sealed class OpenADto : OpenDto { }" +
                "[DwarfMapper] public partial class M { [MapDerivedType<OpenA, OpenADto>] public partial OpenDto Map(Open s); }");
            Assert.DoesNotContain(GeneratorTestHarness.Run(src).Diagnostics, d => string.Equals(d.Id, "DWARF114", StringComparison.Ordinal));
        }

        [Fact]
        public void A_closed_intermediate_is_covered_by_arms_for_all_of_its_children()
        {
            var src = Src(
                "[System.Runtime.CompilerServices.IsClosedType] public abstract class Poly : Shape { }" +
                "public sealed class Tri : Poly { } public sealed class Quad : Poly { }" +
                "public sealed class TriDto : ShapeDto { } public sealed class QuadDto : ShapeDto { }" +
                "[DwarfMapper] public partial class M { [MapDerivedType<Circle, CircleDto>][MapDerivedType<Square, SquareDto>]" +
                "[MapDerivedType<Tri, TriDto>][MapDerivedType<Quad, QuadDto>] public partial ShapeDto Map(Shape s); }");
            Assert.DoesNotContain(GeneratorTestHarness.Run(src).Diagnostics, d => string.Equals(d.Id, "DWARF114", StringComparison.Ordinal));
        }

        [Fact]
        public void An_open_intermediate_needs_its_own_arm_even_when_its_children_have_one()
        {
            // Closedness is not transitive: an OPEN intermediate can gain a subclass anywhere, so arms for the
            // children it has today do not cover it.
            var src = Src(
                "public abstract class Poly : Shape { } public sealed class Tri : Poly { }" +
                "public sealed class TriDto : ShapeDto { }" +
                "[DwarfMapper] public partial class M { [MapDerivedType<Circle, CircleDto>][MapDerivedType<Square, SquareDto>]" +
                "[MapDerivedType<Tri, TriDto>] public partial ShapeDto Map(Shape s); }");
            var hit = Assert.Single(GeneratorTestHarness.Run(src).Diagnostics, d => string.Equals(d.Id, "DWARF114", StringComparison.Ordinal));
            Assert.Contains("'H.Poly'", hit.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
