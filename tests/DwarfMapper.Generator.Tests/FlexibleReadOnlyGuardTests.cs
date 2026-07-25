// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Audit R7: the read-only silent-loss guard (DWARF007) queried <c>sourceGroups</c> with the RAW target
///     name, but under <c>NameConvention.Flexible</c> its keys are <c>NormalizeName</c>d — so a get-only
///     destination whose flexibly-matching source exists was dropped with NO diagnostic. Every other
///     <c>sourceGroups</c> lookup normalizes; this one didn't. Uses the declared-partial-method path (where
///     flexible matching is wired) with snake_case source vs PascalCase destination, so the raw lookup provably
///     misses the normalized key.
/// </summary>
public class FlexibleReadOnlyGuardTests
{
    [Fact]
    public void Flexible_mode_still_reports_read_only_destination_silent_loss()
    {
        // snake_case source `user_code` (settable) → PascalCase get-only destination `UserCode`, both normalize
        // to "usercode". The source value can only reach the get-only member, which it cannot, so DWARF007 must
        // fire. It is the ONLY signal (read-only members are not in WritableMembers, so no DWARF001). Before the
        // fix the raw lookup "UserCode" missed the normalized key "usercode" and Flexible dropped it silently.
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Id { get; set; } public int user_code { get; set; } }
                         public class Dst { public int Id { get; set; } public int UserCode { get; } }
                         [DwarfMapper(NameConvention = NameConvention.Flexible)]
                         public partial class M { public partial Dst Map(Src s); }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);

        Assert.Contains(diags, d => d.Id == "DWARF007");
    }
}
