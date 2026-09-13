// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Members.Phases.cs's ApplySkipNullSourceMembers on an INTERFACE destination. The
// pass collects the destination members SkipNullSourceMembers may defer by walking the target and its base types. An
// interface has no base type, so the walk ends on null rather than on System.Object. An update-into method writing
// into an interface must still guard its nullable assignments.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class SkipNullUpdateIntoInterfaceCoverageTests
    {
        [Fact]
        public void An_update_into_an_interface_target_still_skips_null_source_members()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public string? A { get; set; } }
                                  public interface IDst { string? A { get; set; } }
                                  [DwarfMapper(SkipNullSourceMembers = true)]
                                  public partial class M { public partial void Update(Src s, IDst d); }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("if (s.A is not null) d.A = s.A;", generated, StringComparison.Ordinal);
        }
    }
}
