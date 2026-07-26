// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Bans the source constructs that make generated output depend on <em>where</em> it was generated.
///     <para>
///         Determinism is load-bearing well beyond flaky tests here: helper names are hashes, the golden
///         manifest compares emitted text byte-for-byte, and reproducible builds require identical output
///         across machines. <c>DeterminismFuzzTests</c> already proves the generator is stable when run twice
///         <em>in one process</em> — which is exactly the condition under which a culture- or clock-dependent
///         construct looks innocent. Both of the real defects below were invisible to it.
///     </para>
///     <para>
///         Two were found and fixed in one session, which is the argument that a scan is worth more than
///         vigilance: four <c>SortedSet&lt;(string,string)&gt;</c> built with the default tuple comparer routed
///         to culture-sensitive <c>string.CompareTo</c>, so emitted check order, message order and diagnostic
///         order all varied by machine culture (verified diverging under <c>tr-TR</c>); and <c>StableHash</c>,
///         which feeds every generated helper name, had no test pinning its values at all.
///     </para>
///     <para>
///         Deliberately NOT banned: <c>GetHashCode</c>. Randomised string hashing is fine for a lookup
///         dictionary and is used legitimately by <c>EquatableArray</c> for incremental-cache equality. The
///         defect is never the hash — it is <em>ordering</em> derived from culture, or from a hash, reaching
///         the emitted text. Banning the hash itself would produce a rule people route around.
///     </para>
/// </summary>
public class DeterminismSourceScanTests
{
    /// <summary>
    ///     Each rule is (id, pattern, why). Patterns are deliberately syntactic: this scan must stay cheap and
    ///     must not need a compilation, so it runs on every build rather than being something someone remembers.
    /// </summary>
    private static readonly (string Id, Regex Pattern, string Why)[] Banned =
    [
        // The exact defect fixed in 10b8932. A comparer-less SortedSet/SortedDictionary of strings or string
        // tuples orders through Comparer<T>.Default -> string.CompareTo, which is CULTURE-SENSITIVE.
        ("D1", new Regex(@"new\s+Sorted(Set|Dictionary)\s*<[^>]*>\s*\(\s*\)", RegexOptions.Compiled),
            "a comparer-less SortedSet/SortedDictionary orders via culture-sensitive string.CompareTo; "
            + "pass an explicit ordinal comparer (see AmbientValidator.OrdinalPair)"),

        ("D2", new Regex(@"(CultureInfo|StringComparison|StringComparer)\.CurrentCulture", RegexOptions.Compiled),
            "current-culture comparison makes generated output depend on the build machine's locale; "
            + "use the Ordinal/Invariant form"),

        // Wall-clock and randomness cannot be reproduced by a second build of the same input.
        ("D3", new Regex(@"DateTime\.(Now|UtcNow)|DateTimeOffset\.(Now|UtcNow)", RegexOptions.Compiled),
            "wall-clock time in the generator makes two builds of the same source differ"),

        ("D4", new Regex(@"Guid\.NewGuid\s*\(", RegexOptions.Compiled),
            "a fresh GUID differs per build; derive stable identity from the input instead (see StableHash)"),

        ("D5", new Regex(@"new\s+Random\s*\(\s*\)", RegexOptions.Compiled),
            "unseeded randomness cannot be reproduced; seed it or remove it")
    ];

    /// <summary>
    ///     Justified exemptions, keyed by "<c>&lt;file&gt;:&lt;ruleId&gt;</c>". Must only ever SHRINK — the same
    ///     contract as <c>HollowAllowlist</c> and <c>MatrixExemptAttributes</c>. Empty is the goal and, as of
    ///     this scan's introduction, the actual state.
    /// </summary>
    private static readonly Dictionary<string, string> Allowlist = new(StringComparer.Ordinal);

    [Fact]
    public void Generator_source_contains_no_nondeterministic_constructs()
    {
        var violations = new List<string>();

        foreach (var file in GeneratorSources())
        {
            var relative = Relative(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Comments explain the rules (StableHash's own doc names string.GetHashCode); scanning them
                // would make the scan fire on its own documentation.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                    continue;

                foreach (var (id, pattern, why) in Banned)
                {
                    if (!pattern.IsMatch(line)) continue;
                    if (Allowlist.ContainsKey(relative + ":" + id)) continue;

                    violations.Add($"  {relative}({i + 1}) [{id}] {why}\n      {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Nondeterministic construct(s) in generator source — generated output would depend on the machine "
            + "that built it:\n" + string.Join("\n", violations)
            + "\n\nFix the construct. Only add an entry to DeterminismSourceScanTests.Allowlist if the "
            + "construct provably cannot reach emitted text, and say why in the value.");
    }

    [Fact]
    public void The_scan_is_not_vacuous()
    {
        // Every scan of the form "no file contains X" passes trivially over zero files. Backs the scan above
        // the same way SelfAuditNonVacuityTests backs the descriptor scans.
        var files = GeneratorSources().ToList();

        Assert.True(files.Count >= 20,
            $"Only {files.Count} generator source files found — the determinism scan would be vacuous.");
        Assert.Contains(files, f => f.EndsWith("MapperExtractor.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_rules_actually_match_the_constructs_they_ban()
    {
        // Negative control for the DETECTOR, not the sources. A rule whose regex silently stopped matching
        // would leave the scan green forever — the same asymmetry that hid the hollow-detector's comment hole:
        // too strict is loud, too permissive is silent.
        (string Id, string Sample)[] shouldMatch =
        [
            ("D1", "var s = new SortedSet<(string, string)>();"),
            ("D1", "var d = new SortedDictionary<string, int>();"),
            ("D2", "var c = StringComparer.CurrentCulture;"),
            ("D3", "var t = DateTime.UtcNow;"),
            ("D4", "var g = Guid.NewGuid();"),
            ("D5", "var r = new Random();")
        ];

        foreach (var (id, sample) in shouldMatch)
        {
            var rule = Banned.Single(b => b.Id == id);
            Assert.True(rule.Pattern.IsMatch(sample), $"Rule {id} no longer matches: {sample}");
        }

        // And must NOT fire on the fixed forms, or the rule becomes noise people suppress wholesale.
        (string Id, string Sample)[] shouldNotMatch =
        [
            ("D1", "var s = new SortedSet<(string, string)>(AmbientValidator.OrdinalPair);"),
            ("D1", "var d = new SortedDictionary<string, int>(StringComparer.Ordinal);"),
            ("D2", "var c = StringComparer.Ordinal;"),
            ("D3", "var t = someTimestampPassedIn;"),
            ("D5", "var r = new Random(seed);")
        ];

        foreach (var (id, sample) in shouldNotMatch)
        {
            var rule = Banned.Single(b => b.Id == id);
            Assert.False(rule.Pattern.IsMatch(sample), $"Rule {id} fires on an acceptable form: {sample}");
        }
    }

    private static IEnumerable<string> GeneratorSources()
    {
        return Directory.EnumerateFiles(
                Path.Combine(RepoRoot, "src", "DwarfMapper.Generator"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal)
                        && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    private static string Relative(string full)
    {
        return full.Length > RepoRoot.Length ? full[(RepoRoot.Length + 1)..] : full;
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(DeterminismSourceScanTests).Assembly.Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DwarfMapper.NET.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
