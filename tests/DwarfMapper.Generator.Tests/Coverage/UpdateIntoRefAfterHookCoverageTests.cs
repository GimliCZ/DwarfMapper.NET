// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for MapEmitter's EmitUpdateIntoMethod after-hook call. An [AfterMap] hook may take the destination BY
// REF, and the update-into body must then pass it as `ref d`, or the call does not bind (CS1620). Every update-into
// fixture's after-hook took the destination by value, so the `ref ` prefix was never written.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class UpdateIntoRefAfterHookCoverageTests
    {
        [Fact]
        public void An_after_hook_taking_the_destination_by_ref_is_called_with_ref()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } }
                                  public class Dst { public int A { get; set; } }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      public partial void Update(Src s, Dst d);
                                      [AfterMap] private static void Fix(Src s, ref Dst d) { }
                                  }
                                  """;

            var generated = GeneratorAssert.EmitsCompilableCode(source);

            Assert.Contains("d.A = s.A;", generated, StringComparison.Ordinal);
            Assert.Contains("Fix(s, ref d);", generated, StringComparison.Ordinal);
        }
    }
}
