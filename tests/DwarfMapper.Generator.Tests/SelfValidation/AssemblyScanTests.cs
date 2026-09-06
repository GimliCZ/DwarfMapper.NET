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
//  6a. TargetKind completeness via [InternalsVisibleTo] from the generator
//  6b. SUPERSEDED — see the banner at scan 6b's former position
//
// A NOTE ON CORPORA, learned the expensive way (see Scan6a's remarks): a scan whose corpus contains its
// own needles reports success while measuring nothing. Every text scan below therefore reads a corpus that
// EXCLUDES this file, and Scan2/Scan6a additionally exclude (or step around) the text that declares the
// thing they hunt for. `AllTestSourceText` — the unfiltered blob — survives only for the snapshot scan,
// which legitimately needs to see every test method in the tree, this file's included.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Pipeline;
using DwarfMapper.Generator.Registry;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
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
            "DWARF029",
            // DWARF102/104/105: held by the round-29 plan for tasks still to land, so DWARF106 was allocated
            // out of order rather than renumbering work already specified against those ids. Each entry leaves
            // when its task claims it; this block must only SHRINK. DWARF101 left it in round 29 T0.3
            // (LayoutHygiene / struct padding) and DWARF103 in T2.2 (the transfer-model hint at the mapping
            // site), which is what the block is for.
            "DWARF102",
            "DWARF104",
            "DWARF105"
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

