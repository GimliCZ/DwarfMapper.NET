// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.TestInfrastructure;
using DwarfMapper.Testing;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     K1 — the differential oracle over K0's generated graphs (R22-01), two legs per sampled graph.
///     <para>
///         <b>Leg 1 (must-compile-or-refuse):</b> a loud generator refusal is a valid outcome; generator
///         silence plus CS errors in the output is a seed-replayable red — the pinned-at-0
///         <c>EmittedInvalidCode</c> ratchet generalized from the surface matrix's 866 cells to the
///         generated space. The deterministic proof that the refusal branch executes at all is
///         <see cref="PinnedCorpus.UnmappedDestinationMemberRefusal" /> (K0 disclosed the branch had never
///         been taken).
///     </para>
///     <para>
///         <b>Leg 2 (the oracle):</b> DwarfMapper's executed map versus <see cref="ReflectionOracle" />'s
///         deliberately naive reflective copy, compared with the SHIPPED <c>[RoundTrip]</c> differ
///         (<see cref="StructuralComparer" /> — <c>RoundTrip.Verify</c>'s own member-path diff), dogfooding
///         the <c>DwarfMapper.Testing</c> package instead of growing a parallel comparer, per the audit's
///         binding correction. Any disagreement replays from its pinned case index
///         (<see cref="PinnedSampling" />, I11) and is then classified: generator bug → pinned corpus row
///         + I-row; undocumented semantic → corpus row + documentation-shaped I-row. Never ratified by
///         silently teaching the oracle.
///     </para>
///     <para>
///         <b>Differ depth arithmetic</b> (StructuralComparer.MaxDepth = 12 returns SILENTLY past the cap,
///         so the sampled space must fit under it): at the generator default of maxNodes = 4 per side, the
///         longest possible dest path is root → 3 nested hops, and each hop costs at most 3 levels
///         (member +1, collection index +1, KeyValuePair.Value +1 for a Dictionary edge) plus the leaf
///         member: 3 × 3 + 1 = 10 &lt; 12. Raising maxNodes for this leg would break that arithmetic —
///         re-derive it before touching the generator call below.
///     </para>
/// </summary>
public class DifferentialOracleTests
{
    private readonly ITestOutputHelper _output;

    public DifferentialOracleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Sampled_graphs_compile_or_refuse_and_the_executed_map_agrees_with_the_naive_oracle()
    {
        var executed = 0;
        var refused = 0;

        // Population seed sampled WITH the graph so one pinned case index replays both together.
        PinnedSampling.Run(DeepPopulation.CompilerOracleSeeds,
            Gen.Select(TypeGraphGen.MirroredPair(), Gen.Int[0, int.MaxValue - 1]),
            sample => sample.Item1.Describe()
                      + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
            sample =>
            {
                var (graph, seed) = sample;
                var units = TypeGraphRenderer.Render(graph);
                var (result, assembly) = CompilerTestHarness.RunAndEmit(units);

                // Leg 1 — must compile or refuse loudly (K0's smoke contract, restated over this leg's
                // own samples because the emit path must also hold it before leg 2 may execute).
                if (result.RefusedLoudly)
                {
                    Interlocked.Increment(ref refused);
                    return;
                }

                Assert.True(result.CompilationErrors.Length == 0,
                    "seed-replayable silent miscompilation: the generators were silent but the output has ["
                    + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct())
                    + "]\n--- graph ---\n" + graph.Describe()
                    + "\n--- units ---\n" + string.Join("\n--- next unit ---\n", units)
                    + "\n--- generated ---\n" + result.GeneratedSource);
                Assert.NotNull(assembly);

                // Leg 2 — the differential oracle.
                RunOracleLeg(assembly, graph, seed, result.GeneratedSource);
                Interlocked.Increment(ref executed);
            }, _output, "K1 oracle");

