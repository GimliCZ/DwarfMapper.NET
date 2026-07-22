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
        // B.Orphan has no source member -> DWARF001. Same generated output shape, different diagnostics.
        var withDiagnostic = Sample with
        {
            Source = Sample.Source.Replace(
                "public class B { public int X { get; set; } }",
                "public class B { public int X { get; set; } public int Orphan { get; set; } }",
                StringComparison.Ordinal),
        };

        Assert.NotEqual(GoldenFingerprint.Compute(Sample), GoldenFingerprint.Compute(withDiagnostic));
    }
}
