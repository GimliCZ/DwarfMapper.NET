// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A Linux job names the Ubuntu version it was proven on.</b>
    ///     <para>
    ///         GitHub moves <c>ubuntu-latest</c> from Ubuntu 24.04 to 26.04 over several weeks, 2026-10-19 to
    ///         2026-11-19 (actions/runner-images#14748). Inside that window two jobs of one run can land on different
    ///         images, so a red can be the image rather than the change, and a green says nothing about the image the
    ///         next run gets. The SDK (<c>global.json</c>, <c>rollForward: disable</c>) and every action (a commit SHA)
    ///         are pinned for the same reason: what a gate ran on is part of its verdict.
    ///     </para>
    ///     <para>
    ///         So no job floats on <c>ubuntu-latest</c>, and moving to a new image is one commit that changes
    ///         <see cref="Pinned" /> here and the labels in the workflows together. The one job allowed elsewhere is
    ///         <see cref="CanaryJob" />, which tries the next image before the pins move onto it.
    ///     </para>
    /// </summary>
    public class CiRunnerImagePinScanTests
    {
        private const string Pinned = "ubuntu-24.04";

        private const string Canary = "ubuntu-26.04";

        private const string CanaryJob = "ubuntu-26-canary";

        /// <summary>Every <c>runs-on: ubuntu-…</c> in the workflows, matrix entries included, with the job it is in.</summary>
        private static List<(string File, int Line, string Job, string Label)> UbuntuLabels()
        {
            var found = new List<(string, int, string, string)>();
            var workflows = Directory.GetFiles(Path.Combine(RepoPaths.Root, ".github", "workflows"), "*.yml");
            foreach (var path in workflows.OrderBy(p => p, StringComparer.Ordinal))
            {
                var job = "";
                var lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var key = Regex.Match(lines[i], @"^  ([A-Za-z0-9_-]+):\s*$");
                    if (key.Success) { job = key.Groups[1].Value; }

                    var label = Regex.Match(lines[i], @"^\s*(?:-\s*)?runs-on:\s*['""]?(ubuntu-[^\s'""]+)");
                    if (label.Success) { found.Add((Path.GetFileName(path), i + 1, job, label.Groups[1].Value)); }
                }
            }

            return found;
        }

        [Fact]
        public void Every_Linux_job_runs_on_the_pinned_image_and_only_the_canary_tries_the_next()
        {
            var labels = UbuntuLabels();

            // Non-vacuity: the workflows declare well over a dozen Linux jobs, so a scan that found few is reading
            // the wrong thing and the checks below would pass over nothing.
            Assert.True(labels.Count >= 15, $"found only {labels.Count} `runs-on: ubuntu-…` line(s) across the workflows.");

            var unpinned = labels.Where(l => l.Label != Pinned && !(l.Job == CanaryJob && l.Label == Canary))
                                 .Select(l => $"{l.File}:{l.Line} ({l.Job}) runs on {l.Label}")
                                 .ToList();
            Assert.True(unpinned.Count == 0,
                "Linux job(s) not on the pinned " + Pinned + ": [" + string.Join("; ", unpinned) + "]. `ubuntu-latest` " +
                "moves between images over weeks, so a job on it can be measured on another image than its neighbours. " +
                "Name " + Pinned + ", or move every pin and this constant in one commit.");

            var canary = labels.Where(l => l.Job == CanaryJob).Select(l => l.Label).ToList();
            Assert.True(canary.Count == 1 && canary[0] == Canary,
                CanaryJob + " must run on " + Canary + " and nothing else; found [" + string.Join(", ", canary) + "].");
        }
    }
}
