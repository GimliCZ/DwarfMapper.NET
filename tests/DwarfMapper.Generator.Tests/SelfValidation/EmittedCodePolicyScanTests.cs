// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    public class EmittedCodePolicyScanTests
    {
        // The user's rule for round 29 (Issues/round29/RESEARCH-hardware-mode.md §1): no unsafe, no uninitialized memory,
        // no parallelism in shipped or generated code. Generated code is checked here because BannedSymbols.txt
        // only reaches the analyzers' own source, never a consumer's .g.cs.
        private static readonly string[] Banned =
        {
            "Unsafe.", "GC.AllocateUninitializedArray", "MemoryMarshal.CreateSpan", "MemoryMarshal.CreateReadOnlySpan",
            "StoreNonTemporal", "StoreAlignedNonTemporal", "Parallel.For", "Parallel.ForEach", "unsafe ", "stackalloc",
        };

        [Fact]
        public void Generated_code_never_names_a_banned_api()
        {
            var offenders = new List<string>();
            foreach (var c in GoldenCorpus.Cases())
            {
                var (_, generated) = GeneratorTestHarness.Run(c.Source);
                foreach (var b in Banned)
                    if (generated.Contains(b, StringComparison.Ordinal)) offenders.Add(c.Id + ": " + b);
            }
            Assert.True(offenders.Count == 0, "Emitted code names a banned API:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_scan_can_fail()
        {
            // Positive control: a synthetic source that would emit a banned token trips the same check.
            const string fake = "var x = global::System.Runtime.CompilerServices.Unsafe.As<int, uint>(ref y);";
            Assert.Contains(Banned, b => fake.Contains(b, StringComparison.Ordinal));
        }
    }
}
