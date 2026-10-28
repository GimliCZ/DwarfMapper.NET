// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    public class EmittedCodePolicyScanTests
    {
        // The user's rule for round 29 (Issues/round29/RESEARCH-hardware-mode.md §1): no unsafe, no uninitialized memory,
        // no parallelism in shipped or generated code. Generated code is checked here because BannedSymbols.txt
        // only reaches the analyzers' own source, never a consumer's .g.cs.
        //
        // "unsafe " and "stackalloc" are raw substrings, not resolved symbols (BannedApiAnalyzers cannot express a
        // language keyword as a doc-id) — a doc comment that happens to contain one of these phrases would trip
        // this scan too. Left as-is deliberately: a false positive here fails loudly and gets triaged, which is
        // the correct failure mode for a tripwire; the alternative (a narrower pattern) risks a false negative,
        // which is silent.
        //
        // The scan itself runs once per golden case, in GoldenCaseTests' row for that case (round 34 E5): the row's one
        // generator run serves both the manifest fingerprint and this list. It reads EVERY file the case's generator
        // emitted (GeneratorRunner + GeneratorRegistry, the full-reach dispatch GoldenFingerprint uses), aggregates
        // included, not the single per-mapper file GeneratorTestHarness.Run selects: that helper drops the five
        // assembly-wide outputs and always drives DwarfGenerator, so the corpus's MapToGenerator cases scanned nothing
        // at all until the scan read every output. A case whose output is empty fails its row for the same reason.
        internal static readonly string[] Banned =
        {
            "Unsafe.", "GC.AllocateUninitializedArray", "MemoryMarshal.CreateSpan", "MemoryMarshal.CreateReadOnlySpan",
            "StoreNonTemporal", "StoreAlignedNonTemporal", "Parallel.For", "Parallel.ForEach", "unsafe ", "stackalloc",
        };

        [Fact]
        public void The_scan_can_fail()
        {
            // Positive control: a synthetic source that would emit a banned token trips the same check.
            const string fake = "var x = global::System.Runtime.CompilerServices.Unsafe.As<int, uint>(ref y);";
            Assert.Contains(Banned, b => fake.Contains(b, StringComparison.Ordinal));
        }
    }
}
