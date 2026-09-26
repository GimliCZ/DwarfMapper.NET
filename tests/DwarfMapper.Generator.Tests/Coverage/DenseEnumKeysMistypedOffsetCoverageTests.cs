// SPDX-License-Identifier: GPL-2.0-only

// ReadDenseEnumKeys reads [MapDenseEnumKeys("Member", Offset = n)]. It matches the named argument by key and then by the
// value's type (`named.Key == "Offset" && named.Value.Value is int`), and the type half had never been false: every
// fixture passed an int. A mistyped Offset is what a consumer has on screen mid-edit — CS0029 is already in the
// compilation — and Roslyn still hands the generator the named argument, holding a string. The offset must fall back
// to 0, the attribute's default, rather than being cast.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DenseEnumKeysMistypedOffsetCoverageTests
    {
        [Fact]
        public void A_mistyped_offset_falls_back_to_zero()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               using System.Runtime.CompilerServices;
                               namespace Demo;
                               public enum Platform { Web = 0, Ios = 1, Android = 2 }
                               [InlineArray(3)] public struct Counts3 { private int _e0; }
                               public sealed class A { public Dictionary<Platform, int> Counts { get; set; } }
                               public sealed class B { public Counts3 Counts { get; set; } }
                               [DwarfMapper] public partial class M { [MapDenseEnumKeys("Counts", Offset = "x")] public partial B Map(A a); }
                               """;

            var (_, generated) = GeneratorTestHarness.Run(src);

            // Offset 0: the first declared key lands in slot 0 and the last in slot 2, exactly as with no Offset at all.
            Assert.Contains("case global::Demo.Platform.Web:", generated, StringComparison.Ordinal);
            Assert.Contains("__r[0] = __kv.Value;", generated, StringComparison.Ordinal);
            Assert.Contains("__r[2] = __kv.Value;", generated, StringComparison.Ordinal);
        }
    }
}
