// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// LocationInfo.FromReference replaces five inline `ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None`
// fallbacks in the directive readers. The null arm is also reached through a compilation (a directive inherited from a
// referenced assembly's metadata, RegistryInheritedMetadataDirectiveTests); these pin both answers directly.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class LocationInfoFromReferenceUnitTests
    {
        [Fact]
        public void No_reference_has_no_location_info()
        {
            Assert.Null(LocationInfo.FromReference(null));
        }

        [Fact]
        public void A_source_attribute_is_anchored_at_its_application()
        {
            var tree = CSharpSyntaxTree.ParseText("[System.Obsolete]\nclass C { }\n", path: "C.cs");
            var compilation = CSharpCompilation.Create("FromReference", [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var attribute = Assert.Single(compilation.GetTypeByMetadataName("C")!.GetAttributes());

            var info = LocationInfo.FromReference(attribute.ApplicationSyntaxReference);

            Assert.NotNull(info);
            Assert.Equal("C.cs", info.FilePath);
            Assert.Equal(0, info.LineSpan.Start.Line);
            Assert.Equal(1, info.LineSpan.Start.Character);
        }
    }
}
