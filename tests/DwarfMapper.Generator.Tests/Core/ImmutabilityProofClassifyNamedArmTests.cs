// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="ImmutabilityProof" />'s <c>Nullable&lt;T&gt;</c> unwrap. A nullable of an UNMANAGED struct never
    ///     reaches it, because the unmanaged-type fast path answers first. A nullable of a struct holding a MANAGED member
    ///     does, and must be judged by what the struct holds: a readonly string is Proven, and a readonly array is a
    ///     disproof. Either way the verdict is the struct's own, which is also the one path where the "unsealed
    ///     reference type" refusal is asked about a value type and answers no.
    /// </summary>
    public class ImmutabilityProofClassifyNamedArmTests
    {
        private static ITypeSymbol NullableOf(string structDeclaration)
        {
            var source = "namespace T;\n" + structDeclaration + "\npublic sealed class Holder { public S? Value { get; init; } }\n";
            var compilation = GeneratorTestHarness.BuildCompilation("ImmutabilityClassifyNamedArms", [CSharpSyntaxTree.ParseText(source)]);
            var type = ((IPropertySymbol)compilation.GetTypeByMetadataName("T.Holder")!.GetMembers("Value").Single()).Type;
            Assert.False(type.IsUnmanagedType);
            return type;
        }

        [Fact]
        public void A_nullable_struct_holding_a_readonly_string_is_proven()
        {
            var verdict = ImmutabilityProof.Classify(NullableOf("public struct S { public readonly string Name; public S(string n) { Name = n; } }"), out _);

            Assert.Equal(ImmutabilityVerdict.Proven, verdict);
        }

        [Fact]
        public void A_nullable_struct_holding_an_array_is_disproven_by_the_array()
        {
            var verdict = ImmutabilityProof.Classify(NullableOf("public struct S { public readonly int[] Items; public S(int[] i) { Items = i; } }"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Mutable, verdict);
            Assert.Contains("is an array, whose elements are settable through any reference to it", reason, StringComparison.Ordinal);
        }
    }
}
