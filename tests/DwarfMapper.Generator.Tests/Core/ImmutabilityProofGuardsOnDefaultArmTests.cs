// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="ImmutabilityProof.GuardsOnDefault" /> for a type that is not a named type at all.
    ///     <see cref="ImmutabilityProofArmTests" /> pins <c>ImmutableArray&lt;T&gt;</c> (true) and another named
    ///     collection (false). An array is not named, so it never matches the definition-name test, and it must answer
    ///     false: its null guard is <c>is null</c>, not <c>IsDefault</c>.
    /// </summary>
    public class ImmutabilityProofGuardsOnDefaultArmTests
    {
        [Fact]
        public void An_array_does_not_guard_on_default()
        {
            var compilation = GeneratorTestHarness.BuildCompilation("ImmutabilityGuardsOnDefaultArms", [CSharpSyntaxTree.ParseText("namespace T;")]);

            Assert.False(ImmutabilityProof.GuardsOnDefault(compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32))));
        }
    }
}
