// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Made visible by T08's ResolutionSettings (2026-09-27): the span-map and async-stream endpoints resolved their
    ///     ELEMENT conversion with every optional flag at its parameter default, so <c>ImplicitConversions</c> was
    ///     always true there. Under <c>[DwarfMapper(ImplicitConversions = false)]</c> a lossy <c>long</c> →
    ///     <c>double</c> element, refused as DWARF038 in a create map's member, went through a span or a stream
    ///     silently. The same shape f678f2f fixed for <c>[FlattenGraph]</c> leaves.
    /// </summary>
    public sealed class ElementEndpointStrictConversionTests
    {
        [Theory]
        [InlineData("public partial void Map(System.ReadOnlySpan<long> s, System.Span<double> d);")]
        [InlineData("public partial System.Collections.Generic.IAsyncEnumerable<double> Map(System.Collections.Generic.IAsyncEnumerable<long> s);")]
        public void A_lossy_element_is_refused_under_ImplicitConversions_false(string method)
        {
            var src = "#nullable enable\nusing DwarfMapper;\nnamespace Demo;\n[DwarfMapper(ImplicitConversions = false)]\npublic partial class M\n{\n" + method + "\n}\n";
            var diagnostics = GeneratorTestHarness.Run(src).Diagnostics;
            Assert.True(diagnostics.Any(d => string.Equals(d.Id, "DWARF038", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error),
                "expected DWARF038 as an Error; got [" + string.Join(", ", diagnostics.Select(d => d.Id + " " + d.Severity)) + "]");
        }

        [Theory]
        [InlineData("public partial void Map(System.ReadOnlySpan<long> s, System.Span<double> d);")]
        [InlineData("public partial System.Collections.Generic.IAsyncEnumerable<double> Map(System.Collections.Generic.IAsyncEnumerable<long> s);")]
        public void Without_strict_mode_the_element_keeps_its_suggestion(string method)
        {
            var src = "#nullable enable\nusing DwarfMapper;\nnamespace Demo;\n[DwarfMapper]\npublic partial class M\n{\n" + method + "\n}\n";
            var diagnostics = GeneratorTestHarness.Run(src).Diagnostics;
            Assert.Contains(diagnostics, d => string.Equals(d.Id, "DWARF038", StringComparison.Ordinal));
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
    }
}
