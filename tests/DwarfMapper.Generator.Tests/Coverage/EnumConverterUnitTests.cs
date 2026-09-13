// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for EnumConverter answers no enum reaches (per-branch rule: extract or expose, test directly):
//   - DistinctValuedMembers skipping a constant with no value: an enum constant always has one.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class EnumConverterUnitTests
    {
        [Fact]
        public void DistinctValuedMembers_skips_a_constant_with_no_value_and_an_alias()
        {
            var compilation = CSharpCompilation.Create("EnumConverterUnit",
                [CSharpSyntaxTree.ParseText("namespace Demo { public class C { public const string Missing = null; public const int A = 1; public const int AliasA = 1; public const int B = 2; } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var type = compilation.GetTypeByMetadataName("Demo.C")!;

            Assert.Equal(["A", "B"], EnumConverter.DistinctValuedMembers(type).Select(m => m.Name));
        }

    }
}
