// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public sealed class McSrc
{
    public string Id { get; set; } = "";
    public string Format { get; set; } = "";
    public string AliasText { get; set; } = "";
    public int Cost { get; set; }
    public bool Enabled { get; set; }
}

// Mirrors the real AliasCommand shape: Parsed is get-only (only the 2-arg ctor can populate it), Id is
// init-only (set at construction), and Cost/Enabled are ordinary settable members. A default-picked
// parameterless ctor would leave Parsed empty — so the map must go through the factory.
public sealed class McDst
{
    public McDst()
    {
    }

    public McDst(string format, string aliasText)
    {
        Format = format;
        Alias = aliasText;
        Parsed = aliasText?.ToUpperInvariant() ?? "";
        Id = "ctor-id";
    }

    public string Id { get; init; } = "";
    public string Format { get; } = "";
    public string Alias { get; } = "";
    public string Parsed { get; } = "";
    public int Cost { get; set; }
    public bool Enabled { get; set; }
}

[DwarfMapper]
[GenerateMap<McSrc, McDst>]
[MapConstructor<McSrc, McDst>(nameof(MakeDst))]
public partial class McMapper
{
    private static McDst MakeDst(McSrc s)
    {
        return new McDst(s.Format, s.AliasText);
    }
}

public sealed class MapConstructorRuntimeTests
{
    [Fact]
    public void Factory_constructs_then_settable_members_are_populated()
    {
        var d = new McMapper().Map(new McSrc
        {
            Id = "src-id", Format = "!buy", AliasText = "song", Cost = 5, Enabled = true
        });

        // From the factory's 2-arg ctor:
        Assert.Equal("!buy", d.Format);
        Assert.Equal("song", d.Alias);
        Assert.Equal("SONG", d.Parsed); // get-only: only the ctor could have set this

        // Settable members populated after construction (ConstructUsing semantics):
        Assert.Equal(5, d.Cost);
        Assert.True(d.Enabled);

        // init-only Id is the factory's responsibility and is NOT overwritten from the source:
        Assert.Equal("ctor-id", d.Id);
    }
}
