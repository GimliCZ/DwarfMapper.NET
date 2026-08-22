// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.TestInfrastructure;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     K0's own verification and the precursor of K1's leg 1: every sampled graph must either compile
///     SILENTLY CLEAN or be REFUSED LOUDLY by a generator. Generator silence plus CS errors in the output
///     is a seed-replayable red — the exact defect class the surface-matrix bug A11-F1 shipped as
///     (<c>[MapTo]</c>×struct: null-guarded value type, CS0037, never compiled until round 20).
///     <para>
///         CsCheck owns the sampling, so a failure shrinks to a minimal <see cref="GraphSpec" /> and prints
///         the replay seed; the assertion message carries the rendered source so the repro is pasteable.
///         Any silent-CS-error found here is a REAL FINDING: pin the shrunk spec as a
///         <see cref="PinnedCorpus" /> row (and file an I-row if it is product-shaped) per house rules.
///     </para>
/// </summary>
public class TypeGraphSmokeTests
{
    private readonly ITestOutputHelper _output;

    public TypeGraphSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Sampled_graphs_compile_silently_clean_or_refuse_loudly()
    {
        var accepted = 0;
        var refused = 0;

        // Iteration count from the deep-tier catalog (fast = smoke, DWARF_DEEP=1 = the deep multiplier);
        // the loop is CsCheck-owned and bounded by iter (H7: the variant is CsCheck's own counter).
        TypeGraphGen.MirroredPair().Sample(graph =>
        {
            var units = TypeGraphRenderer.Render(graph);
            var result = CompilerTestHarness.Run(units);

            if (result.RefusedLoudly)
            {
                // A loud refusal (error-severity DWARF/DWARFR diagnostic) is a VALID outcome: the grammar
                // deliberately reaches shapes the mapper documents as unsupported, and "says so with a
                // named diagnostic" is the contract. What it must never be is silent AND broken.
                Interlocked.Increment(ref refused);
                return;
            }

            Interlocked.Increment(ref accepted);
            Assert.True(result.CompilationErrors.Length == 0,
                "seed-replayable silent miscompilation: the generators were silent but the output has ["
                + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct())
                + "]\n--- graph ---\n" + graph.Describe()
                + "\n--- units ---\n" + string.Join("\n--- next unit ---\n", units)
                + "\n--- generated ---\n" + result.GeneratedSource);
        }, iter: DeepTier.Count(DeepPopulation.CompilerGraphSmokeSeeds));

        // Vacuity guard: the compile-clean leg only has teeth when the generator ACCEPTS. A loose floor,
        // not an exact pin — the accept/refuse split of a random sample is a nondeterministic oracle and
        // R4 forbids gating on one; the measured split is reported instead.
        Assert.True(accepted > 0,
            $"the whole sample was refused ({refused} refusals, 0 accepts) — the sampled space has "
            + "drifted into the refusal grammar and the compile-clean invariant is vacuous.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sampled {accepted + refused}: {accepted} accepted (compiled clean), {refused} refused loudly"));
    }
}
