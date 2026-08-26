// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     ARCHITECTURAL RULE: no benchmark may map a STATIC payload.
    ///     <para>
    ///         A single object mapped millions of times sits permanently in L1 with every data-dependent
    ///         branch perfectly predicted. That measures an idealised hot loop rather than mapping, and it
    ///         does not merely inflate the numbers — it CHANGES WHO WINS, which makes the comparison table
    ///         wrong rather than optimistic.
    ///     </para>
    ///     <para>
    ///         This is not a hypothetical. Measured 2026-08-24, converting the single-object rows to
    ///         fixture-seeded rings moved four of them:
    ///     </para>
    ///     <list type="bullet">
    ///         <item><c>Enum</c> — 4.8 ns to 12.97 ns for DwarfMapper while Mapperly stayed flat at 3.3, so a
    ///         reported 1.4x gap was really <b>3.98x</b>. One enum value meant the by-name switch took the same
    ///         arm forever;</item>
    ///         <item><c>Nested</c> — DwarfMapper went from marginally BEHIND Mapperly to <b>1.14x ahead</b>.
    ///         The ranking itself was an artifact;</item>
    ///         <item><c>NullMismatch</c> — 4.4 ns to 6.26 ns once the nullable member was sometimes null;</item>
    ///         <item><c>Flat</c> — the apparent gap vanished into the error bars, so it was never a finding.</item>
    ///     </list>
    ///     <para>
    ///         Payloads come from <c>RealisticPayloads</c>, the same fuzzer/fixture source the test suites
    ///         draw from, one distinct salt per ring slot.
    ///     </para>
    /// </summary>
    public class BenchmarkPayloadRuleTests
    {
        private static readonly string[] RingFields = ["_flat", "_nested", "_enum", "_nm", "_flOrder"];

        private static string BenchmarkSource()
        {
            return File.ReadAllText(Path.Combine(RepoPaths.Root,
                "benchmarks",
                "DwarfMapper.Benchmarks",
                "Program.cs"));
        }

        [Fact]
        public void No_benchmark_maps_a_payload_without_cycling_the_ring()
        {
            var src = BenchmarkSource();
            var offenders = new List<string>();

            // Each [Benchmark] method body, up to the next attribute or the end of the method.
            foreach (Match m in Regex.Matches(src,
                         @"\[Benchmark[^\]]*\][\s\S]{0,400}?public\s+[\w<>\[\]?]+\s+(?<name>\w+)\(\)\s*\{(?<body>[\s\S]*?)\n    \}",
                         RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)))
            {
                var name = m.Groups["name"].Value;
                var body = m.Groups["body"].Value;

                foreach (var f in RingFields)
                {
                    if (!Regex.IsMatch(body, @"\b" + f + @"\b")) { continue; }

                    // Touching a ring field is fine; touching it WITHOUT Next(...) is the defect.
                    if (!body.Contains("Next(" + f + ")", StringComparison.Ordinal))
                    {
                        offenders.Add($"{name} uses {f} without Next({f})");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "Benchmark(s) map a STATIC payload. A single object mapped millions of times is permanently "
                + "cached with its branches perfectly predicted — that does not just flatter the numbers, it "
                + "changes which library wins (measured: the Nested ranking flipped, and an Enum gap reported "
                + "as 1.4x was really 3.98x). Draw from the ring with Next(...):\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_ring_is_seeded_from_the_shared_fixture_factory()
        {
            // The rings must be drawn from RealisticPayloads — the fuzz/fixture source the test suites use —
            // rather than hand-built literals, or the payloads stop resembling the data the mapper meets.
            var src = BenchmarkSource();

            Assert.Contains("RealisticPayloads.One<T>(", src, StringComparison.Ordinal);
            Assert.Contains("private static T[] RingOf<T>(int salt)", src, StringComparison.Ordinal);

            // A distinct salt per slot: a ring of 512 identical draws would satisfy the rule above while
            // reintroducing exactly the defect it exists to prevent.
            Assert.Matches(@"RealisticPayloads\.One<T>\(\(salt \* \d+\) \+ i\)", src);
        }

        [Fact]
        public void The_hand_written_baseline_draws_from_the_same_ring_as_everyone_else()
        {
            // Flat_Hand is the "could you have written this yourself" reference. If it read a cached object
            // while every other arm cycled, it would win on measurement conditions rather than on code.
            var src = BenchmarkSource();
            var hand = Regex.Match(src, @"public FlatDst Flat_Hand\(\)\s*\{(?<body>[\s\S]*?)\n    \}",
                RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));

            Assert.True(hand.Success, "Flat_Hand not found — this rule is reading the wrong file");
            Assert.Contains("Next(_flat)", hand.Groups["body"].Value, StringComparison.Ordinal);
        }

        [Fact]
        public void The_ring_is_large_enough_to_defeat_the_branch_predictor()
        {
            // 512 distinct payloads against a predictor that tracks a few thousand branches. Small rings
            // (4, 8) are memorised and would pass the rule while measuring the cached case again.
            var src = BenchmarkSource();
            var size = Regex.Match(src, @"private const int RingSize = (?<n>\d+);", RegexOptions.ExplicitCapture);

            Assert.True(size.Success, "RingSize constant not found");
            Assert.True(int.Parse(size.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 256,
                "RingSize must stay >= 256; a short ring is memorised and measures the cached case again");
        }
    }
}
