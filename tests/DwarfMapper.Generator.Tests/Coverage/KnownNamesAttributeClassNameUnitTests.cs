// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// KnownNames.AttributeClassName replaces the five inline `attr.AttributeClass?.ToDisplayString()` reads that hand the
// name on to a switch or a local rather than comparing it in place (per-branch rule: a branch no compilation reaches is
// extracted and tested directly). Roslyn gives every AttributeData a class, so the null answer never ran at any of them.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class KnownNamesAttributeClassNameUnitTests
    {
        [Fact]
        public void No_attribute_class_has_no_name()
        {
            Assert.Null(KnownNames.AttributeClassName(null));
        }

        [Fact]
        public void An_attribute_class_is_named_by_its_fully_qualified_display_string()
        {
            var compilation = CSharpCompilation.Create("AttributeClassName",
                [CSharpSyntaxTree.ParseText("namespace DwarfMapper { public sealed class MapIgnoreAttribute : System.Attribute { } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

            Assert.Equal(KnownNames.MapIgnoreFqn, KnownNames.AttributeClassName(compilation.GetTypeByMetadataName("DwarfMapper.MapIgnoreAttribute")));
        }
    }
}
