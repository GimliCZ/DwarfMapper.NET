// SPDX-License-Identifier: GPL-2.0-only
#nullable enable

using Xunit;

namespace DwarfMapper.IntegrationTests;

// The trust-boundary guard, proven at run time.
//
// Mass assignment (OWASP API6): an untrusted input whose field names line up with a domain entity silently
// over-posts protected fields. With DwarfMapper's default by-name auto-matching, `IsAdmin` from the input would
// be copied straight onto the entity. Under [DwarfMapper(AutoMatchMembers = false)] nothing is auto-wired — the
// developer maps only the fields a request is allowed to change, and the protected field is ignored, so no
// value from the untrusted input can reach it regardless of what the attacker sends.
//
// A generator test proves DWARF072 fires; this proves the actual behaviour: the protected field keeps the
// entity's own value, not the attacker's.

public sealed class AccountUpdateInput
{
    public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public decimal Balance { get; set; }
}

public sealed class AccountEntity
{
    public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public decimal Balance { get; set; }
}

[DwarfMapper(AutoMatchMembers = false)]
[MapIgnore("IsAdmin")]
[MapIgnore("Balance")]
public partial class AccountUpdateMapper
{
    // Only DisplayName is mappable from an untrusted update. IsAdmin and Balance are protected: ignored, so
    // the request cannot touch them.
    [MapProperty(nameof(AccountUpdateInput.DisplayName), nameof(AccountEntity.DisplayName))]
    public partial AccountEntity Map(AccountUpdateInput input);
}

public class MassAssignmentGuardRuntimeTests
{
    [Fact]
    public void A_protected_field_cannot_be_over_posted_from_untrusted_input()
    {
        // The attacker sets everything they can.
        var hostile = new AccountUpdateInput { DisplayName = "Mallory", IsAdmin = true, Balance = 1_000_000m };

        var entity = new AccountUpdateMapper().Map(hostile);

        // The one allowed field flows.
        Assert.Equal("Mallory", entity.DisplayName);

        // The protected fields do NOT — they keep the freshly-constructed entity's defaults, not the
        // attacker's values. IsAdmin=true and Balance=1,000,000 never reach the entity.
        Assert.False(entity.IsAdmin);
        Assert.Equal(0m, entity.Balance);
    }
}
