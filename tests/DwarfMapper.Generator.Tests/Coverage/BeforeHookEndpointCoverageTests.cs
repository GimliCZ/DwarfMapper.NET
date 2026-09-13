// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for [BeforeMap] wiring at the two endpoints that build their own hook list and had never been
// handed a hook that APPLIES: the update-into map and the [GenerateMap] pair. Both loops ran on every fixture
// with a hook declared, but only ever to reject it, so a regression that dropped the before-hook call from
// either endpoint would have passed the suite.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class BeforeHookEndpointCoverageTests
    {
        [Fact]
        public void Update_into_calls_an_applicable_before_hook()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial void Update(Src s, Dst d);
                                   [BeforeMap] private static void Prepare(Src s) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("Prepare(s);", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Generate_map_pair_calls_an_applicable_before_hook()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<Src, Dst>]
                               public partial class M
                               {
                                   [BeforeMap] private static void Prepare(Src s) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("Prepare(src);", generated, StringComparison.Ordinal);
        }
    }
}
