// SPDX-License-Identifier: GPL-2.0-only

// 09 — Conditions, constants, and null substitution.
// Three more things other mappers do with lambdas, expressed declaratively:
//   When = nameof(P)        guard the assignment with a bool method (member keeps its default when false)
//   NullSubstitute = v      emit `src ?? v` for a nullable source
//   [MapValue(tgt, const)]  give a source-less destination member a constant (counts as mapped)
using System;
using DwarfMapper;

namespace DwarfMapper.Gallery.Ex09;

public sealed class Member { public string Name { get; set; } = ""; public string? Nickname { get; set; } public bool IsVip { get; set; } public int Score { get; set; } }
public sealed class MemberDto { public string Name { get; set; } = ""; public string Nickname { get; set; } = ""; public string Tier { get; set; } = ""; public int Score { get; set; } }

[DwarfMapper]
public partial class Mapper
{
    [MapProperty(nameof(Member.Nickname), nameof(MemberDto.Nickname), NullSubstitute = "(none)")]
    [MapValue(nameof(MemberDto.Tier), "guild")]
    [MapProperty(nameof(Member.Score), nameof(MemberDto.Score), When = nameof(IsActive))]
    public partial MemberDto ToDto(Member m);

    private static bool IsActive(Member m) => m.IsVip;   // Score is copied only for VIPs; others keep 0
}

public static class Example
{
    public static void Run()
    {
        MemberDto vip = new Mapper().ToDto(new Member { Name = "Thorin", Nickname = "Oakenshield", IsVip = true, Score = 90 });
        MemberDto plain = new Mapper().ToDto(new Member { Name = "Bombur", Nickname = null, IsVip = false, Score = 90 });
        Console.WriteLine($"09 When/Value/Null    -> {vip.Name} '{vip.Nickname}' {vip.Tier} score {vip.Score}; " +
                          $"{plain.Name} '{plain.Nickname}' score {plain.Score}");
    }
}
