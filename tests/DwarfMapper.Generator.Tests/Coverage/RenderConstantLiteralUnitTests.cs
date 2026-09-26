// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.RenderConstantLiteral (per-branch rule: expose and test directly). Its callers hand it
// a constant's own type, which Roslyn always supplies for a non-null constant, and a value SymbolDisplay.FormatPrimitive
// can always spell: the attribute path checks IsRenderableConstant first, and the MapConfig path takes a compile-time
// constant. So a missing value type and the renderer's `?? "null"` fallback were outcomes no mapper reached. All are
// pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class RenderConstantLiteralUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("RenderConstantLiteral",
            [CSharpSyntaxTree.ParseText("public enum Kind { A, B }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static ITypeSymbol Int32 => Compilation.GetSpecialType(SpecialType.System_Int32);

        private static ITypeSymbol Kind => Compilation.GetTypeByMetadataName("Kind")!;

        [Fact]
        public void A_value_without_its_type_is_rendered_as_a_plain_primitive()
        {
            Assert.Equal("7", MapperExtractor.RenderConstantLiteral(7, null, Int32));
        }

        [Fact]
        public void A_value_the_renderer_cannot_spell_falls_back_to_null()
        {
            Assert.Equal("null", MapperExtractor.RenderConstantLiteral(new object(), Int32, Int32));
        }

        [Fact]
        public void An_enum_value_the_renderer_cannot_spell_falls_back_to_null_inside_the_cast()
        {
            Assert.Equal("(global::Kind)(null)", MapperExtractor.RenderConstantLiteral(new object(), Kind, Int32));
        }

        [Fact]
        public void A_floating_target_keeps_its_cast()
        {
            var single = Compilation.GetSpecialType(SpecialType.System_Single);

            Assert.Equal("(float)(1)", MapperExtractor.RenderConstantLiteral(1, Int32, single));
        }
    }
}