// A frozen `PredatesTheChangelog` baseline used to sit here: the 76 DWARF0xx ids that existed before
// CHANGELOG.md did, exempted from Scan9 because the file's per-id mandate could not retroactively demand
// prose for a diagnostic that shipped before the file existed to hold it. Its own comment said it may only
// SHRINK, and on 2026-08-21 it shrank to empty: task D-e (Issues/round20/TASKS.md) wrote all 76 entries as
// the "initial diagnostic surface" block under CHANGELOG.md's `### Added`, and the emptied set was deleted
// rather than kept as dead code. Scan9 now guards every live DWARF0xx id uniformly, with no exemptions.
//
// Scope note that survives the deletion: Scan9 is DWARF0xx only, deliberately. The DWARFR (registry)
// family is announced in CHANGELOG.md as the range "DWARFR01–DWARFR10" (see the `### Added` entry for
// ISSUE-047) — a per-id substring scan would fail on DWARFR02..08 despite the family being fully
// announced. Do not "fix" that by adding DWARFR ids to Scan9; the range notation is the intended
// announcement.

    public sealed class AssemblyScanTests
    {
        /// <summary>This file's own name — excluded from every corpus it would otherwise pollute.</summary>
        private const string ThisFile = "AssemblyScanTests.cs";

        private const string RegistryIdPattern = @"^DWARFR\d{2}$";
        // ── Assembly & path resolution ────────────────────────────────────────────

        private static readonly Assembly DwarfMapperAssembly =
            typeof(DwarfMapperAttribute).Assembly;

        /// <summary>
        ///     Read all test source text into a single concatenated blob for substring scanning.
        ///     Cached per test run.
        ///     <para>
        ///         Use this ONLY where the scan's needles cannot appear in this file — in practice, only the
        ///         snapshot scan, whose needles are other files' test-method names. Every other scan reads
        ///         <see cref="TestSourceTextExcluding" /> instead, because this file is itself under
        ///         <c>tests/</c>: a diagnostic id listed in <see cref="ReservedIds" /> or written as a control
        ///         literal in Scan9's non-vacuity guard, an option named in a comment, or an enum value written
        ///         out in a doc-comment all satisfy a substring scan whose corpus includes them, and the scan
        ///         then reports success having measured nothing.
        ///     </para>
        /// </summary>
        private static readonly Lazy<string> AllTestSourceText = new(() =>
            string.Concat(TestSources().Select(File.ReadAllText)));

        /// <summary>
        ///     The test-source blob with the named files removed, so a scan cannot be satisfied by text that
        ///     exists only to declare or exempt the very thing being scanned for.
        /// </summary>
        /// <param name="fileNames">Bare file names (not paths) to drop from the corpus.</param>
        private static string TestSourceTextExcluding(params string[] fileNames)
        {
            return string.Concat(
                TestSources()
                    .Where(f => Array.IndexOf(fileNames, Path.GetFileName(f)) < 0)
                    .Select(File.ReadAllText));
        }

        // ── Shared helpers ────────────────────────────────────────────────────────
        //
        // C5: a private repo-root walk and a private source enumerator used to live here, doing what RepoPaths
        // was extracted to do for everyone. RepoPaths finds the root the same way (upward for
        // DwarfMapper.NET.sln — a marker that is a real file in a git WORKTREE as well as in a plain clone,
        // which is why it, and not ".git", is what the walk looks for) and its SourceFiles additionally drops
        // bin/. Re-measured when the switch went in: 56 generator sources and 443 test sources either way, so
        // the corpora this file scans are byte-for-byte the ones they were.

        private static IEnumerable<string> GeneratorSources()
        {
            return RepoPaths.SourceFiles(RepoPaths.GeneratorSrcDir);
        }

        private static IEnumerable<string> TestSources()
        {
            return RepoPaths.SourceFiles(RepoPaths.Tests);
        }

        // ── Self-validation: every [DwarfMapper] option must be exercised by a test ──
        // This is the guard that would have caught a new option (e.g. AllowNonPublic) shipping with no test:
        // every public settable property on DwarfMapperAttribute must be named somewhere in the test sources.
        //
        // The corpus excludes THIS file. The comment two lines up names `AllowNonPublic`, and this file lives
        // under tests/ — so with the unfiltered blob the scan was partly self-satisfying: a reviewer who added
        // an option and mentioned it in a comment here would have discharged the very gate meant to catch them.
        // Re-measured when the exclusion went in: nothing changed, all 18 options are referenced 6–181 times
        // outside this file. The flaw was in the mechanism, not (yet) in the result.
        [Fact]
        public void Scan5_Every_DwarfMapper_option_has_a_test_reference()
        {
            var testText = TestSourceTextExcluding(ThisFile);

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
                .EnumerateFiles(Path.Combine(RepoPaths.Root, "tests"), "*" + suffix, SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                .Select(f => Path.GetFileName(f))
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
            var path = Path.Combine(RepoPaths.Root, "src", "DwarfMapper.Generator", "AnalyzerReleases.Unshipped.md");
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

                // ARCH-06: repo writes go through RepoWriteGuard; under a Stryker mutation run the write is
                // refused, `missing` stays populated, and the assert below fails truthfully instead of claiming
                // a heal that never touched the file (T3-H1).
                if (RepoWriteGuard.WriteBackLines(path, lines))
                {
                    missing = new List<DiagnosticDescriptor>(); // healed
                }
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
                if (!releaseRows.TryGetValue(desc.Id, out var row))
                {
                    continue; // checked by 1a
                }

                var expectedSeverity = row.Severity;
                var actualSeverity = desc.DefaultSeverity.ToString();

                if (!string.Equals(expectedSeverity, actualSeverity, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(
                        $"{desc.Id} ({fieldName}): descriptor Severity={actualSeverity} " +
                        $"but AnalyzerReleases says {expectedSeverity}");
                }
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
                {
                    for (var g = allIds[i - 1] + 1; g < allIds[i]; g++)
                        gaps.Add($"DWARF{g:D3}");
                }

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
            // Exclude the declaration file — that is where the fields are declared, so leaving it in would
            // make every descriptor find itself and this scan would pass by construction.
            //
            // Matched by PREFIX, not by exact name, and that is load-bearing rather than tidiness. Round 27
            // planned to split this 1,746-line file into DiagnosticDescriptors.<something>.cs partials. Under
            // an exact-name exclusion those partials would have stayed in the searched text, every one of the
            // 96 descriptors would have matched its own declaration, and this scan would have gone green while
            // measuring nothing. The split was not made, for reasons recorded in Issues/round27 — but the trap
            // it revealed is real and outlives the decision, so the exclusion is widened now rather than left
            // for whoever does split the file to discover.
            var pipelineText = string.Concat(
                GeneratorSources()
                    .Where(f => !Path.GetFileName(f)
                        .StartsWith("DiagnosticDescriptors.", StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));

            // The direct non-vacuity check, which no naming scheme can defeat: assert the PROPERTY the filename
            // filter is trying to achieve, rather than trusting the pattern to achieve it. If any part of the
            // declaring class survived, every descriptor would match its own definition and the result below
            // would mean nothing.
            //
            // Keyed on the CLASS, not on "public static readonly DiagnosticDescriptor". A first version used the
            // field-declaration text and failed at once on RegistryDiagnostics.cs, which declares the twelve
            // DWARFR descriptors — a different class, checked by its own gates in RegistryDiagnosticsGenTests,
            // and harmless in this search because none of the 96 field names appears there. Excluding it would
            // have been a fix to a problem that did not exist.
            Assert.False(pipelineText.Contains("class DiagnosticDescriptors", StringComparison.Ordinal),
                "Part of the DiagnosticDescriptors class reached the searched text, so every descriptor would "
                + "match its own definition and this scan would pass by construction. A partial was added "
                + "outside the DiagnosticDescriptors.* prefix the filter above excludes.");

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

        // Two files in the tests/ tree held diagnostic ids for the express purpose of EXEMPTING them, and both
        // were in this scan's corpus by default:
        //
        //   • this file's `PredatesTheChangelog`      — Scan9's frozen baseline; drained and deleted 2026-08-21
        //                                               (task D-e), but this file still holds id literals in
        //                                               `ReservedIds` and Scan9's non-vacuity controls;
        //   • `DiagnosticCoverageRatchetTests.cs`'s
        //     `PredatesThisProject`                   — 73 ids, the negative-case ratchet's opt-out list.
        //
        // Between them they named almost every live id as a bare string literal, so "the id appears somewhere in
        // tests/" was discharged by the lists that say the id is NOT covered — the scan agreeing with the
        // paperwork instead of with the tests. Both files are excluded here. Re-measured when the exclusion went
        // in: all 84 live ids still appear in at least one other test file (min 1, median 2), so the scan still
        // passes, now for a reason. No allowlist entry was needed and DiagnosticTestAllowlist stays empty.
        /// <summary>
        ///     "This list must only SHRINK" was prose on two id stores and a gate on neither (B8), so appending
        ///     an id instead of writing the test — or instead of writing the CHANGELOG entry — was a one-line
        ///     edit nothing failed on.
        ///     <para>
        ///         One guard covered both stores when the row was filed. The second, <c>PredatesTheChangelog</c>,
        ///         shrank to empty and was DELETED on 2026-08-21 by task D-e (see the banner above), so the
        ///         obligation now has one store to hold — and it holds it as an EXACT PIN at zero, not as a
        ///         shrink-only ratchet. A tolerance band over a population of zero passes every value it could
        ///         ever take; below eleven the house rule is exactness, which is why
        ///         <c>SurfaceParityTests.AssertRatchet</c> refuses a ceiling of ten or less outright.
        ///     </para>
        ///     <para>
        ///         Deliberately NOT phrased as "the count did not grow": the whole point is that
        ///         <see cref="Scan3_Every_diagnostic_id_has_a_test_reference" /> subtracts this set from its
        ///         corpus, so every id put here is an id nothing tests. Zero is the only honest value, and
        ///         raising it has to be a visible edit to this assertion with its justification beside it.
        ///     </para>
        /// </summary>
        [Fact]
        public void Scan3s_allowlist_is_exactly_empty_and_may_not_grow()
        {
            Assert.Empty(DiagnosticTestAllowlist.Ids);
        }

        [Fact]
        public void Scan3_Every_diagnostic_id_has_a_test_reference()
        {
            var testText = TestSourceTextExcluding(ThisFile, "DiagnosticCoverageRatchetTests.cs");

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
            // Corpus excludes this file: the remarks below write out `NullStrategy.Throw` in full, which is
            // exactly the qualified needle the scan looks for. The qualified form fixed one vacuity (a bare
            // `Contains("Throw")` off any `Assert.Throws`) and left the other — a corpus containing the needle.
            var testText = TestSourceTextExcluding(ThisFile);

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
                {
                    missing.Add($"{enumType.Name}.{valueName}");
                }

            Assert.True(missing.Count == 0,
                "Public enum value(s) with no test reference (as the qualified `EnumType.Value`):\n" + string.Join("\n", missing));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SCAN 6a — TargetKind completeness (via InternalsVisibleTo from generator)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Every value of the generator's internal collection taxonomy must be referenced, as a qualified
        ///     <c>TargetKind.Value</c>, somewhere in the generator's own source — i.e. some recognition site
        ///     assigns it and some emission site acts on it. A value nothing names is a taxonomy entry the
        ///     pipeline can never produce or consume.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>This scan passed by construction from the day it was written until 2026-08-17.</b> Its
        ///         corpus is all of <c>src/DwarfMapper.Generator/**</c>, and it searched for the BARE value name
        ///         — but that corpus includes <c>Pipeline/CollectionConverter.cs</c>, which is where
        ///         <c>enum TargetKind</c> is declared. Every needle matched its own declaration line, so
        ///         <c>missing</c> was empty no matter what the pipeline did, and deleting the whole test would
        ///         have changed nothing. It sat five scans below
        ///         <see cref="Scan2_Every_descriptor_is_referenced_in_generator_pipeline_source" />,
        ///         which had already solved the identical problem by excluding its own defining file.
        ///     </para>
        ///     <para>
        ///         Scan2's remedy does not transfer verbatim: <c>DiagnosticDescriptors.cs</c> is a pure
        ///         declaration file, whereas <c>CollectionConverter.cs</c> declares the enum AND holds most of
        ///         the switch arms that consume it. Excluding the file would have discarded the real references
        ///         and failed 14 of the 17 values for no defect at all. The fix is instead to exclude the
        ///         DECLARATION TEXT rather than the file, by requiring the qualified <c>TargetKind.Value</c>
        ///         form, which the enum body (<c>Array, // T[] …</c>) never produces. That is the same remedy
        ///         <see cref="Scan5_Every_public_enum_value_has_a_test_reference" /> already applies to the
        ///         public enums, for a related reason.
        ///     </para>
        ///     <para>
        ///         Measured after the change: every one of the 17 values has 2–8 qualified references, so the
        ///         scan still passes — and now for a reason.
        ///         <see cref="Scan6a_qualified_needle_is_not_satisfied_by_the_enum_declaration" />
        ///         pins the predicate against the exact text that used to satisfy it.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Scan6a_TargetKind_values_are_referenced_in_generator_source()
        {
            // The generator assembly exposes CollectionConverter.TargetKind via [InternalsVisibleTo].
            var allGeneratorText = string.Concat(GeneratorSources().Select(File.ReadAllText));

            var missing = Enum.GetNames<CollectionConverter.TargetKind>()
                .Where(name => !IsQualifiedEnumReference(allGeneratorText, "TargetKind", name))
                .Select(name => $"TargetKind.{name}")
                .ToList();

            Assert.True(missing.Count == 0,
                "TargetKind value(s) not referenced anywhere in generator source as `TargetKind.<value>`:\n" +
                string.Join("\n", missing));
        }

        /// <summary>
        ///     Scan6a's needle, isolated so it can be shown to REJECT things. The boundaries matter three ways:
        ///     the enum name may not be a suffix of a longer identifier (<c>DictTargetKind.Dictionary</c> is a
        ///     reference to the OTHER taxonomy, not this one); the value name may not be a prefix of a longer
        ///     member (<c>TargetKind.ImmutableList</c> must not discharge a hypothetical <c>Immutable</c>); and
        ///     a leading dot is fine, because <c>CollectionConverter.TargetKind.Array</c> — the form every
        ///     reference outside the declaring file uses — is a perfectly good reference.
        /// </summary>
        private static bool IsQualifiedEnumReference(string text, string enumName, string valueName)
        {
            return Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(enumName)}\.{Regex.Escape(valueName)}\b");
        }

        [Fact]
        public void Scan6a_qualified_needle_is_not_satisfied_by_the_enum_declaration()
        {
            // The known-bad input is not hypothetical: it is a verbatim slice of the enum body that made Scan6a
            // vacuous for its entire life. If this ever returns true again, Scan6a has stopped measuring.
            //
            // Stated limit (same as Scan9's control): this pins the PREDICATE, not Scan6a's [Fact] wiring to
            // it — deleting the call to IsQualifiedEnumReference from Scan6a would still leave this green.
            // Pinning the wiring too needs a mutation run.
            const string declarationBody = """
                                           internal enum TargetKind
                                           {
                                               Array, // T[]            — projection-translatable
                                               List, // List<T>        — projection-translatable
                                           }
                                           """;
            Assert.False(IsQualifiedEnumReference(declarationBody, "TargetKind", "Array"));
            Assert.False(IsQualifiedEnumReference(declarationBody, "TargetKind", "List"));

            // A real use site is accepted, in the switch-arm, assignment and type-qualified forms. The last one
            // is not decoration: every TargetKind reference outside the declaring file writes
            // `CollectionConverter.TargetKind.X`, and an over-strict lookbehind that rejected a leading dot
            // silently discarded all of them — caught here while writing this control, not in review.
            Assert.True(IsQualifiedEnumReference("case TargetKind.Array:", "TargetKind", "Array"));
            Assert.True(IsQualifiedEnumReference("targetKind = TargetKind.List;", "TargetKind", "List"));
            Assert.True(IsQualifiedEnumReference(
                "collShape.Target == CollectionConverter.TargetKind.Array",
                "TargetKind",
                "Array"));

            // The sibling taxonomy must not launder a value across enums, and a longer member name must not
            // discharge a shorter one that is its prefix.
            Assert.False(IsQualifiedEnumReference("DictTargetKind.Dictionary", "TargetKind", "Dictionary"));
            Assert.False(IsQualifiedEnumReference("TargetKind.ImmutableList", "TargetKind", "Immutable"));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SCAN 6b — SUPERSEDED by CollectionCoverageSelfValidationTests (2026-08-17)
        //
        // It asked whether each TargetKind value's BARE name appeared anywhere under tests/. Every value is a
        // BCL type name — Array, List, HashSet, Queue, Stack, IEnumerable — so the needles matched ordinary C#
        // in unrelated files, and substring nesting made it worse still: `List` was discharged by any `IList`,
        // `ISet` by any `IReadOnlySet`. It could not be repaired the way Scan6a was, either: no test source
        // writes a qualified `TargetKind.Value` at all (measured: zero, all 17 values), because tests exercise
        // the taxonomy through the mapped collection TYPE, never through the internal enum.
        //
        // The replacement asks the question by running it. CollectionCoverageSelfValidationTests reads the same
        // enum reflectively — so a new value cannot escape by being added after this deletion — and then
        // demands the value actually be EMITTED: once by the combinatorial matrix (crossed against widening,
        // cycle mode, update-into and null strategy) and once by the fuzz schema, with ObjectFactoryV2 proven to
        // populate the shape with real elements. That is the check that caught the IEnumerable<T> aliasing bug
        // this scan's text search sat green through.
        // ─────────────────────────────────────────────────────────────────────────

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
            var docPath = Path.Combine(RepoPaths.Root, "docs", "diagnostics.md");
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
                "Diagnostic(s) have no '## dwarfNNN' section in docs/diagnostics.md (the IDE 'learn more' link " + "would 404):\n" + string.Join("\n", undocumented));
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
            var docPath = Path.Combine(RepoPaths.Root, "docs", "diagnostics.md");
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

            var offenders = GetAllDescriptors()
                .Where(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
                .Select(d => d.Descriptor.Id)
                .Where(id => !ReservedIds.Ids.Contains(id))
                .Where(id => sections.TryGetValue(id, out var body) && !StatesAFix(body))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                "Error diagnostic(s) whose docs/diagnostics.md section never states a fix (an error stops the " + "build — naming the problem without naming the remedy leaves the reader stuck):\n" + string.Join("\n", offenders));
        }

        /// <summary>
        ///     Scan8's verdict on one section body, isolated so it can be shown to REJECT things. Tolerant of
        ///     the punctuation the docs actually use — <c>**Fix:**</c>, <c>**Fix**</c>, <c>**Fix** — …</c> —
        ///     and deliberately NOT tolerant of <c>**Fix (optional):**</c>, which is the warning-level form.
        /// </summary>
        private static bool StatesAFix(string sectionBody)
        {
            var fix = new Regex(@"\*\*Fix\*?\*?", RegexOptions.IgnoreCase);
            var optional = new Regex(@"\*\*Fix\s*\(optional\)", RegexOptions.IgnoreCase);
            return fix.IsMatch(sectionBody) && !optional.IsMatch(sectionBody);
        }

        [Fact]
        public void Scan8_is_not_vacuous_it_actually_inspects_error_diagnostics()
        {
            // Two halves, and only the first was here originally. The COUNT half proves Scan8's corpus is real:
            // deleting the DefaultSeverity filter's contents, or an id-format drift that made every TryGetValue
            // miss, would leave it permanently and silently green. That says nothing about whether the VERDICT
            // works — Scan8 gutted to `Assert.True(true)` would still satisfy a corpus check — so the second
            // half feeds StatesAFix the three inputs it must separate.
            //
            // Floors are set to the values measured on 2026-08-17 (was `>= 40` for both, against actuals of 60
            // and 84 — slack from birth, so a two-thirds collapse in either corpus passed unnoticed). They may
            // only ever be TIGHTENED to a re-measured value, never raised past one.
            //
            // Stated limit (same as Scan9's control): the three StatesAFix assertions below pin the PREDICATE,
            // not Scan8's [Fact] wiring to it — deleting the call to StatesAFix from Scan8 would still leave
            // this green. Pinning the wiring too needs a mutation run.
            var errorIds = GetAllDescriptors()
                .Where(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
                .Select(d => d.Descriptor.Id)
                .Where(id => !ReservedIds.Ids.Contains(id))
                .ToList();

            Assert.True(errorIds.Count >= 60,
                $"Expected Scan8 to inspect a substantial number of error diagnostics, saw {errorIds.Count}.");

            var docText = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "diagnostics.md"));
            var headings = Regex.Count(docText, @"(?im)^##\s+dwarf\d{3}\b");
            Assert.True(headings >= 84, $"Expected to parse many doc sections, parsed {headings}.");

            Assert.True(StatesAFix("## dwarf001\nBody.\n\n**Fix:** map the member or ignore it."));
            Assert.False(StatesAFix("## dwarf001\nBody describing the problem, and no remedy at all."));
            Assert.False(StatesAFix("## dwarf038\nBody.\n\n**Fix (optional):** widen the target member."));
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

            var missing = UnannouncedIds(
                GetAllDescriptors().Select(d => d.Descriptor.Id),
                changelogText);

            Assert.True(missing.Count == 0,
                "Diagnostic id(s) with no CHANGELOG.md entry (add one under the current Unreleased heading — a " + "new/retired/re-severitied diagnostic id is a user-visible change per the file's own " + "preamble):\n" + string.Join("\n", missing));
        }

        /// <summary>
        ///     Scan9's decision, isolated from its file I/O so a control can feed it inputs it must REJECT.
        ///     Returns the ids that are neither reserved nor named anywhere in the changelog text.
        /// </summary>
        private static List<string> UnannouncedIds(IEnumerable<string> descriptorIds, string changelogText)
        {
            return descriptorIds
                .Where(id => !ReservedIds.Ids.Contains(id))
                .Where(id => !changelogText.Contains(id, StringComparison.Ordinal))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        [Fact]
        public void Scan9_is_not_vacuous_it_actually_inspects_the_changelog()
        {
            // Non-vacuity guard (Issues/round20/CARRY-FORWARD.md §6 — this repository has hit "a scan finds
            // nothing and passes by construction" six times, most recently a scan whose corpus included the
            // very declaration it was meant to check). Two checks on the CORPUS: the descriptor set Scan9
            // draws from is real and substantial, and CHANGELOG.md actually loaded and contains a
            // known-present id.
            //
            // Those catch the failure this guard was written for — a mistyped path, six times over — but they
            // would NOT catch Scan9 itself being gutted to `Assert.True(true)`: proving the inputs are real is
            // not proving the assertion works. The block below closes that by driving Scan9's extracted
            // verdict, UnannouncedIds, over known-bad and known-good input directly. Stated limit: it pins the
            // PREDICATE, not the [Fact]'s wiring to it — deleting the call from Scan9 would still leave this
            // green. Pinning the wiring too needs a mutation run, which is where that belongs.
            var liveIds = GetAllDescriptors()
                .Select(d => d.Descriptor.Id)
                .Where(id => !ReservedIds.Ids.Contains(id))
                .ToHashSet(StringComparer.Ordinal);

            // Floor tightened from `>= 80` to the value measured on 2026-08-17. May only ever move down.
            Assert.True(liveIds.Count >= 84,
                $"Expected Scan9 to inspect a substantial number of live DWARF0xx ids, saw {liveIds.Count}.");

            var changelogText = File.ReadAllText(Path.Combine(RepoPaths.Root, "CHANGELOG.md"));
            Assert.Contains("DWARF063", changelogText, StringComparison.Ordinal);

            // Known-bad / known-good, on synthetic text so each arm is proved in isolation rather than by
            // whatever CHANGELOG.md happens to say today. DWARF999 exists nowhere; DWARF004 is reserved.
            Assert.Equal(["DWARF999"], UnannouncedIds(["DWARF999"], ""));
            Assert.Empty(UnannouncedIds(["DWARF999"], "- DWARF999 now refuses X. (#1)"));
            Assert.Empty(UnannouncedIds(["DWARF004"], ""));
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
                if (!releaseRows.TryGetValue(desc.Id, out var row))
                {
                    continue; // checked by 1f
                }

                if (!string.Equals(row.Severity, desc.DefaultSeverity.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add($"{desc.Id} ({fieldName}): descriptor Severity={desc.DefaultSeverity} " +
                                   $"but AnalyzerReleases says {row.Severity}");
                }

                if (!string.Equals(row.Category, desc.Category, StringComparison.Ordinal))
                {
                    mismatches.Add($"{desc.Id} ({fieldName}): descriptor Category='{desc.Category}' " +
                                   $"but AnalyzerReleases says '{row.Category}'");
                }
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
                $"Expected the registry scans to inspect all DWARFR descriptors, saw " + $"{GetAllRegistryDescriptors().Count}.");
            Assert.True(ParseAnalyzerReleases(RegistryIdPattern).Count >= 9,
                "Expected to parse the DWARFR rows out of AnalyzerReleases.");
        }

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
                RepoPaths.Root,
                "src",
                "DwarfMapper.Generator",
                "AnalyzerReleases.Unshipped.md");
            var shipped = Path.Combine(
                RepoPaths.Root,
                "src",
                "DwarfMapper.Generator",
                "AnalyzerReleases.Shipped.md");

            foreach (var filePath in new[]
                     {
                         unshipped, shipped
                     })
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                foreach (var line in File.ReadAllLines(filePath))
                {
                    // Skip comment/empty lines
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(';'))
                    {
                        continue;
                    }

                    // Data rows look like:
                    // DWARF001 | DwarfMapper | Error | Destination member is not mapped
                    var parts = trimmed.Split('|');
                    if (parts.Length < 3)
                    {
                        continue;
                    }

                    var id = parts[0].Trim();
                    if (!Regex.IsMatch(id, idPattern))
                    {
                        continue;
                    }

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
}
