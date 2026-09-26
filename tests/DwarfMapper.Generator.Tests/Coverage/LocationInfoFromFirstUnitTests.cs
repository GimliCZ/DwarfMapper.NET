// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// LocationInfo.FromFirst replaces six inline `Locations.FirstOrDefault() ?? Location.None` fallbacks (per-branch rule:
// a branch no compilation reaches is extracted and tested directly). Every symbol the generator anchors a diagnostic
// at is declared in source, so the empty-array fallback never ran through a generator fixture.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class LocationInfoFromFirstUnitTests
    {
        [Fact]
        public void A_symbol_with_no_locations_has_no_location_info()
        {
            Assert.Null(LocationInfo.FromFirst(ImmutableArray<Location>.Empty));
        }

        [Fact]
        public void A_source_symbol_is_anchored_at_its_first_declaration()
        {
            var tree = CSharpSyntaxTree.ParseText("class C { }\n", path: "C.cs");
            var compilation = CSharpCompilation.Create("FromFirst", [tree]);
            var symbol = compilation.GetTypeByMetadataName("C")!;

            var info = LocationInfo.FromFirst(symbol.Locations);

            Assert.NotNull(info);
            Assert.Equal("C.cs", info.FilePath);
            Assert.Equal(0, info.LineSpan.Start.Line);
        }
    }
}
