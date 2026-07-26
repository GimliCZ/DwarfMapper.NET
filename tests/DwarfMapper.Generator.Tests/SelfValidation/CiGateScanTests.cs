// SPDX-License-Identifier: GPL-2.0-only

using System.IO;

namespace DwarfMapper.Generator.Tests.SelfValidation;

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
/// </summary>
public class CiGateScanTests
{
    private static string Workflow => File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));

    [Theory]
    // Job name, and what its absence would silently stop proving.
    [InlineData("build-test", "the entire test suite would stop running")]
    [InlineData("conformance-gate", "the 47-assertion conformance sample would stop being executed")]
    [InlineData("aot-trim-gate", "trim/AOT cleanliness would stop being verified")]
    [InlineData("codeql", "static security analysis would stop running")]
    public void The_workflow_declares_the_gate_job(string job, string consequence)
    {
        Assert.True(Workflow.Contains("\n  " + job + ":", StringComparison.Ordinal),
            $"CI job '{job}' is no longer declared in ci.yml — {consequence}, and every build would stay "
            + "green while it happened.");
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
            "ci.yml declares a conformance-gate job but never RUNS scripts/conformance-gate.sh. A job that "
            + "invokes nothing is a green check that proves nothing.");

        Assert.True(File.Exists(Path.Combine(RepoRoot, "scripts", "conformance-gate.sh")),
            "ci.yml invokes scripts/conformance-gate.sh but the script does not exist — CI would fail, but "
            + "only after a push; this catches it locally.");
    }

    [Fact]
    public void The_AOT_gate_executes_the_binary_rather_than_only_publishing_it()
    {
        // The specific regression this repo already shipped once: CORRECTNESS.md claimed CI "runs a
        // behavioural gate over the published native binary" while the job only ever published it. Publishing
        // proves it COMPILES trim/AOT-clean and says nothing about behaviour.
        Assert.True(Workflow.Contains("Execute the published AOT binary", StringComparison.Ordinal),
            "The aot-trim-gate job no longer executes the published binary — it would be proving compilation "
            + "only, while SEC-07 and CORRECTNESS.md claim behavioural verification.");
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

    private static string? _repoRoot;

    private static string RepoRoot => _repoRoot ??= FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(CiGateScanTests).Assembly.Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DwarfMapper.NET.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
