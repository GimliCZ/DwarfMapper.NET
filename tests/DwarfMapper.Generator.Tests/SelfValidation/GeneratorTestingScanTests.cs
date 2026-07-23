// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Ratchets that keep the framework applied. Deduplicating once fixes today; without a gate the next
///     generator ships uncovered — which is exactly how MapToGenerator ended up with no cacheability tests at
///     all while DwarfGenerator had six.
/// </summary>
public class GeneratorTestingScanTests
{
    [Fact]
    public void Every_generator_in_src_is_registered_for_testing()
    {
        var declared = typeof(DwarfGenerator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var registered = GeneratorRegistry.All.Select(g => g.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(declared.SequenceEqual(registered, StringComparer.Ordinal),
            "Generators in src/ do not match GeneratorRegistry.All.\n  declared:   "
            + string.Join(", ", declared) + "\n  registered: " + string.Join(", ", registered)
            + "\nAdd the new generator to GeneratorRegistry so it gets cacheability and golden coverage.");
    }

    [Fact]
    public void Every_tracking_name_constant_is_registered_in_the_battery()
    {
        // A WithTrackingName the battery never asserts is decoration. Enumerate every IIncrementalGenerator
        // type actually present in src/ — rather than a hardcoded two-tuple — so a third generator is checked
        // automatically instead of being silently exempt. The sibling ratchet above forces a new generator
        // into GeneratorRegistry, but that alone proves nothing about whether ITS step names are covered.
        var generatorTypes = typeof(DwarfGenerator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IIncrementalGenerator).IsAssignableFrom(t));

        foreach (var assemblyType in generatorTypes)
        {
            var registered = GeneratorRegistry.All.SingleOrDefault(g => g.Name == assemblyType.Name);
            Assert.True(registered is not null,
                $"'{assemblyType.Name}' has no entry in GeneratorRegistry.All — add it there, or the "
                + "battery never asserts any of its step names are cacheable.");

            var declared = assemblyType
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                            && f.Name.EndsWith("StepName", StringComparison.Ordinal))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            foreach (var name in declared)
                Assert.True(registered!.TrackingNames.Contains(name),
                    $"{assemblyType.Name} declares step name '{name}' but AllStepNames does not include it, so "
                    + "the battery never asserts that step is cacheable.");
        }
    }

    [Fact]
    public void The_golden_corpus_covers_every_registered_generator()
    {
        var covered = GoldenCorpus.Cases().Select(c => c.GeneratorName).Distinct(StringComparer.Ordinal).ToList();

        foreach (var g in GeneratorRegistry.All)
            Assert.True(covered.Contains(g.Name, StringComparer.Ordinal),
                $"Generator '{g.Name}' contributes no golden cases, so a refactor could change its output "
                + "undetected. Add feature cases for it to GoldenCorpus.FeatureCases().");
    }

    // Baselines recorded against DwarfMapper.dll on 2026-07-22: 31 public attribute types, 14 public enum
    // values (7 enums, 2 values each). GoldenCorpus.FeatureCases() is a hand-curated list, NOT derived from
    // these taxonomies — see the design spec's Known Limitations note. This ratchet is the honest substitute:
    // it cannot force a specific new case the way true derivation would, but it forces a human to notice
    // growth and decide whether the feature axis needs a new pinned case, rather than the corpus silently
    // going stale next to an attribute or enum value nobody golden-tested.
    private const int BaselineAttributeTypeCount = 31;
    private const int BaselineEnumValueCount = 14;

