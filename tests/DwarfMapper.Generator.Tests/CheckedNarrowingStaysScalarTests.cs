// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>R25-01</c> — checked narrowing is PARKED, and this is the pin that keeps it parked.
    ///     <para>
    ///         Parked on evidence, not on running out of time. <c>TensorPrimitives.ConvertChecked</c> is
    ///         scalar-only by design — its fallback operator declares <c>Vectorizable =&gt; false</c> and the
    ///         Vector128/256/512 paths throw <c>NotSupportedException</c> — it throws without identifying the
    ///         faulting element, and it leaves partial writes behind. Both contestants being scalar, the
    ///         container's 1.49x is anomalous and must not be cited. RyuJIT compiles <c>checked((int)x)</c> to a
    ///         flags test plus a not-taken <c>jo</c>, which is close to free; the RFC's own v2 pass measured a
    ///         hand-written vector kernel at <b>0.27x</b> — three times SLOWER — at small and medium sizes.
    ///     </para>
    ///     <para>
    ///         The point of a pin rather than a paragraph: a future contributor reading "SIMD everything" in the
    ///         RFC could wire one of these in, watch the suite stay green, and ship a regression. This fails
    ///         instead, and says why.
    ///     </para>
    /// </summary>
    public class CheckedNarrowingStaysScalarTests
    {
        private const string NarrowingMapper = """
                                               using DwarfMapper;
                                               namespace Demo;
                                               public class C { public long[] V { get; set; } = System.Array.Empty<long>(); }
                                               public class D { public int[] V { get; set; } = System.Array.Empty<int>(); }
                                               [DwarfMapper] public partial class M { public partial D Map(C c); }
                                               """;

        [Fact]
        public void The_checked_narrowing_emission_is_the_scalar_loop_and_stays_that_way()
        {
            var (_, gen) = GeneratorTestHarness.Run(NarrowingMapper);

            // Anti-vacuity first: if the fixture stopped producing a checked narrowing at all, every
            // assertion below would pass while measuring nothing.
            Assert.Contains("CreateChecked", gen, StringComparison.Ordinal);

            Assert.DoesNotContain("TensorPrimitives", gen, StringComparison.Ordinal);
            Assert.DoesNotContain("ConvertChecked", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void No_vector_kernel_is_emitted_for_a_narrowing_pair()
        {
            var (_, gen) = GeneratorTestHarness.Run(NarrowingMapper);

            // Vector.Widen is a legitimate neighbour — it serves the seven lossless WIDENING pairs — so this
            // asserts the absence of vectorisation on the NARROWING path specifically.
            Assert.DoesNotContain("Vector.Widen", gen, StringComparison.Ordinal);
            Assert.DoesNotContain("GreaterThanAny", gen, StringComparison.Ordinal);
            Assert.DoesNotContain("Vector.Narrow", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_widening_neighbour_still_DOES_vectorise()
        {
            // The control. Without it, the two tests above would also pass if vectorisation had been ripped
            // out of the codebase entirely — which is the failure mode this repository keeps finding.
            const string widening = """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class C { public int[] V { get; set; } = System.Array.Empty<int>(); }
                                    public class D { public long[] V { get; set; } = System.Array.Empty<long>(); }
                                    [DwarfMapper] public partial class M { public partial D Map(C c); }
                                    """;
            var (_, gen) = GeneratorTestHarness.Run(widening);

            Assert.Contains("Vector.Widen", gen, StringComparison.Ordinal);
        }
    }
}
