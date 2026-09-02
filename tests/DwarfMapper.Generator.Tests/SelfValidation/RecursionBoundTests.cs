// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     SECURITY INVARIANT [SEC-3]: the recursion bound is enforced identically by the generator and the
    ///     runtime, because both compile the SAME constant.
    ///     <para>
    ///         The bound is a resource limit — the guard that stops an unbounded walk of a cyclic object graph
    ///         exhausting the stack — so a disagreement between the two sides is a security-relevant defect
    ///         rather than a tidiness issue.
    ///     </para>
    ///     <para>
    ///         FOUND 2026-08-26. It WAS enforced in two places that could not see each other:
    ///         <c>DwarfRefContext</c> clamped to its own <c>AbsoluteMaxDepth</c>, and the generator clamped to a
    ///         hard-coded literal <c>1000</c> carrying the comment <i>"matches
    ///         DwarfRefContext.AbsoluteMaxDepth"</i> — a claim nothing verified, on a bound with no test at all
    ///         on either side. Raising the runtime constant alone would have silently capped users below the
    ///         documented maximum: the runtime would allow the deeper value while the generator went on
    ///         refusing it.
    ///     </para>
    ///     <para>
    ///         The fix single-sources the constants through <c>src/Shared/DwarfLimits.cs</c>, linked into both
    ///         assemblies — the generator is <c>netstandard2.0</c> and cannot reference the runtime, so a
    ///         project reference was never available. That removes the drift rather than testing for it, which
    ///         is why these tests pin BEHAVIOUR at both ends plus the structural absence of stray literals,
    ///         instead of comparing two numbers.
    ///     </para>
    /// </summary>
    public class RecursionBoundTests
    {
        [Fact]
        public void The_runtime_clamps_a_configured_depth_into_the_shared_bounds()
        {
            // Above the cap, below the floor, and an ordinary value passed through untouched.
            Assert.Equal(DwarfRefContext.AbsoluteMaxDepth, new DwarfRefContext(500_000).MaxDepth);
            Assert.Equal(1, new DwarfRefContext(0).MaxDepth);
            Assert.Equal(1, new DwarfRefContext(int.MinValue).MaxDepth);
            Assert.Equal(64, new DwarfRefContext(64).MaxDepth);
        }

        [Fact]
        public void The_generator_and_the_runtime_agree_on_the_cap_end_to_end()
        {
            // The assertion the old arrangement could not make. A mapper asking for a depth far above the cap
            // must EMIT the cap: if the generator's ceiling and the runtime's differed, this is where it shows.
            const string source = """
                                  using DwarfMapper;

                                  public class Node { public Node? Next { get; set; } public int Value { get; set; } }
                                  public class NodeDto { public NodeDto? Next { get; set; } public int Value { get; set; } }

                                  [DwarfMapper(MaxDepth = 500000, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                                  [GenerateMap<Node, NodeDto>]
                                  public partial class DeepMapper;
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

            var depths = Regex.Matches(generated, @"new global::DwarfMapper\.DwarfRefContext\(\s*(?<n>\d+)",
                    RegexOptions.ExplicitCapture)
                .Select(m => int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            Assert.True(depths.Count > 0,
                "No DwarfRefContext construction was emitted, so this test proves nothing about the bound. The "
                + "shape above must produce a cycle-tracking context — check the fixture, not the assertion.\n\n"
                + generated);

            Assert.All(depths,
                d => Assert.True(d == DwarfRefContext.AbsoluteMaxDepth,
                    $"The generator emitted MaxDepth {d} where the runtime's cap is "
                    + $"{DwarfRefContext.AbsoluteMaxDepth}. The two sides have drifted — which is "
                    + "exactly what src/Shared/DwarfLimits.cs exists to make impossible."));
        }

        [Fact]
        public void Neither_side_carries_a_stray_bound_literal_any_more()
        {
            // The structural half. Single-sourcing only helps while nobody re-types the number, and the
            // original defect was precisely a re-typed number with a comment claiming it matched.
            var offenders = new List<string>();

            var files = RepoPaths.SourceFiles(Path.Combine(RepoPaths.Src, "DwarfMapper"))
                .Concat(RepoPaths.SourceFiles(Path.Combine(RepoPaths.Src, "DwarfMapper.Generator")));

            foreach (var path in files)
            {
                var name = Path.GetFileName(path);

                // DwarfLimits.cs is where the number is ALLOWED to appear — it is the single source.
                if (string.Equals(name, "DwarfLimits.cs", StringComparison.Ordinal)) { continue; }

                var code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", string.Empty);
                code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

                foreach (Match m in Regex.Matches(code, @"(?<!\w)1000(?!\w)"))
                {
                    offenders.Add($"{name}: literal 1000 at offset {m.Index}");
                }
            }

            Assert.True(offenders.Count == 0,
                "A bare recursion-bound literal reappeared outside src/Shared/DwarfLimits.cs. That is how the "
                + "two sides drifted apart the first time — a hard-coded 1000 with a comment claiming it "
                + "matched the constant. Reference DwarfLimits instead:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_literal_scan_would_actually_catch_a_reintroduced_bound()
        {
            // Control: the regex must match a bare bound and must NOT match it inside a larger number or
            // identifier, or the guard above would be either vacuous or unusably noisy.
            Assert.Matches(@"(?<!\w)1000(?!\w)", "if (i > 1000) return 1000;");
            Assert.DoesNotMatch(@"(?<!\w)1000(?!\w)", "var x = 10000;");
            Assert.DoesNotMatch(@"(?<!\w)1000(?!\w)", "var y = 21000;");
            Assert.DoesNotMatch(@"(?<!\w)1000(?!\w)", "const int Depth1000 = 5;");
        }
    }
}
