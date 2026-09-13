// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Conversions.cs's IsMappableObjectPair refusals that a nested member reaches:
//   - a source that is not a named type (an array member into a class member): no collection arm claims it, because
//     the target is not a collection, so auto-nest is asked and must decline;
//   - an abstract class target: it is a class, so the kind gate passes, but it cannot be constructed.
// Each must fall through to DWARF005 on the member rather than synthesize a nested map.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AutoNestObjectPairShapeCoverageTests
    {
        private static Diagnostic[] Run(string source) =>
            GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics.ToArray();

        private static void AssertRefusedOnMemberI(string source)
        {
            var diagnostics = Run(source);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF005");
            Assert.StartsWith("Cannot map to 'I':", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARF033");
        }

        [Fact]
        public void An_array_source_into_a_class_target_is_not_auto_nested()
        {
            AssertRefusedOnMemberI("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class InnerDto { public int A { get; set; } }
                                   public class Outer { public int[] I { get; set; } = new int[0]; }
                                   public class OuterDto { public InnerDto? I { get; set; } }
                                   [DwarfMapper(AutoNest = true)]
                                   public partial class M { public partial OuterDto Map(Outer o); }
                                   """);
        }

        [Fact]
        public void An_abstract_class_target_is_not_auto_nested()
        {
            AssertRefusedOnMemberI("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Inner { public int A { get; set; } }
                                   public abstract class InnerDto { public int A { get; set; } }
                                   public class Outer { public Inner I { get; set; } = new(); }
                                   public class OuterDto { public InnerDto? I { get; set; } }
                                   [DwarfMapper(AutoNest = true)]
                                   public partial class M { public partial OuterDto Map(Outer o); }
                                   """);
        }
    }
}
