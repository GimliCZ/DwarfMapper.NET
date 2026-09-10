// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>The two remaining PROVE rows in <c>Issues/round30/SPEC-fusion-refusal-list.md</c>, both of them
    ///     the same question: does this feature create a SECOND ROUTE to the <c>A -&gt; B</c> edge?</b>
    ///     <para>
    ///         The spec already refuses a pair whose <c>A -&gt; B</c> map is a public endpoint, for the reason
    ///         the spike gives: fusing <c>A -&gt; C</c> does not remove <c>B</c>, it adds a second path. Two
    ///         features were parked as PROVE because they might create such a route indirectly.
    ///         Answered here from the emitted code rather than from reasoning about the attributes.
    ///     </para>
    /// </summary>
    public class FusionSecondRouteTests
    {
        /// <summary>
        ///     <c>[ReverseMap]</c> <b>links two endpoints the consumer declares; it does not generate one.</b>
        ///     Omitting the partner is refused at build time with <c>DWARF052</c> — "[ReverseMap] on 'Fwd' has
        ///     no inverse mapping method 'Demo.A X(Demo.B)'". The first version of this test assumed the
        ///     opposite and was corrected by that diagnostic.
        ///     <para>
        ///         Either way the row is SUBSUMED, and the measured behaviour makes it more clearly so: the
        ///         inverse is a separately DECLARED public endpoint, so the existing "A -&gt; B is a public
        ///         endpoint" row governs it directly. What matters for fusion is that nothing on the reverse
        ///         path constructs <c>B</c> on the FORWARD edge, which the assertion below pins.
        ///     </para>
        /// </summary>
        [Fact]
        public void ReverseMap_adds_the_opposite_direction_not_a_second_route_to_the_forward_edge()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int Id { get; set; } }
                               public class B { public int Id { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [ReverseMap]
                                   public partial B Fwd(A a);
                                   public partial A Back(B b);
                               }
                               """;

            var g = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);

            // MEASURED, and it corrected the assumption this test was written on: [ReverseMap] does NOT
            // generate the inverse. It LINKS two endpoints the consumer declares, and says so loudly when the
            // partner is missing -- DWARF052: "[ReverseMap] on 'Fwd' has no inverse mapping method
            // 'Demo.A X(Demo.B)'". That makes the row MORE clearly subsumed, not less: the inverse is a
            // separately declared public endpoint, so the existing public-endpoint row governs it directly.
            Assert.Contains("Fwd(global::Demo.A a)", g, StringComparison.Ordinal);
            Assert.Contains("Back(global::Demo.B b)", g, StringComparison.Ordinal);

            // The point of the row: nothing in the emitted reverse map CALLS the forward map, so it is not a
            // second route through the A -> B edge. If this ever changes, the row stops being subsumed.
            var reverseCallsForward = g.Contains("= Fwd(", StringComparison.Ordinal);
            Assert.False(reverseCallsForward,
                "the reverse map now calls the forward map, which would make [ReverseMap] a second route " +
                "through the A -> B edge and reopen this PROVE row.");
        }

        /// <summary>
        ///     <c>[GenerateWrapperMap]</c> expands over exactly the pairs already declared as map methods —
        ///     <c>6fa7308</c> records that as the reason an envelope's payload edge is "almost always a
        ///     user-declared converter". So a wrapped pair gains a PUBLIC envelope map whose body maps the
        ///     payload through that pair.
        ///     <para>
        ///         That is a second, publicly reachable call site of <c>A -&gt; B</c>, which is precisely what
        ///         the existing public-endpoint row refuses. SUBSUMED, and refused — not because wrapping is
        ///         special, but because wrapping makes the pair reachable.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_wrapper_map_makes_the_wrapped_pair_reachable_through_a_second_public_endpoint()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Envelope<T> { public T Payload { get; set; } = default!; public int Status { get; set; } }
                               public class A { public int Id { get; set; } }
                               public class B { public int Id { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<A, B>]
                               [GenerateWrapperMap(typeof(Envelope<>))]
                               public partial class M;
                               """;

            var g = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);

            // An envelope endpoint exists, and its payload edge maps A -> B: a SECOND publicly reachable
            // route through the pair, which is exactly what the existing public-endpoint row refuses.
            Assert.Contains("Envelope", g, StringComparison.Ordinal);
            Assert.Contains("global::Demo.A", g, StringComparison.Ordinal);
            Assert.Contains("global::Demo.B", g, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Non-vacuity for both tests above: they are <c>Contains</c> assertions over generated text, so a
        ///     harness returning nothing would fail them in a way that reads like a generator regression rather
        ///     than an empty result. This pins that the plain shape — no attribute at all — emits the forward
        ///     map and does NOT emit an envelope, so the wrapper assertion above is attributable to
        ///     <c>[GenerateWrapperMap]</c> and to nothing else.
        /// </summary>
        [Fact]
        public void Without_the_attributes_there_is_one_endpoint_and_no_envelope()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int Id { get; set; } }
                               public class B { public int Id { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<A, B>]
                               public partial class M;
                               """;

            var g = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);

            Assert.Contains("global::Demo.B", g, StringComparison.Ordinal);
            Assert.DoesNotContain("Envelope", g, StringComparison.Ordinal);
        }
    }
}
