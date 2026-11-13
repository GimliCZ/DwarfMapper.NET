// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.CompilerTests.TypeGraphs;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     <b>A sample compiles on the thread that runs it.</b>
    ///     <para>
    ///         Round 34 hill-climb, round 2. The population facts already run their samples in parallel
    ///         (<see cref="PinnedSampling" />'s <c>Parallel.For</c>), so Roslyn's own concurrent build nested a second
    ///         level of parallel work inside every sample, on the same four CPUs. Measured in the four-CPU container over
    ///         300 pinned samples: 5.00 -> 4.56 ms per sample at the facts' 4-way parallelism. Single-threaded it costs
    ///         (14.8 -> 18.6 ms), which only the handful of one-sample tests pay. This holds the harness to it.
    ///     </para>
    /// </summary>
    public class SampleCompilationOptionsTests
    {
        [Fact]
        public void A_sample_compiles_on_the_thread_that_runs_it()
        {
            var row = PinnedCorpus.Rows.Single(r => r.Id == "A11-representation-mirror");
            var (_, output) = CompilerTestHarness.Generate(TypeGraphRenderer.Render(row.Graph), "TypeGraphAsm");
            Assert.False(output.Options.ConcurrentBuild);
        }
    }
}
