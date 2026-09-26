// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Drives <see cref="CoverageFloorReader" /> directly. Its one consumer asserts that the coverage floors
    ///     EXIST; these assert that the reader would notice if they stopped existing in the shape it expects.
    ///     A reader that silently matched nothing would let the gate's absolute line disappear while every check
    ///     stayed green — which is the whole failure this file is about, and the reason the count is asserted
    ///     rather than inferred.
    /// </summary>
    public class CoverageFloorReaderTests
    {
        /// <summary>
        ///     The five assemblies the coverage gate floors. Pinned by NAME, deliberately not by value: the
        ///     numbers are measurements that move by design (invariant R1 re-measures them), and a copy of them
        ///     here would be a second hand-typed number. The identities are not measurements — an assembly
        ///     silently leaving the gate is a finding.
        /// </summary>
        private static readonly string[] FlooredAssemblies =
        [
            "DwarfMapper", "DwarfMapper.Generator", "DwarfMapper.DocTooling", "DwarfMapper.CodeFixes",
            "DwarfMapper.Testing"
        ];

        [Fact]
        public void The_coverage_floors_are_read_from_the_gate_script_itself()
        {
            var floors = CoverageFloorReader.ParseCoverageFloors(
                File.ReadAllText(Path.Combine(RepoLayout.Root, "scripts", "housekeeping.ps1")));

            Assert.Equal(FlooredAssemblies, floors.Select(f => f.Assembly).ToArray());
            Assert.All(floors, f => Assert.InRange(f.Floor, 1.0, 100.0));
        }

        [Fact]
        public void A_renamed_floors_block_is_a_loud_failure_not_a_silent_nothing()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CoverageFloorReader.ParseCoverageFloors("$somethingElse = [ordered]@{\n  'A' = 1.0\n}\n"));

            Assert.Contains("scripts/housekeeping.ps1", ex.Message, StringComparison.Ordinal);
            Assert.Contains("no '$coverageFloors", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_floors_block_that_lost_an_assembly_is_refused_rather_than_read_short()
        {
            // The realistic drift: the block is still there and still parses, but one line's shape changed (or
            // an assembly left the gate) and the reader would see four floors where five gates exist.
            var ex = Assert.Throws<InvalidOperationException>(() => CoverageFloorReader.ParseCoverageFloors(
                "$coverageFloors = [ordered]@{\n    'A' = 91.2\n    'B' = 93.7\n}\n"));

            Assert.Contains("parsed 2 coverage floor(s), expected 5", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_floor_that_is_not_a_percentage_means_the_parse_is_wrong()
        {
            var block = "$coverageFloors = [ordered]@{\n" + string.Concat(FlooredAssemblies.Select(a => $"    '{a}' = 000.0\n")) + "}\n";

            var ex = Assert.Throws<InvalidOperationException>(() => CoverageFloorReader.ParseCoverageFloors(block));
            Assert.Contains("is not a percentage", ex.Message, StringComparison.Ordinal);
        }
    }
}
