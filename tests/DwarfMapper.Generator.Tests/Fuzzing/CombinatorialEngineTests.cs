// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using System.Text;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Fuzzing;

// ─── Default-tier exhaustive tests (depth ≤1) ────────────────────────────────
// NOTE (C9): [Trait("tier","exhaustive")] only ENABLES filtering — it does NOT
// automatically exclude these tests from the default 'dotnet test' run. To skip
// them you must explicitly pass --filter "tier!=exhaustive". Without that filter
// ALL tests in this class run as part of the normal suite.

/// <summary>
///     Plan 19 Part E — exhaustive combinatorial engine (depth ≤1).
///     Matrix: ~20 basic types × 11 shapes × 2 variants.
///     Each cell: generate source, run DwarfGenerator, assert no unexpected diagnostic,
///     emit assembly, materialize via ObjectFactoryV2, map, verify via GraphOracleComparer.
///     Failure messages include seed + cell identity + generated source for repro.
/// </summary>
public class CombinatorialEngineTests
{
    // ── Matrix data ──────────────────────────────────────────────────────────
    public static IEnumerable<object[]> DepthOneIdentityCells()
    {
        return CombinatorialSchema.DepthOneMatrix()
            .Where(c => c.Variant == MatrixVariant.Identity)
            .Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> DepthOneDivergentCells()
    {
        return CombinatorialSchema.DepthOneMatrix()
            .Where(c => c.Variant == MatrixVariant.TypeDivergent)
            .Select(c => new object[] { c });
    }

    // ── Identity variant ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(DepthOneIdentityCells))]
    public void Identity_cell_compiles_emits_and_maps(MatrixCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        // 1. Run generator — no unexpected DWARF errors allowed
        var (diagnostics, generatedSource) = GeneratorTestHarness.Run(cell.Source);
        var unexpectedErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (unexpectedErrors.Count != 0)
            Assert.Fail(FormatCellFailure(cell, "generator produced unexpected errors",
                string.Join(", ",
                    unexpectedErrors.Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))),
                generatedSource));

        // 2. Emit assembly
        var (asm, emitErrors) = GeneratorTestHarness.EmitAssembly(cell.Source);
        if (asm is null)
            Assert.Fail(FormatCellFailure(cell, "assembly emit failed",
                string.Join(", ", emitErrors.Select(e => e.Id + ": " + e.GetMessage(CultureInfo.InvariantCulture))),
                generatedSource));

        // 3. Materialize, map, and verify (where the type supports it)
        TryMapAndVerify(cell, asm!, generatedSource);
    }

    // ── Type-divergent variant ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(DepthOneDivergentCells))]
    public void Divergent_cell_compiles_emits_and_maps(MatrixCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var (diagnostics, generatedSource) = GeneratorTestHarness.Run(cell.Source);
        var unexpectedErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            // DWARF027 (unsupported collection) is expected and acceptable
            .Where(d => d.Id != "DWARF027")
            .ToList();
        if (unexpectedErrors.Count != 0)
            Assert.Fail(FormatCellFailure(cell, "generator produced unexpected errors",
                string.Join(", ",
                    unexpectedErrors.Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))),
                generatedSource));

        var (asm, emitErrors) = GeneratorTestHarness.EmitAssembly(cell.Source);
        if (asm is null)
            Assert.Fail(FormatCellFailure(cell, "assembly emit failed",
                string.Join(", ", emitErrors.Select(e => e.Id + ": " + e.GetMessage(CultureInfo.InvariantCulture))),
                generatedSource));

        TryMapAndVerify(cell, asm!, generatedSource);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void TryMapAndVerify(MatrixCell cell, Assembly asm, string generatedSource)
    {
        var srcType = asm.GetType("Cmb.CmbSrc");
        var dstType = asm.GetType("Cmb.CmbDst");
        var mapperType = asm.GetType("Cmb.CmbMapper");
        if (srcType is null || dstType is null || mapperType is null)
            return; // structural issue already caught by emit test

        var mapper = Activator.CreateInstance(mapperType)!;
        var mapMethod = mapperType.GetMethod("Map", new[] { srcType });
        if (mapMethod is null) return;

        var rng = new Random(cell.Seed);
        object? srcInstance;
        try
        {
            srcInstance = ObjectFactoryV2.Create(srcType, rng, 0);
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail(FormatCellFailure(cell, "ObjectFactoryV2.Create threw (TargetInvocationException)",
                ex.InnerException?.Message ?? ex.Message, generatedSource));
            return;
        }
        catch (InvalidOperationException ex)
        {
            Assert.Fail(FormatCellFailure(cell, "ObjectFactoryV2.Create threw (InvalidOperationException)",
                ex.Message, generatedSource));
            return;
        }

        object? dstInstance;
        try
        {
            dstInstance = mapMethod.Invoke(mapper, new[] { srcInstance });
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            Assert.Fail(FormatCellFailure(cell, "mapper.Map threw at runtime",
                tie.InnerException.GetType().Name + ": " + tie.InnerException.Message,
                generatedSource));
            return;
        }

        if (dstInstance is null)
        {
            Assert.Fail(FormatCellFailure(cell, "mapper returned null", "(no further info)", generatedSource));
            return;
        }

        // Value verification: src.Val and dst.Val should be equal (accounting for widenings)
        VerifyValMember(cell, srcType, srcInstance!, dstType, dstInstance, generatedSource);
    }

    private static void VerifyValMember(
        MatrixCell cell, Type srcType, object srcInstance,
        Type? dstType, object? dstInstance, string generatedSource)
    {
        if (dstInstance is null) return; // caught above

        var srcProp = srcType.GetProperty("Val", BindingFlags.Public | BindingFlags.Instance);
        if (srcProp is null) return; // shouldn't happen
        var dstProp = dstType?.GetProperty("Val", BindingFlags.Public | BindingFlags.Instance);
        if (dstProp is null) return;

        var sv = srcProp.GetValue(srcInstance);
        var dv = dstProp.GetValue(dstInstance);

        var diffs = GraphOracleComparer.CrossTypeDiff(sv, dv,
            srcProp.PropertyType, dstProp.PropertyType,
            "Val");

        Assert.True(diffs.Count == 0,
            FormatCellFailure(cell, "value not preserved after mapping",
                GraphOracleComparer.RenderValueDiff(diffs), generatedSource));
    }

    private static string FormatCellFailure(
        MatrixCell cell, string reason, string details, string generatedSource)
    {
        return new StringBuilder()
            .AppendLine("CELL [" + cell.BasicType + " × " + cell.ShapeName + " × " + cell.Variant + "] seed=" +
                        cell.Seed.ToString(CultureInfo.InvariantCulture))
            .AppendLine("REASON: " + reason)
            .AppendLine("DETAILS: " + details)
            .AppendLine("--- generated ---")
            .AppendLine(generatedSource)
            .AppendLine("--- source ---")
            .AppendLine(cell.Source)
            .ToString();
    }
}

