// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Every check in the AOT sample fails the AOT gate when it fails.</b>
    ///     <para>
    ///         ci.yml runs the published NativeAOT binary as a behavioural gate and reads nothing but its exit code. Two
    ///         of the sample's checks, the narrow overflow and the string-to-int parse, printed <c>ERROR: …</c> and then
    ///         carried on, so the binary still exited 0 and the gate could not fail on them (round 34's silenced-checks
    ///         inventory). Every other check returned 1. So the rule is held here: each <c>ERROR:</c> line is followed
    ///         by a non-zero <c>return</c>.
    ///     </para>
    /// </summary>
    public sealed class AotSampleGateScanTests
    {
        [Fact]
        public void Every_ERROR_line_in_the_AOT_sample_is_followed_by_a_non_zero_return()
        {
            var path = Path.Combine(RepoPaths.Root, "samples", "DwarfMapper.AotSample", "Program.cs");
            var lines = File.ReadAllLines(path);

            var errors = lines.Select((text, index) => (Text: text.Trim(), Index: index))
                .Where(x => x.Text.StartsWith("Console.WriteLine(\"ERROR", StringComparison.Ordinal))
                .ToList();
            // Non-vacuity: the sample is the gate's whole content, so finding none means this read the wrong file.
            Assert.True(errors.Count >= 30, $"found only {errors.Count} ERROR line(s) in {path}; this scan is reading the wrong thing.");

            var silent = errors
                .Where(e => lines.Skip(e.Index + 1).Select(l => l.Trim()).FirstOrDefault(l => l.Length != 0) is not { } next ||
                            !next.StartsWith("return ", StringComparison.Ordinal) || next == "return 0;")
                .Select(e => $"Program.cs:{e.Index + 1} {e.Text}")
                .ToList();

            Assert.True(silent.Count == 0,
                "AOT sample check(s) that print an error and carry on, so the published binary still exits 0 and the AOT " +
                "gate passes: [" + string.Join(" | ", silent) + "]. Return 1 after each.");
        }
    }
}
