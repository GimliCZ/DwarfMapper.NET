// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Two conflicts [MapDenseEnumKeys] must refuse rather than resolve silently, each reached by nothing before:
// - the member is also ignored: the prologue reports the ignore/explicit conflict instead of planning a fill;
// - the member is claimed by a Use= converter while ANOTHER dense member was already refused: the check for an
//   earlier refusal scans the diagnostics this resolution raised, and must step over another member's DWARF105 and
//   over a different descriptor to find — or not find — the one naming this member.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DenseEnumDirectiveConflictCoverageTests
    {
        private const string Shapes = """
                                      using DwarfMapper;
                                      using System.Collections.Generic;
                                      using System.Runtime.CompilerServices;
                                      namespace Demo;
                                      public enum Platform { Web = 0, Ios = 1, Android = 2 }
                                      [InlineArray(3)] public struct Counts3 { private int _e0; }

                                      """;

        [Fact]
        public void A_dense_member_that_is_also_ignored_is_refused_as_an_ignore_conflict()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                    public sealed class A { public Dictionary<Platform, int> Counts { get; set; } = new(); }
                                                                    public sealed class B { public Counts3 Counts { get; set; } }
                                                                    [DwarfMapper] public partial class M { [MapIgnore("Counts")][MapDenseEnumKeys("Counts")] public partial B Map(A a); }
                                                                    """);

            var conflict = Assert.Single(diagnostics, d => d.Id == "DWARF012");
            Assert.Contains("'Counts'", conflict.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void Each_refused_dense_member_is_named_once_beside_another_refusal()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                    public sealed class A { public Dictionary<Platform, int> X { get; set; } = new(); public Dictionary<Platform, int> Y { get; set; } = new(); }
                                                                    public sealed class B { public Counts3 X { get; set; } public Counts3 Y { get; set; } }
                                                                    [DwarfMapper]
                                                                    public partial class M
                                                                    {
                                                                        [MapValue("Y", Use = nameof(Make))]
                                                                        [MapProperty(nameof(A.X), nameof(B.X), Use = nameof(Pack))]
                                                                        [MapDenseEnumKeys("Y")]
                                                                        [MapDenseEnumKeys("X")]
                                                                        public partial B Map(A a);
                                                                        private static Counts3 Make() => default;
                                                                        private static Counts3 Pack(Dictionary<Platform, int> d) => default;
                                                                    }
                                                                    """);

            var refusals = diagnostics.Where(d => d.Id == "DWARF105").Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(2, refusals.Count);
            Assert.Single(refusals, m => m.Contains("member 'Y' is also assigned by [MapValue]", StringComparison.Ordinal));
            Assert.Single(refusals, m => m.Contains("member 'X' also carries a [MapProperty]", StringComparison.Ordinal));
        }

        /// <summary>
        ///     A dense member bound through the CONSTRUCTOR is never offered the directive, and nothing refused it
        ///     earlier, so the "not in force" report fires — after stepping over another member's refusal and over a
        ///     diagnostic of a different kind raised by the same resolution.
        /// </summary>
        [Fact]
        public void A_constructor_bound_dense_member_is_reported_as_not_in_force_beside_other_diagnostics()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(Shapes + """
                                                                    public sealed class A { public Counts3 X { get; set; } public Dictionary<Platform, int> Y { get; set; } = new(); }
                                                                    public sealed class B { public B(Counts3 x) { X = x; } public Counts3 X { get; set; } public Counts3 Y { get; set; } }
                                                                    [DwarfMapper]
                                                                    public partial class M
                                                                    {
                                                                        [MapValue("Y", Use = nameof(Make))]
                                                                        [MapDenseEnumKeys("Y")]
                                                                        [MapDenseEnumKeys("X")]
                                                                        public partial B Map(A a);
                                                                        private static Counts3 Make() => default;
                                                                    }
                                                                    """);

            Assert.Contains(diagnostics, d => d.Id == "DWARF064");
            var notInForce = Assert.Single(diagnostics,
                d => d.Id == "DWARF105" && d.GetMessage(CultureInfo.InvariantCulture).Contains("member 'X' was never reached", StringComparison.Ordinal));
            Assert.Contains("a constructor parameter", notInForce.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
