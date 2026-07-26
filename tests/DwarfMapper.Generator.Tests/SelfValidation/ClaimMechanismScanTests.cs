// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Binds every published correctness/security claim to something that executes, and fails when the binding
///     rots.
///     <para>
///         Documentation is the one artefact in this repo with no test behind it, and it has already drifted
///         three times: <c>AutoValidate</c> was documented as a fail-fast safety net while the generator never
///         read the flag; the "informed dumps" output was described in a shape the differ never produced; and
///         <c>CORRECTNESS.md</c> asserted CI "runs a behavioural gate over the published native binary" when
///         CI only ever published it. Each was found by hand, months apart. A claim that outlives its
///         mechanism is worse than one never made — it is trusted.
///     </para>
///     <para>
///         So each claim carries a stable id and names its mechanism, and this scan checks the mechanism still
///         exists. It deliberately verifies EXISTENCE rather than correctness: proving the depth guard works is
///         <c>DepthSafetyRuntimeTests</c>' job, and duplicating that here would be a second, weaker copy that
///         drifts on its own. What nothing else can catch is the binding silently pointing at nothing.
///     </para>
/// </summary>
public class ClaimMechanismScanTests
{
    /// <summary>Matches a register row: <c>| SEC-01 | claim text | `TestName` |</c>.</summary>
    private static readonly Regex RegisterRow = new(
        @"^\|\s*(?<id>(SEC|COR)-\d{2})\s*\|(?<claim>[^|]*)\|\s*`(?<mech>[^`]+)`\s*\|",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static List<(string Id, string Claim, string Mechanism, string File)> Register()
    {
        var rows = new List<(string Id, string Claim, string Mechanism, string File)>();
        foreach (var doc in new[] { "SECURITY.md", "CORRECTNESS.md" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", doc));
            foreach (Match m in RegisterRow.Matches(text))
                rows.Add((m.Groups["id"].Value, m.Groups["claim"].Value.Trim(),
                    m.Groups["mech"].Value.Trim(), doc));
        }

        return rows;
    }

    /// <summary>
    ///     Every test type name declared anywhere under <c>tests/</c>, read from SOURCE rather than reflection.
    ///     <para>
    ///         Reflection was the first attempt and was wrong: this scan runs inside
    ///         <c>DwarfMapper.Generator.Tests</c>, so <c>AppDomain.GetAssemblies()</c> cannot see
    ///         <c>DwarfMapper.IntegrationTests</c> or <c>DwarfMapper.Testing.Tests</c> — three perfectly valid
    ///         bindings (<c>DepthSafetyRuntimeTests</c>, <c>MassAssignmentGuardRuntimeTests</c>,
    ///         <c>RoundTripTests</c>) reported as missing. A scan that cannot see two thirds of the test suite
    ///         would have forced those claims to be unbound or the bindings faked. Source scanning is also what
    ///         the repo's other cross-project scans already do.
    ///     </para>
    /// </summary>
    // Lazy, not a field initializer: static initializers run in declaration order, so building this eagerly
    // ran before RepoRoot was assigned and threw inside the type initializer.
    private static HashSet<string>? _testTypeNames;

    private static HashSet<string> TestTypeNames => _testTypeNames ??= BuildTestTypeNames();

    private static HashSet<string> BuildTestTypeNames()
    {
        var declaration = new Regex(@"\b(?:class|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests"), "*.cs",
                     SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                continue;

            foreach (Match m in declaration.Matches(File.ReadAllText(file)))
                names.Add(m.Groups["name"].Value);
        }

        return names;
    }

    [Fact]
    public void Every_published_claim_names_a_mechanism_that_exists()
    {
        var unbound = new List<string>();

        foreach (var (id, claim, mechanism, doc) in Register())
        {
            // CI: bindings name a workflow job rather than a test — honest about being the weaker form.
            if (mechanism.StartsWith("CI:", StringComparison.Ordinal))
            {
                var job = mechanism["CI:".Length..];
                var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));
                if (!workflow.Contains(job + ":", StringComparison.Ordinal))
                    unbound.Add($"  {doc} {id} → CI job '{job}' is not defined in ci.yml — claim: {claim}");
                continue;
            }

            if (!TestTypeNames.Contains(mechanism))
                unbound.Add($"  {doc} {id} → test type '{mechanism}' does not exist — claim: {claim}");
        }

        Assert.True(unbound.Count == 0,
            "Published claim(s) whose mechanism is missing — the documentation asserts a guarantee nothing "
            + "enforces:\n" + string.Join("\n", unbound)
            + "\n\nEither restore the mechanism, or delete the claim. A claim without a mechanism is the "
            + "drift this scan exists to end.");
    }

    [Fact]
    public void The_register_is_not_vacuous()
    {
        // The scan's shape is "for each row, check X" — which passes trivially if the regex stops matching,
        // e.g. after someone reformats the table. Backed the same way SelfAuditNonVacuityTests backs the
        // descriptor scans.
        var rows = Register();

        Assert.True(rows.Count >= 13,
            $"Only {rows.Count} claim rows parsed (expected >= 13: SEC-01..07 + COR-01..06). The register "
            + "regex has probably stopped matching after a table edit, which would make this scan vacuous.");

        Assert.Contains(rows, r => r.Id.StartsWith("SEC-", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Id.StartsWith("COR-", StringComparison.Ordinal));
    }

    [Fact]
    public void Claim_ids_are_unique_and_contiguous()
    {
        var rows = Register();
        var ids = rows.Select(r => r.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        // A gap usually means a claim was deleted from the table while its id stayed referenced elsewhere.
        foreach (var prefix in new[] { "SEC", "COR" })
        {
            var numbers = ids.Where(i => i.StartsWith(prefix, StringComparison.Ordinal))
                .Select(i => int.Parse(i[4..], System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(n => n).ToList();

            Assert.NotEmpty(numbers);
            Assert.Equal(Enumerable.Range(1, numbers.Count).ToList(), numbers);
        }
    }

    [Fact]
    public void The_scan_detects_a_broken_binding()
    {
        // Negative control for the detector. Existence-checking is exactly the kind of rule that keeps
        // "passing" after its lookup silently stops finding anything, so prove a bogus mechanism is rejected
        // and a real one is accepted.
        Assert.DoesNotContain("ZzNoSuchTestType", TestTypeNames);
        Assert.Contains("DepthSafetyRuntimeTests", TestTypeNames);
    }

    private static string? _repoRoot;

    private static string RepoRoot => _repoRoot ??= FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(ClaimMechanismScanTests).Assembly.Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DwarfMapper.NET.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
