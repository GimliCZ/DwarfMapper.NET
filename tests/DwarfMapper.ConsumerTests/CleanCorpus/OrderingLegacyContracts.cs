// SPDX-License-Identifier: GPL-2.0-only

#nullable disable

namespace CleanCorpus.Ordering.Legacy
{
    // ═══ The un-annotated half ════════════════════════════════════════════════════════════════════════════════
// These types were written before nullable reference types existed and were never annotated, which is the
// state most .NET code in existence is still in: `#nullable disable` is legal, oblivious reference types are
// neither nullable nor non-nullable, and a migration that has reached the new code but not the old one has
// exactly this file in it. Both directions across that boundary are mapped, because both happen.

    public sealed class LegacyCustomerRecord
    {
        public string CustomerNumber { get; set; }

        public string Name { get; set; }

        public string Email { get; set; }

        public string Phone { get; set; }

        public DateTime? LastOrderedOn { get; set; }

        public List<LegacyLineRecord> Lines { get; set; }
    }

    public sealed class LegacyLineRecord
    {
        public string ProductCode { get; set; }

        public int Quantity { get; set; }

        public decimal LinePrice { get; set; }
    }
}
