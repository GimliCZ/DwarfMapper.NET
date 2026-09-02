// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     A diagnostic reported with <c>Location.None</c> must lead its message with its own id; one reported at
    ///     a real location must NOT.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A located diagnostic is rendered by the compiler as <c>File.cs(12,5): warning DWARF001: …</c> — the
    ///         id and the position are already there, and repeating the id in the message would read
    ///         "DWARF001: DWARF001: …". A location-less one gets none of that. Worse, IDEs title generator
    ///         diagnostics generically: Rider shows every one as
    ///         <c>Generator 'DwarfGenerator' failed to generate sources</c> — a string that lives in
    ///         JetBrains.Rider.Roslyn.Host.dll, is identical for a Warning and for a crash, and that we cannot
    ///         change. A consumer saw 128 of those and reported them as blocking bugs; they were one Warning,
    ///         DWARF063, repeated.
    ///     </para>
    ///     <para>
    ///         So the rule is conditional, and this scan derives the condition from the SOURCE rather than from a
    ///         hand-maintained list: it reads the report sites out of <c>DwarfGenerator.cs</c>. Add a new
    ///         location-less diagnostic and this fails until its message names itself — which is the point. A
    ///         hand-kept list would simply go stale, and this repo has been bitten by that shape before.
    ///     </para>
    /// </remarks>
    public class LocationlessDiagnosticsSelfIdentifyTests
    {
        // Matches: DiagnosticDescriptors.<Name>,  <optional trivia/comments>  Location.None
        private static readonly Regex LocationlessSite = new(
            @"DiagnosticDescriptors\.(?<name>\w+)\s*,\s*(?://[^\n]*\n\s*)*Location\.None",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex IdPrefix = new(@"^DWARF[A-Z]?\d+:\s", RegexOptions.Compiled);

        private static Dictionary<string, DiagnosticDescriptor> DescriptorsByFieldName()
        {
            return typeof(DiagnosticDescriptors)
                .GetFields()
                .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
                .ToDictionary(f => f.Name, f => (DiagnosticDescriptor)f.GetValue(null)!, StringComparer.Ordinal);
        }

        private static HashSet<string> LocationlessFieldNames()
        {
            var text = File.ReadAllText(Path.Combine(RepoPaths.GeneratorSrcDir, "DwarfGenerator.cs"));
            return LocationlessSite.Matches(text)
                .Select(m => m.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        [Fact]
        public void The_scan_finds_report_sites_at_all()
        {
            // Non-vacuity: if the regex ever stops matching (a refactor moves or reformats the report sites),
            // every assertion below would pass over an empty set and this gate would silently stop gating.
            var found = LocationlessFieldNames();

            Assert.NotEmpty(found);
            Assert.True(found.Count >= 4,
                $"Expected several location-less report sites, found {found.Count}: {string.Join(", ", found)}. " +
                "If they were genuinely removed, lower this floor deliberately; if the regex broke, fix the regex.");
        }

        [Fact]
        public void Every_locationless_diagnostic_leads_its_message_with_its_id()
        {
            var byField = DescriptorsByFieldName();
            var offenders = new List<string>();

            foreach (var field in LocationlessFieldNames().OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!byField.TryGetValue(field, out var d))
                {
                    continue;
                }

                var message = d.MessageFormat.ToString(CultureInfo.InvariantCulture);
                if (!message.StartsWith(d.Id + ": ", StringComparison.Ordinal))
                {
                    offenders.Add($"  {d.Id} ({field}) — message starts: \"{message[..Math.Min(60, message.Length)]}…\"");
                }
            }

            Assert.True(offenders.Count == 0,
                "These diagnostics are reported with Location.None, so the message text is the only thing that " +
                "identifies them — the IDE shows a generic title and there is no file or line. Prefix the " +
                $"MessageFormat with \"<id>: \".\n{string.Join("\n", offenders)}");
        }

        [Fact]
        public void A_diagnostic_reported_at_a_real_location_does_not_repeat_its_id()
        {
            var locationless = LocationlessFieldNames();
            var offenders = new List<string>();

            foreach (var (field, d) in DescriptorsByFieldName().OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (locationless.Contains(field))
                {
                    continue;
                }

                var message = d.MessageFormat.ToString(CultureInfo.InvariantCulture);
                if (IdPrefix.IsMatch(message))
                {
                    offenders.Add($"  {d.Id} ({field})");
                }
            }

            Assert.True(offenders.Count == 0,
                "These carry a real location, so the compiler already renders \"file(l,c): severity ID: message\". " +
                "Prefixing the message repeats the id and reads \"DWARF001: DWARF001: …\".\n" +
                string.Join("\n", offenders));
        }
    }
}
