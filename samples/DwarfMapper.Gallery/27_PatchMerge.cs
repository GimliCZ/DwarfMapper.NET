// SPDX-License-Identifier: GPL-2.0-only

// 27 — PATCH-MERGE: letting a null in the source mean "leave this alone" instead of "clear this".
//
// This is the shape behind a PATCH endpoint, a partial settings update, or any merge where the caller sends
// only the fields they want to change. Without it, every unsent field arrives as null and wipes the value
// that was already there.
//
// `SkipNullSourceMembers` is the switch, and [MapNullSkip] narrows it to one map — because a mapper usually
// needs BOTH behaviours: a full replace where null clears, and a patch where null is silence.
//
// (Migrating from AutoMapper? This is `ForAllMembers(o => o.Condition((_,_,src) => src != null))`. That was
// configured per map, which is exactly why the option needs a scope narrower than the class.)

namespace DwarfMapper.Gallery.Ex27;

/// <summary>What the caller sends: every member nullable, because "absent" has to be expressible.</summary>
public sealed class AnvilPatchDto
{
    public string? Owner { get; set; }
    public string? Inscription { get; set; }
    public int? Weight { get; set; }
}

/// <summary>
///     What the store already holds. The two text members are nullable because "cleared" is a real state for
///     them — and because the generator insists: a non-nullable member fed from a nullable source raises
///     <c>DWARF070</c>, which is precisely the question "what should a replace write here?" asked at build
///     time. Modelling the answer beats suppressing the question.
/// </summary>
public sealed class Anvil
{
    public string? Owner { get; set; }
    public string? Inscription { get; set; }
    public int Weight { get; set; }
}

// <snippet: patch-merge>
// NullStrategy.SetDefault decides what a null BECOMES when it is written (int? -> int would otherwise
// throw); [MapNullSkip] decides whether it is written at all. The two are orthogonal and compose.
[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
public partial class Mapper
{
    /// <summary>Full replace — an absent field CLEARS the stored value.</summary>
    public partial void Replace(AnvilPatchDto src, Anvil dest);

    /// <summary>Patch-merge — an absent field LEAVES the stored value alone.</summary>
    [MapNullSkip]
    public partial void Patch(AnvilPatchDto src, Anvil dest);
}
// </snippet>

[DocExample(27, Tier.Advanced, "Patch-merge: a null source member leaves the destination alone",
    Shows = "[MapNullSkip] scoping SkipNullSourceMembers to one map, so replace and patch coexist")]
public static class Example
{
    public static void Run()
    {
        var mapper = new Mapper();

        // The caller is renaming the owner and says nothing about the rest.
        var patch = new AnvilPatchDto { Owner = "Durin" };

        var patched = Stored();
        mapper.Patch(patch, patched);

        var replaced = Stored();
        mapper.Replace(patch, replaced);

        // Same mapper, same input, opposite intent — and both are stated in the code rather than implied.
        // Note "cleared" means DEFAULT: default(string) is null, not "".
        Console.WriteLine(
            $"27 Patch-merge        -> patch keeps \"{patched.Inscription}\" ({patched.Weight}kg); "
            + $"replace clears it to {Show(replaced.Inscription)} ({replaced.Weight}kg)");
    }

    private static Anvil Stored() => new()
    {
        Owner = "Narvi",
        Inscription = "Speak friend and enter",
        Weight = 300
    };

    private static string Show(string? value) => value is null ? "null" : $"\"{value}\"";
}
