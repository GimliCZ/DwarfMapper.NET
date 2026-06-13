// SPDX-License-Identifier: GPL-2.0-only
using System;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// SIMD-widening fast-path: a lossless primitive widen pair in an array→array member emits a
/// Vector.Widen loop with a hardware-accelerated guard and a scalar tail (= scalar implicit widen).
/// </summary>
public class SimdWidenGeneratorTests
{
    [Fact]
    public void Int_to_long_array_emits_Vector_Widen_with_scalar_tail()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int[] V { get; set; } = System.Array.Empty<int>(); }
            public class D { public long[] V { get; set; } = System.Array.Empty<long>(); }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Vector.Widen", generated, StringComparison.Ordinal);
        Assert.Contains("Vector.IsHardwareAccelerated", generated, StringComparison.Ordinal);
        // Scalar tail / fallback (the implicit widen).
        Assert.Contains("for (; __i < src.Length; __i++)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Float_to_double_array_uses_widen_path()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public float[] V { get; set; } = System.Array.Empty<float>(); }
            public class D { public double[] V { get; set; } = System.Array.Empty<double>(); }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("__DwarfWiden_", generated, StringComparison.Ordinal);
        Assert.Contains("Vector.Widen", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_widen_pair_does_not_use_SIMD()
    {
        // long→int is NARROWING (not a Widen pair) → must go through CreateChecked, not Vector.Widen.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long[] V { get; set; } = System.Array.Empty<long>(); }
            public class D { public int[] V { get; set; } = System.Array.Empty<int>(); }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal); // loud narrowing instead
    }

    [Fact]
    public void Same_size_pair_blits_not_widens()
    {
        // int→uint is same-size → the blit fast-path, NOT Vector.Widen.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int[] V { get; set; } = System.Array.Empty<int>(); }
            public class D { public uint[] V { get; set; } = System.Array.Empty<uint>(); }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain("Vector.Widen", generated, StringComparison.Ordinal);
    }
}
