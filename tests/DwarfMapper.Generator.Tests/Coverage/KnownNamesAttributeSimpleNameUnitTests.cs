// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// KnownNames.AttributeSimpleName and KnownNames.IsAttributeNamed replace the last five `attr.AttributeClass?.Name` reads
// (SuppressMessage and [MapConstructor] matching, [ProvidesMap] recognition, the transfer-model attribute scan). Per-branch
// rule: a branch no compilation reaches is extracted and tested directly. Roslyn gives every AttributeData a class, so the
// null-conditional's null answer never ran at any of those sites.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class KnownNamesAttributeSimpleNameUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("AttributeSimpleName",
            [CSharpSyntaxTree.ParseText("""
                                        namespace DwarfMapper { public sealed class ProvidesMapAttribute : System.Attribute { } }
                                        namespace Other { public sealed class ProvidesMapAttribute : System.Attribute { } }
                                        """)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static INamedTypeSymbol Named(string metadataName) =>
            Compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName + " not found");

        [Fact]
        public void No_attribute_class_has_no_simple_name_and_matches_nothing()
        {
            Assert.Null(KnownNames.AttributeSimpleName(null));
            Assert.False(KnownNames.IsAttributeNamed(null, KnownNames.ProvidesMap, KnownNames.Ns));
        }

        [Fact]
        public void An_attribute_class_is_named_by_its_simple_name()
        {
            Assert.Equal(KnownNames.ProvidesMap, KnownNames.AttributeSimpleName(Named("DwarfMapper.ProvidesMapAttribute")));
        }

        [Fact]
        public void A_named_match_needs_both_the_simple_name_and_the_namespace()
        {
            var dwarf = Named("DwarfMapper.ProvidesMapAttribute");

            Assert.True(KnownNames.IsAttributeNamed(dwarf, KnownNames.ProvidesMap, KnownNames.Ns));
            Assert.False(KnownNames.IsAttributeNamed(dwarf, KnownNames.MapConstructor, KnownNames.Ns));
            Assert.False(KnownNames.IsAttributeNamed(Named("Other.ProvidesMapAttribute"), KnownNames.ProvidesMap, KnownNames.Ns));
        }
    }
}
