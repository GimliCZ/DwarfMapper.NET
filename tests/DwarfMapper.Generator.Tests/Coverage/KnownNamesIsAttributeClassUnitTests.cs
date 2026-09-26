// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// KnownNames.IsAttributeClass replaces thirty inline `attr.AttributeClass?.ToDisplayString() == Fqn` checks (per-branch
// rule: a branch no compilation reaches is extracted and tested directly). Roslyn gives every AttributeData a class, so
// the null-conditional's null answer never ran through a generator fixture at any of those sites.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class KnownNamesIsAttributeClassUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("IsAttributeClass",
            [CSharpSyntaxTree.ParseText("namespace DwarfMapper { public sealed class FlattenAttribute : System.Attribute { } public sealed class OtherAttribute : System.Attribute { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        [Fact]
        public void No_attribute_class_matches_nothing()
        {
            Assert.False(KnownNames.IsAttributeClass(null, KnownNames.FlattenFqn));
        }

        [Fact]
        public void An_attribute_class_matches_its_own_name_and_no_other()
        {
            var flatten = Compilation.GetTypeByMetadataName("DwarfMapper.FlattenAttribute");
            var other = Compilation.GetTypeByMetadataName("DwarfMapper.OtherAttribute");

            Assert.True(KnownNames.IsAttributeClass(flatten, KnownNames.FlattenFqn));
            Assert.False(KnownNames.IsAttributeClass(other, KnownNames.FlattenFqn));
        }
    }
}
