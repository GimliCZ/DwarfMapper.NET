// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>This host runs with round 34's test-host runtime settings (E3), not the runtime's defaults.</b>
    ///     <para>
    ///         Every generator test emits and loads a fresh assembly, so the host JIT-compiles new methods at tier 0 all
    ///         the time. The runtime holds back call counting, and with it every tier-1 recompile, while such start-up-like
    ///         activity goes on, so a host like this one ran Roslyn (IL only in its package) on unoptimised tier-0 code for
    ///         long stretches. Directory.Build.targets gives every test project three settings: call counting from the start,
    ///         no PGO pass, no background GC. Measured on this project on 2026-10-28, against the defaults in the same
    ///         batch: the full run (8,097 tests) went from 62.8 s and 288 CPU-seconds to 29.8 s and 140, and a serial
    ///         226-test session, the shape of a Stryker session, from 8.1 s to 5.5 s. The code that runs is the same.
    ///     </para>
    ///     <para>
    ///         The settings travel in the test project's runtimeconfig.json, which every host of the assembly reads:
    ///         dotnet test, the IDE, coverage runs and Stryker's sessions. This reads them back as the runtime received
    ///         them, and the collector's latency mode shows the GC really runs without background collections.
    ///     </para>
    /// </summary>
    public class TestHostRuntimeTuningTests
    {
        [Theory]
        [InlineData("System.Runtime.TieredPGO", "false")]
        [InlineData("System.Runtime.TieredCompilation.CallCountingDelayMs", "0")]
        [InlineData("System.GC.Concurrent", "false")]
        public void The_host_receives_the_tuned_runtime_setting(string name, string expected)
        {
            Assert.Equal(expected, AppContext.GetData(name) as string);
        }

        [Fact]
        public void The_collector_runs_without_background_collections()
        {
            Assert.Equal(GCLatencyMode.Batch, GCSettings.LatencyMode);
        }
    }
}
