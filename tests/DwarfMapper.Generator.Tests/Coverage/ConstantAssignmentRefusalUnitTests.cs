// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.ConstantAssignmentRefusal, extracted from TryFormatConstant's enum and primitive arms
// (per-branch rule: extract and test directly). Each arm tested the constant's type for null, which Roslyn never leaves
// null on a non-null enum or primitive constant, so both tests had an outcome no attribute application reached. The
// refusal of an untyped constant, and both wordings, are pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ConstantAssignmentRefusalUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("ConstantAssignmentRefusal",
            [CSharpSyntaxTree.ParseText("namespace Demo { public enum Kind { A, B } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static ITypeSymbol Int32 => Compilation.GetSpecialType(SpecialType.System_Int32);

        private static ITypeSymbol Int64 => Compilation.GetSpecialType(SpecialType.System_Int64);

        private static ITypeSymbol String => Compilation.GetSpecialType(SpecialType.System_String);

        [Fact]
        public void An_assignable_constant_is_not_refused()
        {
            Assert.Null(MapperExtractor.ConstantAssignmentRefusal(Int32, false, Int64, Compilation));
        }

        [Fact]
        public void A_primitive_constant_that_does_not_convert_is_refused_as_a_constant()
        {
            Assert.Equal("[MapValue] constant of type 'int' is not assignable to 'string'",
                MapperExtractor.ConstantAssignmentRefusal(Int32, false, String, Compilation));
        }

        [Fact]
        public void An_enum_constant_that_does_not_convert_is_refused_as_an_enum_constant()
        {
            var kind = Compilation.GetTypeByMetadataName("Demo.Kind")!;

            Assert.Equal("[MapValue] enum constant of type 'Demo.Kind' is not assignable to 'string'",
                MapperExtractor.ConstantAssignmentRefusal(kind, true, String, Compilation));
        }

        [Fact]
        public void A_constant_without_a_type_is_refused()
        {
            Assert.Equal("[MapValue] constant of type '' is not assignable to 'int'",
                MapperExtractor.ConstantAssignmentRefusal(null, false, Int32, Compilation));
        }
    }
}
