// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for ConstructorSelector answers no generator input reaches (per-branch rule: expose, test directly):
//   - UnusableReason's fallback for a constructor that fails none of IsUsableCandidate's tests: the report asks for a
//     reason only after IsUsableCandidate refused the constructor;
//   - AccessibilityWord's "public" and default arms: IsAccessible admits every public constructor, and a
//     constructor's declared accessibility is never NotApplicable.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ConstructorSelectorUnitTests
    {
        [Fact]
        public void UnusableReason_for_a_usable_constructor_says_no_reason_was_found()
        {
            var compilation = CSharpCompilation.Create("ConstructorSelectorUnit",
                [CSharpSyntaxTree.ParseText("namespace Demo { public class Dst { public Dst(int a) { } } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            var target = compilation.GetTypeByMetadataName("Demo.Dst")!;
            var ctor = target.InstanceConstructors.Single();

            var reason = ConstructorSelector.UnusableReason(ctor, target, compilation, false);

            Assert.Contains("no specific reason could be determined", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void AccessibilityWord_spells_public_and_falls_back_for_not_applicable()
        {
            Assert.Equal("public", ConstructorSelector.AccessibilityWord(Accessibility.Public));
            Assert.Equal("not accessible from the mapper", ConstructorSelector.AccessibilityWord(Accessibility.NotApplicable));
        }
    }
}
