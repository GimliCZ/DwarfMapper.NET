// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.Conversions.cs guards that only a malformed declaration reaches:
//   - CollectHooks' arity refusals — a [BeforeMap] with more than one parameter and an [AfterMap] with more than two
//     (each hook reports DWARF018 on its own name);
//   - IsMappableObjectPair's interface-allowed branch rejecting an ENUM source, which only a [MapDerivedType(typeof,
//     typeof)] arm on an `object` dispatch can hand it (an enum converts to object, so the arm passes validation);
//   - IsAbstractOrInterfaceAutoNestSource's abstract-TARGET refusal, reached when an abstract source is auto-nested
//     into an abstract destination.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ConversionGateCoverageTests
    {
        [Fact]
        public void Hooks_with_too_many_parameters_are_each_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial Dst Map(Src s);
                                   [BeforeMap] private static void Pre(Src s, int extra) { }
                                   [AfterMap] private static void Post(Src s, Dst d, int extra) { }
                               }
                               """;

            var messages = GeneratorAssert.Reports(src, "DWARF018")
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .ToList();
            Assert.Equal(2, messages.Count);
            Assert.Contains(messages, m => m.Contains("'Pre'", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("'Post'", StringComparison.Ordinal));
        }

        [Fact]
        public void Derived_type_arm_with_an_enum_source_is_not_auto_nestable()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum Kind { A, B }
                               public class Dto { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(Kind), typeof(Dto))]
                                   public partial Dto Map(object o);
                               }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF035")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'global::Demo.Kind'", message, StringComparison.Ordinal);
            Assert.Contains("not auto-nestable", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Abstract_source_into_an_abstract_target_is_unmappable_not_an_abstract_source_warning()
        {
            // DWARF033 is about a CONSTRUCTIBLE target losing derived-only members. An abstract target cannot be
            // constructed at all, so the abstract-source check declines and the member is plainly unmappable.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Shape { public int X { get; set; } }
                               public abstract class AbstractDto { public int X { get; set; } }
                               public class Src { public Shape C { get; set; } = null!; }
                               public class Dst { public AbstractDto C { get; set; } = null!; }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            Assert.Contains("'C'", Assert.Single(GeneratorAssert.Reports(src, "DWARF005")).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            GeneratorAssert.DoesNotReport(src, "DWARF033");
        }
    }
}
