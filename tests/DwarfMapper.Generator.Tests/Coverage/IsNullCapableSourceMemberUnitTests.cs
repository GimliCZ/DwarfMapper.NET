// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.IsNullCapableSourceMember, extracted from ApplySkipNullSourceMembers' inline lookup
// (per-branch rule: extract and test directly). The pass only asks it about members resolved from a readable source
// member, so through a mapper the name is always found. "Not a source member" is pinned here with the three answers a
// mapper does produce.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class IsNullCapableSourceMemberUnitTests
    {
        private static readonly Compilation Corlib = CSharpCompilation.Create("IsNullCapableSourceMember",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        private static readonly Dictionary<string, ITypeSymbol> Members = new(StringComparer.Ordinal)
        {
            ["Text"] = Corlib.GetSpecialType(SpecialType.System_String),
            ["MaybeCount"] = Corlib.GetSpecialType(SpecialType.System_Nullable_T).Construct(Corlib.GetSpecialType(SpecialType.System_Int32)),
            ["Count"] = Corlib.GetSpecialType(SpecialType.System_Int32),
        };

        [Theory]
        [InlineData("Text", true)]
        [InlineData("MaybeCount", true)]
        [InlineData("Count", false)]
        [InlineData("NotAMember", false)]
        public void Only_a_declared_reference_or_nullable_value_member_can_hold_null(string name, bool expected)
        {
            Assert.Equal(expected, MapperExtractor.IsNullCapableSourceMember(Members, name));
        }
    }
}
