// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.EnumFitsIntegral / IntegralFitsEnum, extracted from ResolveProjectionExpr's enum and
// integral cast checks (per-branch rule: extract and test directly). The projection only asks them about real enums,
// whose underlying type is never null, so "a type with no underlying type" never reached the inline null tests through
// a compilation. A plain class and an array are that input here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class EnumFitUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("EnumFit",
            [CSharpSyntaxTree.ParseText("""
                                        namespace T
                                        {
                                            public enum Small : byte { A }
                                            public enum Big : long { A }
                                            public class NotEnum { }
                                        }
                                        """)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static INamedTypeSymbol Named(string metadataName) =>
            Compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName + " not found");

        private static ITypeSymbol Int32 => Compilation.GetSpecialType(SpecialType.System_Int32);

        private static ITypeSymbol Int64 => Compilation.GetSpecialType(SpecialType.System_Int64);

        [Fact]
        public void An_enum_fits_an_integral_its_underlying_type_widens_to()
        {
            Assert.True(MapperExtractor.EnumFitsIntegral(Named("T.Small"), Int32));
            Assert.False(MapperExtractor.EnumFitsIntegral(Named("T.Big"), Int32));
        }

        [Fact]
        public void An_integral_fits_an_enum_whose_underlying_type_it_widens_to()
        {
            Assert.True(MapperExtractor.IntegralFitsEnum(Int32, Named("T.Big")));
            Assert.False(MapperExtractor.IntegralFitsEnum(Int64, Named("T.Small")));
        }

        [Fact]
        public void A_type_with_no_underlying_type_never_fits()
        {
            var array = Compilation.CreateArrayTypeSymbol(Int32);

            Assert.False(MapperExtractor.EnumFitsIntegral(Named("T.NotEnum"), Int32));
            Assert.False(MapperExtractor.EnumFitsIntegral(array, Int32));
            Assert.False(MapperExtractor.IntegralFitsEnum(Int32, Named("T.NotEnum")));
            Assert.False(MapperExtractor.IntegralFitsEnum(Int32, array));
        }
    }
}
