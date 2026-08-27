// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Asserts the CI workflow still INVOKES the proofs this repo relies on.
    ///     <para>
    ///         "Did the gate run?" cannot be answered from inside the gate — <c>conformance-gate.sh</c> can refuse
    ///         to certify a run it could not measure, but it cannot notice that nobody called it. Deleting the job
    ///         from <c>ci.yml</c> makes every build green and every proof silent, which is the same shape as a
    ///         vacuous test: absence of failure read as evidence of success.
    ///     </para>
    ///     <para>
    ///         This is the cheap half of that problem, and it is worth being explicit that it IS only half. A test
    ///         can see that the job is declared; it cannot see whether the job is a required check on the branch,
    ///         nor whether someone re-ran a workflow with it skipped. Those are repository settings, and the
    ///         honest position is to name the gap rather than imply the test closes it.
    ///     </para>
    ///     <para>
    ///         Round 23, I16: the job list is now COMPLETE BY CONSTRUCTION rather than by memory. It was filed
    ///         as five missing rows and was actually eight, three of them missing since well before round 23 —
    ///         a hand-maintained list of things-to-check drifting behind the thing it checks, which is the
    ///         failure mode this class exists to catch one level down. <see cref="DeclaredJobs" /> is compared
    ///         against the workflow's own job keys in both directions by
    ///         <see cref="Every_job_in_ci_yml_is_covered_by_a_row_of_this_scan" />, so the next job added to
    ///         <c>ci.yml</c> without a row is a red rather than another silent hole.
    ///     </para>
    /// </summary>
    public class CiGateScanTests
    {
        private static string? _repoRoot;

        private static string Workflow => File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));

        private static string RepoRoot => _repoRoot ??= FindRepoRoot();

        /// <summary>
        ///     Every job <c>ci.yml</c> declares, and what its deletion would silently stop proving.
        ///     <para>
        ///         A <c>[MemberData]</c> list rather than <c>[InlineData]</c> rows for one reason:
        ///         <see cref="Every_job_in_ci_yml_is_covered_by_a_row_of_this_scan" /> reads this same
        ///         collection and compares it to the jobs actually declared in the workflow. Two lists could
        ///         drift apart; one cannot. That guard is the half of I16 the filing did not ask for and the
        ///         evidence says is the real fix — see the class remarks.
        ///     </para>
        /// </summary>
        public static IEnumerable<object[]> DeclaredJobs()
        {
            // ── Per-push tier ────────────────────────────────────────────────────────────────────────────
            yield return ["build-test", "the entire test suite would stop running"];
            yield return ["surface-matrix", "the 866-cell surface census would stop being measured, and its " + "red cells are the only place a silently-ignored option is visible"];
            yield return ["conformance-gate", "the 47-assertion conformance sample would stop being executed"];
            yield return ["aot-trim-gate", "trim/AOT cleanliness would stop being verified"];
            yield return ["codeql", "static security analysis would stop running"];
            yield return ["sbom", "the release artifacts would stop carrying a signed bill of materials"];
            // `continue-on-error: true` today by its own comment's design, pending its first green run. So
            // this row asserts DECLARATION only and says nothing about the leg's verdict — which is the
            // honest claim for it either way, since a continue-on-error leg cannot fail the workflow.
            yield return ["roslyn-forward-compat", "the generator would stop being built and tested against " + "an unpinned newer SDK (declaration only — the leg is " + "continue-on-error and cannot fail the workflow)"];

            // ── Nightly tier: cron on the default branch plus manual dispatch, never on push ──────────────
            // "Declared" is an even weaker claim for these than for the per-push jobs above — but deleting
            // any of them would still turn a measured floor into decoration, silently.
            yield return ["mutation", "all five Stryker legs' break floors would stop being measured nightly"];
            yield return ["deep-test", "the deep suite, the coverage floors and ILVerify would stop running nightly"];
            // ── The five added in round-23 Layer 3 (I16) ─────────────────────────────────────────────────
            yield return ["reproducible-build", "the shipped packages would stop being byte-compared across " + "two clean builds of the same commit"];
            yield return ["package-size", "the package-size ceiling would stop being measured, and the class " + "of regression it alone catches — something unintended starting " + "to ship — would go back to being invisible"];
            yield return ["cross-platform", "Windows and macOS would stop being tested at all, leaving ubuntu " + "as the only platform any suite has ever run on in CI"];
            // continue-on-error: true by design (a preview SDK's own breakage must not red the nightly).
            yield return ["preview-sdk-canary", "the next major SDK would stop being tried against this repo " + "before it ships (declaration only — the leg is " + "continue-on-error and cannot fail the workflow)"];
            // The ALERT STEP is continue-on-error; the job's input-sanity step is not. So this row, too,
            // asserts declaration and nothing about a verdict.
            yield return ["bench-wall-time-alert", "benchmark wall-time would stop being tracked night to " + "night (declaration only — the alert step is " + "continue-on-error by deliberate design)"];
        }

        [Theory]
        [MemberData(nameof(DeclaredJobs))]
        public void The_workflow_declares_the_gate_job(string job, string consequence)
        {
            Assert.True(Workflow.Contains("\n  " + job + ":", StringComparison.Ordinal),
                $"CI job '{job}' is no longer declared in ci.yml — {consequence}, and every build would stay " + "green while it happened.");
        }

        /// <summary>
        ///     The completeness half, and the reason I16 was more than the five rows it was filed as.
        ///     <para>
        ///         The row list above is hand-maintained, and a hand-maintained list of things-to-check drifts
        ///         behind the thing it checks — silently, because a missing row does not fail anything. That is
        ///         not a hypothesis here: when I16 was fixed the list was missing EIGHT jobs, not the five the
        ///         filing named. Three of them (<c>surface-matrix</c>, <c>roslyn-forward-compat</c>,
        ///         <c>sbom</c>) had been unlisted since long before round 23 and nobody had noticed, including
        ///         the filing that was specifically auditing this list.
        ///     </para>
        ///     <para>
        ///         So this asserts the row set and the workflow's job set are EQUAL, in both directions: a new
        ///         job added to <c>ci.yml</c> without a row reds here (the drift that produced I16), and a row
        ///         naming a job that no longer exists reds too (a stale row is a scan that proves nothing while
        ///         looking like it proves something — the vacuity shape this whole class is about).
        ///     </para>
        /// </summary>
        [Fact]
        public void Every_job_in_ci_yml_is_covered_by_a_row_of_this_scan()
        {
            var declared = DeclaredJobs().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);
            var actual = JobNamesInWorkflow();

            // Non-vacuity: an empty or unparsed job set would make the set comparison pass by being equally
            // empty on both sides only if the row list were empty too — but a parser that silently found
            // nothing while rows exist must be loud about WHY, not just about the difference.
            Assert.True(actual.Count > 5,
                $"only {actual.Count} job(s) parsed out of ci.yml — the `jobs:` section or the two-space job " + "indentation this scan (and the substring check above) both rely on has changed shape, and " + "every assertion in this class is now measuring the wrong thing.");

            var missingRows = actual.Except(declared, StringComparer.Ordinal).OrderBy(j => j, StringComparer.Ordinal).ToList();
            var staleRows = declared.Except(actual, StringComparer.Ordinal).OrderBy(j => j, StringComparer.Ordinal).ToList();

            Assert.True(missingRows.Count == 0,
                "ci.yml declares job(s) that no row of this scan names: [" + string.Join(", ", missingRows) + "]. Deleting one of them would be a SILENT deletion — the exact hole filed as I16. Add a row " + "to DeclaredJobs() with the sentence naming what its absence would stop proving.");

            Assert.True(staleRows.Count == 0,
                "this scan has row(s) for job(s) ci.yml no longer declares: [" + string.Join(", ", staleRows) + "]. Either the job was deleted (and the row is the alarm — read it before deleting it) or it " + "was renamed (and the row must be renamed with it, or it proves nothing while looking like " + "it proves something).");
        }

        /// <summary>
        ///     The job keys of <c>ci.yml</c>: two-space-indented mapping keys AFTER the <c>jobs:</c> line.
        ///     Anchored on <c>jobs:</c> deliberately — <c>on:</c> and <c>permissions:</c> also carry
        ///     two-space keys (<c>push:</c>, <c>schedule:</c>, <c>contents:</c>), and a parser that swept the
        ///     whole file would demand rows for those.
        /// </summary>
        private static HashSet<string> JobNamesInWorkflow()
        {
            var text = Workflow;
            var start = text.IndexOf("\njobs:", StringComparison.Ordinal);
            Assert.True(start >= 0, "ci.yml has no top-level `jobs:` key — this scan cannot locate any job.");

            return Regex.Matches(text[start..], @"^  (?<job>[A-Za-z0-9_-]+):[ \t]*$", RegexOptions.Multiline)
                .Select(m => m.Groups["job"].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        [Fact]
        public void The_conformance_gate_is_actually_invoked_not_merely_declared()
        {
            // A job can exist and no longer call the script — the check above would still pass, so it is not
            // sufficient on its own.
            // Must match the RUN step, not merely the filename: an earlier version checked for the bare string
            // and was satisfied by the adjacent `chmod +x scripts/conformance-gate.sh` line, so replacing the real
            // invocation with `echo skipped` left the test green. Verified by mutation.
            Assert.True(Workflow.Contains("run: ./scripts/conformance-gate.sh", StringComparison.Ordinal),
                "ci.yml declares a conformance-gate job but never RUNS scripts/conformance-gate.sh. A job that " + "invokes nothing is a green check that proves nothing.");

            Assert.True(File.Exists(Path.Combine(RepoRoot, "scripts", "conformance-gate.sh")),
                "ci.yml invokes scripts/conformance-gate.sh but the script does not exist — CI would fail, but " + "only after a push; this catches it locally.");
        }

        [Fact]
        public void The_AOT_gate_executes_the_binary_rather_than_only_publishing_it()
        {
            // The specific regression this repo already shipped once: CORRECTNESS.md claimed CI "runs a
            // behavioural gate over the published native binary" while the job only ever published it. Publishing
            // proves it COMPILES trim/AOT-clean and says nothing about behaviour.
            Assert.True(Workflow.Contains("Execute the published AOT binary", StringComparison.Ordinal),
                "The aot-trim-gate job no longer executes the published binary — it would be proving compilation " + "only, while SEC-07 and CORRECTNESS.md claim behavioural verification.");
        }

        [Fact]
        public void The_workflow_file_is_parseable_enough_to_scan()
        {
            // Non-vacuity: every assertion here is a substring check, which passes trivially over an empty or
            // unreadable file.
            var text = Workflow;
            Assert.True(text.Length > 500, "ci.yml is suspiciously small — these scans would be vacuous.");
            Assert.Contains("jobs:", text, StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(CiGateScanTests).Assembly.Location)!);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DwarfMapper.NET.sln")))
                dir = dir.Parent;

            Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
            return dir.FullName;
        }
    }
}
