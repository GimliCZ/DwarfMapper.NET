// SPDX-License-Identifier: GPL-2.0-only

// Assembly-scanning / reflection-driven self-validation suite.
// Purpose: catch FORGOTTEN registrations and missing coverage when new
// diagnostics / attributes / enum values are added.
//
//  1. DWARF descriptor <=> AnalyzerReleases sync (both directions + metadata match)
//  2. No dead/orphan diagnostic (every descriptor is actually emittable)
//  3. Every diagnostic id has a triggering test
//  4. SUPERSEDED — see the banner at scan 4's former position
//  5. Every public enum VALUE has at least one test reference
//  6. TargetKind completeness via [InternalsVisibleTo] from the generator

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Pipeline;
using DwarfMapper.Generator.Registry;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Ids that are intentionally reserved and must have NO descriptor field.
///     DWARF004 — reserved since initial design (do not reuse).
///     DWARF006 — superseded by DWARF026 (NoMappableConstructor); descriptor removed, id retired.
///     DWARF029 — reserved since initial design (do not reuse).
/// </summary>
file static class ReservedIds
{
    public static readonly IReadOnlySet<string> Ids = new HashSet<string>(StringComparer.Ordinal)
    {
        "DWARF004",
        "DWARF006",
        "DWARF019", // retired; superseded by DWARF028 (ProjectionNotTranslatable)
        "DWARF029"
    };
}