        // Vacuity guard, mirroring the smoke's: the oracle only has teeth when maps EXECUTE. A loose
        // floor, not an exact pin — the accept/refuse split is a property of the sampled GRAMMAR, so it
        // is reported rather than gated; the product-side gate is the comparison itself.
        Assert.True(executed > 0,
            $"no sampled graph executed ({refused} refusals, 0 executions) — the sampled space has "
            + "drifted into the refusal grammar and the differential leg is vacuous.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sampled {executed + refused}: {executed} executed and compared, {refused} refused loudly"));
    }

    /// <summary>
    ///     The two ACCEPTED deterministic corpus rows run through the full oracle as well — a
    ///     seed-independent anchor: if the differential leg ever breaks on these, it broke on a shape whose
    ///     history is already written down. Divergence-pinned and refusal-pinned rows have nothing to
    ///     execute and are skipped by their own contracts.
    /// </summary>
    [Theory]
    [InlineData("P5-K0-partial-split-struct-pair")]
    [InlineData("A11-representation-mirror")]
    public void Accepted_pinned_corpus_rows_agree_with_the_naive_oracle(string id)
    {
        var row = PinnedCorpus.Rows.Single(r => r.Id == id);
        Assert.True(row.KnownSilentCsIds is null && row.ExpectedRefusalIds is null,
            $"row '{id}' is not an accepted row any more — move this anchor deliberately");

        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(row.Graph));
        Assert.False(result.RefusedLoudly);
        Assert.True(result.CompilationErrors.Length == 0);
        Assert.NotNull(assembly);

        RunOracleLeg(assembly, row.Graph, seed: 12345, result.GeneratedSource);
    }

    /// <summary>
    ///     The NullCollections switch's own deterministic regression pin, independent of sampling (the
    ///     sampled coverage rides on the populator's null-probability constants, which could drift): a
    ///     hand-forced NULL source collection must map to an EMPTY destination collection — the documented
    ///     default (docs/options.md, class-options row <c>NullCollections</c>: "Null source collection →
    ///     AsEmpty (never throws)") — on BOTH sides of the differential, and the naive no-switch answer
    ///     (null) must disagree, proving the switch is load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public void Null_source_collection_maps_to_empty_on_both_sides_per_the_documented_default()
    {
        var graph = new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.List, 1)],
                    BaseRef: null),
                new NodeSpec("S1", TypeKind.Class,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.List, 3)],
                    BaseRef: null),
                new NodeSpec("D1", TypeKind.Class,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0");

        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(graph));
        Assert.False(result.RefusedLoudly);
        Assert.True(result.CompilationErrors.Length == 0);
        Assert.NotNull(assembly);

        var sourceType = assembly.GetType("T.S0")!;
        var source = Activator.CreateInstance(sourceType)!; // M0_0 stays NULL — the forced case
        var mapped = CompilerTestHarness.InvokeMap(assembly, source);

        // The product side: the documented default materializes an empty list.
        var destMember = assembly.GetType("T.D0")!.GetProperty("M0_0")!.GetValue(mapped);
        var asEnumerable = Assert.IsAssignableFrom<IEnumerable>(destMember);
        Assert.Empty(asEnumerable.Cast<object>());

        // The oracle side, through the switch: agreement, zero diffs.
        var expected = ReflectionOracle.NaiveMap(
            source, assembly.GetType("T.D0")!, ReflectionOracle.OracleOptions.DocumentedDefaults);
        Assert.Empty(StructuralComparer.Diff(expected, mapped));

        // And the switch is load-bearing: the naive AsNull answer disagrees with the product default.
        var naiveNull = ReflectionOracle.NaiveMap(
            source, assembly.GetType("T.D0")!,
            new ReflectionOracle.OracleOptions(NullCollectionStrategy.AsNull));
        Assert.NotEmpty(StructuralComparer.Diff(naiveNull, mapped));
    }

    /// <summary>
    ///     I5's runtime pin (TASKS.md I5, FIXED in round 23 N1): a null element crossing a synthesized
    ///     element map yields a NULL ELEMENT, in every wrapper the descriptor can express and for both
    ///     destination element kinds. Emission alone is not the proof — the CS1503 that started this was an
    ///     emission fact, but "it compiles now" would be satisfied by a helper that threw, or by one that
    ///     dropped the element. So this executes: the source collection holds <c>[null, value]</c> and the
    ///     destination must hold <c>[null, mapped-value]</c> — the null lifted AND the non-null still
    ///     mapped, cardinality preserved (a silent drop would leave a one-element result).
    ///     <para>
    ///     The emission shape is asserted too, and deliberately at the seam that broke: the element call
    ///     must be guarded by a <c>HasValue</c> test and typed by an explicit cast to the destination
    ///     element type, rather than being handed the <c>S?</c> raw. Matching on
    ///     that fragment rather than on a whole emitted line keeps the pin stable across helper-name hashes
    ///     and across the per-wrapper element accessor (<c>__item</c> / <c>src[__i]</c> / <c>__kv.Value</c>).
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData(CollShape.List, TypeKind.Struct, TypeKind.Struct)]
    [InlineData(CollShape.List, TypeKind.Struct, TypeKind.Class)]
    [InlineData(CollShape.List, TypeKind.RecordStruct, TypeKind.Record)]
    [InlineData(CollShape.Array, TypeKind.Struct, TypeKind.Struct)]
    [InlineData(CollShape.Array, TypeKind.Struct, TypeKind.Class)]
    [InlineData(CollShape.IReadOnlyList, TypeKind.Struct, TypeKind.Struct)]
    [InlineData(CollShape.IReadOnlyList, TypeKind.RecordStruct, TypeKind.Class)]
    [InlineData(CollShape.HashSet, TypeKind.Struct, TypeKind.Struct)]
    [InlineData(CollShape.HashSet, TypeKind.Struct, TypeKind.Class)]
    [InlineData(CollShape.Dictionary, TypeKind.Struct, TypeKind.Struct)]
    [InlineData(CollShape.Dictionary, TypeKind.RecordStruct, TypeKind.Class)]
    public void I5_nullable_struct_element_lifts_null_to_null_in_every_wrapper(
        CollShape wrapper, TypeKind sourceElement, TypeKind destElement)
    {
        var graph = new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, wrapper, 1)],
                    BaseRef: null),
                new NodeSpec("S1", sourceElement,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, wrapper, 3)],
                    BaseRef: null),
                new NodeSpec("D1", destElement,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0");

        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(graph));
        Assert.False(result.RefusedLoudly, "I5's shapes must map, not refuse — the ruling was LIFT");
        Assert.True(result.CompilationErrors.Length == 0,
            "I5 regression — the element loop emitted code that does not compile: ["
            + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct()) + "]");
        Assert.NotNull(assembly);
        Assert.Contains(".HasValue ? (global::T.D1?)__DwarfMap_Obj_", result.GeneratedSource,
            StringComparison.Ordinal);

        var sourceType = assembly.GetType("T.S0")!;
        var source = Activator.CreateInstance(sourceType)!;
        var member = sourceType.GetProperty("M0_0")!;
        var elementType = assembly.GetType("T.S1")!;
        var nonNull = Activator.CreateInstance(elementType)!;
        member.SetValue(source, TwoElementPayload(member.PropertyType, nonNull));

        var mapped = CompilerTestHarness.InvokeMap(assembly, source);
        var values = DestinationElements(assembly.GetType("T.D0")!.GetProperty("M0_0")!.GetValue(mapped));

        Assert.Equal(2, values.Count);
        Assert.Single(values, v => v is null);
        Assert.Single(values, v => v is not null);
    }

    /// <summary>
    ///     Builds a two-entry collection of <paramref name="collectionType" /> holding <c>null</c> and
    ///     <paramref name="nonNull" />. Reflection is test-side only (the house no-reflection stance governs
    ///     the shipped product, not the oracles) and every branch corresponds to one <see cref="CollShape" />
    ///     the renderer can emit — an unhandled shape throws rather than silently testing a weaker payload.
    /// </summary>
    private static object TwoElementPayload(Type collectionType, object nonNull)
    {
        if (collectionType.IsArray)
        {
            var array = Array.CreateInstance(collectionType.GetElementType()!, 2);
            array.SetValue(nonNull, 1);
            return array;
        }

        // IReadOnlyList<T> is an interface — instantiate the List<T> the mapper will enumerate.
        var concrete = collectionType.IsInterface
            ? typeof(List<>).MakeGenericType(collectionType.GetGenericArguments()[0])
            : collectionType;
        var instance = Activator.CreateInstance(concrete)!;

        if (typeof(IDictionary).IsAssignableFrom(concrete))
        {
            var dictionary = (IDictionary)instance;
            dictionary["a"] = null;
            dictionary["b"] = nonNull;
            return dictionary;
        }

        var add = concrete.GetMethod("Add")
                  ?? throw new InvalidOperationException($"no Add on payload type {concrete}");
        add.Invoke(instance, [null]);
        add.Invoke(instance, [nonNull]);
        return instance;
    }

    /// <summary>The destination collection's elements (a dictionary's VALUES), as a flat list.</summary>
    private static List<object?> DestinationElements(object? destinationMember)
    {
        return destinationMember switch
        {
            IDictionary dictionary => dictionary.Values.Cast<object?>().ToList(),
            IEnumerable enumerable => enumerable.Cast<object?>().ToList(),
            _ => throw new InvalidOperationException("destination member is not a collection")
        };
    }

    /// <summary>
    ///     I7's pin (TASKS.md I7; found by this leg's FIRST 1,000-sample deep run, seed 0vihQF5Vee7b,
    ///     minimized by a 6-cell kind-pair probe, FIXED across N1/N2): a plain nullable nested member across
    ///     a re-kinded pair lifts <c>null → null</c> for <b>every</b> kind pair, because the destination
    ///     member is nullable-capable in every one of them. Before the fix the same member's behaviour was
    ///     decided by the mirrored type's KIND: the two diagonals lifted, four cells threw
    ///     <c>"Source member 'M0_0' was null"</c>, and the reverse genre threw a different message again,
    ///     <c>"Cannot map a null 'global::T.S1' to value-type 'global::T.D1'."</c>.
    ///     <para>
    ///     The table is run in FULL rather than only over the cells that used to throw: the diagonals are
    ///     the control, and a fix that lifted the broken cells by breaking the working ones would pass a
    ///     four-cell version of this test. The two pinned corpus rows (<c>I7-nullable-rekind-*</c>) are the
    ///     two ends of it and are asserted to be present in the table, so the corpus and the executor cannot
    ///     drift apart.
    ///     </para>
    ///     <para>
    ///     This must stay a DETERMINISTIC executor even now that the I7 sampled-space exclusion is gone:
    ///     the reference-source half is unreachable by sampling for a reason that has nothing to do with the
    ///     product — the oracle's population never nulls reference members, a declared bias in the
    ///     <c>ReflectionOracle</c> header. Sampling does not cover it; this does.
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData(TypeKind.Struct, TypeKind.Struct)] // control — always lifted
    [InlineData(TypeKind.Class, TypeKind.Class)] // control — always propagated
    [InlineData(TypeKind.Struct, TypeKind.Class)] // was: "Source member 'M0_0' was null"
    [InlineData(TypeKind.Struct, TypeKind.Record)] // was: the same throw
    [InlineData(TypeKind.RecordStruct, TypeKind.Class)] // was: the same throw
    [InlineData(TypeKind.Class, TypeKind.Struct)] // was: "Cannot map a null … to value-type …"
    public void I7_null_across_a_rekinded_pair_lifts_for_every_kind_pair(TypeKind sourceKind, TypeKind destKind)
    {
        var graph = new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.None, 1)],
                    BaseRef: null),
                new NodeSpec("S1", sourceKind,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.None, 3)],
                    BaseRef: null),
                new NodeSpec("D1", destKind,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0");

        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(graph));
        Assert.False(result.RefusedLoudly, "I7's shapes must map, not refuse — the ruling was LIFT");
        Assert.True(result.CompilationErrors.Length == 0, "I7 regression — the re-kinded pair no longer compiles");
        Assert.NotNull(assembly);

        var sourceType = assembly.GetType("T.S0")!;
        var destMember = assembly.GetType("T.D0")!.GetProperty("M0_0")!;

        // Null in → null out. Not "it does not throw": the destination member must actually BE null,
        // which a default-constructed D1 (the other way to make the exception disappear) would fail.
        var nullSource = Activator.CreateInstance(sourceType)!; // M0_0 stays null — the pinned case
        Assert.Null(destMember.GetValue(CompilerTestHarness.InvokeMap(assembly, nullSource)));

        // …and a non-null still maps, so the lift is not a blanket "write null and move on".
        var valueSource = Activator.CreateInstance(sourceType)!;
        sourceType.GetProperty("M0_0")!.SetValue(valueSource, Activator.CreateInstance(assembly.GetType("T.S1")!));
        Assert.NotNull(destMember.GetValue(CompilerTestHarness.InvokeMap(assembly, valueSource)));
    }

    /// <summary>
    ///     The two pinned I7 corpus rows are exactly the two ends of the kind-pair table above — asserted
    ///     rather than assumed, so renaming or re-shaping a row cannot quietly remove a cell from the
    ///     executor while both still look green.
    /// </summary>
    [Theory]
    [InlineData("I7-nullable-rekind-value-to-reference")]
    [InlineData("I7-nullable-rekind-reference-to-value")]
    public void I7_pinned_corpus_rows_lift_null_to_null(string id)
    {
        var row = PinnedCorpus.Rows.Single(r => r.Id == id);
        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(row.Graph));
        Assert.False(result.RefusedLoudly, $"I7 row '{id}' now REFUSES — re-file, don't absorb");
        Assert.True(result.CompilationErrors.Length == 0, $"I7 row '{id}' no longer compiles clean");
        Assert.NotNull(assembly);

        var source = Activator.CreateInstance(assembly.GetType("T.S0")!)!; // M0_0 stays null — the pinned case
        var mapped = CompilerTestHarness.InvokeMap(assembly, source);
        Assert.Null(assembly.GetType("T.D0")!.GetProperty("M0_0")!.GetValue(mapped));
    }

    private static void RunOracleLeg(
        System.Reflection.Assembly assembly, GraphSpec graph, int seed, string generatedSource)
    {
        var sourceType = assembly.GetType("T." + graph.RootSource)
                         ?? throw new InvalidOperationException($"no type T.{graph.RootSource} in emitted assembly");
        var destType = assembly.GetType("T." + graph.RootDest)
                       ?? throw new InvalidOperationException($"no type T.{graph.RootDest} in emitted assembly");

        var source = ReflectionOracle.Populate(sourceType, seed);
        object? actual;
        try
        {
            actual = CompilerTestHarness.InvokeMap(assembly, source);
        }
        catch (Exception e)
        {
            // The generated map THREW — a runtime-behaviour fact about the product on this input, which is
            // as much a differential outcome as a wrong value. Rethrown with the full repro context so the
            // shrunk sample is classifiable (documented semantics vs finding) without re-running.
            throw new InvalidOperationException(
                $"generated map THREW {e.GetType().Name}: {e.Message}\n"
                + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
                + "\n--- graph ---\n" + graph.Describe()
                + "\n--- generated ---\n" + generatedSource, e);
        }

        var expected = ReflectionOracle.NaiveMap(
            source, destType, ReflectionOracle.OracleOptions.DocumentedDefaults);

        var diffs = StructuralComparer.Diff(expected, actual);
        Assert.True(diffs.Count == 0,
            "seed-replayable ORACLE DISAGREEMENT (shrink, then classify: generator bug -> pinned corpus "
            + "row + I-row; undocumented semantic -> corpus row + documentation-shaped I-row; never teach "
            + "the oracle silently):\n"
            + StructuralComparer.Render(diffs)
            + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
            + "\n--- graph ---\n" + graph.Describe()
            + "\n--- generated ---\n" + generatedSource);
    }
}