    [Fact]
    public void The_feature_taxonomy_has_not_grown_past_its_recorded_baseline()
    {
        var runtimeAssembly = typeof(DwarfMapperAttribute).Assembly;

        var attributeTypeCount = runtimeAssembly.GetTypes()
            .Count(t => t.IsPublic && typeof(Attribute).IsAssignableFrom(t));

        Assert.True(attributeTypeCount == BaselineAttributeTypeCount,
            $"{runtimeAssembly.GetName().Name} now has {attributeTypeCount} public attribute types; the "
            + $"recorded baseline is {BaselineAttributeTypeCount}. A taxonomy grew, so consider adding a "
            + "golden feature case, then update the baseline deliberately.");

        var enumValueCount = runtimeAssembly.GetTypes()
            .Where(t => t.IsPublic && t.IsEnum)
            .Sum(t => Enum.GetValues(t).Length);

        Assert.True(enumValueCount == BaselineEnumValueCount,
            $"{runtimeAssembly.GetName().Name} now has {enumValueCount} public enum values across its public "
            + $"enums; the recorded baseline is {BaselineEnumValueCount}. A taxonomy grew, so consider adding "
            + "a golden feature case, then update the baseline deliberately.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }

    // Keeps the shared engine core actually shared. Two-engine drift caused 5 of the 32 audit issues, and the
    // divergence this trio of ratchets guards — the registry enumerating members without walking base types —
    // silently dropped data for years because nothing forced the two engines to agree. Split into three [Fact]s
    // (rather than one bundled assertion) so each clause fails independently instead of the first assertion
    // masking whether the other two would ever have caught anything.

    private static readonly string GeneratorSrcRoot = Path.Combine("src", "DwarfMapper.Generator");

    // Glob rather than hard-code a single file: MapperExtractor is a PARTIAL class (MapperExtractor.cs +
    // MapperExtractor.MapConfig.cs today, more once sub-project 4 splits it further), and the registry may grow
    // additional files under Registry/. A path literal silently drains coverage the moment either happens.
    private static IEnumerable<string> ClassModelFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), GeneratorSrcRoot, "Pipeline"), "MapperExtractor*.cs",
            SearchOption.TopDirectoryOnly);

    private static IEnumerable<string> RegistryFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), GeneratorSrcRoot, "Registry"), "*.cs",
            SearchOption.TopDirectoryOnly);

    private static IEnumerable<string> AllGeneratorFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), GeneratorSrcRoot), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(seg => seg is "obj" or "bin"));

    [Fact]
    public void MapToGenerator_does_not_call_GetMembers_directly()
    {
        // Registry-scoped on purpose: MapperExtractor.MapConfig.cs legitimately calls GetMembers() (enumerating
        // methods, not class-model members), so a class-model ban would fail immediately and isn't what this
        // ratchet guards. The divergence being guarded is the REGISTRY re-growing its own shallow member
        // enumeration instead of going through Core.MemberFacts.
        foreach (var path in RegistryFiles())
        {
            var text = File.ReadAllText(path);
            Assert.False(text.Contains("GetMembers()", StringComparison.Ordinal),
                $"{Path.GetFileName(path)} calls GetMembers() directly again. Member enumeration must go "
                + "through Core.MemberFacts, or the registry silently loses inherited members as it did before.");
        }
    }

    [Fact]
    public void Neither_engine_declares_its_own_FNV_constant()
    {
        // ISSUE-015 was a TEN-file problem — the FNV-1a constant copied into converter after converter. Guarding
        // two hard-coded files only catches a regression in those two; scan every generator source file so a
        // NEW file copying the constant is caught too.
        foreach (var path in AllGeneratorFiles())
        {
            var text = File.ReadAllText(path);
            var isCanonicalHome = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .EndsWith(Path.Combine("Core", "StableHash.cs"), StringComparison.Ordinal);
            if (isCanonicalHome) continue;

            Assert.False(text.Contains("2166136261", StringComparison.Ordinal),
                $"{Path.GetFileName(path)} contains the FNV-1a offset basis constant. Hashing belongs solely to "
                + "Core.StableHash — ten copies of it is what ISSUE-015 was.");
        }
    }

    [Fact]
    public void Both_engines_call_MemberFacts_Readable_and_Writable()
    {
        // A bare mention of "MemberFacts" — a stray comment, a dead using — proves nothing. Require the actual
        // call syntax for both the readable- and writable-member entry points, so a regression that keeps a
        // dead reference while re-implementing enumeration elsewhere still fails this. Globbed so a partial-class
        // split (class model) or a new file (registry) stays covered.
        foreach (var (engineName, files) in new[]
                 {
                     ("[DwarfMapper] class model", ClassModelFiles()),
                     ("[MapTo] registry", RegistryFiles())
                 })
        {
            var combined = string.Join("\n", files.Select(File.ReadAllText));

            Assert.True(combined.Contains("MemberFacts.Readable(", StringComparison.Ordinal),
                $"The {engineName} no longer calls MemberFacts.Readable(...) — member enumeration may have been "
                + "re-implemented locally instead of routed through the shared core.");
            Assert.True(combined.Contains("MemberFacts.Writable(", StringComparison.Ordinal),
                $"The {engineName} no longer calls MemberFacts.Writable(...) — member enumeration may have been "
                + "re-implemented locally instead of routed through the shared core.");
        }
    }

    // A real \n ESCAPE, anywhere in the line. The negative lookbehind excludes `\\n` (an escaped backslash then
    // an n, which emits a literal backslash-n and is not a line break) and `'\n'` (a char literal closing an
    // Append chain — not a multi-line string payload). The previous pattern was @"\\n""" — an escape only counted
    // when it was immediately followed by the closing quote, so every MID-string break, the common form, was
    // invisible: `w.Line("if (x)\n{\n}")` scored zero and the ratchet reported PASS. Measured pre-migration, it
    // saw 106/35/22/9 of the 121/50/24/13 escapes actually present.
    private const string NewlineEscapePattern = @"(?<![\\'])\\n";

    // A quote followed by four or more spaces: the hand-counted indent run. 439 of these were the headline
    // measure of the problem this sub-project removed, and nothing guarded them — `w.Line("        foo")` passed.
    private const string IndentRunPattern = @"""[ ]{4,}";

    // Deliberately unmigrated (spec §4.2): each emits a single-line body and is below the density where a writer
    // earns its change-risk — ParsableConverter 6 escapes, NumericConverter 1, UserConversionConverter 1. Exempt
    // BY NAME rather than by narrowing the glob, so migrating one is a one-line deletion here and any NEW
    // converter is scanned by default. (UserConversionConverter is absent from the spec's file table but is the
    // same species as NumericConverter; it is recorded here so the exemption is a decision, not an accident.)
    private static readonly string[] UnmigratedConverters =
    {
        "ParsableConverter.cs",
        "NumericConverter.cs",
        "UserConversionConverter.cs"
    };

    // Glob, not a path list (spec §5). File.Exists catches a RENAME but not a SPLIT: when sub-project 4 splits
    // CollectionConverter.cs, a hard-coded list leaves the new sibling silently unratcheted — the same coverage
    // drain the ClassModelFiles() note above describes.
    private static IEnumerable<string> MigratedEmitterFiles()
    {
        var root = RepoRoot();
        return Directory
            .EnumerateFiles(Path.Combine(root, GeneratorSrcRoot, "Pipeline"), "*Converter.cs",
                SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, GeneratorSrcRoot, "Registry"), "*.cs",
                SearchOption.TopDirectoryOnly))
            .Where(p => !UnmigratedConverters.Contains(Path.GetFileName(p), StringComparer.Ordinal));
    }

    /// <summary>
    ///     Keeps the migrated emitters on CodeWriter. Hand-counted indentation and \n escapes inside emitted
    ///     strings are what made these files hard to edit correctly — the same layering produced two
    ///     extension-method-in-instance-form bugs that could not compile. A migrated file must not regress.
    /// </summary>
    [Fact]
    public void Migrated_emitters_do_not_reintroduce_hand_rolled_emission()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var path in MigratedEmitterFiles())
        {
            scanned++;
            var name = Path.GetFileName(path);
            var text = File.ReadAllText(path);

            // Both halves of the spec'd assertion, reported as distinct kinds: a \n escape and a hand-counted
            // indent run are different regressions and a reader should not have to guess which one fired.
            var newlineEscapes = System.Text.RegularExpressions.Regex.Matches(text, NewlineEscapePattern).Count;
            if (newlineEscapes > 0) offenders.Add($"{name}: {newlineEscapes} literal \\n escape(s)");

            var indentRuns = System.Text.RegularExpressions.Regex.Matches(text, IndentRunPattern).Count;
            if (indentRuns > 0) offenders.Add($"{name}: {indentRuns} hard-coded indent run(s)");
        }

        // A glob that matches nothing scans nothing and passes green — the same defect species as the path list
        // this replaced. Five files are in scope today (3 converters + 2 registry files); splits only raise that.
        Assert.True(scanned >= 5,
            $"The migrated-emitter glob matched only {scanned} file(s) — it is broken or the files moved, and a "
            + "ratchet that scans nothing is worse than none. Fix the glob, do not lower this floor.");

        Assert.True(offenders.Count == 0,
            "Migrated emitter(s) reintroduced hand-rolled emission:\n  " + string.Join("\n  ", offenders)
            + "\nUse CodeWriter (Line/Block/Indent) — hand-counted indentation and \\n escapes are what this "
            + "migration removed.");
    }
}
