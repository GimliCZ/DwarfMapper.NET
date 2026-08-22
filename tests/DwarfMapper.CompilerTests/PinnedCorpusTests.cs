// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.CompilerTests.TypeGraphs;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     Runs every <see cref="PinnedCorpus" /> row through the same must-compile-or-refuse invariant the
///     smoke sampling enforces — a pinned row is a REGRESSION pin, so it runs on every fast-tier pass, not
///     behind the deep knob.
/// </summary>
public class PinnedCorpusTests
{
    public static IEnumerable<object[]> RowIds()
    {
        return PinnedCorpus.Rows.Select(r => new object[] { r.Id });
    }

    private static CorpusRow Row(string id)
    {
        return PinnedCorpus.Rows.Single(r => r.Id == id);
    }

    [Theory]
    [MemberData(nameof(RowIds))]
    public void Every_pinned_row_is_valid_and_compiles_clean_or_refuses_loudly(string id)
    {
        var row = Row(id);
        row.Graph.Validate();

        var result = CompilerTestHarness.Run(TypeGraphRenderer.Render(row.Graph));

        if (row.ExpectedRefusalIds is not null)
        {
            // A pinned REFUSAL contract (K1): the generators must say no, loudly, with exactly these
            // error-severity DWARF ids. Deliberately NO assertion on CompilationErrors — a refused map
            // leaves its partial method unimplemented, so CS errors in the output are part of the refusal
            // shape (docs/diagnostics.md: DWARF001 is "enforced by construction"). This row is the
            // deterministic executor of the RefusedLoudly true-branch, which no sampled graph has reached.
            Assert.True(result.RefusedLoudly,
                $"corpus row '{row.Id}' no longer refuses — the pinned refusal contract changed; "
                + "re-measure and move this pin deliberately\n" + row.Graph.Describe());
            Assert.Equal(row.ExpectedRefusalIds.Order(StringComparer.Ordinal),
                result.GeneratorDiagnostics
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));
            return;
        }

        if (row.KnownSilentCsIds is not null)
        {
            // A pinned KNOWN divergence: silent generators, exactly these CS ids. Both assertions go red
            // the moment the product changes — a fix flips this row to the normal contract (and deletes
            // any matching sampled-space exclusion in TypeGraphGen) in the same commit; a refusal or a
            // different error set is a new fact that must be re-filed, not absorbed.
            Assert.False(result.RefusedLoudly,
                $"corpus row '{row.Id}' now REFUSES — the filed divergence changed; re-file, don't absorb");
            Assert.Equal(row.KnownSilentCsIds.Order(StringComparer.Ordinal),
                result.CompilationErrors.Select(e => e.Id).Distinct().Order(StringComparer.Ordinal));
            return;
        }

        if (result.RefusedLoudly) return;
        Assert.True(result.CompilationErrors.Length == 0,
            $"corpus row '{row.Id}' miscompiled silently: ["
            + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct())
            + "]\n" + row.Graph.Describe());
    }

    /// <summary>
    ///     A row cannot pin "silent with CS errors" and "refuses loudly" at once — the outcomes are
    ///     mutually exclusive by definition, so a row claiming both is a defect of the corpus, caught here
    ///     rather than resolved arbitrarily by branch order in the sweep above.
    /// </summary>
    [Fact]
    public void No_row_pins_both_a_silent_divergence_and_a_refusal()
    {
        Assert.All(PinnedCorpus.Rows, row =>
            Assert.False(row.KnownSilentCsIds is not null && row.ExpectedRefusalIds is not null,
                $"corpus row '{row.Id}' pins both contracts — pick one"));
    }

    /// <summary>
    ///     The P5 forward-reference row's own claim, end-to-end: the SAME graph compiled with its split
    ///     node's files in both orders produces the same accept/refuse outcome and byte-identical generated
    ///     source. This is the K0-level restatement of
    ///     <c>CanReinterpret_partial_file_struct_verdict_is_file_order_independent</c> (P5's comparator-mutant
    ///     kill in Generator.Tests) — same shape, measured at the emission seam instead of the proof seam.
    /// </summary>
    [Fact]
    public void Partial_split_row_verdict_and_emission_are_file_order_independent()
    {
        var units = TypeGraphRenderer.Render(PinnedCorpus.PartialFileSplitStructPair.Graph);
        Assert.True(units.Count == 2,
            "the split row must render exactly two compilation units, or the order axis is vacuous");

        var forward = CompilerTestHarness.Run(units);
        var reversed = CompilerTestHarness.Run([.. units.Reverse()]);

        Assert.Equal(forward.RefusedLoudly, reversed.RefusedLoudly);
        Assert.Equal(forward.CompilationErrors.Select(e => e.Id), reversed.CompilationErrors.Select(e => e.Id));
        Assert.Equal(forward.GeneratedSource, reversed.GeneratedSource);

        // And the row itself must not be dead weight: in today's product this pair is ACCEPTED (a
        // field-compatible struct pair), so the order-independence comparison above compares real emission,
        // not two refusals. If a future product change legitimately refuses it, this pin moves in that
        // commit with the reasoning recorded.
        Assert.False(forward.RefusedLoudly,
            "the split struct pair is expected to be accepted; a refusal here means the corpus row went "
            + "vacuous for the emission comparison — re-measure and move this pin deliberately");
        Assert.True(forward.CompilationErrors.Length == 0, "accepted split pair must compile clean");
    }
}
