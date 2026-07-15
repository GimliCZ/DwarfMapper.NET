// SPDX-License-Identifier: GPL-2.0-only

// Assembly-scanning / reflection-driven self-validation suite.
// Purpose: catch FORGOTTEN registrations and missing coverage when new
// diagnostics / attributes / enum values are added.
//
//  1. DWARF descriptor <=> AnalyzerReleases sync (both directions + metadata match)
//  2. No dead/orphan diagnostic (every descriptor is actually emittable)
//  3. Every diagnostic id has a triggering test
//  4. Every public Attribute type has at least one test reference
//  5. Every public enum VALUE has at least one test reference
//  6. TargetKind completeness via [InternalsVisibleTo] from the generator

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Pipeline;
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
            .Select(method => method.Split('_')[0] == method ? method : method) // keep full method name
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
    // SCAN 4 — Every public Attribute type has a test reference
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan4_Every_public_attribute_type_has_a_test_reference()
    {
        var testText = AllTestSourceText.Value;

        // Reflect all public Attribute subclasses from the DwarfMapper assembly.
        // Strip the "Attribute" suffix for the usage form check (e.g. [MapProperty]).
        var attrTypes = DwarfMapperAssembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(Attribute).IsAssignableFrom(t))
            .ToList();

        var missing = new List<string>();
        foreach (var t in attrTypes)
        {
            // For generic types (MapDerivedTypeAttribute`2) strip the backtick suffix.
            var rawName = t.Name;
            var usageName = rawName.EndsWith("Attribute", StringComparison.Ordinal)
                ? rawName[..^"Attribute".Length]
                : rawName;
            // Also handle generic mangling: e.g. "MapDerivedTypeAttribute`2" → "MapDerivedType"
            var backtickIdx = usageName.IndexOf('`', StringComparison.Ordinal);
            if (backtickIdx >= 0) usageName = usageName[..backtickIdx];

            if (!testText.Contains(usageName, StringComparison.Ordinal))
                missing.Add($"{t.FullName ?? t.Name} (searched for '{usageName}')");
        }

        Assert.True(missing.Count == 0,
            "Public attribute type(s) with no test reference:\n" + string.Join("\n", missing));
    }

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

        var missing = new List<string>();
        foreach (var enumType in publicEnums)
        foreach (var valueName in Enum.GetNames(enumType))
            if (!testText.Contains(valueName, StringComparison.Ordinal))
                missing.Add($"{enumType.Name}.{valueName}");

        Assert.True(missing.Count == 0,
            "Public enum value(s) with no test reference:\n" + string.Join("\n", missing));
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

    private static Dictionary<string, ReleaseRow> ParseAnalyzerReleases()
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
                if (!Regex.IsMatch(id, @"^DWARF\d{3}$")) continue;

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