/// <summary>
///     DWARF ids for which triggering a test is accepted as genuinely impractical at the
///     generator-test level. Keep this list as small as possible; every entry must have a
///     justification. This list must only SHRINK — adding entries requires explicit justification.
/// </summary>
file static class DiagnosticTestAllowlist
{
    // intentionally empty — every non-reserved id must appear in at least one test source
    public static readonly IReadOnlySet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
///     DWARF0xx ids that existed before <c>CHANGELOG.md</c> did. The file's own preamble mandates an entry
///     for every new/retired/re-severitied diagnostic id — Scan9 below enforces it — but that mandate is
///     forward-looking: it was written the same round <c>CHANGELOG.md</c> was created (see the file's own
///     "This file" bullet under <c>### Added</c>), and cannot retroactively demand prose for a diagnostic
///     that shipped before the file existed to hold it.
///     <para>
///         This set is CLOSED, not an escape hatch: it is exactly "DWARF0xx ids present when
///         <c>CHANGELOG.md</c> was introduced, minus the ones already documented at that point", frozen at
///         commit b723ece (2026-08-16). It may only SHRINK — remove an id the moment somebody writes its
///         entry — and Scan9 pins it by EXACT membership (not a count) so one id silently swapping for
///         another fails the build instead of passing as a wash. The eventual write-up is tracked as a
///         `LATER` item in <c>Issues/round20/CARRY-FORWARD.md</c> §1.
///     </para>
///     <para>
///         Scope: DWARF0xx only, deliberately. The DWARFR (registry) family is announced in
///         <c>CHANGELOG.md</c> as the range "DWARFR01–DWARFR09" (see the <c>### Added</c> entry for
///         ISSUE-047) — a per-id substring scan would fail on DWARFR02..08 despite the family being fully
///         announced. Do not "fix" that by adding DWARFR ids here or to Scan9; the range notation is the
///         intended announcement.
///     </para>
/// </summary>
file static class PredatesTheChangelog
{
    public static readonly IReadOnlySet<string> Ids = new HashSet<string>(StringComparer.Ordinal)
    {
        "DWARF002", "DWARF003", "DWARF005", "DWARF007", "DWARF008", "DWARF009", "DWARF010",
        "DWARF011", "DWARF012", "DWARF013", "DWARF015", "DWARF016", "DWARF017", "DWARF018",
        "DWARF020", "DWARF021", "DWARF022", "DWARF023", "DWARF024", "DWARF025", "DWARF026",
        "DWARF027", "DWARF028", "DWARF030", "DWARF031", "DWARF032", "DWARF033", "DWARF034",
        "DWARF035", "DWARF036", "DWARF037", "DWARF038", "DWARF039", "DWARF040", "DWARF041",
        "DWARF042", "DWARF044", "DWARF046", "DWARF047", "DWARF048", "DWARF049", "DWARF050",
        "DWARF051", "DWARF052", "DWARF053", "DWARF054", "DWARF055", "DWARF056", "DWARF057",
        "DWARF058", "DWARF059", "DWARF060", "DWARF061", "DWARF062", "DWARF064", "DWARF065",
        "DWARF066", "DWARF067", "DWARF068", "DWARF069", "DWARF070", "DWARF071", "DWARF072",
        "DWARF073", "DWARF074", "DWARF075", "DWARF076", "DWARF077", "DWARF078", "DWARF079",
        "DWARF080", "DWARF081", "DWARF082", "DWARF083", "DWARF084", "DWARF085"
    };
}

public sealed class AssemblyScanTests
{
    // ── Assembly & path resolution ────────────────────────────────────────────

    private static readonly Assembly DwarfMapperAssembly =
        typeof(DwarfMapperAttribute).Assembly;

    /// <summary>
    ///     Read all test source text into a single concatenated blob for substring scanning.
    ///     Cached per test run.
    /// </summary>
    private static readonly Lazy<string> AllTestSourceText = new(() =>
        string.Concat(TestSources().Select(File.ReadAllText)));

    private static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    ///     Walk upward from the test assembly location to find the repository root
    ///     (identified by the presence of "DwarfMapper.NET.sln").
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AssemblyScanTests).Assembly.Location)!);

        while (dir != null)
        {
            if (dir.GetFiles("DwarfMapper.NET.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Cannot locate repository root: no DwarfMapper.NET.sln found walking upward from " +
            typeof(AssemblyScanTests).Assembly.Location);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>Enumerate all .cs source files under a relative sub-path of the repo.</summary>
    private static IEnumerable<string> EnumerateSources(string subPath)
    {
        return Directory.EnumerateFiles(
                Path.Combine(RepoRoot, subPath), "*.cs",
                SearchOption.AllDirectories)
            // Exclude generated obj/ artefacts
            .Where(f => !f.Contains(
                Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal));
    }

    private static IEnumerable<string> GeneratorSources()
    {
        return EnumerateSources(Path.Combine("src", "DwarfMapper.Generator"));
    }

    private static IEnumerable<string> TestSources()
    {
        return EnumerateSources("tests");
    }

    // ── Self-validation: every [DwarfMapper] option must be exercised by a test ──
    // This is the guard that would have caught a new option (e.g. AllowNonPublic) shipping with no test:
    // every public settable property on DwarfMapperAttribute must be named somewhere in the test sources.
    [Fact]
    public void Scan5_Every_DwarfMapper_option_has_a_test_reference()
    {
        var testText = AllTestSourceText.Value;

        var untested = typeof(DwarfMapperAttribute)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .Where(name => !testText.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(untested.Count == 0,
            "[DwarfMapper] option(s) with no test reference (add a test exercising the option):\n" +
            string.Join("\n", untested));
    }

    // ── Self-validation: no orphan Verify snapshots ──
    // Every `<Class>.<Method>.verified.txt` must correspond to a test method still present in the source.
    // A stale snapshot (method renamed/deleted) is otherwise invisible — it just sits unused forever.
    [Fact]
    public void Scan6_Every_snapshot_has_a_live_test_method()
    {
        var testText = AllTestSourceText.Value;

        const string suffix = ".verified.txt";
        var orphans = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "tests"), "*" + suffix, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)
            .Select(name => name[..^suffix.Length]) // strip ".verified.txt"
            .Select(stem => stem.Split('.').Last()) // "Class.Method" → "Method"
            .Where(method => !testText.Contains(method, StringComparison.Ordinal))
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "Orphan snapshot(s) — a .verified.txt exists with no matching test method (delete the snapshot or restore the test):\n" +
            string.Join("\n", orphans));
    }

    // ── Self-HEAL (heal-or-fail): AnalyzerReleases rows stay in sync with the descriptors ──
    // Normally asserts the invariant (every descriptor has a release row). Run with DWARF_SELF_HEAL=1 to
    // instead APPEND a row for every descriptor that lacks one (the common drift when a new DWARF diagnostic
    // is added) and then pass — turning the validator into a one-command fixer.
    [Fact]
    public void SelfHeal_AnalyzerReleases_rows_are_in_sync()
    {
        var path = Path.Combine(RepoRoot, "src", "DwarfMapper.Generator", "AnalyzerReleases.Unshipped.md");
        var existing = ParseAnalyzerReleases();

        var missing = GetAllDescriptors()
            .Select(d => d.Descriptor)
            .Where(d => !existing.ContainsKey(d.Id))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0 && Environment.GetEnvironmentVariable("DWARF_SELF_HEAL") == "1")
        {
            var lines = File.ReadAllLines(path).ToList();
            lines.AddRange(missing.Select(d => $"{d.Id} | {d.Category} | {d.DefaultSeverity} | {d.Title}"));
            File.WriteAllLines(path, lines);
            missing = new List<DiagnosticDescriptor>(); // healed
        }

        Assert.True(missing.Count == 0,
            "AnalyzerReleases.Unshipped.md is missing a row for: " +
            string.Join(", ", missing.Select(d => d.Id)) +
            " — re-run with DWARF_SELF_HEAL=1 to auto-append them.");
    }

    // ── Helpers: reflection over DiagnosticDescriptors ────────────────────────

    private static List<(string FieldName, DiagnosticDescriptor Descriptor)>
        GetAllDescriptors()
    {
        return typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (f.Name, (DiagnosticDescriptor)f.GetValue(null)!))
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 1 — Descriptor ↔ AnalyzerReleases sync (both directions)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan1a_Every_descriptor_has_an_AnalyzerReleases_entry()
    {
        var releaseRows = ParseAnalyzerReleases();
        var descriptors = GetAllDescriptors();

        var missing = descriptors
            .Where(d => !releaseRows.ContainsKey(d.Descriptor.Id))
            .Select(d => d.Descriptor.Id)
            .ToList();

        Assert.True(missing.Count == 0,
            "Descriptor(s) have no AnalyzerReleases entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Scan1b_Every_AnalyzerReleases_entry_has_a_descriptor()
    {
        var releaseRows = ParseAnalyzerReleases();
        var descriptorIds = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);

        var missing = releaseRows.Keys
            .Where(id => !descriptorIds.Contains(id))
            .ToList();

        Assert.True(missing.Count == 0,
            "AnalyzerReleases row(s) have no descriptor: " + string.Join(", ", missing));
    }

    [Fact]
    public void Scan1c_Descriptor_Severity_matches_AnalyzerReleases_entry()
    {
        var releaseRows = ParseAnalyzerReleases();
        var descriptors = GetAllDescriptors();

        var mismatches = new List<string>();
        foreach (var (fieldName, desc) in descriptors)
        {
            if (!releaseRows.TryGetValue(desc.Id, out var row)) continue; // checked by 1a

            var expectedSeverity = row.Severity;
            var actualSeverity = desc.DefaultSeverity.ToString();

            if (!string.Equals(expectedSeverity, actualSeverity, StringComparison.OrdinalIgnoreCase))
                mismatches.Add(
                    $"{desc.Id} ({fieldName}): descriptor Severity={actualSeverity} " +
                    $"but AnalyzerReleases says {expectedSeverity}");
        }

        Assert.True(mismatches.Count == 0,
            "Severity mismatches:\n" + string.Join("\n", mismatches));
    }

    [Fact]
    public void Scan1d_Descriptor_Category_is_DwarfMapper()
    {
        var bad = GetAllDescriptors()
            .Where(d => !string.Equals(d.Descriptor.Category, "DwarfMapper", StringComparison.Ordinal))
            .Select(d => $"{d.Descriptor.Id} ({d.FieldName}): Category='{d.Descriptor.Category}'")
            .ToList();

        Assert.True(bad.Count == 0,
            "Descriptor(s) with wrong Category (expected 'DwarfMapper'):\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Scan1e_Descriptor_Id_format_is_DWARFddd()
    {
        var bad = GetAllDescriptors()
            .Where(d => !Regex.IsMatch(d.Descriptor.Id, @"^DWARF\d{3}$"))
            .Select(d => $"{d.FieldName}: Id='{d.Descriptor.Id}'")
            .ToList();

        Assert.True(bad.Count == 0,
            "Descriptor(s) with malformed Id (expected DWARF###):\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Scan1f_Descriptor_Id_has_no_gaps_except_reserved()
    {
        var allIds = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .Concat(ReservedIds.Ids)
            .Select(id => int.Parse(id.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture)) // "DWARF" = 5 chars
            .OrderBy(n => n)
            .ToList();

        var gaps = new List<string>();
        for (var i = 1; i < allIds.Count; i++)
            if (allIds[i] - allIds[i - 1] > 1)
                for (var g = allIds[i - 1] + 1; g < allIds[i]; g++)
                    gaps.Add($"DWARF{g:D3}");

        Assert.True(gaps.Count == 0,
            "Gap(s) in DWARF numbering (neither a descriptor nor a reserved id): " +
            string.Join(", ", gaps));
    }

    [Fact]
    public void Scan1g_Reserved_ids_have_no_descriptor_field()
    {
        var descriptorIds = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);

        var violations = ReservedIds.Ids
            .Where(id => descriptorIds.Contains(id))
            .ToList();

        Assert.True(violations.Count == 0,
            "Reserved id(s) unexpectedly have a descriptor: " + string.Join(", ", violations));
    }

    [Fact]
    public void Scan1h_Descriptor_Title_and_MessageFormat_are_non_empty()
    {
        var bad = GetAllDescriptors()
            .Where(d =>
                string.IsNullOrWhiteSpace(d.Descriptor.Title.ToString(CultureInfo.InvariantCulture)) ||
                string.IsNullOrWhiteSpace(d.Descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture)))
            .Select(d => d.Descriptor.Id)
            .ToList();

        Assert.True(bad.Count == 0,
            "Descriptor(s) with empty Title or MessageFormat: " + string.Join(", ", bad));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 2 — No dead/orphan descriptor (every field name referenced in generator source
    //          OUTSIDE the definition file DiagnosticDescriptors.cs itself)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan2_Every_descriptor_is_referenced_in_generator_pipeline_source()
    {
        // Exclude DiagnosticDescriptors.cs itself (that's where the fields are declared).
        var pipelineText = string.Concat(
            GeneratorSources()
                .Where(f => !f.EndsWith("DiagnosticDescriptors.cs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        var dead = GetAllDescriptors()
            .Where(d => !pipelineText.Contains(d.FieldName, StringComparison.Ordinal))
            .Select(d => $"{d.FieldName} ({d.Descriptor.Id})")
            .ToList();

        Assert.True(dead.Count == 0,
            "Dead/orphan descriptor(s) — defined but never used in generator pipeline " +
            "(DiagnosticDescriptors.cs excluded from search):\n" +
            string.Join("\n", dead));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 3 — Every diagnostic id appears in at least one test source file
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan3_Every_diagnostic_id_has_a_test_reference()
    {
        var testText = AllTestSourceText.Value;

        var untested = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .Where(id => !DiagnosticTestAllowlist.Ids.Contains(id))
            .Where(id => !testText.Contains(id, StringComparison.Ordinal))
            .ToList();

        Assert.True(untested.Count == 0,
            "Diagnostic id(s) with no test reference (add a test or add to DiagnosticTestAllowlist):\n" +
            string.Join("\n", untested));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 4 — SUPERSEDED by SurfaceObligationTests (2026-08-16)
    //
    // It asked whether every public attribute's NAME appeared anywhere in the test sources, which a
    // doc-comment mentioning it satisfied — so an attribute could sit at zero real use and still pass,
    // and thirteen of them did. The replacement asks a different question per attribute, chosen at the
    // attribute's own declaration: SurfaceDeclarationTests forces every public attribute to carry a
    // [DwarfSurface] category, SurfaceObligationTests forces every category to carry an obligation, and
    // the obligations demand a written USE in the corpus that category names — a consumer assembly and a
    // runnable sample, a NegativeCases row, a multi-assembly fixture, or an assertion over emitted text.
    //
    // Nothing is lost by the removal: the set this scan covered is the set SurfaceDeclarationTests
    // requires a category for, so a new attribute cannot escape by being added after this deletion.
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 5 — Every public enum VALUE has a test reference
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan5_Every_public_enum_value_has_a_test_reference()
    {
        var testText = AllTestSourceText.Value;

        var publicEnums = DwarfMapperAssembly
            .GetTypes()
            .Where(t => t.IsPublic && t.IsEnum)
            .ToList();

        // Match the QUALIFIED form `EnumType.Value`, not the bare value name. A bare `Contains("Throw")`
        // passes vacuously off any `Assert.Throws`, `Contains("None")` off any unrelated `None`, etc. — the
        // gate would give false assurance for common-word values. Requiring `NullStrategy.Throw` proves the
        // value is actually referenced as that enum member.
        var missing = new List<string>();
        foreach (var enumType in publicEnums)
        foreach (var valueName in Enum.GetNames(enumType))
            if (!testText.Contains($"{enumType.Name}.{valueName}", StringComparison.Ordinal))
                missing.Add($"{enumType.Name}.{valueName}");

        Assert.True(missing.Count == 0,
            "Public enum value(s) with no test reference (as the qualified `EnumType.Value`):\n"
            + string.Join("\n", missing));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 6 — TargetKind completeness (via InternalsVisibleTo from generator)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan6a_TargetKind_values_are_referenced_in_generator_source()
    {
        // The generator assembly exposes CollectionConverter.TargetKind via [InternalsVisibleTo].
        var allGeneratorText = string.Concat(GeneratorSources().Select(File.ReadAllText));

        var missing = Enum.GetNames<CollectionConverter.TargetKind>()
            .Where(name => !allGeneratorText.Contains(name, StringComparison.Ordinal))
            .Select(name => $"TargetKind.{name}")
            .ToList();

        Assert.True(missing.Count == 0,
            "TargetKind value(s) not referenced anywhere in generator source:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Scan6b_TargetKind_values_are_covered_by_a_test()
    {
        var testText = AllTestSourceText.Value;

        var missing = Enum.GetNames<CollectionConverter.TargetKind>()
            .Where(name => !testText.Contains(name, StringComparison.Ordinal))
            .Select(name => $"TargetKind.{name}")
            .ToList();

        Assert.True(missing.Count == 0,
            "TargetKind value(s) with no test reference:\n" + string.Join("\n", missing));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 7 — Every descriptor has a prose section in docs/diagnostics.md
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan7_Every_descriptor_is_documented_in_diagnostics_md()
    {
        // The descriptor <-> AnalyzerReleases sync (Scan1) is machine-checked, but the human-facing docs are
        // not: a new diagnostic can ship with a helpLinkUri pointing at a "#dwarfNNN" anchor that does not
        // exist. Every id the IDE "learn more" link targets must resolve to a real section. Reserved/retired
        // ids have no descriptor and need no section.
        var docPath = Path.Combine(RepoRoot, "docs", "diagnostics.md");
        Assert.True(File.Exists(docPath), $"docs/diagnostics.md not found at {docPath}");
        var docText = File.ReadAllText(docPath);

        // Sections are written lowercase, e.g. "## dwarf070"; ids are uppercase "DWARF070".
        var documented = Regex.Matches(docText, @"(?im)^##\s+dwarf(\d{3})\b")
            .Select(m => "DWARF" + m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var undocumented = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .Where(id => !ReservedIds.Ids.Contains(id))
            .Where(id => !documented.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(undocumented.Count == 0,
            "Diagnostic(s) have no '## dwarfNNN' section in docs/diagnostics.md (the IDE 'learn more' link "
            + "would 404):\n" + string.Join("\n", undocumented));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 8 — Every ERROR diagnostic's docs section states a remedy
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan8_Every_error_diagnostic_documents_a_fix()
    {
        // Scan7 proves the section EXISTS; it says nothing about whether the section helps. An error stops the
        // build, so its page has one job beyond naming the problem: say what to do instead. `AllowNonPublic` is
        // the cautionary case this encodes — it refused correctly while reporting "no matching source member"
        // for a member that plainly existed, which sent people looking for a typo that was not there.
        //
        // This scan found nothing when it was written: all 51 error ids already carried a fix clause. It is a
        // ratchet for the NEXT diagnostic, not a discovery, and is worth having only because the convention is
        // otherwise unenforced and invisible to a reviewer adding id 77.
        //
        // Warnings and Info may use "**Fix (optional):**" or omit a fix entirely — DWARF038 describes a
        // conversion that is working as intended, and there is nothing to repair. Errors may not.
        var docPath = Path.Combine(RepoRoot, "docs", "diagnostics.md");
        var docText = File.ReadAllText(docPath);

        // Sections run from one "## dwarfNNN" heading to the next.
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        var headings = Regex.Matches(docText, @"(?im)^##\s+dwarf(\d{3})\b").ToList();
        for (var i = 0; i < headings.Count; i++)
        {
            var start = headings[i].Index;
            var end = i + 1 < headings.Count ? headings[i + 1].Index : docText.Length;
            sections["DWARF" + headings[i].Groups[1].Value] = docText[start..end];
        }

        // Tolerant of the punctuation the docs actually use: "**Fix:**", "**Fix**", "**Fix** — ...".
        // Deliberately NOT tolerant of "**Fix (optional):**" for an error.
        var fix = new Regex(@"\*\*Fix\*?\*?", RegexOptions.IgnoreCase);
        var optional = new Regex(@"\*\*Fix\s*\(optional\)", RegexOptions.IgnoreCase);

        var offenders = GetAllDescriptors()
            .Where(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            .Select(d => d.Descriptor.Id)
            .Where(id => !ReservedIds.Ids.Contains(id))
            .Where(id => sections.TryGetValue(id, out var body)
                         && (!fix.IsMatch(body) || optional.IsMatch(body)))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Error diagnostic(s) whose docs/diagnostics.md section never states a fix (an error stops the "
            + "build — naming the problem without naming the remedy leaves the reader stuck):\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Scan8_is_not_vacuous_it_actually_inspects_error_diagnostics()
    {
        // Without this, deleting the DefaultSeverity filter's contents — or an id-format drift that made every
        // TryGetValue miss — would leave Scan8 permanently, silently green.
        var errorIds = GetAllDescriptors()
            .Where(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            .Select(d => d.Descriptor.Id)
            .Where(id => !ReservedIds.Ids.Contains(id))
            .ToList();

        Assert.True(errorIds.Count >= 40,
            $"Expected Scan8 to inspect a substantial number of error diagnostics, saw {errorIds.Count}.");

        var docText = File.ReadAllText(Path.Combine(RepoRoot, "docs", "diagnostics.md"));
        var headings = Regex.Count(docText, @"(?im)^##\s+dwarf\d{3}\b");
        Assert.True(headings >= 40, $"Expected to parse many doc sections, parsed {headings}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 9 — Every live DWARF0xx id is announced in CHANGELOG.md
    // ─────────────────────────────────────────────────────────────────────────
    // Found by the final round-20 whole-branch review: DWARF086 was correctly present in
    // AnalyzerReleases.Unshipped.md, docs/diagnostics.md and the generated index, but CHANGELOG.md's own
    // preamble mandates an entry for any new/retired/re-severitied diagnostic id, and
    // .github/workflows/release.yml publishes the matching section verbatim as the GitHub Release notes —
    // so a new build-breaking Error would have shipped unannounced. No test in this repository read
    // CHANGELOG.md at all before this scan; AssemblyScanTests otherwise stopped at the descriptor ↔
    // AnalyzerReleases sync (Scan1) and never looked past it.

    [Fact]
    public void Scan9_Every_diagnostic_id_is_announced_in_the_changelog()
    {
        var changelogPath = Path.Combine(RepoPaths.Root, "CHANGELOG.md");
        Assert.True(File.Exists(changelogPath), $"CHANGELOG.md not found at {changelogPath}");
        var changelogText = File.ReadAllText(changelogPath);

        var missing = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .Where(id => !ReservedIds.Ids.Contains(id))
            .Where(id => !PredatesTheChangelog.Ids.Contains(id))
            .Where(id => !changelogText.Contains(id, StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Diagnostic id(s) with no CHANGELOG.md entry (add one under the current Unreleased heading — a "
            + "new/retired/re-severitied diagnostic id is a user-visible change per the file's own preamble; "
            + "if the id genuinely predates CHANGELOG.md, that belongs in PredatesTheChangelog instead, which "
            + "may only shrink):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Scan9_is_not_vacuous_it_actually_inspects_the_changelog()
    {
        // Non-vacuity guard (Issues/round20/CARRY-FORWARD.md §6 — this repository has hit "a scan finds
        // nothing and passes by construction" six times, most recently a scan whose corpus included the
        // very declaration it was meant to check). Three independent checks: the descriptor corpus Scan9
        // draws from is a real, substantial set; CHANGELOG.md actually loaded and contains a known-present
        // id; and every frozen PredatesTheChangelog entry is still a live descriptor, so a descriptor
        // rename/removal makes the baseline itself fail rather than silently stop meaning anything.
        var liveIds = GetAllDescriptors()
            .Select(d => d.Descriptor.Id)
            .Where(id => !ReservedIds.Ids.Contains(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(liveIds.Count >= 80,
            $"Expected Scan9 to inspect a substantial number of live DWARF0xx ids, saw {liveIds.Count}.");

        var changelogText = File.ReadAllText(Path.Combine(RepoPaths.Root, "CHANGELOG.md"));
        Assert.Contains("DWARF063", changelogText, StringComparison.Ordinal);

        var staleBaselineEntries = PredatesTheChangelog.Ids
            .Where(id => !liveIds.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        Assert.True(staleBaselineEntries.Count == 0,
            "PredatesTheChangelog contains id(s) that are no longer live descriptors (remove them — the set "
            + "may only shrink, towards ids that still need writing up):\n"
            + string.Join("\n", staleBaselineEntries));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN 1f/1g/1h — the same sync, for the DWARFR registry family
    // ─────────────────────────────────────────────────────────────────────────
    // ISSUE-047: RegistryDiagnostics used to suppress RS2000/RS2001, so DWARFR01–R09 appeared in no
    // AnalyzerReleases file at all — shipping in the same package, and in the same IDE error list, as
    // rules whose sync IS machine-checked. The suppression is gone; these three scans are what replaced
    // it. They deliberately mirror Scan1a/1b/1c rather than generalising them, so a change to the
    // DWARF0xx contract cannot silently weaken the DWARFR one.

    [Fact]
    public void Scan1f_Every_registry_descriptor_has_an_AnalyzerReleases_entry()
    {
        var releaseRows = ParseAnalyzerReleases(RegistryIdPattern);

        var missing = GetAllRegistryDescriptors()
            .Where(d => !releaseRows.ContainsKey(d.Descriptor.Id))
            .Select(d => d.Descriptor.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Registry descriptor(s) have no AnalyzerReleases entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Scan1g_Every_registry_AnalyzerReleases_entry_has_a_descriptor()
    {
        var descriptorIds = GetAllRegistryDescriptors()
            .Select(d => d.Descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = ParseAnalyzerReleases(RegistryIdPattern).Keys
            .Where(id => !descriptorIds.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "AnalyzerReleases row(s) have no registry descriptor: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Scan1h_Registry_descriptor_metadata_matches_AnalyzerReleases_entry()
    {
        var releaseRows = ParseAnalyzerReleases(RegistryIdPattern);

        var mismatches = new List<string>();
        foreach (var (fieldName, desc) in GetAllRegistryDescriptors())
        {
            if (!releaseRows.TryGetValue(desc.Id, out var row)) continue; // checked by 1f

            if (!string.Equals(row.Severity, desc.DefaultSeverity.ToString(), StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"{desc.Id} ({fieldName}): descriptor Severity={desc.DefaultSeverity} " +
                               $"but AnalyzerReleases says {row.Severity}");

            if (!string.Equals(row.Category, desc.Category, StringComparison.Ordinal))
                mismatches.Add($"{desc.Id} ({fieldName}): descriptor Category='{desc.Category}' " +
                               $"but AnalyzerReleases says '{row.Category}'");
        }

        Assert.True(mismatches.Count == 0,
            "Registry descriptor/AnalyzerReleases mismatches:\n" + string.Join("\n", mismatches));
    }

    [Fact]
    public void Scan1i_Registry_descriptor_Id_format_is_DWARFRdd()
    {
        var bad = GetAllRegistryDescriptors()
            .Where(d => !Regex.IsMatch(d.Descriptor.Id, RegistryIdPattern))
            .Select(d => $"{d.FieldName}: Id='{d.Descriptor.Id}'")
            .ToList();

        Assert.True(bad.Count == 0,
            "Registry descriptor(s) with malformed Id (expected DWARFR##):\n" + string.Join("\n", bad));
    }

    // Non-vacuity guard for the four scans above: they are all "no counterexamples" assertions, which pass
    // trivially if reflection ever stops finding the descriptors (class renamed, fields made non-public).
    [Fact]
    public void Scan1j_Registry_scans_actually_see_the_descriptors()
    {
        Assert.True(GetAllRegistryDescriptors().Count >= 9,
            $"Expected the registry scans to inspect all DWARFR descriptors, saw "
            + $"{GetAllRegistryDescriptors().Count}.");
        Assert.True(ParseAnalyzerReleases(RegistryIdPattern).Count >= 9,
            "Expected to parse the DWARFR rows out of AnalyzerReleases.");
    }

    private const string RegistryIdPattern = @"^DWARFR\d{2}$";

    private static List<(string FieldName, DiagnosticDescriptor Descriptor)> GetAllRegistryDescriptors()
    {
        return typeof(RegistryDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (f.Name, (DiagnosticDescriptor)f.GetValue(null)!))
            .ToList();
    }

    // The id pattern is a parameter so the DWARF0xx scans and the DWARFR scans read the SAME table
    // through the same parser: one file format, two families, no second parser to drift.
    private static Dictionary<string, ReleaseRow> ParseAnalyzerReleases(string idPattern = @"^DWARF\d{3}$")
    {
        var result = new Dictionary<string, ReleaseRow>(StringComparer.Ordinal);

        var unshipped = Path.Combine(
            RepoRoot, "src", "DwarfMapper.Generator", "AnalyzerReleases.Unshipped.md");
        var shipped = Path.Combine(
            RepoRoot, "src", "DwarfMapper.Generator", "AnalyzerReleases.Shipped.md");

        foreach (var filePath in new[] { unshipped, shipped })
        {
            if (!File.Exists(filePath)) continue;
            foreach (var line in File.ReadAllLines(filePath))
            {
                // Skip comment/empty lines
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(';'))
                    continue;

                // Data rows look like:
                // DWARF001 | DwarfMapper | Error | Destination member is not mapped
                var parts = trimmed.Split('|');
                if (parts.Length < 3) continue;

                var id = parts[0].Trim();
                if (!Regex.IsMatch(id, idPattern)) continue;

                var category = parts[1].Trim();
                var severity = parts[2].Trim();

                result[id] = new ReleaseRow(id, category, severity);
            }
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AnalyzerReleases parser
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record ReleaseRow(string Id, string Category, string Severity);
}
