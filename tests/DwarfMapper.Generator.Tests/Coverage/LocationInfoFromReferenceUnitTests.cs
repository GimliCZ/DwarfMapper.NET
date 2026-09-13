// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

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

        [Fact]
        public void With_a_fallback_no_reference_answers_the_fallback()
        {
            var fallback = new LocationInfo("Class.cs", new TextSpan(3, 4), new LinePositionSpan(new LinePosition(2, 0), new LinePosition(2, 4)));

            Assert.Same(fallback, LocationInfo.FromReference(null, fallback));
        }

        [Fact]
        public void With_a_fallback_a_source_reference_still_answers_its_own_location()
        {
            var tree = CSharpSyntaxTree.ParseText("[System.Obsolete]\nclass C { }\n", path: "C.cs");
            var compilation = CSharpCompilation.Create("FromReferenceFallback", [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var attribute = Assert.Single(compilation.GetTypeByMetadataName("C")!.GetAttributes());
            var fallback = new LocationInfo("Class.cs", new TextSpan(3, 4), new LinePositionSpan(new LinePosition(2, 0), new LinePosition(2, 4)));

            var info = LocationInfo.FromReference(attribute.ApplicationSyntaxReference, fallback);

            Assert.NotNull(info);
            Assert.Equal("C.cs", info.FilePath);
        }
    }
}
