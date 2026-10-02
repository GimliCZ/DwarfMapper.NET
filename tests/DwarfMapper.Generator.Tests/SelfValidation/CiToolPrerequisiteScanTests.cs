// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A CI job that can reach a tool-dependent test must install the tool.</b>
    ///     <para>
    ///         <see cref="EmittedIlIsVerifiableTests" /> enforces the ruling that every method DwarfMapper emits
    ///         is verifiable IL, and it FAILS LOUDLY rather than skipping when <c>ilverify</c> is absent — "a
    ///         machine that cannot verify the IL cannot make this claim either, so absence is a loud failure, not
    ///         a skip". That design is deliberate and is not what this test guards. What it guards is the
    ///         consequence: a loud test is only useful if every job that runs it can actually satisfy it.
    ///     </para>
    ///     <para>
    ///         <b>The regression this exists for.</b> Round 29 added the tool install to <c>build-test</c> and to
    ///         <c>deep-test</c>, and stopped there. Three other jobs run the suite in a way that reaches the same
    ///         test — <c>roslyn-forward-compat</c> and <c>preview-sdk-canary</c> with
    ///         <c>--filter "Category!=SurfaceMatrix"</c>, and <c>cross-platform</c> with no filter at all. The
    ///         first of those went red the moment the round-29 PR reached CI, with two failures reading
    ///         <i>"ilverify is not on PATH"</i>; the other two are schedule-gated and would have failed on the
    ///         next nightly instead, which is the more expensive place to find out.
    ///     </para>
    ///     <para>
    ///         The fix was three lines of YAML. The reason this test exists rather than only those three lines is
    ///         that nothing else would notice the FOURTH job, whenever someone adds one.
    ///     </para>
    ///     <para>
    ///         <b>Round 32: the fourth job had arrived, and the scan could not see it.</b> CI's <c>mutation</c> job runs
    ///         the suite through <c>dotnet stryker</c>, not <c>dotnet test</c>, and <c>release.yml</c> runs
    ///         <c>dotnet test</c> in a file this scan never opened. Neither installed the tool, so every Stryker leg
    ///         started with two failing tests (nightly run 213), and a release tag would have failed its own test
    ///         step. The scan now reads every workflow file and counts <c>dotnet stryker</c> as running the suite
    ///         (round 32 T03; <c>Issues/round32/FINDING-T02-ci-phantom-kills.md</c>).
    ///     </para>
    /// </summary>
    public class CiToolPrerequisiteScanTests
    {
        /// <summary>
        ///     A job whose filter SELECTS one category — <c>Category=SurfaceMatrix</c>, or since round 31
        ///     <c>Category=Perf</c> — cannot reach the ILVerify tests, which carry neither, so it is exempt. Any other
        ///     <c>dotnet test</c> invocation can, whether it filters a category OUT (<c>!=</c>, which these strings do
        ///     not match) or not at all.
        /// </summary>
        private const string SurfaceMatrixOnly = "Category=SurfaceMatrix";

        private const string PerfOnly = "Category=Perf";

        /// <summary>Every workflow file, as (file name, YAML). Not only <c>ci.yml</c>: <c>release.yml</c> runs the suite too.</summary>
        private static List<(string File, string Yaml)> Workflows()
        {
            var dir = Path.Combine(RepoPaths.Root, ".github", "workflows");
            return Directory.GetFiles(dir, "*.yml").Concat(Directory.GetFiles(dir, "*.yaml"))
                            .OrderBy(p => p, StringComparer.Ordinal)
                            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)))
                            .ToList();
        }

        /// <summary><c>file:job</c> -> the job's YAML body, over every workflow file.</summary>
        private static Dictionary<string, string> AllJobs()
        {
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (file, yaml) in Workflows())
            {
                foreach (var (name, body) in Jobs(yaml)) { all[file + ":" + name] = body; }
            }

            return all;
        }

        /// <summary>
        ///     A job runs the suite if it says <c>dotnet test</c>, or <c>dotnet stryker</c>: Stryker's initial run and
        ///     every mutant run execute the test projects, and a test that cannot pass there fails there too.
        /// </summary>
        private static bool RunsTheSuite(string body)
        {
            return body.Contains("dotnet test", StringComparison.Ordinal) ||
                   body.Contains("dotnet stryker", StringComparison.Ordinal);
        }

        /// <summary>Job name -> its YAML body, split on the two-space keys directly under <c>jobs:</c>.</summary>
        private static Dictionary<string, string> Jobs(string yaml)
        {
            var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var starts = new List<(int Line, string Name)>();
            for (var i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^  ([A-Za-z0-9_-]+):\s*$");
                if (m.Success) { starts.Add((i, m.Groups[1].Value)); }
            }

            var jobs = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var k = 0; k < starts.Count; k++)
            {
                var end = k + 1 < starts.Count ? starts[k + 1].Line : lines.Length;
                jobs[starts[k].Name] = string.Join("\n", lines[(starts[k].Line + 1)..end]);
            }

            return jobs;
        }

        [Fact]
        public void Every_job_that_can_reach_the_ILVerify_tests_installs_ilverify()
        {
            var workflows = Workflows();
            var jobs = AllJobs();

            // Non-vacuity first. A parser that found no jobs, or a workflow that stopped running tests, would
            // make the loop below pass by iterating over nothing.
            Assert.True(workflows.Count >= 2, $"found only {workflows.Count} workflow file(s); ci.yml and release.yml both run the suite.");
            Assert.True(jobs.Count(j => j.Key.StartsWith("ci.yml:", StringComparison.Ordinal)) >= 10,
                $"parsed only {jobs.Count(j => j.Key.StartsWith("ci.yml:", StringComparison.Ordinal))} ci.yml job(s); the workflow has many more.");

            var runners = jobs.Where(j => RunsTheSuite(j.Value)).ToList();
            Assert.True(runners.Count >= 4,
                $"only {runners.Count} job(s) appear to run `dotnet test` or `dotnet stryker`; this scan is looking at the wrong thing.");

            var offenders = new List<string>();
            foreach (var (name, body) in runners)
            {
                // Exempt: filtered to the surface matrix, so the ILVerify tests are out of scope for it.
                if (body.Contains(SurfaceMatrixOnly, StringComparison.Ordinal) || body.Contains(PerfOnly, StringComparison.Ordinal)) { continue; }

                if (!body.Contains("dotnet-ilverify", StringComparison.Ordinal))
                {
                    offenders.Add(name);
                }
            }

            Assert.True(offenders.Count == 0,
                "Workflow job(s) run the test suite (`dotnet test` or `dotnet stryker`) in a way that reaches EmittedIlIsVerifiableTests but do not " +
                "install ilverify, so they will fail with \"ilverify is not on PATH\" rather than with a real " +
                "finding: [" + string.Join(", ", offenders) + "]. Add " +
                "`- run: dotnet tool install --global dotnet-ilverify --version 10.0.11` to each, or filter the " +
                "job to " + SurfaceMatrixOnly + " if it genuinely should not run them.");
        }

        /// <summary>
        ///     The exemption has to be real, or the check above passes by excusing everything. This pins that at
        ///     least one job IS exempt and that it is exempt for the stated reason — it filters to the surface
        ///     matrix — rather than because the scan failed to see it.
        /// </summary>
        [Fact]
        public void The_surface_matrix_exemption_applies_to_a_job_that_really_is_filtered()
        {
            var jobs = AllJobs();

            var exempt = jobs.Where(j => j.Value.Contains("dotnet test", StringComparison.Ordinal) &&
                                         j.Value.Contains(SurfaceMatrixOnly, StringComparison.Ordinal))
                             .Select(j => j.Key)
                             .ToList();

            Assert.True(exempt.Count > 0,
                "no CI job filters to " + SurfaceMatrixOnly + ", so the exemption in the check beside this one " +
                "excuses nothing and may be silently mis-written.");

            // And the exemption must not be swallowing the whole suite: at least one job must still be REQUIRED
            // to install the tool, or the guard above is vacuous.
            var required = jobs.Count(j => j.Value.Contains("dotnet test", StringComparison.Ordinal) &&
                                           !j.Value.Contains(SurfaceMatrixOnly, StringComparison.Ordinal));
            Assert.True(required > 0,
                "every test-running job claims the surface-matrix exemption, so the ilverify requirement is " +
                "enforced against nothing.");
        }

        /// <summary>
        ///     The tool version is pinned everywhere it is installed, for the reason the workflow's own comments
        ///     give about Stryker and CycloneDX: the thing computing a gate's verdict must not move under the
        ///     gate. An unpinned install would let a tool upgrade change a verdict with no repository change to
        ///     point at.
        /// </summary>
        [Fact]
        public void Every_ilverify_install_pins_the_same_version()
        {
            // Every workflow, so an install added to release.yml is held to the same pin as ci.yml's.
            var text = string.Join("\n", Workflows().Select(w => w.Yaml));
            var versions = Regex.Matches(text, @"dotnet tool install --global dotnet-ilverify --version ([\d.]+)")
                                .Select(m => m.Groups[1].Value)
                                .ToList();

            var bare = Regex.Matches(text, @"dotnet tool install --global dotnet-ilverify(?! --version)").Count;

            Assert.True(bare == 0, $"{bare} ilverify install(s) do not pin a version.");
            Assert.True(versions.Count >= 2, $"expected several pinned ilverify installs, found {versions.Count}.");
            Assert.True(versions.Distinct(StringComparer.Ordinal).Count() == 1,
                "ilverify is pinned to more than one version across the workflow: [" +
                string.Join(", ", versions.Distinct(StringComparer.Ordinal)) + "]. The tool computing a gate's " +
                "verdict must not differ between the jobs that compute it.");
        }
    }
}
