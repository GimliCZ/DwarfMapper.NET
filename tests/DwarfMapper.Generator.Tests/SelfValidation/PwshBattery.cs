// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Drives a scenario battery through <c>pwsh</c> against the REAL <c>scripts/gate-checks.ps1</c>.
///     <para>
///         The gates are PowerShell because <c>housekeeping.ps1</c> is, so the only honest test runs the
///         actual functions against fake inputs — a C# re-implementation would prove a copy. Every battery
///         is one script printing one <c>CASE &lt;name&gt;: &lt;OK result|THREW message&gt;</c> line per
///         scenario, run in ONE process start to keep the fast-tier cost to a single spawn.
///     </para>
///     <para>
///         Shared by <see cref="GateBandLogicTests" /> (R2 band logic) and
///         <see cref="MutationDecontaminationSweepTests" /> (I4's sweep) rather than copied into each: two
///         copies of process plumbing are two things that can drift, and the H7 termination bound is the
///         part that must not.
///     </para>
///     <para>
///         Prerequisite: <c>pwsh</c> on PATH — the same prerequisite <c>housekeeping.ps1</c> itself has
///         (its shebang is <c>/usr/bin/env pwsh</c>; the H8 note tracks documenting it). A machine that
///         cannot run the gates cannot verify them either, so absence is a loud failure, not a skip.
///     </para>
/// </summary>
internal static class PwshBattery
{
    /// <summary>
    ///     Runs <c>battery.ps1</c> from <paramref name="workDir" /> and returns the parsed CASE lines.
    ///     Hard-bounded wait (H7: no unbounded loop — a hung pwsh is killed and named, never idled on).
    /// </summary>
    /// <param name="workDir">Temp directory holding <c>battery.ps1</c> and its fixtures.</param>
    /// <param name="expectedCases">
    ///     Exactly how many CASE lines the battery must print. A scenario that silently vanishes is the
    ///     vacuity these batteries exist to prevent, so the count is asserted, not observed.
    /// </param>
    /// <param name="subject">What the battery proves, for the failure messages.</param>
    internal static Dictionary<string, string> Run(string workDir, int expectedCases, string subject)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(workDir, "battery.ps1"));
        psi.ArgumentList.Add("-GateChecks");
        psi.ArgumentList.Add(Path.Combine(RepoPaths.Root, "scripts", "gate-checks.ps1"));
        psi.ArgumentList.Add("-WorkDir");
        psi.ArgumentList.Add(workDir);

        using var process = new Process();
        process.StartInfo = psi;
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Assert.Fail($"pwsh (PowerShell 7) is not on PATH — {subject} cannot be verified on this "
                        + "machine, and housekeeping.ps1 itself needs the same prerequisite (H8). "
                        + $"Underlying error: {e.Message}");
        }

        // Async reads BEFORE the bounded wait, so neither a full pipe nor a hung pwsh can defeat the
        // 120 s ceiling (H7: the wait is the termination bound; Kill closes the streams and completes
        // both reads either way).
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"The battery for {subject} did not finish within 120 s — killed. These checks "
                        + "must be milliseconds; a hang here means gate-checks.ps1 gained blocking "
                        + "behaviour.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        Assert.True(process.ExitCode == 0,
            $"The battery for {subject} exited {process.ExitCode} — the harness itself failed rather than "
            + $"a scenario (every scenario catches its own throw).\nstdout:\n{stdout}\nstderr:\n{stderr}");

        var cases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("CASE ", StringComparison.Ordinal)) continue;
            var colon = trimmed.IndexOf(": ", StringComparison.Ordinal);
            cases[trimmed[5..colon]] = trimmed[(colon + 2)..];
        }

        Assert.True(cases.Count == expectedCases,
            $"Expected {expectedCases} CASE lines from the {subject} battery, got {cases.Count} — a "
            + "scenario vanished, which is exactly the vacuity this test exists to prevent.\nstdout:\n"
            + $"{stdout}\nstderr:\n{stderr}");
        return cases;
    }

    /// <summary>
    ///     What every battery script opens with, so the two batteries cannot disagree about the contract
    ///     <see cref="Run" /> parses: the two parameters, strict error handling, the real gate file
    ///     dot-sourced, and the <c>Show</c> helper — run one scenario, print exactly one CASE line, never
    ///     let a throw escape (a scenario throwing OUT would kill the harness and be reported as a harness
    ///     failure rather than as the failure it is). Concatenate the scenarios after it.
    /// </summary>
    internal const string ScriptPrelude = """
        param([string]$GateChecks, [string]$WorkDir)
        $ErrorActionPreference = 'Stop'
        . $GateChecks
        function Show([string]$name, [scriptblock]$body) {
            try {
                $r = & $body
                if ($null -eq $r) { $r = 'PASS' }
                Write-Output "CASE ${name}: OK ${r}"
            } catch {
                # Flattened: the contract Run() parses is ONE line per case, and a gate whose message
                # lists offending files across several lines would otherwise be truncated to its first
                # line — the assertions would then silently stop checking the part that names the files.
                Write-Output "CASE ${name}: THREW $($_.Exception.Message -replace '\r?\n', ' | ')"
            }
        }

        """;
}
