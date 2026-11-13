// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Runtime;
using Xunit;

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     <b>This host keeps the runtime's defaults.</b> Round 34 (E3) gives every other test host three runtime settings
    ///     (call counting from the start, no PGO pass, no background GC; see Generator.Tests' TestHostRuntimeTuningTests).
    ///     This project's oracles measure the runtime itself: <see cref="InterfaceBucketTests" /> times interface dispatch,
    ///     which PGO decides how to compile, and the allocation contracts count bytes that optimised code may keep off the
    ///     heap. So the project sets <c>DwarfKeepRuntimeDefaults</c> and runs as a consumer's process does;
    ///     PerfCategoryIsolationScanTests holds every project with such an oracle to that.
    /// </summary>
    public class PerfHostRuntimeDefaultsTests
    {
        [Theory]
        [InlineData("System.Runtime.TieredPGO")]
        [InlineData("System.Runtime.TieredCompilation.CallCountingDelayMs")]
        [InlineData("System.GC.Concurrent")]
        public void The_host_receives_no_tuned_runtime_setting(string name)
        {
            Assert.Null(AppContext.GetData(name));
        }

        [Fact]
        public void The_collector_runs_background_collections_as_a_consumer_process_does()
        {
            Assert.Equal(GCLatencyMode.Interactive, GCSettings.LatencyMode);
        }
    }
}
