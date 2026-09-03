// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

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
        private static readonly string[] Banned =
        {
            "Unsafe.", "GC.AllocateUninitializedArray", "MemoryMarshal.CreateSpan", "MemoryMarshal.CreateReadOnlySpan",
            "StoreNonTemporal", "StoreAlignedNonTemporal", "Parallel.For", "Parallel.ForEach", "unsafe ", "stackalloc",
        };

        [Fact]
        public void Generated_code_never_names_a_banned_api()
        {
            // Full reach, not the single per-mapper file GeneratorTestHarness.Run selects: that helper drops five
            // assembly-wide aggregate outputs (Extensions/ServiceCollectionExtensions/AmbientRegistration/
            // AmbientRequires/Validate) and always drives DwarfGenerator, so the corpus's MapToGenerator cases would
            // scan nothing at all. GeneratorRunner + GeneratorRegistry — the same full-reach dispatch
            // GoldenFingerprint uses — runs whichever generator the case names and concatenates EVERY file it
            // emitted, aggregates included.
            var offenders = new List<string>();
            var emptyScans = new List<string>();
            foreach (var c in GoldenCorpus.Cases())
            {
                var generator = GeneratorRegistry.All.Single(g => g.Name == c.GeneratorName);
                var generated = GeneratorRunner.Run(generator.Create(), c.Source).AllOutputsConcatenated;

                if (generated.Length == 0) emptyScans.Add(c.Id);

                foreach (var b in Banned)
                    if (generated.Contains(b, StringComparison.Ordinal)) offenders.Add(c.Id + ": " + b);
            }

            // A case that scans empty text passes every "no banned token" check while checking nothing — the exact
            // corpus hole the per-mapper-file version of this scan had for every MapToGenerator case (5 today,
            // confirmed empty by temporarily reverting to GeneratorTestHarness.Run while writing this fix). Fail
            // loudly instead of silently, so the hole cannot reopen unnoticed.
            Assert.True(emptyScans.Count == 0,
                "Case(s) contributed an empty scan (checks nothing):\n  " + string.Join("\n  ", emptyScans));
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
