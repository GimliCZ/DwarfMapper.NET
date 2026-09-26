// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;

// MapperExtractor.AccessibilityText writes a declared class's or method's accessibility back into generated C#. Every
// declared-accessibility arm is reached through a mapper (NestedMapperAccessibilityTests drives the protected forms);
// the fallback answers only NotApplicable, which no declared class or method reports, so it is asked directly.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AccessibilityTextUnitTests
    {
        [Theory]
        [InlineData(Accessibility.Public, "public")]
        [InlineData(Accessibility.Internal, "internal")]
        [InlineData(Accessibility.Protected, "protected")]
        [InlineData(Accessibility.ProtectedOrInternal, "protected internal")]
        [InlineData(Accessibility.ProtectedAndInternal, "private protected")]
        [InlineData(Accessibility.Private, "private")]
        [InlineData(Accessibility.NotApplicable, "public")]
        public void Each_accessibility_is_written_as_its_CSharp_modifier(Accessibility accessibility, string expected)
        {
            Assert.Equal(expected, MapperExtractor.AccessibilityText(accessibility));
        }
    }
}
