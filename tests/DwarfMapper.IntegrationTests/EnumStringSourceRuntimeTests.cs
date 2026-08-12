// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;
using System.Runtime.Serialization;

namespace DwarfMapper.IntegrationTests;

// ── The enum that nearly cost a database ──────────────────────────────────────
// NextDay carries [Description("Next-Day")] for a combo-box label. Under the default the annotation becomes the
// PERSISTED string, so a migration off .ToString() would have begun writing "Next-Day" into a store full of
// "NextDay" — breaking reads of every existing document. DWARF083 reports it; EnumStringSource is the switch.
public enum EssDispatchChannel
{
    [Description("Next-Day")] NextDay,

    // A second precedence level, so the test covers what actually wins rather than just "an attribute".
    [EnumMember(Value = "standard-post")] [Description("Standard (display)")]
    Standard,

    Direct
}

public class EssDispatch
{
    public EssDispatchChannel Source { get; set; }
}

public class EssDispatchDoc
{
    public string Source { get; set; } = "";
}

/// <summary>The default — <c>EnumStringSource.Attribute</c>, stated explicitly so the value is exercised.</summary>
[DwarfMapper(EnumStringSource = EnumStringSource.Attribute)]
[GenerateMap<EssDispatch, EssDispatchDoc>]
[GenerateMap<EssDispatchDoc, EssDispatch>]
public partial class EssAttributeMapper;

/// <summary>The parity switch — the identifier, exactly as <c>Enum.ToString()</c> produces it.</summary>
[DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
[GenerateMap<EssDispatch, EssDispatchDoc>]
[GenerateMap<EssDispatchDoc, EssDispatch>]
public partial class EssIdentifierMapper;

public class EnumStringSourceRuntimeTests
{
    [Theory]
    [InlineData(EssDispatchChannel.NextDay, "Next-Day")]
    [InlineData(EssDispatchChannel.Standard, "standard-post")] // [EnumMember] beats [Description]
    [InlineData(EssDispatchChannel.Direct, "Direct")] // no annotation → the identifier anyway
    public void Attribute_writes_the_annotated_text(EssDispatchChannel source, string expected)
    {
        Assert.Equal(expected, new EssAttributeMapper().Map(new EssDispatch { Source = source }).Source);
    }

    [Theory]
    [InlineData(EssDispatchChannel.NextDay)]
    [InlineData(EssDispatchChannel.Standard)]
    [InlineData(EssDispatchChannel.Direct)]
    public void Identifier_writes_exactly_what_ToString_would(EssDispatchChannel source)
    {
        // The parity claim, asserted against the thing it claims parity with rather than a hand-typed literal.
        Assert.Equal(source.ToString(),
            new EssIdentifierMapper().Map(new EssDispatch { Source = source }).Source);
    }

    [Theory]
    [InlineData(EssDispatchChannel.NextDay)]
    [InlineData(EssDispatchChannel.Standard)]
    [InlineData(EssDispatchChannel.Direct)]
    public void Identifier_round_trips(EssDispatchChannel source)
    {
        // Both directions or neither: the parse switch matches on the serialized text, so a writer and a
        // reader that disagree do not merely store the wrong string — the reader throws on every row.
        var mapper = new EssIdentifierMapper();
        var written = mapper.Map(new EssDispatch { Source = source });

        Assert.Equal(source, mapper.Map(written).Source);
    }

    [Theory]
    [InlineData(EssDispatchChannel.NextDay)]
    [InlineData(EssDispatchChannel.Standard)]
    [InlineData(EssDispatchChannel.Direct)]
    public void Attribute_round_trips_too(EssDispatchChannel source)
    {
        var mapper = new EssAttributeMapper();
        var written = mapper.Map(new EssDispatch { Source = source });

        Assert.Equal(source, mapper.Map(written).Source);
    }

    [Fact]
    public void The_two_settings_coexist_over_one_enum_in_one_assembly()
    {
        // The design hazard behind the option: the synthesized helper used to be keyed by TYPE alone, so two
        // mappers choosing differently for the same enum would have shared one helper and whichever was
        // synthesized first would have decided the persisted format for both — silently. Folding the strategy
        // into the helper's name is what makes this assertion possible at all.
        var donation = new EssDispatch { Source = EssDispatchChannel.NextDay };

        Assert.Equal("Next-Day", new EssAttributeMapper().Map(donation).Source);
        Assert.Equal("NextDay", new EssIdentifierMapper().Map(donation).Source);
    }

    [Fact]
    public void An_identifier_writer_and_an_attribute_reader_do_not_agree()
    {
        // Stated as a test rather than a comment: this is the failure the option exists to let a consumer
        // avoid, and it is what "you must choose one and mean it" costs if you get it wrong.
        var written = new EssIdentifierMapper().Map(new EssDispatch { Source = EssDispatchChannel.NextDay });

        Assert.Throws<ArgumentOutOfRangeException>(() => new EssAttributeMapper().Map(written));
    }
}
