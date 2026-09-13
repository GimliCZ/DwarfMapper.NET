// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Members.Phases.cs's DWARF080 condition in ResolveAutoMatchedMembers. A
// [MapConstructor] factory owns a `required` destination member, so the generator does not assign it, and DWARF080
// warns that the matching SOURCE value is discarded. With no source member of that name, nothing is discarded, and a
// warning would be noise on every factory-built type with a required member.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class FactoryDropsMemberSourceMatchCoverageTests
    {
        [Fact]
        public void A_factory_owned_required_member_with_no_source_member_is_not_reported()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Inner { public int A { get; set; } }
                                  public class InnerDto { public int A { get; set; } public required int Q { get; set; } }
                                  [DwarfMapper]
                                  [GenerateMap<Inner, InnerDto>]
                                  [MapConstructor<Inner, InnerDto>(nameof(Make))]
                                  public partial class M { private static InnerDto Make(Inner i) => new InnerDto { Q = 1 }; }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF080");
            Assert.Contains("__dwarf_target.A = src.A;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__dwarf_target.Q", generated, StringComparison.Ordinal);
        }
    }
}
