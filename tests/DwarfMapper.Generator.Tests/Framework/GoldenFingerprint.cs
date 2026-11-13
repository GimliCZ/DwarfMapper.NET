// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DwarfMapper.Generator.Tests.Framework
{
    /// <summary>
    ///     sha256 over EVERYTHING observable for a case: every emitted file plus the full diagnostics, message text
    ///     included. Message text is in deliberately — nothing observable may move undetected. The cost is churn
    ///     when a message is reworded; the curated snapshots carry the readable diff for those.
    /// </summary>
    internal static class GoldenFingerprint
    {
        public static string Compute(GoldenCase c)
        {
            return Compute(Run(c));
        }

        /// <summary>The case's source run through the generator the case names.</summary>
        public static GeneratorRun Run(GoldenCase c)
        {
            ArgumentNullException.ThrowIfNull(c);

            var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
            return GeneratorRunner.Run(generator.Create(), c.Source);
        }

        /// <summary>
        ///     The fingerprint of a run already made, so a caller that inspects the run as well runs the generator
        ///     once (round 34 E5: <c>GoldenCaseTests</c>).
        /// </summary>
        public static string Compute(GeneratorRun run)
        {
            ArgumentNullException.ThrowIfNull(run);

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
}
