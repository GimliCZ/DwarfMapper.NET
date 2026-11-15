// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // One chain type pair per mapper below: every declared map is also registered with the ambient facade, and two
    // mappers of one pair would be two answers to one question there.
    public class ReA { public int V { get; set; } public ReA? Next { get; set; } }

    public class ReADto { public int V { get; set; } public ReADto? Next { get; set; } }

    public class ReB { public int V { get; set; } public ReBChild? Child { get; set; } }

    public class ReBChild { public ReB? Back { get; set; } }

    public class ReBDto { public int V { get; set; } public ReBChildDto? Child { get; set; } }

    public class ReBChildDto { public ReBDto? Back { get; set; } }

    public class ReC { public int V { get; set; } public ReC? Next { get; set; } }

    public class ReCDto { public int V { get; set; } public ReCDto? Next { get; set; } }

    public class ReD { public int V { get; set; } public ReD? Next { get; set; } }

    public class ReDDto { public int V { get; set; } public ReDDto? Next { get; set; } }

    public class ReE { public int V { get; set; } public ReE? Next { get; set; } }

    public class ReEDto { public int V { get; set; } public ReEDto? Next { get; set; } }

    public class ReF { public int V { get; set; } public ReF? Next { get; set; } }

    public class ReFDto { public int V { get; set; } public ReFDto? Next { get; set; } }

    public class ReG { public int V { get; set; } public ReG? Next { get; set; } }

    public class ReGDto { public int V { get; set; } public ReGDto? Next { get; set; } }

    public class ReH { public int V { get; set; } public ReH? Next { get; set; } }

    public class ReHDto { public int V { get; set; } public ReHDto? Next { get; set; } }

    // 1. A [MapProperty(Use = ...)] converter that maps the rest of the chain.
    [DwarfMapper]
    public partial class ReUseMapper
    {
        [MapProperty(nameof(ReA.Next), nameof(ReADto.Next), Use = nameof(Rest))]
        public partial ReADto Map(ReA n);

        private ReADto? Rest(ReA? n) => n is null ? null : Map(n);
    }

    // 2. A hand-written method adopted by its signature, no attribute.
    [DwarfMapper]
    public partial class ReAdoptedMapper
    {
        public partial ReBDto Map(ReB n);

        private ReBChildDto Convert(ReBChild c) => new() { Back = c.Back is null ? null : Map(c.Back) };
    }

    // 3. An [AfterMap] hook, without reference handling and under Preserve.
    [DwarfMapper]
    public partial class ReAfterMapper
    {
        [MapIgnore(nameof(ReCDto.Next))]
        public partial ReCDto Map(ReC n);

        [AfterMap]
        private void Finish(ReC s, ReCDto d)
        {
            if (s.Next is not null)
            {
                d.Next = Map(s.Next);
            }
        }
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class ReAfterPreserveMapper
    {
        [MapIgnore(nameof(ReDDto.Next))]
        public partial ReDDto Map(ReD n);

        [AfterMap]
        private void Finish(ReD s, ReDDto d)
        {
            if (s.Next is not null)
            {
                d.Next = Map(s.Next);
            }
        }
    }

    // 4. The converter of 1 under OnCycle = SetNull.
    [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
    public partial class ReUseSetNullMapper
    {
        [MapProperty(nameof(ReE.Next), nameof(ReEDto.Next), Use = nameof(Rest))]
        public partial ReEDto Map(ReE n);

        private ReEDto? Rest(ReE? n) => n is null ? null : Map(n);
    }

    // 5. An update-into method re-entered from its converter.
    [DwarfMapper]
    public partial class ReUpdateMapper
    {
        [MapProperty(nameof(ReF.Next), nameof(ReFDto.Next), Use = nameof(Rest))]
        public partial void Update(ReF n, ReFDto d);

        private ReFDto? Rest(ReF? n)
        {
            if (n is null)
            {
                return null;
            }

            var d = new ReFDto();
            Update(n, d);
            return d;
        }
    }

    // 6. The converter reaches the mapper through the ambient facade.
    [DwarfMapper]
    public partial class ReAmbientMapper
    {
        [MapProperty(nameof(ReG.Next), nameof(ReGDto.Next), Use = nameof(Rest))]
        public partial ReGDto Map(ReG n);

        private static ReGDto? Rest(ReG? n) => n is null ? null : Dwarf.Map<ReG, ReGDto>(n);
    }

    // 7. A span map whose hand-written element converter maps a span of the rest.
    [DwarfMapper]
    public partial class ReSpanMapper
    {
        public partial void MapSpan(ReadOnlySpan<ReH> s, Span<ReHDto> d);

        private ReHDto Convert(ReH n)
        {
            var dto = new ReHDto { V = n.V };
            if (n.Next is not null)
            {
                var one = new ReHDto[1];
                MapSpan(new[] { n.Next }, one);
                dto.Next = one[0];
            }

            return dto;
        }
    }

    /// <summary>
    ///     <b>User-written code that calls the mapper again ends in a catchable exception, not a stack overflow</b>
    ///     (round 35, T20).
    ///     <para>
    ///         The depth guard counts the calls the generator emits. A converter, an adopted method or a hook that
    ///         calls a mapping method again is a loop it never sees, and on a deep input that loop ran until the
    ///         stack ended - measured before the fix in a probe that runs one process per case, because a stack
    ///         overflow cannot be caught and would have ended this test host: every shape below, 200,000 levels deep,
    ///         killed the process (Issues/round35/ReentryProbe.cs). A declared mapping method of a mapper that holds
    ///         user-written code now checks the stack on entry, which raises
    ///         <see cref="InsufficientExecutionStackException" />.
    ///     </para>
    ///     <para>
    ///         Each case runs on a thread with a stack of one megabyte, so the three platforms agree on how much
    ///         stack there is; the chain is far deeper than that stack can hold.
    ///     </para>
    /// </summary>
    public class ReentryStackCheckRuntimeTests
    {
        private const int Depth = 200_000;

        private const int StackBytes = 1024 * 1024;

        /// <summary>What <paramref name="map" /> threw on a one-megabyte thread, or <see langword="null" />.</summary>
        private static Exception? ThrownOnASmallStack(Action map)
        {
            Exception? thrown = null;
            var thread = new Thread(() => thrown = Record.Exception(map), StackBytes);
            thread.Start();
            thread.Join();
            return thrown;
        }

        private static T Chain<T>(Func<int, T?, T> link)
            where T : class
        {
            T? head = null;
            for (var i = 0; i < Depth; i++) head = link(i, head);
            return head!;
        }

        [Fact]
        public void A_member_converter_that_maps_the_rest()
        {
            var chain = Chain<ReA>((i, next) => new ReA { V = i, Next = next });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReUseMapper().Map(chain)));
        }

        [Fact]
        public void A_hand_written_method_adopted_by_its_signature()
        {
            var chain = Chain<ReB>((i, next) => new ReB { V = i, Child = new ReBChild { Back = next } });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReAdoptedMapper().Map(chain)));
        }

        [Fact]
        public void An_after_hook_without_reference_handling_and_under_Preserve()
        {
            var plain = Chain<ReC>((i, next) => new ReC { V = i, Next = next });
            var preserved = Chain<ReD>((i, next) => new ReD { V = i, Next = next });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReAfterMapper().Map(plain)));
            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReAfterPreserveMapper().Map(preserved)));
        }

        [Fact]
        public void A_member_converter_under_SetNull()
        {
            var chain = Chain<ReE>((i, next) => new ReE { V = i, Next = next });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReUseSetNullMapper().Map(chain)));
        }

        [Fact]
        public void An_update_into_method_re_entered_from_its_converter()
        {
            var chain = Chain<ReF>((i, next) => new ReF { V = i, Next = next });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReUpdateMapper().Update(chain, new ReFDto())));
        }

        [Fact]
        public void A_converter_that_calls_the_ambient_facade()
        {
            var chain = Chain<ReG>((i, next) => new ReG { V = i, Next = next });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReAmbientMapper().Map(chain)));
        }

        [Fact]
        public void A_span_map_whose_element_converter_maps_a_span()
        {
            var chain = Chain<ReH>((i, next) => new ReH { V = i, Next = next });

            Assert.IsType<InsufficientExecutionStackException>(ThrownOnASmallStack(() => new ReSpanMapper().MapSpan(new[] { chain }, new ReHDto[1])));
        }

        // The check does not stand in the way of a re-entry the stack can hold: 500 levels map, level for level.
        [Fact]
        public void A_chain_the_stack_can_hold_is_mapped_through_the_converter()
        {
            ReA? chain = null;
            for (var i = 0; i < 500; i++) chain = new ReA { V = i, Next = chain };
            ReADto? mapped = null;

            var thrown = ThrownOnASmallStack(() => mapped = new ReUseMapper().Map(chain!));

            Assert.Null(thrown);
            var levels = 0;
            for (var cur = mapped; cur is not null; cur = cur.Next) Assert.Equal(499 - levels++, cur.V);
            Assert.Equal(500, levels);
        }
    }
}
