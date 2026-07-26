// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DwarfMapper.DocTooling;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Every prose section in <c>docs/diagnostics.md</c> opens with a line that restates the diagnostic's
///     title and severity — mechanical facts the descriptors already own. This asserts the restatement still
///     matches, so a title reworded or a severity re-tiered in code cannot leave the page quietly wrong.
///     <para>
///         Not marker-injected like the option tables, deliberately: the header is one line per section in a
///         77-section document, and wrapping each in a marker pair would add more ceremony than it removes.
///         Comparing is enough — there is nothing here a human would want to write differently.
///     </para>
///     <para>
///         This is the guard the mutation battery's M21 was written for. That mutant downgrades an ERROR's
///         <c>**Fix:**</c> to optional advice; this test covers the neighbouring decay, where the severity
///         itself changes in code and the prose keeps asserting the old one.
///     </para>
/// </summary>
public class DiagnosticProseIsCurrentTests
{
    private static readonly Regex SectionHeader =
        new(@"^## (dwarf\d{3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    ///     Ids whose prose deliberately says MORE about severity than <c>DefaultSeverity</c> can. For these the
    ///     descriptor's severity must still appear in the phrase, but the phrase may qualify it. Keep this as
    ///     small as possible; every entry needs a reason, and the list must only shrink.
    /// </summary>
    private static readonly Dictionary<string, string> RicherSeverityProse = new(StringComparer.Ordinal)
    {
        ["DWARF038"] = "severity is computed per conversion (lossy -> Warning, otherwise Info, escalating to "
                       + "Error under ImplicitConversions = false), so a flat DefaultSeverity understates it"
    };

    private static List<DiagnosticDescriptor> Descriptors() =>
        typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToList();

    private static string[] Prose() =>
        DocSet.Read("docs/diagnostics.md").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    [Fact]
    public void Every_section_restates_its_diagnostics_real_title_and_severity()
    {
        var byId = Descriptors().ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var lines = Prose();
        var wrong = new List<string>();
        var checkedSections = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = SectionHeader.Match(lines[i].Trim());
            if (!match.Success) continue;

            var id = match.Groups[1].Value.ToUpperInvariant();
            if (!byId.TryGetValue(id, out var descriptor))
            {
                wrong.Add($"{id}: has a prose section but no descriptor — retired or renamed?");
                continue;
            }

            var header = lines.Skip(i + 1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            var title = descriptor.Title.ToString(CultureInfo.InvariantCulture);
            var severity = descriptor.DefaultSeverity.ToString();
            checkedSections++;

            // Backticks are stripped before comparing: prose formats `[MapCollectionKey]` as code, which a
            // descriptor Title cannot carry. That is the doc being better than the string, not drift.
            var split = header.LastIndexOf('·');
            var proseTitle = split < 0 ? header : header[..split].Trim();
            var proseSeverity = split < 0 ? "" : header[(split + 1)..].Trim();

            if (!string.Equals(Strip(proseTitle), Strip($"**{title}**"), StringComparison.Ordinal))
            {
                wrong.Add($"{id} title:\n    prose: {proseTitle}\n    code:  **{title}**");
                continue;
            }

            var severityOk = RicherSeverityProse.ContainsKey(id)
                ? proseSeverity.Contains(severity, StringComparison.Ordinal)
                : string.Equals(proseSeverity, severity, StringComparison.Ordinal);

            if (!severityOk)
                wrong.Add($"{id} severity:\n    prose: {proseSeverity}\n    code:  {severity}");
        }

        Assert.True(checkedSections >= 60,
            $"Only {checkedSections} diagnostic sections were checked — the section-header pattern has "
            + "probably changed, which would make this test vacuous rather than passing.");

        Assert.True(wrong.Count == 0,
            "docs/diagnostics.md restates a title or severity the descriptors no longer agree with. The code "
            + "is the truth; update the prose:\n  " + string.Join("\n  ", wrong));
    }

    private static string Strip(string text) => text.Replace("`", "", StringComparison.Ordinal);

    [Fact]
    public void Every_richer_severity_entry_is_still_needed()
    {
        // An entry left behind after a descriptor was simplified would quietly re-permit a mismatch.
        var lines = Prose();
        var byId = Descriptors().ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var unnecessary = new List<string>();

        foreach (var id in RicherSeverityProse.Keys)
        {
            var index = Array.FindIndex(lines,
                l => string.Equals(l.Trim(), $"## {id}", StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;

            var header = lines.Skip(index + 1).FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            var split = header.LastIndexOf('·');
            var proseSeverity = split < 0 ? "" : header[(split + 1)..].Trim();

            if (byId.TryGetValue(id, out var d)
                && string.Equals(proseSeverity, d.DefaultSeverity.ToString(), StringComparison.Ordinal))
                unnecessary.Add(id);
        }

        Assert.True(unnecessary.Count == 0,
            "RicherSeverityProse names id(s) whose prose now states the plain severity — remove them so the "
            + "check tightens: " + string.Join(", ", unnecessary));
    }

    [Fact]
    public void Every_diagnostic_the_generator_can_report_has_a_prose_section()
    {
        // The generated index proves the LIST is complete; this proves someone explained each one.
        var documented = Prose()
            .Select(l => SectionHeader.Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var undocumented = Descriptors()
            .Select(d => d.Id)
            .Where(id => !documented.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(undocumented.Count == 0,
            "Diagnostic(s) the generator can report with no '## dwarfNNN' section in docs/diagnostics.md. A "
            + "reader who hits one has nothing to read:\n  " + string.Join("\n  ", undocumented));
    }
}
