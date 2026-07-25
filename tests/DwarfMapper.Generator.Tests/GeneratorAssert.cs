// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Shared assertion fixture for generator tests — the three things almost every test does.
///     <para>
///     The "it compiled" idiom was hand-written 138 times across 77 files:
///     <code>
///     var (diags, generated) = GeneratorTestHarness.Run(src);
///     Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
///     Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
///     </code>
///     Four helper classes had grown to wrap it, but every one was declared <c>file static</c>, so no other test
///     could reach them and the next file open-coded it again.
///     </para>
///     <para>
///     Deduplication is the lesser benefit. The real one is the FAILURE MESSAGE:
///     <c>Assert.Empty(collection)</c> prints a bare collection dump, so a broken emission tells you only "not
///     empty" — you then have to re-run by hand and print the generated source to find out what actually went
///     wrong. (That happened twice while fixing the audit issues.) These print the diagnostic ids AND the
///     generated code, so a failure is diagnosable from the test output alone.
///     </para>
/// </summary>
internal static class GeneratorAssert
{
    /// <summary>
    ///     Asserts the generator reports no ERROR diagnostic AND that the code it emitted compiles.
    ///     Returns the generated source so the caller can go on asserting against it.
    /// </summary>
    public static string CompilesClean(string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var (diagnostics, generated) = GeneratorTestHarness.Run(source, nullable);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "The generator reported error diagnostic(s):\n  " + Describe(errors)
            + "\n\n--- source ---\n" + source);

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source, nullable);
        Assert.True(compileErrors.Length == 0,
            "The generator ACCEPTED this source but the code it emitted does not compile:\n  "
            + Describe(compileErrors)
            + "\n\n--- generated ---\n" + generated
            + "\n\n--- source ---\n" + source);

        return generated;
    }

    /// <summary>
    ///     Asserts only that the emitted code COMPILES, without requiring the generator to be diagnostic-free.
    ///     Separate from <see cref="CompilesClean" /> because plenty of tests legitimately expect a warning or
    ///     an Info diagnostic (DWARF038, DWARF070, DWARF075 …) and still require a compilable emission.
    ///     Returns the generated source.
    /// </summary>
    public static string EmitsCompilableCode(string source,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var (_, generated) = GeneratorTestHarness.Run(source, nullable);

        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(source, nullable);
        Assert.True(compileErrors.Length == 0,
            "The generator ACCEPTED this source but the code it emitted does not compile:\n  "
            + Describe(compileErrors)
            + "\n\n--- generated ---\n" + generated
            + "\n\n--- source ---\n" + source);

        return generated;
    }

    /// <summary>
    ///     Asserts the generator reports <paramref name="diagnosticId" />, and returns the matching diagnostics
    ///     (so a caller can assert on the message). Does NOT require the emission to compile — a refused source
    ///     legitimately may not emit anything.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Reports(string source, string diagnosticId,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(source, nullable);

        var matches = diagnostics.Where(d => d.Id == diagnosticId).ToList();
        Assert.True(matches.Count > 0,
            $"Expected diagnostic {diagnosticId}, but the generator reported "
            + (diagnostics.Length == 0 ? "nothing." : "only:\n  " + Describe(diagnostics))
            + "\n\n--- source ---\n" + source);

        return matches;
    }

    /// <summary>Asserts the generator does NOT report <paramref name="diagnosticId" />.</summary>
    public static void DoesNotReport(string source, string diagnosticId,
        NullableContextOptions nullable = NullableContextOptions.Disable)
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(source, nullable);

        var matches = diagnostics.Where(d => d.Id == diagnosticId).ToList();
        Assert.True(matches.Count == 0,
            $"Expected NO {diagnosticId}, but the generator reported:\n  " + Describe(matches)
            + "\n\n--- source ---\n" + source);
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n  ",
            diagnostics.Select(d => d.Id + " (" + d.Severity + "): "
                                    + d.GetMessage(CultureInfo.InvariantCulture)));
    }

    private static string Describe(ImmutableArray<Diagnostic> diagnostics)
    {
        return Describe((IEnumerable<Diagnostic>)diagnostics);
    }
}