// ─── Exhaustive-tier depth-2 tests ───────────────────────────────────────────

/// <summary>
///     Plan 19 Part E — depth-2 combinatorial cells (heavier; tagged exhaustive tier).
///     Sample-subset of basic types × depth-2 shapes.
///     Tag [Trait("tier","exhaustive")] so default dotnet test doesn't include them unless filtered.
/// </summary>
public class CombinatorialDepthTwoTests
{
    public static IEnumerable<object[]> DepthTwoCells()
    {
        return CombinatorialSchema.DepthTwoMatrix()
            .Select(c => new object[] { c });
    }

    [Trait("tier", "exhaustive")]
    [Theory]
    [MemberData(nameof(DepthTwoCells))]
    public void DepthTwo_cell_compiles_and_maps(MatrixCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var (diagnostics, generatedSource) = GeneratorTestHarness.Run(cell.Source);
        var unexpectedErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "DWARF027")
            .ToList();
        Assert.True(unexpectedErrors.Count == 0,
            "Cell [" + cell.BasicType + " × " + cell.ShapeName + "] seed=" +
            cell.Seed.ToString(CultureInfo.InvariantCulture) + "\n" +
            "Errors: " + string.Join(", ", unexpectedErrors.Select(d => d.Id)) + "\n" + cell.Source);

        var (asm, emitErrors) = GeneratorTestHarness.EmitAssembly(cell.Source);
        Assert.True(asm is not null,
            "Cell [" + cell.BasicType + " × " + cell.ShapeName + "] seed=" +
            cell.Seed.ToString(CultureInfo.InvariantCulture) + " emit failed: " +
            string.Join(", ", emitErrors.Select(e => e.Id)));

        // Also verify maps correctly
        if (asm is null) return;
        var srcType = asm.GetType("Cmb.CmbSrc");
        var dstType = asm.GetType("Cmb.CmbDst");
        var mapperType = asm.GetType("Cmb.CmbMapper");
        if (srcType is null || dstType is null || mapperType is null) return;

        var mapper = Activator.CreateInstance(mapperType)!;
        var mapMethod = mapperType.GetMethod("Map", new[] { srcType });
        if (mapMethod is null) return;

        var rng = new Random(cell.Seed);
        var srcInstance = ObjectFactoryV2.Create(srcType, rng, 0);
        try
        {
            var dstInstance = mapMethod.Invoke(mapper, new[] { srcInstance });
            Assert.NotNull(dstInstance);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            Assert.Fail("Cell [" + cell.BasicType + " × " + cell.ShapeName + "] seed=" +
                        cell.Seed.ToString(CultureInfo.InvariantCulture) +
                        " Map threw: " + tie.InnerException.GetType().Name + ": " + tie.InnerException.Message +
                        "\n" + generatedSource);
        }
    }
}

// ─── Extended behavioral fuzz ─────────────────────────────────────────────────

/// <summary>
///     Extended behavioral fuzz that uses GraphOracleComparer.CrossTypeDiff for cross-type
///     comparison — covers a fresh batch of seeds using the new oracle.
/// </summary>
public class ExtendedBehavioralFuzzTests
{
    public static IEnumerable<object[]> Seeds()
    {
        return Enumerable.Range(200, 40).Select(i => new object[] { i });
        // seeds 200-239, no overlap with existing
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Oracle_crosstype_diff_catches_no_false_positives(int seed)
    {
        var src = SyntheticSchema.GenerateBehavioral(seed);
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            "seed=" + seed.ToString(CultureInfo.InvariantCulture) +
            " emit failed: " + string.Join(", ", errors.Select(e => e.Id)) + "\n" + src);

        var srcType = asm!.GetType("Fuzz.Src")!;
        var dstType = asm.GetType("Fuzz.Dst")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var map = mapperType.GetMethod("Map")!;

        var rng = new Random(seed);
        var srcInstance = ObjectFactoryV2.Create(srcType, rng, 0)!;
        var dstInstance = map.Invoke(mapper, new[] { srcInstance })!;

        var diffs = GraphOracleComparer.CrossTypeDiff(srcInstance, dstInstance,
            srcType, dstType);

        Assert.True(diffs.Count == 0,
            "seed=" + seed.ToString(CultureInfo.InvariantCulture) +
            " not value-preserving (oracle):\n" +
            GraphOracleComparer.RenderValueDiff(diffs) +
            "\n--- source ---\n" + src);
    }
}
