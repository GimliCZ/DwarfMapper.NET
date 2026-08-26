// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;
using Mapster;

namespace DwarfMapper.DifferentialTests
{
    // ── S12 twin enums whose DECLARATION ORDER differs ───────────────────────────────────────────────────
    // Every member name exists on both sides, so nothing is reportable at build time by anyone. What differs
    // is the ordinal each name carries. That is the entire question a by-name/by-value choice answers, and it
    // is invisible on the shapes the rest of this project uses, where the orders happen to coincide.
    //
    // This is not a contrived shape. Reordering an enum is one of the most common source-compatible edits
    // there is -- adding a member alphabetically, sorting a list, moving a deprecated value to the end -- and
    // it silently repoints every by-value mapping in the program.
    public enum Phase
    {
        Pending,

        Active,

        Closed
    }

    public enum PhaseDto
    {
        Closed,

        Pending,

        Active
    }

    public sealed class PhaseSrc
    {
        public Phase Status { get; set; }
    }

    public sealed class PhaseDst
    {
        public PhaseDto Status { get; set; }
    }

    [DwarfMapper]
    [GenerateMap<PhaseSrc, PhaseDst>]
    public partial class DwarfPhase;

    /// <summary>
    ///     DwarfMapper opted into the OTHER strategy. EnumStrategy.ByValue is a supported, documented option
    ///     — the same switch that lets an enum array take the blit fast path — so the by-value half of the
    ///     benchmark compares DwarfMapper against Mapperly and Mapster on equal terms rather than leaving the
    ///     whole strategy unmeasured.
    /// </summary>
    [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
    [GenerateMap<PhaseSrc, PhaseDst>]
    public partial class DwarfPhaseByValue;

    /// <summary>Mapperly's DEFAULT strategy: a raw value cast.</summary>
    [Riok.Mapperly.Abstractions.Mapper]
    public partial class MapperlyPhaseByValue
    {
        public partial PhaseDst ToPhase(PhaseSrc src);
    }

    /// <summary>
    ///     The same mapping told to go by NAME. The strategy sits on the MAPPER rather than the method because
    ///     Mapperly's [MapEnum] configures an enum-to-enum method and rejects a class mapping with RMG063.
    ///     Fully qualified for the reason MapperlyShapes records: this file sits inside a namespace beginning
    ///     `DwarfMapper`, so DwarfMapper's own attributes are reachable unqualified, and a name that binds to
    ///     the wrong library misconfigures the ORACLE and produces confident nonsense.
    /// </summary>
    [Riok.Mapperly.Abstractions.Mapper(
        EnumMappingStrategy = Riok.Mapperly.Abstractions.EnumMappingStrategy.ByName)]
    public partial class MapperlyPhaseByName
    {
        public partial PhaseDst ToPhase(PhaseSrc src);
    }

    /// <summary>
    ///     Pins what each mapper actually RETURNS when two enums carry the same names in a different order —
    ///     and, through that, what the benchmark's Enum row is entitled to claim.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         FOUND 2026-08-26, from a performance question rather than a correctness one. The benchmark
    ///         reported Mapperly at 3.3 ns against DwarfMapper's 13.0 ns on a row headed
    ///         "Enum (scalar member, <b>by name</b>)". The gap was real and the label was not: the benchmark
    ///         left Mapperly at its DEFAULT strategy, which is by VALUE. DwarfMapper was walking a switch over
    ///         declared names while Mapperly emitted <c>(PhaseDto)s.Status</c>. Those are not two
    ///         implementations of one operation, and on reordered enums they do not even agree — so roughly a
    ///         4x "win" was partly the cost of answering a different question.
    ///     </para>
    ///     <para>
    ///         The fix was to the benchmark, not to the library: its Mapperly arm is now explicitly by-name,
    ///         with the by-value arm kept alongside and labelled not-comparable so the price of the semantic
    ///         difference is a measured number. These tests are what stops the label and the configuration
    ///         drifting apart again.
    ///     </para>
    /// </remarks>
    public class EnumOrderSensitivityTests
    {
        private static readonly DwarfPhase Dwarf = new();
        private static readonly DwarfPhaseByValue DwarfByValue = new();

        private static readonly MapperlyPhaseByName MapperlyByName = new();

        private static readonly MapperlyPhaseByValue MapperlyByValue = new();

        private static readonly IMapper Auto =
            new MapperConfiguration(c => c.CreateMap<PhaseSrc, PhaseDst>()).CreateMapper();

        [Theory]
        [InlineData(Phase.Pending, PhaseDto.Pending)]
        [InlineData(Phase.Active, PhaseDto.Active)]
        [InlineData(Phase.Closed, PhaseDto.Closed)]
        public void DwarfMapper_maps_by_name_so_reordering_the_destination_changes_nothing(Phase src, PhaseDto expected)
        {
            Assert.Equal(expected, Dwarf.Map(new PhaseSrc { Status = src }).Status);
        }

        [Theory]
        [InlineData(Phase.Pending, PhaseDto.Closed)]
        [InlineData(Phase.Active, PhaseDto.Pending)]
        [InlineData(Phase.Closed, PhaseDto.Active)]
        public void DwarfMapper_told_to_map_by_value_agrees_with_Mapperly_and_Mapster(Phase src, PhaseDto expected)
        {
            // The point of the opt-in, and the thing that makes the benchmark's by-value category honest:
            // asked for a cast, DwarfMapper emits a cast and lands on exactly the answer Mapperly's and
            // Mapster's defaults give. Without this the by-value row would be an unverified claim that our
            // arm does the same work as theirs.
            Assert.Equal(expected, DwarfByValue.Map(new PhaseSrc { Status = src }).Status);
        }

        [Theory]
        [InlineData(Phase.Pending, PhaseDto.Pending)]
        [InlineData(Phase.Active, PhaseDto.Active)]
        [InlineData(Phase.Closed, PhaseDto.Closed)]
        public void Mapperly_told_to_map_by_name_agrees_with_DwarfMapper_exactly(Phase src, PhaseDto expected)
        {
            // The apples-to-apples configuration, and the one the benchmark now uses. Agreement here is what
            // makes the two timings comparable at all.
            Assert.Equal(expected, MapperlyByName.ToPhase(new PhaseSrc { Status = src }).Status);
        }

        [Theory]
        // Pending is ordinal 0 on the source and Closed is ordinal 0 on the destination, so the cast
        // silently produces Closed. This is the defect the fast path buys, stated as a fact rather than
        // implied by a footnote.
        [InlineData(Phase.Pending, PhaseDto.Closed)]
        [InlineData(Phase.Active, PhaseDto.Pending)]
        [InlineData(Phase.Closed, PhaseDto.Active)]
        public void Mapperly_at_its_default_maps_by_value_and_therefore_disagrees(Phase src, PhaseDto wrongButExpected)
        {
            Assert.Equal(wrongButExpected, MapperlyByValue.ToPhase(new PhaseSrc { Status = src }).Status);
        }

        [Theory]
        [InlineData(Phase.Pending, PhaseDto.Pending)]
        [InlineData(Phase.Active, PhaseDto.Active)]
        [InlineData(Phase.Closed, PhaseDto.Closed)]
        public void AutoMapper_maps_by_name_as_well(Phase src, PhaseDto expected)
        {
            // MEASURED, and it corrected me. I expected by-value here, reasoning from the ledger's
            // EnumToString entry and from LoudRatherThanSilentTests, where "AutoMapper maps enums by VALUE"
            // is written down. That statement is about an UNDEFINED value, which AutoMapper passes through
            // as the raw number -- it is not a claim about defined members, and AutoMapper 14 matches those
            // by name. The two facts coexist; the ledger sentence was never wrong, only narrower than I read
            // it. Asserting the guess would have published a false column.
            Assert.Equal(expected, Auto.Map<PhaseDst>(new PhaseSrc { Status = src }).Status);
        }

        [Theory]
        [InlineData(Phase.Pending, PhaseDto.Closed)]
        [InlineData(Phase.Active, PhaseDto.Pending)]
        [InlineData(Phase.Closed, PhaseDto.Active)]
        public void Mapster_maps_by_value(Phase src, PhaseDto wrongButExpected)
        {
            // MEASURED, and it corrected me a second time. Mapster benchmarks at 12.6 ns against
            // DwarfMapper's 13.0 ns, so I reasoned it must be walking names too -- "looks switch-shaped" is
            // not evidence. It is a raw cast, and it is level with our switch only because Mapster's own
            // per-call dispatch dominates whatever the cast costs.
            //
            // That is the useful half of this finding: of the four mappers, DwarfMapper and AutoMapper match
            // by NAME and Mapperly and Mapster match by VALUE. Two of the three columns DwarfMapper is
            // measured against on the Enum row were answering a different question.
            Assert.Equal(wrongButExpected, new PhaseSrc { Status = src }.Adapt<PhaseDst>().Status);
        }
    }
}
