// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Pairs.cs's CollectFactoryExcludedMembers: the destination members a
// [MapConstructor] factory owns, which the generator must not assign after it runs. Two shapes no fixture reached:
//   - an INTERFACE destination: its base-type walk ends on null rather than on System.Object, and its settable
//     member is still assigned after the factory;
//   - FIELDS on the destination: a readonly field and a const are construction-time only and are excluded exactly as
//     a get-only property is, while a required field is owned by the factory and reported (DWARF080).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class FactoryExcludedMembersCoverageTests
    {
        [Fact]
        public void An_interface_destination_built_by_a_factory_still_gets_its_settable_members()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Inner { public int A { get; set; } }
                                  public interface IInnerDto { int A { get; set; } }
                                  public class InnerDto : IInnerDto { public int A { get; set; } }
                                  [DwarfMapper]
                                  [GenerateMap<Inner, IInnerDto>]
                                  [MapConstructor<Inner, IInnerDto>(nameof(Make))]
                                  public partial class M { private static IInnerDto Make(Inner i) => new InnerDto(); }
                                  """;

            var generated = GeneratorAssert.EmitsCompilableCode(source);

            Assert.Contains("var __dwarf_target = Make(src);", generated, StringComparison.Ordinal);
            Assert.Contains("__dwarf_target.A = src.A;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Readonly_const_and_required_fields_on_a_factory_destination_are_left_to_the_factory()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Inner { public int A { get; set; } public int R; public int Q; public int C; }
                                  public class InnerDto { public int A { get; set; } public readonly int R; public required int Q; public const int C = 1; }
                                  [DwarfMapper]
                                  [GenerateMap<Inner, InnerDto>]
                                  [MapConstructor<Inner, InnerDto>(nameof(Make))]
                                  public partial class M { private static InnerDto Make(Inner i) => new InnerDto { Q = i.Q }; }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            Assert.Contains("__dwarf_target.A = src.A;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__dwarf_target.R", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__dwarf_target.Q", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__dwarf_target.C", generated, StringComparison.Ordinal);
            var owned = Assert.Single(diagnostics, d => d.Id == "DWARF080");
            Assert.StartsWith("Destination member 'Q' is init-only or required", owned.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
