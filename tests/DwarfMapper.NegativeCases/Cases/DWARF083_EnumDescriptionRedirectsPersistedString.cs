// SPDX-License-Identifier: GPL-2.0-only
// CASE: [Description] on an enum member quietly becoming the persisted string
// WHY:  Round 18 came within one code review of shipping this. DonationSource.Kofi carried
//       [Description("Ko-Fi")], the previous mapper used .ToString(), and the migration would have started
//       writing "Ko-Fi" into a store full of "Kofi" — breaking reads of every existing document. The
//       precedence is a deliberate feature (InProgress -> "in_progress" with no converter); the hazard is
//       that [Description] is overwhelmingly a DISPLAY annotation, so the message must say so.
// EXPECT: DWARF083
// EXPECT-MESSAGE DWARF083: DonationSource
// EXPECT-MESSAGE DWARF083: Kofi
// EXPECT-MESSAGE DWARF083: Ko-Fi
// EXPECT-MESSAGE DWARF083: display

using System.ComponentModel;
using DwarfMapper;

namespace Demo;

public enum DonationSource
{
    [Description("Ko-Fi")] Kofi,
    Patreon
}

public class Donation
{
    public DonationSource Source { get; set; }
}

public class DonationDocument
{
    public string Source { get; set; } = "";
}

[DwarfMapper]
[GenerateMap<Donation, DonationDocument>]
public partial class M;
