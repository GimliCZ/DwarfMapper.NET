// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for AmbientValidator answers no compilation reaches (per-branch rule: extract or expose, test directly):
//   - IsHandWritten without a syntax reference: every attribute of the compilation's own assembly has one;
//   - EmitValidateDiExtension with nothing consumed: its only caller runs it after EmitValidateMethod, which already
//     returns empty for an empty set;
//   - EmitValidateMethod with nothing consumed: a validation root that consumes nothing DOES reach it, but not
//     deterministically under the test harness. The consumed set includes every referenced assembly's
//     DwarfRequiresMap manifest, and GeneratorTestHarness builds its shared reference set once from the assemblies
//     the process has loaded by then. Two full runs of the suite differed exactly here (4 hits, then 0) with no
//     change to AmbientValidator, so the branch is pinned directly rather than left to test order.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AmbientValidatorUnitTests
    {
        private static SyntaxReference AttributeReferenceIn(string path)
        {
            var compilation = CSharpCompilation.Create("AmbientValidatorUnit",
                [CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyTitle(\"x\")]", path: path)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            return Assert.Single(compilation.Assembly.GetAttributes()).ApplicationSyntaxReference!;
        }

        [Fact]
        public void No_syntax_reference_is_not_hand_written()
        {
            Assert.False(AmbientValidator.IsHandWritten(null));
        }

        [Fact]
        public void An_attribute_in_ordinary_source_is_hand_written()
        {
            Assert.True(AmbientValidator.IsHandWritten(AttributeReferenceIn("Manifest.cs")));
        }

        [Fact]
        public void An_attribute_in_a_generated_file_is_not_hand_written()
        {
            Assert.False(AmbientValidator.IsHandWritten(AttributeReferenceIn("Manifest" + GeneratedSourceExtensions.GeneratedFileSuffix)));
        }

        [Fact]
        public void Nothing_consumed_emits_no_Validate_method()
        {
            Assert.Equal(string.Empty, AmbientValidator.EmitValidateMethod(Array.Empty<(string, string)>(), autoValidate: true, hasOwnRegistration: true));
        }

        [Fact]
        public void Nothing_consumed_emits_no_DI_validation_extension()
        {
            Assert.Equal(string.Empty, AmbientValidator.EmitValidateDiExtension(Array.Empty<(string, string)>()));
        }

        [Fact]
        public void A_consumed_pair_emits_the_DI_validation_extension()
        {
            var source = AmbientValidator.EmitValidateDiExtension([("global::Demo.Src", "global::Demo.Dst")]);

            Assert.Contains("ValidateDwarfMaps(", source, StringComparison.Ordinal);
        }
    }
}
