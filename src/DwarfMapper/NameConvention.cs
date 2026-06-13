// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper;

/// <summary>
/// Member-name matching strategy, set via <see cref="DwarfMapperAttribute.NameConvention"/>.
/// </summary>
public enum NameConvention
{
    /// <summary>
    /// (Default) Exact name matching (honouring <see cref="DwarfMapperAttribute.CaseInsensitive"/>).
    /// </summary>
    Exact = 0,

    /// <summary>
    /// Flexible matching across casing styles: <c>PascalCase</c>, <c>camelCase</c>, <c>snake_case</c> and
    /// <c>UPPER_CASE</c> are interchangeable (names are normalized by removing <c>_</c> and lowercasing
    /// before matching). A post-normalization collision — two source members reducing to one target — is
    /// the build error <c>DWARF048</c>.
    /// </summary>
    Flexible = 1,
}
