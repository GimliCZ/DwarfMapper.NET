// SPDX-License-Identifier: GPL-2.0-only

using System.ComponentModel;
using System.Runtime.Serialization;

namespace DwarfMapper.IntegrationTests;

// ── The enum that nearly cost a database ──────────────────────────────────────
// Kofi carries [Description("Ko-Fi")] for a combo-box label. Under the default the annotation becomes the
// PERSISTED string, so a migration off .ToString() would have begun writing "Ko-Fi" into a store full of
// "Kofi" — breaking reads of every existing document. DWARF083 reports it; EnumStringSource is the switch.
public enum EssDonationSource
{
    [Description("Ko-Fi")] Kofi,

    // A second precedence level, so the test covers what actually wins rather than just "an attribute".
    [EnumMember(Value = "patreon.com")] [Description("Patreon (display)")]
    Patreon,

    Direct
}

public class EssDonation
{
    public EssDonationSource Source { get; set; }
}

public class EssDonationDoc
{
    public string Source { get; set; } = "";
}

/// <summary>The default — <c>EnumStringSource.Attribute</c>, stated explicitly so the value is exercised.</summary>
[DwarfMapper(EnumStringSource = EnumStringSource.Attribute)]
[GenerateMap<EssDonation, EssDonationDoc>]
[GenerateMap<EssDonationDoc, EssDonation>]
public partial class EssAttributeMapper;

/// <summary>The parity switch — the identifier, exactly as <c>Enum.ToString()</c> produces it.</summary>
[DwarfMapper(EnumStringSource = EnumStringSource.Identifier)]
[GenerateMap<EssDonation, EssDonationDoc>]
[GenerateMap<EssDonationDoc, EssDonation>]
public partial class EssIdentifierMapper;

public class EnumStringSourceRuntimeTests
{
    [Theory]
    [InlineData(EssDonationSource.Kofi, "Ko-Fi")]
    [InlineData(EssDonationSource.Patreon, "patreon.com")] // [EnumMember] beats [Description]
    [InlineData(EssDonationSource.Direct, "Direct")] // no annotation → the identifier anyway
    public void Attribute_writes_the_annotated_text(EssDonationSource source, string expected)
    {
        Assert.Equal(expected, new EssAttributeMapper().Map(new EssDonation { Source = source }).Source);
    }

    [Theory]
    [InlineData(EssDonationSource.Kofi)]
    [InlineData(EssDonationSource.Patreon)]
    [InlineData(EssDonationSource.Direct)]
    public void Identifier_writes_exactly_what_ToString_would(EssDonationSource source)
    {
        // The parity claim, asserted against the thing it claims parity with rather than a hand-typed literal.
        Assert.Equal(source.ToString(),
            new EssIdentifierMapper().Map(new EssDonation { Source = source }).Source);
    }

    [Theory]
    [InlineData(EssDonationSource.Kofi)]
    [InlineData(EssDonationSource.Patreon)]
    [InlineData(EssDonationSource.Direct)]
    public void Identifier_round_trips(EssDonationSource source)
    {
        // Both directions or neither: the parse switch matches on the serialized text, so a writer and a
        // reader that disagree do not merely store the wrong string — the reader throws on every row.
        var mapper = new EssIdentifierMapper();
        var written = mapper.Map(new EssDonation { Source = source });

        Assert.Equal(source, mapper.Map(written).Source);
    }

    [Theory]
    [InlineData(EssDonationSource.Kofi)]
    [InlineData(EssDonationSource.Patreon)]
    [InlineData(EssDonationSource.Direct)]
    public void Attribute_round_trips_too(EssDonationSource source)
    {
        var mapper = new EssAttributeMapper();
        var written = mapper.Map(new EssDonation { Source = source });

        Assert.Equal(source, mapper.Map(written).Source);
    }

    [Fact]
    public void The_two_settings_coexist_over_one_enum_in_one_assembly()
    {
        // The design hazard behind the option: the synthesized helper used to be keyed by TYPE alone, so two
        // mappers choosing differently for the same enum would have shared one helper and whichever was
        // synthesized first would have decided the persisted format for both — silently. Folding the strategy
        // into the helper's name is what makes this assertion possible at all.
        var donation = new EssDonation { Source = EssDonationSource.Kofi };

        Assert.Equal("Ko-Fi", new EssAttributeMapper().Map(donation).Source);
        Assert.Equal("Kofi", new EssIdentifierMapper().Map(donation).Source);
    }

    [Fact]
    public void An_identifier_writer_and_an_attribute_reader_do_not_agree()
    {
        // Stated as a test rather than a comment: this is the failure the option exists to let a consumer
        // avoid, and it is what "you must choose one and mean it" costs if you get it wrong.
        var written = new EssIdentifierMapper().Map(new EssDonation { Source = EssDonationSource.Kofi });

        Assert.Throws<ArgumentOutOfRangeException>(() => new EssAttributeMapper().Map(written));
    }
}
