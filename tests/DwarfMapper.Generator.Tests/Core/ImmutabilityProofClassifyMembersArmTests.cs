// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     <see cref="ImmutabilityProof" />'s member walk, on two members the existing arm tests never met:
    ///     <list type="bullet">
    ///         <item>
    ///             an EVENT, a disproof, because its invocation list is written by every subscriber;
    ///         </item>
    ///         <item>
    ///             a readonly field of a PROVEN type, which leaves the type Proven. That is the control for the
    ///             readonly-field disproof and degradation tests: a readonly field is judged by what it holds, and one
    ///             holding an int costs the type nothing.
    ///         </item>
    ///     </list>
    /// </summary>
    public class ImmutabilityProofClassifyMembersArmTests
    {
        private static ITypeSymbol Type(string source, string name) =>
            GeneratorTestHarness.BuildCompilation("ImmutabilityClassifyMembersArms", [CSharpSyntaxTree.ParseText(source)]).GetTypeByMetadataName(name)!;

        [Fact]
        public void An_event_is_disproven()
        {
            var verdict = ImmutabilityProof.Classify(Type("namespace T;\npublic sealed class Bus { public event System.Action Changed; }\n", "T.Bus"), out var reason);

            Assert.Equal(ImmutabilityVerdict.Mutable, verdict);
            Assert.Equal("'T.Bus.Changed' is an event, whose invocation list is mutable from anywhere", reason);
        }

        [Fact]
        public void A_readonly_field_of_a_proven_type_leaves_the_type_proven()
        {
            var verdict = ImmutabilityProof.Classify(Type("namespace T;\npublic sealed class Counter { public readonly int Count; }\n", "T.Counter"), out _);

            Assert.Equal(ImmutabilityVerdict.Proven, verdict);
        }
    }
}
