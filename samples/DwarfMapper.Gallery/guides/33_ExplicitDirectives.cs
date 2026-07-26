// SPDX-License-Identifier: GPL-2.0-only

// 33 — The three ways to satisfy the completeness gate for a destination member with no obvious source.
// This is the triage a migrating reader performs on every DWARF001: the member was renamed, it is a constant,
// or dropping it is deliberate. There is no fourth option, and that is the point — "I forgot" cannot compile.

namespace DwarfMapper.Gallery.Guides.G33;

public sealed class Src
{
    public string Existing { get; set; } = "";
}

public sealed class Dst
{
    public string Renamed { get; set; } = "";
    public string Source { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

[DwarfMapper]
public partial class DirectiveMapper
{
    // <snippet: explicit-directives>
    [MapProperty(nameof(Src.Existing), nameof(Dst.Renamed))]  // it had a differently-named source
    [MapValue(nameof(Dst.Source), "api-v2")]                  // it's a constant/computed value
    [MapIgnore(nameof(Dst.PasswordHash))]                     // dropping it is intentional and audited
    public partial Dst ToDst(Src s);
    // </snippet>
}

[DocExample(33, Tier.Guides, "Satisfying the completeness gate",
    Shows = "`[MapProperty]` / `[MapValue]` / `[MapIgnore]` — the three answers to `DWARF001`")]
public static class Example
{
    public static void Run()
    {
        var dst = new DirectiveMapper().ToDst(new Src { Existing = "kept" });

        Console.WriteLine(
            $"33 Explicit directives-> {dst.Renamed}, {dst.Source}, hash='{dst.PasswordHash}' (ignored)");
    }
}
