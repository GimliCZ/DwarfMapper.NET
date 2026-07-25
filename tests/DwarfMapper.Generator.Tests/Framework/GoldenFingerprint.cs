// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>
///     sha256 over EVERYTHING observable for a case: every emitted file plus the full diagnostics, message text
///     included. Message text is in deliberately — nothing observable may move undetected. The cost is churn
///     when a message is reworded; the curated snapshots carry the readable diff for those.
/// </summary>
internal static class GoldenFingerprint
{
    public static string Compute(GoldenCase c)
    {
        ArgumentNullException.ThrowIfNull(c);

        var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
        var run = GeneratorRunner.Run(generator.Create(), c.Source);

        var sb = new StringBuilder();
        sb.Append(run.AllOutputsConcatenated);
        sb.Append("\n|DIAGNOSTICS|\n");

        // Emission order is not guaranteed stable, so sort before hashing.
        foreach (var d in run.Diagnostics
                     .OrderBy(d => d.Id, StringComparer.Ordinal)
                     .ThenBy(d => d.Location.ToString(), StringComparer.Ordinal)
                     .ThenBy(d => d.GetMessage(CultureInfo.InvariantCulture), StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture,
                $"{d.Id}:{d.Severity}:{d.Location}:{d.GetMessage(CultureInfo.InvariantCulture)}\n");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
