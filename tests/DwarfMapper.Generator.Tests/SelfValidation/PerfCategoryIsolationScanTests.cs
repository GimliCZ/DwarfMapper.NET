// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>No test run CI reaches times a <c>Category=Perf</c> ratio beside the rest of the suite.</b>
    ///     <para>
    ///         Round 31 T19 gave the wall-clock ratio tests a job of their own, <c>perf-tests</c>, and took the
    ///         category out of every plain <c>dotnet test</c> step in ci.yml and release.yml. It did not take it out
    ///         of <c>scripts/housekeeping.ps1</c>, which is what the <c>deep-test</c> job runs, so deep-test kept
    ///         timing <c>InterfaceBucketTests</c>' 1.5x bound inside the parallel suite and went red on it on
    ///         2026-10-24 (787.8 to 1383.4 ns) and on PR #9 (857.2 to 1394.0 ns). The perf-tests row of
    ///         <see cref="CiGateScanTests" /> says the category runs nowhere else "since every other leg excludes
    ///         them"; nothing checked that sentence.
    ///     </para>
    ///     <para>
    ///         So the population is built, not listed: every workflow file, and every script a workflow runs. An
    ///         invocation is exempt only when it SELECTS what it runs, by a positive <c>Category=</c> or a
    ///         <c>FullyQualifiedName</c> filter, because then a Perf test can only be in it by name.
    ///     </para>
    /// </summary>
    public class PerfCategoryIsolationScanTests
    {
        private const string ExcludesPerf = "Category!=Perf";

        private static readonly Regex DotnetTest = new(@"(?<![\w-])dotnet\s+test\b", RegexOptions.CultureInvariant);

        private static readonly Regex SelectsByName = new(@"--filter\s+[""']?(Category=|FullyQualifiedName[~=])", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

        private static readonly Regex ScriptRun = new(@"scripts/[A-Za-z0-9_.-]+\.(?:ps1|sh)", RegexOptions.CultureInvariant);

        [Fact]
        public void Every_test_run_CI_reaches_keeps_the_Perf_category_out_unless_it_selects_by_name()
        {
            var invocations = TestInvocations();

            // Non-vacuity: the loop below passes over nothing if the parser finds nothing, and the defect this
            // exists for lived in a SCRIPT, so a scan that stopped reading scripts would pass while blind to it.
            Assert.True(invocations.Count >= 8, $"found only {invocations.Count} `dotnet test` invocation(s) CI can reach; this scan is reading the wrong thing.");
            Assert.Contains(invocations, i => i.Source.EndsWith("housekeeping.ps1", StringComparison.Ordinal));

            var offenders = invocations.Where(i => !i.Command.Contains(ExcludesPerf, StringComparison.Ordinal) && !SelectsByName.IsMatch(i.Command))
                                       .Select(i => i.Source + ": " + i.Command)
                                       .ToList();

            Assert.True(offenders.Count == 0,
                "test run(s) CI reaches would time the Category=Perf ratio tests inside the parallel suite, where " +
                "a neighbour's load decides the verdict: [" + string.Join(" | ", offenders) + "]. Add --filter \"" +
                ExcludesPerf + "\" (or join it to the existing filter with &); the perf-tests job runs them alone.");
        }

        /// <summary>
        ///     Every <c>dotnet test</c> command in the workflows and in the scripts they run, one per logical line:
        ///     PowerShell's backtick and the shell's backslash continuations are joined, comment lines are skipped.
        /// </summary>
        private static List<(string Source, string Command)> TestInvocations()
        {
            var workflows = Directory.GetFiles(Path.Combine(RepoPaths.Root, ".github", "workflows"), "*.yml");
            var scripts = workflows.SelectMany(w => ScriptRun.Matches(File.ReadAllText(w)).Select(m => m.Value))
                                   .Distinct(StringComparer.Ordinal)
                                   .Select(s => Path.Combine(RepoPaths.Root, s))
                                   .Where(File.Exists);

            var found = new List<(string, string)>();
            foreach (var file in workflows.Concat(scripts))
            {
                var source = Path.GetRelativePath(RepoPaths.Root, file).Replace('\\', '/');
                foreach (var line in LogicalLines(File.ReadAllText(file)))
                {
                    if (!line.TrimStart().StartsWith('#') && DotnetTest.IsMatch(line)) { found.Add((source, line.Trim())); }
                }
            }

            return found;
        }

        private static IEnumerable<string> LogicalLines(string text)
        {
            var pending = "";
            foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.EndsWith('`') || line.EndsWith('\\'))
                {
                    pending += line[..^1] + " ";
                    continue;
                }

                yield return pending + line;
                pending = "";
            }

            if (pending.Length > 0) { yield return pending; }
        }
    }
}
