// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Conversions.cs's IsAbstractOrInterfaceAutoNestSource target-kind refusal. DWARF033
// names an abstract source that auto-nest would map into a constructible object; an abstract source into an ENUM or an
// INTERFACE target is not that shape, since nothing could be synthesized either way. It must stay the plain DWARF005
// on the member, not a DWARF033 blaming the source.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AbstractSourceTargetKindCoverageTests
    {
        private static void AssertDwarf005NotDwarf033(string targetDeclaration, string targetType)
        {
            var source = $$"""
                           using DwarfMapper;
                           namespace Demo;
                           public abstract class Inner { public int A { get; set; } }
                           {{targetDeclaration}}
                           public class Outer { public Inner? I { get; set; } }
                           public class OuterDto { public {{targetType}} I { get; set; } }
                           [DwarfMapper(AutoNest = true)]
                           public partial class M { public partial OuterDto Map(Outer o); }
                           """;

            var diagnostics = GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics;

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF005");
            Assert.StartsWith("Cannot map to 'I':", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARF033");
        }

        [Fact]
        public void An_abstract_source_into_an_enum_target_is_DWARF005_not_DWARF033()
        {
            AssertDwarf005NotDwarf033("public enum Color { Red }", "Color");
        }

        [Fact]
        public void An_abstract_source_into_an_interface_target_is_DWARF005_not_DWARF033()
        {
            AssertDwarf005NotDwarf033("public interface IInnerDto { int A { get; set; } }", "IInnerDto?");
        }
    }
}
