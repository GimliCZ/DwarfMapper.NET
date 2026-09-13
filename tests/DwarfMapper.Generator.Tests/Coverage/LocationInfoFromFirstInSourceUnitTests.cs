// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

// LocationInfo.FromFirstInSource is the padded-struct report's anchor, lifted out of it (per-branch rule: a branch no
// compilation reaches is extracted and tested directly). LayoutHygiene.Measure refuses a struct without a source
// declaration, so the report never ran its fallback through a generator fixture.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class LocationInfoFromFirstInSourceUnitTests
    {
        private static readonly LocationInfo Fallback = new("Member.cs", new TextSpan(3, 4), default);

        [Fact]
        public void A_symbol_declared_only_in_metadata_takes_the_fallback()
        {
            var compilation = CSharpCompilation.Create("FromFirstInSource",
                references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var int32 = compilation.GetSpecialType(SpecialType.System_Int32);

            Assert.NotEmpty(int32.Locations);
            Assert.Same(Fallback, LocationInfo.FromFirstInSource(int32.Locations, Fallback));
        }

        [Fact]
        public void A_source_symbol_is_anchored_at_its_declaration_not_the_fallback()
        {
            var tree = CSharpSyntaxTree.ParseText("struct S { }\n", path: "S.cs");
            var compilation = CSharpCompilation.Create("FromFirstInSource", [tree]);
            var symbol = compilation.GetTypeByMetadataName("S")!;

            var info = LocationInfo.FromFirstInSource(symbol.Locations, Fallback);

            Assert.NotNull(info);
            Assert.Equal("S.cs", info.FilePath);
        }
    }
}
