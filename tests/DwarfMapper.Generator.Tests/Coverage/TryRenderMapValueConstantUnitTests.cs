// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.TryRenderMapValueConstant, extracted from the create map's and the projection's inline
// "was this [MapValue] constant pre-rendered?" tests (per-branch rule: extract and test directly). The projection only
// ever receives method-level [MapValue], which is never pre-rendered, so its inline test had an outcome no mapper
// reached. Both answers are pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class TryRenderMapValueConstantUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("TryRenderMapValueConstant",
            [CSharpSyntaxTree.ParseText("""
                                        [System.AttributeUsage(System.AttributeTargets.Class)]
                                        public sealed class ProbeAttribute : System.Attribute { public ProbeAttribute(object value) { } }
                                        [Probe(7)]
                                        public class C { }
                                        """)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static TypedConstant Seven => Assert.Single(Compilation.GetTypeByMetadataName("C")!.GetAttributes()).ConstructorArguments[0];

        private static ITypeSymbol Int32 => Compilation.GetSpecialType(SpecialType.System_Int32);

        [Fact]
        public void A_pre_rendered_literal_is_used_as_it_stands()
        {
            Assert.True(MapperExtractor.TryRenderMapValueConstant("(global::System.Int32)42", Seven, Int32, Compilation, out var literal, out var why));
            Assert.Equal("(global::System.Int32)42", literal);
            Assert.Equal("", why);
        }

        [Fact]
        public void Without_one_the_attribute_constant_is_rendered()
        {
            Assert.True(MapperExtractor.TryRenderMapValueConstant(null, Seven, Int32, Compilation, out var literal, out _));
            Assert.Equal("7", literal);
        }

        [Fact]
        public void A_null_constant_is_refused_for_a_target_that_is_neither_a_reference_nor_a_named_type()
        {
            // An unconstrained type parameter is not a reference type and not a named type, so it is not Nullable<T>
            // either. No mapping method's destination member is one, so this was an outcome no mapper reached.
            var compilation = CSharpCompilation.Create("TryRenderMapValueConstantNull",
                [CSharpSyntaxTree.ParseText("""
                                            [System.AttributeUsage(System.AttributeTargets.Class)]
                                            public sealed class ProbeAttribute : System.Attribute { public ProbeAttribute(object value) { } }
                                            [Probe(null)]
                                            public class G<T> { }
                                            """)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var generic = compilation.GetTypeByMetadataName("G`1")!;
            var nullConstant = Assert.Single(generic.GetAttributes()).ConstructorArguments[0];

            Assert.False(MapperExtractor.TryRenderMapValueConstant(null, nullConstant, generic.TypeParameters[0], compilation, out _, out var why));
            Assert.Equal("[MapValue] cannot assign null to non-nullable type 'T'", why);
        }
    }
}
