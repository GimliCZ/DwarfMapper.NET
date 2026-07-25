// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Framework;

namespace DwarfMapper.Generator.Tests.Golden;

public class GoldenFingerprintTests
{
    private static readonly GoldenCase Sample = new(
        "test:sample",
        """
        using DwarfMapper;
        namespace Demo;
        public class A { public int X { get; set; } }
        public class B { public int X { get; set; } }
        [DwarfMapper] public partial class M { public partial B Map(A a); }
        """,
        "DwarfGenerator");

    [Fact]
    public void Fingerprint_is_deterministic()
    {
        Assert.Equal(GoldenFingerprint.Compute(Sample), GoldenFingerprint.Compute(Sample));
    }

    [Fact]
    public void Fingerprint_changes_when_generated_output_changes()
    {
        // Renaming a mapped member changes the emitted assignment, so the fingerprint MUST move. If this ever
        // passes-by-equality the whole safety net is inert.
        var altered = Sample with
        {
            // Rename the mapped member on BOTH sides: the emitted assignment changes (X = -> Renamed =) while
            // the mapping stays complete. That isolates OUTPUT sensitivity from DIAGNOSTIC sensitivity, which
            // the next test covers separately.
            Source = Sample.Source.Replace("int X { get; set; }", "int Renamed { get; set; }",
                StringComparison.Ordinal),
        };

        Assert.NotEqual(GoldenFingerprint.Compute(Sample), GoldenFingerprint.Compute(altered));
    }

    [Fact]
    public void Fingerprint_includes_diagnostics()
    {
        // RequiredMapping = Both turns on source-side completeness, so an unconsumed SOURCE member (A.Orphan)
        // fires DWARF039 (an Info diagnostic) without touching destination-side completeness. Unlike an
        // unmapped DESTINATION member (DWARF001, an Error that suppresses emission entirely), this keeps the
        // generated output byte-identical — verified below — so the two fingerprints differing isolates the
        // diagnostic dimension instead of leaning on that as an assumption.
        var baseline = Sample with
        {
            Source = Sample.Source.Replace(
                "[DwarfMapper] public partial class M",
                "[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)] public partial class M",
                StringComparison.Ordinal),
        };
        var withDiagnostic = baseline with
        {
            Source = baseline.Source.Replace(
                "public class A { public int X { get; set; } }",
                "public class A { public int X { get; set; } public int Orphan { get; set; } }",
                StringComparison.Ordinal),
        };

        Assert.NotEqual(GoldenFingerprint.Compute(baseline), GoldenFingerprint.Compute(withDiagnostic));

        // Prove the fingerprints differ BECAUSE of diagnostics, not because the generated output also moved.
        var generator = GeneratorRegistry.All.Single(g => g.Name == Sample.GeneratorName);
        var baseRun = GeneratorRunner.Run(generator.Create(), baseline.Source);
        var orphanRun = GeneratorRunner.Run(generator.Create(), withDiagnostic.Source);
        Assert.Equal(baseRun.AllOutputsConcatenated, orphanRun.AllOutputsConcatenated);
    }
}
