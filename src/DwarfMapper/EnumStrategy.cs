// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>How enum-to-enum mappings are resolved.</summary>
public enum EnumStrategy
{
    /// <summary>Match members by name (default); a source member with no same-named destination member is a build error.</summary>
    ByName = 0,

    /// <summary>Match members by their underlying numeric value (a cast).</summary>
    ByValue = 1
}
