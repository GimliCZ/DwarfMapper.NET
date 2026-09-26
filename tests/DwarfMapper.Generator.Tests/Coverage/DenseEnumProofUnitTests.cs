// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for DenseEnumProof answers no compilation reaches (per-branch rule: expose and test directly):
//   - TryValueOf on a constant that is not an integer: both callers pass an enum member's constant, which always is one;
//   - DeclaredSlots skipping a member with no long value: it only runs after TryProve, which has already refused an enum
//     declaring such a member.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DenseEnumProofUnitTests
    {
        [Fact]
        public void A_constant_that_is_not_an_integer_has_no_value()
        {
            Assert.False(DenseEnumProof.TryValueOf("text", out var value, out var printed));
            Assert.Equal(0, value);
            Assert.Equal("?", printed);
        }

        [Fact]
        public void A_floating_constant_has_no_value()
        {
            Assert.False(DenseEnumProof.TryValueOf(3.5, out _, out var printed));
            Assert.Equal("?", printed);
        }

        [Fact]
        public void Declared_slots_skip_a_member_whose_value_has_no_long_form()
        {
            var compilation = CSharpCompilation.Create("DenseEnumProofUnit",
                [CSharpSyntaxTree.ParseText("namespace Demo { public enum Big : ulong { A = 0, B = 1, Huge = ulong.MaxValue } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var big = compilation.GetTypeByMetadataName("Demo.Big")!;

            var slots = DenseEnumProof.DeclaredSlots(big, 0).ToList();

            Assert.Equal(["global::Demo.Big.A", "global::Demo.Big.B"], slots.Select(s => s.MemberFq));
            Assert.Equal([0, 1], slots.Select(s => s.Index));
        }
    }
}
