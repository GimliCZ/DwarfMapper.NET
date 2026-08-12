// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>DWARF081</c> — one logical nested pair, auto-synthesized into two mappers that do not agree.
/// </summary>
/// <remarks>
///     <para>
///         A synthesized helper inherits the policy of the mapper that reached it, so two mappers reaching the
///         same <c>(S, T)</c> each get a private copy — and if their options differ, so do the copies. Both
///         compile. Both are correct in isolation. The same two types are mapped two different ways in one
///         assembly and nothing else in the build says so.
///     </para>
///     <para>
///         Round 18 hit exactly this: <c>SkipNullSourceMembers</c> was class-scoped, a profile mixing
///         patch-merge maps with ordinary ones had to be split across two mapper classes, and the split
///         produced one null-guarded and one unguarded copy of the same nested pair — "a real behavioural
///         difference, not a cosmetic one", because the store could deserialize nulls into those members.
///     </para>
///     <para>
///         An earlier attempt compared <c>MapperClassModel.SynthesizedMethods</c> and was reverted after a
///         probe measured it EMPTY for a compilation whose output plainly contained the helper. The auto-nested
///         object mappers are not in that collection: they are private, non-partial entries in <c>Methods</c>.
///         The detection reads those, which is why it works and the first attempt could not.
///     </para>
/// </remarks>
public class DivergentSynthesizedPairTests
{
    private const string Id = "DWARF081";

    /// <summary>The Round-18 shape: one class-scoped option, two mappers, one shared nested pair.</summary>
    private const string SplitProfile = """
        using DwarfMapper;
        namespace Demo;
        public class Inner { public string? Note { get; set; } }
        public class InnerDto { public string? Note { get; set; } }
        public class Outer { public Inner Child { get; set; } = new(); }
        public class OuterDto { public InnerDto Child { get; set; } = new(); }

        [DwarfMapper]
        [GenerateMap<Outer, OuterDto>]
        public partial class Replace;

        [DwarfMapper(SkipNullSourceMembers = true)]
        [GenerateMap<Outer, OuterDto>]
        public partial class Patch;
        """;

    [Fact]
    public void Reports_when_two_mappers_synthesize_the_pair_differently()
    {
        Assert.NotEmpty(GeneratorAssert.Reports(SplitProfile, Id));
    }

    [Fact]
    public void The_message_names_both_mappers_the_pair_and_what_differs()
    {
        var message = GeneratorAssert.Reports(SplitProfile, Id)[0].GetMessage(CultureInfo.InvariantCulture);

        // Both mappers, because the reader has to know which two to reconcile.
        Assert.Contains("'Patch'", message, StringComparison.Ordinal);
        Assert.Contains("'Replace'", message, StringComparison.Ordinal);

        // The pair, by name — the helper it is really about is private and generated, so naming THAT would
        // send the reader to code they never wrote.
        Assert.Contains("Demo.Inner", message, StringComparison.Ordinal);
        Assert.Contains("Demo.InnerDto", message, StringComparison.Ordinal);

        // And the member whose treatment differs, which is what points at the option responsible.
        Assert.Contains("Note", message, StringComparison.Ordinal);

        // The remedy that removes the cause rather than the symptom.
        Assert.Contains("[MapNullSkip", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_silent_when_both_mappers_agree()
    {
        // Two mappers reaching the same nested pair is ordinary and common. Firing here would make the id
        // noise on every assembly with more than one mapper.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string? Note { get; set; } }
            public class InnerDto { public string? Note { get; set; } }
            public class Outer { public Inner Child { get; set; } = new(); }
            public class OuterDto { public InnerDto Child { get; set; } = new(); }

            [DwarfMapper]
            [GenerateMap<Outer, OuterDto>]
            public partial class First;

            [DwarfMapper]
            [GenerateMap<Outer, OuterDto>]
            public partial class Second;
            """;

        GeneratorAssert.CompilesClean(src);
        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Is_silent_for_a_single_mapper()
    {
        // There is nothing to disagree with.
        GeneratorAssert.DoesNotReport("""
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string? Note { get; set; } }
            public class InnerDto { public string? Note { get; set; } }
            public class Outer { public Inner Child { get; set; } = new(); }
            public class OuterDto { public InnerDto Child { get; set; } = new(); }

            [DwarfMapper(SkipNullSourceMembers = true)]
            [GenerateMap<Outer, OuterDto>]
            public partial class Only;
            """, Id);
    }

    [Fact]
    public void Is_silent_once_the_option_is_narrowed_to_the_pair()
    {
        // The remedy actually works, which is the difference between a diagnostic and a complaint. Declaring
        // [MapNullSkip<Inner, InnerDto>] on BOTH mappers makes the nested pair's policy a property of the
        // pair rather than of whichever class reached it — so the two copies agree again while the classes
        // still differ at the top level, which was the reason for the split in the first place.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string? Note { get; set; } }
            public class InnerDto { public string? Note { get; set; } }
            public class Outer { public Inner Child { get; set; } = new(); }
            public class OuterDto { public InnerDto Child { get; set; } = new(); }

            [DwarfMapper]
            [MapNullSkip<Inner, InnerDto>]
            [GenerateMap<Outer, OuterDto>]
            public partial class Replace;

            [DwarfMapper(SkipNullSourceMembers = true)]
            [MapNullSkip<Inner, InnerDto>]
            [GenerateMap<Outer, OuterDto>]
            public partial class Patch;
            """;

        GeneratorAssert.CompilesClean(src);
        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Catches_divergence_from_an_option_nobody_enumerated()
    {
        // The reason the check compares the MODELS rather than a list of options: it catches divergence from
        // any cause. CaseInsensitive is not null-handling and was never on anyone's list for this.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Inner { public string Note { get; set; } = ""; }
            public class InnerDto { public string NOTE { get; set; } = ""; }
            public class Outer { public Inner Child { get; set; } = new(); }
            public class OuterDto { public InnerDto Child { get; set; } = new(); }

            [DwarfMapper(CaseInsensitive = true)]
            [GenerateMap<Outer, OuterDto>]
            public partial class Loose;

            [DwarfMapper(CaseInsensitive = true)]
            [GenerateMap<Outer, OuterDto>]
            public partial class AlsoLoose;

            [DwarfMapper(CaseInsensitive = true)]
            [MapIgnore<InnerDto>(nameof(InnerDto.NOTE))]
            [GenerateMap<Outer, OuterDto>]
            public partial class Strict;
            """;

        var reported = GeneratorAssert.Reports(src, Id);

        Assert.NotEmpty(reported);
        Assert.Contains("'Strict'", reported[0].GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_once_per_pair_not_once_per_mapper()
    {
        // Three mappers, one disagreement. One report — a per-mapper wall is how a useful Info gets filtered.
        Assert.Single(GeneratorAssert.Reports(SplitProfile, Id));
    }

    [Fact]
    public void Is_Info_because_two_deliberately_different_mappers_are_legitimate()
    {
        // Configuring two mappers differently is a design, not a defect, and [MapNullSkip] now removes the
        // reason the split was ever forced. What this adds is that the consequence is stated rather than
        // discovered — so it must not break a warnings-as-errors build.
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info,
            GeneratorAssert.Reports(SplitProfile, Id)[0].Severity);
    }
}
