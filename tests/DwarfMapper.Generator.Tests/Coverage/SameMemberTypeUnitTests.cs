// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.SameMemberType (and the MemberTypeOf walk under it), widened from private to internal
// (per-branch rule: extract, expose, test — no deletion). The drift check only asks it about a base target and a
// DERIVED target for a member the base pair maps, so the member is always found on both, and "not found on one side"
// never reaches it through a compilation. The contract is still that a missing member is never "the same member".
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class SameMemberTypeUnitTests
    {
        private static readonly Compilation Compilation = CSharpCompilation.Create("SameMemberType",
            [CSharpSyntaxTree.ParseText("""
                                        namespace T
                                        {
                                            public class Base { public int A { get; set; } }
                                            public class Derived : Base { }
                                            public class Redeclared : Base { public new string A { get; set; } = ""; }
                                            public class Other { public int B { get; set; } }
                                        }
                                        """)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static INamedTypeSymbol Named(string metadataName) =>
            Compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName + " not found");

        [Fact]
        public void An_inherited_member_is_the_same_member()
        {
            Assert.True(MapperExtractor.SameMemberType(Named("T.Base"), Named("T.Derived"), "A"));
        }

        [Fact]
        public void A_member_redeclared_with_another_type_is_not()
        {
            Assert.False(MapperExtractor.SameMemberType(Named("T.Base"), Named("T.Redeclared"), "A"));
        }

        [Fact]
        public void A_member_missing_on_either_side_is_not()
        {
            Assert.False(MapperExtractor.SameMemberType(Named("T.Other"), Named("T.Base"), "A"));
            Assert.False(MapperExtractor.SameMemberType(Named("T.Base"), Named("T.Other"), "A"));
        }
    }
}
