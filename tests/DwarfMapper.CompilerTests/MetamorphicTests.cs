// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.TestInfrastructure;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     K2 — metamorphic relations over K0's generated graphs (R22-02, the EMI move): assert relations
///     BETWEEN outcomes instead of absolute outcomes, which pressures exactly the axes R4 forbids gating
///     via branch % — a violated relation is a deterministic, seed-replayable red.
///     <para>
///         <b>MR-1</b> member declaration order is semantics-free; <b>MR-2</b> a source member no
///         destination can consume is behaviour-neutral; <b>MR-3</b> Class/Record/RecordStruct
///         representation invariance (the A11-F1/F2 bug class generalized). Each relation compares
///         <see cref="Outcome" /> strings: a loud refusal fingerprints as its sorted diagnostic ids
///         (refusal parity IS part of every relation), an executed map fingerprints via
///         <see cref="ResultFingerprint" /> (order-independent — see its header for why the shipped
///         <c>StructuralComparer</c> cannot serve here), and a map that throws fingerprints as the
///         exception type + message (throw PARITY is relation material — a kind-dependent throw is
///         exactly the I7 genre; ABSOLUTE no-throw coverage is K1's oracle leg's job, not this file's).
///         Silent generators + CS errors is never fingerprinted — it is an immediate red (K1 leg-1
///         shape), so a relation can never go green on a pair of identical silent miscompilations.
///     </para>
///     <para>
///         Population is NAME-KEYED (<c>ReflectionOracle.Populate</c>, the audit's binding correction),
///         so the same logical graph reordered, fattened, or re-kinded populates identically — the
///         relations compare mappings, never population artifacts. <b>With one measured exception</b>, and
///         it is the reason <see cref="ListifySetsOfStructuralElements" /> exists: a <c>HashSet</c> member
///         whose element type is a graph NODE is a container whose content depends on the element type's
///         own <c>Equals</c>, which is exactly the axis MR-2 and MR-3 vary — see that method for the
///         declaration and the pin. The population seed is sampled WITH the graph so one pinned case index
///         replays both together (<see cref="PinnedSampling" />: the case set of every relation is a fixed,
///         replayable list, not a fresh random draw — invariant R4, finding I11).
///     </para>
/// </summary>
public class MetamorphicTests
{
    private readonly ITestOutputHelper _output;

    public MetamorphicTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ─── MR-1: member declaration order ──────────────────────────────────────────

    [Fact]
    public void MR1_member_declaration_order_is_semantics_free()
    {
        var executed = 0;
        var refused = 0;

        PinnedSampling.Run(DeepPopulation.CompilerMrMemberOrderSeeds,
            Gen.Select(TypeGraphGen.MirroredPair(), Gen.Int[0, int.MaxValue - 1]),
            sample => sample.Item1.Describe()
                      + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
            sample =>
            {
                var (graph, seed) = sample;

                // No set normalization here, deliberately: MR-1 changes neither kinds nor member SETS, and
                // record/struct equality is insensitive to declaration order (it compares all instance
                // fields), so a HashSet of nodes holds the same content in both variants. Set-of-node
                // members therefore stay fully under MR-1 — the exclusion is precondition-shaped, not a
                // blanket ban on the shape.
                // The variant reverses the member list of EVERY node. Reversal is the strongest single
                // deterministic permutation (every non-fixed member moves, ctor parameter order included —
                // the renderer emits ctor params in member order, so the product's name-based ctor matching
                // is under test too); node order itself cannot vary (NestedRef/BaseRef are indexes, V2/V5).
                var reversed = graph with
                {
                    Nodes = graph.Nodes
                        .Select(n => n with { Members = n.Members.Reverse().ToList() })
                        .ToList()
                };

                var baseline = Outcome(graph, seed);
                var variant = Outcome(reversed, seed);
                Assert.True(baseline == variant,
                    "MR-1 VIOLATED: reversing member declaration order changed the outcome.\n"
                    + "--- baseline outcome ---\n" + baseline
                    + "\n--- reversed outcome ---\n" + variant
                    + "\n--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
                    + "\n--- graph ---\n" + graph.Describe());

                Count(baseline, ref executed, ref refused);
            }, _output, "MR-1");

        AssertNotVacuous(executed, refused, "MR-1");
    }

    // ─── MR-2: unmapped-member neutrality ────────────────────────────────────────

    [Fact]
    public void MR2_a_source_member_no_destination_consumes_is_behaviour_neutral()
    {
        var executed = 0;
        var refused = 0;

        PinnedSampling.Run(DeepPopulation.CompilerMrUnmappedMemberSeeds,
            Gen.Select(TypeGraphGen.MirroredPair(), Gen.Int[0, int.MaxValue - 1]),
            sample => sample.Item1.Describe()
                      + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
            sample =>
            {
                var (rolled, seed) = sample;
                var graph = NormalizeForInjection(rolled);
                var fat = InjectUnmappedMembers(graph);

                var baseline = Outcome(graph, seed);
                var variant = Outcome(fat, seed);
                Assert.True(baseline == variant,
                    "MR-2 VIOLATED: an injected source member no destination consumes changed the outcome.\n"
                    + "--- baseline outcome ---\n" + baseline
                    + "\n--- fattened outcome ---\n" + variant
                    + "\n--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
                    + "\n--- graph ---\n" + graph.Describe());

                Count(baseline, ref executed, ref refused);
            }, _output, "MR-2");

        AssertNotVacuous(executed, refused, "MR-2");
    }

    /// <summary>
    ///     MR-2's input normalization, the counterpart of MR-3's <see cref="Normalize" />: sets whose
    ///     ELEMENT type has structural equality are outside this relation's premise, because the injected
    ///     member changes the element's IDENTITY — the SOURCE set can then hold a different number of
    ///     elements before any mapping happens (measured:
    ///     <see cref="Set_shaped_members_are_unmapped_member_dependent_by_design" />). Only structural
    ///     element kinds are listified, so class-element sets keep their full MR-2 coverage — MR-2 reaches
    ///     the shared precondition without re-kinding anything, so unlike MR-3 it can afford the narrow
    ///     predicate. See <see cref="ListifySetsOfStructuralElements" /> for the whole declaration.
    /// </summary>
    private static GraphSpec NormalizeForInjection(GraphSpec graph)
    {
        return ListifySetsOfStructuralElements(graph, n => n.Kind is not TypeKind.Class);
    }

    /// <summary>
    ///     Appends one extra member (<c>X{i}</c>, plain non-nullable <c>int</c> auto-property) to every
    ///     node the mapping cannot consume as a DESTINATION — the complement of the dest-reachable set,
    ///     i.e. the whole source subgraph plus any dest node unreachable from the root. Injecting into a
    ///     dest-REACHABLE node would change the completeness obligation itself (an extra unmatched dest
    ///     member legitimately refuses, DWARF001), which is a different experiment, not this relation.
    ///     <para>
    ///         Collision with V4's naming is impossible BY CONSTRUCTION: sampled member names are all
    ///         <c>M{i}_{k}</c> and the injected prefix <c>X</c> is disjoint — asserted defensively below,
    ///         and the renderer's <c>Validate</c> call (V4, CS0102) is the belt behind the assertion.
    ///         AutoProp shape keeps rule V5b honest on base-target nodes (no new ctor parameters).
    ///     </para>
    /// </summary>
    private static GraphSpec InjectUnmappedMembers(GraphSpec graph)
    {
        var destReachable = ReachableFrom(graph, graph.RootDest);
        var nodes = new List<NodeSpec>(graph.Nodes.Count);
        for (var i = 0; i < graph.Nodes.Count; i++)
        {
            var node = graph.Nodes[i];
            if (destReachable.Contains(i))
            {
                nodes.Add(node);
                continue;
            }

            var injectedName = FormattableString.Invariant($"X{i}");
            Assert.DoesNotContain(injectedName, node.Members.Select(m => m.Name));
            nodes.Add(node with
            {
                Members =
                [
                    .. node.Members,
                    new MemberSpec(injectedName, "int", Nullable: false, MemberShape.AutoProp,
                        CollShape.None, NestedRef: null)
                ]
            });
        }

        return graph with { Nodes = nodes };
    }

    /// <summary>
    ///     Index set reachable from the named root via NestedRef and BaseRef edges. H7: every node is
    ///     enqueued at most once (the seen-set guard), so the loop is bounded by Nodes.Count.
    /// </summary>
    private static HashSet<int> ReachableFrom(GraphSpec graph, string rootName)
    {
        var root = Enumerable.Range(0, graph.Nodes.Count).Single(i => graph.Nodes[i].Name == rootName);
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = graph.Nodes[queue.Dequeue()];
            foreach (var member in node.Members)
            {
                if (member.NestedRef is int j && seen.Add(j)) queue.Enqueue(j);
            }

            if (node.BaseRef is int b && seen.Add(b)) queue.Enqueue(b);
        }

        return seen;
    }

    // ─── MR-3: representation invariance ─────────────────────────────────────────

    [Fact]
    public void MR3_class_record_and_recordstruct_representations_map_identically()
    {
        var executed = 0;
        var refused = 0;

        PinnedSampling.Run(DeepPopulation.CompilerMrRekindSeeds,
            Gen.Select(TypeGraphGen.MirroredPair(), Gen.Int[0, int.MaxValue - 1]),
            sample => sample.Item1.Describe()
                      + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
            sample =>
            {
                var (graph, seed) = sample;
                var normalized = Normalize(graph);
                var outcomes = RekindSet(normalized)
                    .Select(kind => (Kind: kind, Outcome: Outcome(Rekind(normalized, kind), seed)))
                    .ToList();

                var distinct = outcomes.Select(o => o.Outcome).Distinct(StringComparer.Ordinal).ToList();
                Assert.True(distinct.Count == 1,
                    "MR-3 VIOLATED: representation changed the outcome.\n"
                    + string.Join("\n", outcomes.Select(o =>
                        $"--- {o.Kind} outcome ---\n{o.Outcome}"))
                    + "\n--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
                    + "\n--- normalized graph ---\n" + normalized.Describe());

                Count(outcomes[0].Outcome, ref executed, ref refused);
            }, _output, "MR-3");

        AssertNotVacuous(executed, refused, "MR-3");
    }

    /// <summary>
    ///     MR-3's whole input normalization, in the order the reasons were found: nested nullability is
    ///     cleared (<see cref="ClearNestedNullability" />), then sets of graph-node elements are listified
    ///     (<see cref="ListifySetsOfStructuralElements" />). Both are DECLARED preconditions of the
    ///     relation, each with its own evidence; neither weakens the relation for any other shape.
    /// </summary>
    private static GraphSpec Normalize(GraphSpec graph)
    {
        // MR-3's re-kind set ALWAYS contains Record (see RekindSet), so every graph-node element type is
        // structural in at least one variant — the predicate is unconditionally true here, unlike MR-2's.
        return ListifySetsOfStructuralElements(ClearNestedNullability(graph), _ => true);
    }

    /// <summary>
    ///     <b>The set-shaped precondition</b> (round 22, finding I11; the counterexample is
    ///     <c>PinnedCorpus.SetOfStructuralElements</c> and its executor is
    ///     <see cref="Set_shaped_members_are_representation_dependent_by_design" />). A member declared
    ///     <c>HashSet&lt;E&gt;</c> where <c>E</c> is a graph NODE is rewritten to <c>List&lt;E&gt;</c> for
    ///     the relations whose variants can change <c>E</c>'s equality.
    ///     <para>
    ///         <b>Why the relation genuinely does not hold there.</b> A set's CONTENT is a function of its
    ///         element type's <c>Equals</c>. Class elements compare by reference — two independently
    ///         populated elements are always distinct. Record, struct and record-struct elements compare
    ///         structurally — two independently populated elements collapse to one whenever their member
    ///         values coincide. The divergence therefore happens at POPULATION time, before any mapping:
    ///         the measured counterexample (seed 269828994) builds a SOURCE set of two elements under the
    ///         class representation and one under record / record struct, and the mapper faithfully
    ///         reproduces whichever it was given. Both behaviours are correct; the relation's premise —
    ///         the variants receive the SAME input — is what fails. That is <see cref="ClearNestedNullability" />
    ///         reason (2) generalized from nullability to equality.
    ///     </para>
    ///     <para>
    ///         <b>Why the population cannot be fixed instead</b> (the alternative disposition, rejected on
    ///         evidence rather than effort): input parity would require the populator to guarantee that
    ///         the elements of a set are pairwise structurally distinct, and it cannot — the element value
    ///         space can be SMALLER than the element count. A node whose only member is <c>bool</c> has two
    ///         inhabitants, so a two-element set of it collides with probability ≈ ½ under any populator;
    ///         a node whose only member is <c>byte</c>, ≈ 1/256. The counterexample's own collision is of
    ///         that family (both elements' single <c>long?</c> member drew null under the documented ~25 %
    ///         nullable-scalar bias). No seeding discipline removes a pigeonhole.
    ///     </para>
    ///     <para>
    ///         <b>Scope, deliberately narrow.</b> Only <c>HashSet</c>, and only when the element is a graph
    ///         node: a set of SCALARS is untouched (scalar equality is representation-invariant), and every
    ///         other <c>CollShape</c> is untouched — arrays and lists are index-keyed, and the grammar's
    ///         dictionaries are <c>Dictionary&lt;string, E&gt;</c> only (TypeGraphRenderer), so no
    ///         dictionary in this space is keyed by an element whose equality a re-kind could change. The
    ///         I11 filing's "or a dictionary keyed by the element" branch is therefore unreachable here,
    ///         and stated so rather than guarded speculatively. Rewriting to <c>List</c> rather than
    ///         dropping the graph keeps every pinned case live: the member is still a collection edge, so
    ///         collection mapping stays under test — only the container's equality semantics leave.
    ///     </para>
    /// </summary>
    /// <param name="graph">The sampled graph.</param>
    /// <param name="elementIsStructural">
    ///     Whether this relation's variants can give that element node structural equality. MR-3 passes a
    ///     constant true (Record is always in the re-kind set); MR-2 passes the node's OWN kind test, so
    ///     class-element sets keep their MR-2 coverage.
    /// </param>
    private static GraphSpec ListifySetsOfStructuralElements(
        GraphSpec graph, Func<NodeSpec, bool> elementIsStructural)
    {
        return graph with
        {
            Nodes = graph.Nodes
                .Select(n => n with
                {
                    Members = n.Members
                        .Select(m => m is { Coll: CollShape.HashSet, NestedRef: int target }
                                     && elementIsStructural(graph.Nodes[target])
                            ? m with { Coll = CollShape.List }
                            : m)
                        .ToList()
                })
                .ToList()
        };
    }

    /// <summary>
    ///     Half of MR-3's input design (<see cref="Normalize" /> is the whole of it; the set half is
    ///     <see cref="ListifySetsOfStructuralElements" />): nested-member nullability is cleared before
    ///     re-kinding, for three distinct
    ///     load-bearing reasons, each keyed to its evidence. (1) <b>I5</b> — a struct-kind re-kind of a
    ///     nullable collection ELEMENT is the pinned silent-CS1503 miscompile
    ///     (<c>PinnedCorpus.NullableStructElementMap</c>); the relation must not walk into a divergence
    ///     that is already pinned deterministically. (2) <b>Population parity</b> — a nullable member over
    ///     a value-kind node is <c>Nullable&lt;T&gt;</c> at runtime (nulled ~25% by the populator), while
    ///     over a reference-kind node the annotation is erased and the oracle's DECLARED bias never nulls
    ///     it; the variants would receive DIFFERENT inputs and the relation would compare populations, not
    ///     mappings. (3) <b>I7</b> — null across nested pairs is the pinned undocumented-throw family;
    ///     its coverage lives in the I7 pins and K1's sampling, not here. Scalar nullability stays: a
    ///     <c>Nullable&lt;int&gt;</c> populates identically under every kind. The nullable-NESTED axis is
    ///     therefore a DECLARED exclusion of this relation (this remark is the declaration), not a silent
    ///     one — it dies with the I5/I7 pins.
    /// </summary>
    private static GraphSpec ClearNestedNullability(GraphSpec graph)
    {
        return graph with
        {
            Nodes = graph.Nodes
                .Select(n => n with
                {
                    Members = n.Members
                        .Select(m => m.NestedRef is not null ? m with { Nullable = false } : m)
                        .ToList()
                })
                .ToList()
        };
    }

    /// <summary>
    ///     The kinds a graph can be uniformly re-kinded to. Class and Record are UNCONDITIONAL — a
    ///     reference kind expresses every shape the grammar produces — so the set is always ≥ 2 and the
    ///     relation cannot go vacuous for any graph. RecordStruct drops out exactly when the graph uses
    ///     inheritance: a struct kind cannot declare a base type (rule V5, CS0527) — the shape is
    ///     genuinely inexpressible, so the re-kind set shrinks WITH THIS STATED REASON rather than the
    ///     graph being excluded. (TypeKind.Struct is deliberately not in the set: plain structs and record
    ///     structs share the value-kind mapping paths, and record struct is the kind the A11 bug family
    ///     shipped against — the ratchet for kind coverage lives in EnumCoverageRatchetTests.)
    /// </summary>
    private static IReadOnlyList<TypeKind> RekindSet(GraphSpec graph)
    {
        return graph.Nodes.Any(n => n.BaseRef is not null)
            ? [TypeKind.Class, TypeKind.Record]
            : [TypeKind.Class, TypeKind.Record, TypeKind.RecordStruct];
    }

    /// <summary>
    ///     Uniform re-kind of every node, guarded by REUSING K0's validity rules per the audit's binding
    ///     correction: the explicit <c>Validate</c> call re-runs V3 (no value-type cycles — independent of
    ///     V2, so the guard survives any future relaxation of the DAG rule) and V5 (no struct base — the
    ///     belt behind <see cref="RekindSet" />'s shrink). The renderer validates again before emitting;
    ///     graphs are ≤ 8 nodes, so the double check is noise-level cost for a visible guard.
    /// </summary>
    private static GraphSpec Rekind(GraphSpec graph, TypeKind kind)
    {
        var rekinded = graph with
        {
            Nodes = graph.Nodes.Select(n => n with { Kind = kind }).ToList()
        };
        rekinded.Validate();
        return rekinded;
    }

    // ─── I11: the preserved counterexamples behind the set-shaped precondition ───

    /// <summary>
    ///     MR-3's excluded shape, executed deterministically so the knowledge outlives the exclusion
    ///     (<c>PinnedCorpus.SetOfStructuralElements</c>, TASKS.md I11). Three claims, in the order that
    ///     makes the exclusion honest rather than convenient:
    ///     <list type="number">
    ///         <item>the cardinality really is representation-dependent — 2 under Class, 1 under Record and
    ///             record struct;</item>
    ///         <item>it is a POPULATION difference, not a mapping one — the SOURCE set already holds 2 vs 1
    ///             before the mapper is invoked, and the mapped set matches its own source every time, so
    ///             the mapper is exonerated by measurement rather than by assertion;</item>
    ///         <item><see cref="Normalize" /> removes it — the same graph, listified, satisfies MR-3.</item>
    ///     </list>
    ///     Claim 3 is what makes this test the exclusion's tripwire: were the listification deleted, MR-3
    ///     would red here deterministically instead of at random.
    /// </summary>
    [Fact]
    public void Set_shaped_members_are_representation_dependent_by_design()
    {
        var graph = PinnedCorpus.SetOfStructuralElements.Graph;
        const int seed = PinnedCorpus.SetOfStructuralElementsSeed;

        var expected = new Dictionary<TypeKind, int>
        {
            [TypeKind.Class] = 2, [TypeKind.Record] = 1, [TypeKind.RecordStruct] = 1
        };

        foreach (var (kind, cardinality) in expected)
        {
            var (sourceCount, outcome) = RunRootSet(Rekind(graph, kind), seed);
            Assert.True(sourceCount == cardinality,
                $"the {kind} SOURCE set holds {sourceCount}, expected {cardinality} — the I11 pin rests on "
                + "the populator's ~25% nullable-scalar bias nulling both elements' long? member; if that "
                + "bias moved, re-measure the pinned seed rather than relaxing this number.");
            Assert.Contains(
                "r.M0_1.#=" + cardinality.ToString(CultureInfo.InvariantCulture), outcome,
                StringComparison.Ordinal);
        }

        // Claim 3: with the set listified, the very same graph satisfies MR-3.
        var normalized = Normalize(graph);
        Assert.DoesNotContain(CollShape.HashSet,
            normalized.Nodes.SelectMany(n => n.Members).Select(m => m.Coll));
        var listified = RekindSet(normalized)
            .Select(kind => Outcome(Rekind(normalized, kind), seed))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(listified.Count == 1,
            "the listified graph must satisfy MR-3, or the declared precondition names the wrong axis:\n"
            + string.Join("\n---\n", listified));
    }

    /// <summary>
    ///     The same precondition reached WITHOUT re-kinding — MR-2's route
    ///     (<c>PinnedCorpus.SetOfStructuralElementsRecordToClass</c>, TASKS.md I11). A record source
    ///     element makes two independently populated set elements collapse to one; the injected unmapped
    ///     member <c>X1</c> differs per element and splits the tie, so the fattened variant's source set
    ///     holds two. The destination element is a CLASS, so the mapped set faithfully keeps whichever
    ///     count it was handed. Deduced from MR-3's root cause and then MEASURED here — MR-2 had never
    ///     drawn it, which is precisely why it is pinned rather than left to sampling.
    /// </summary>
    [Fact]
    public void Set_shaped_members_are_unmapped_member_dependent_by_design()
    {
        var graph = PinnedCorpus.SetOfStructuralElementsRecordToClass.Graph;
        const int seed = PinnedCorpus.SetOfStructuralElementsSeed;

        var (baselineCount, baseline) = RunRootSet(graph, seed);
        var (fattenedCount, fattened) = RunRootSet(InjectUnmappedMembers(graph), seed);
        Assert.True(baselineCount == 1 && fattenedCount == 2,
            $"the I11/MR-2 pin expects source sets of 1 (baseline) and 2 (fattened), measured "
            + $"{baselineCount} and {fattenedCount} — re-measure before moving the pin.");
        Assert.NotEqual(baseline, fattened, StringComparer.Ordinal);

        // And MR-2's own normalization — the very method the relation calls, so this is its tripwire —
        // removes it, while leaving class-element sets alone (the second assertion: the MR-3 pin, whose
        // element node IS a class, keeps its HashSet under MR-2's narrower predicate).
        var normalized = NormalizeForInjection(graph);
        Assert.Equal(
            Outcome(normalized, seed),
            Outcome(InjectUnmappedMembers(normalized), seed),
            StringComparer.Ordinal);
        Assert.Contains(CollShape.HashSet,
            NormalizeForInjection(PinnedCorpus.SetOfStructuralElements.Graph)
                .Nodes.SelectMany(n => n.Members).Select(m => m.Coll));
    }

    /// <summary>
    ///     Compiles, populates and maps one graph, returning the ROOT set member's source-side cardinality
    ///     alongside the usual outcome string — the seam the two I11 executors need and the relations do
    ///     not (a relation compares outcomes; these two must show WHERE the difference is born).
    /// </summary>
    private static (int SourceCount, string Outcome) RunRootSet(GraphSpec graph, int seed)
    {
        var units = TypeGraphRenderer.Render(graph);
        var (result, assembly) = CompilerTestHarness.RunAndEmit(units);
        Assert.False(result.RefusedLoudly, "the I11 pins are ACCEPTED shapes: " + graph.Describe());
        Assert.True(result.CompilationErrors.Length == 0,
            "the I11 pins compile clean: ["
            + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct()) + "]");
        Assert.NotNull(assembly);

        var sourceType = assembly.GetType("T." + graph.RootSource)
                         ?? throw new InvalidOperationException($"no type T.{graph.RootSource}");
        var source = ReflectionOracle.Populate(sourceType, seed);
        var set = sourceType.GetProperty("M0_1")?.GetValue(source)
                  ?? throw new InvalidOperationException("the I11 pins declare their set member as M0_1");
        var count = ((System.Collections.IEnumerable)set).Cast<object?>().Count();

        return (count, ResultFingerprint.Compute(CompilerTestHarness.InvokeMap(assembly, source)));
    }

    // ─── the shared outcome seam ─────────────────────────────────────────────────

    /// <summary>
    ///     Renders, runs both generators, and reduces one graph to a comparable outcome string:
    ///     <c>REFUSED:</c> + sorted error ids, <c>THREW:</c> + exception type and message, or the
    ///     order-independent <see cref="ResultFingerprint" /> of the executed map. Silent CS errors are
    ///     never an outcome — they red immediately (K1 leg-1 shape), so no relation can hold vacuously
    ///     over two identical miscompilations.
    /// </summary>
    private static string Outcome(GraphSpec graph, int seed)
    {
        var units = TypeGraphRenderer.Render(graph);

        // MR-2's premise, pinned EXECUTABLY (the audit: "pin the base config — no explicit-only/strict
        // modes — or additions legitimately refuse"): the rendered mapper carries the bare attribute and
        // nothing else, so the mapping runs under documented defaults. If the renderer ever grows an
        // options argument, this trips before any relation can mis-blame the product.
        Assert.Contains("[DwarfMapper]", units[0], StringComparison.Ordinal);
        Assert.DoesNotContain("[DwarfMapper(", units[0], StringComparison.Ordinal);

        var (result, assembly) = CompilerTestHarness.RunAndEmit(units);
        if (result.RefusedLoudly)
        {
            return "REFUSED:" + string.Join(",",
                result.GeneratorDiagnostics
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));
        }

        Assert.True(result.CompilationErrors.Length == 0,
            "seed-replayable silent miscompilation inside a metamorphic variant: the generators were "
            + "silent but the output has ["
            + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct())
            + "]\n--- graph ---\n" + graph.Describe()
            + "\n--- units ---\n" + string.Join("\n--- next unit ---\n", units)
            + "\n--- generated ---\n" + result.GeneratedSource);
        Assert.NotNull(assembly);

        var sourceType = assembly.GetType("T." + graph.RootSource)
                         ?? throw new InvalidOperationException(
                             $"no type T.{graph.RootSource} in emitted assembly");
        var source = ReflectionOracle.Populate(sourceType, seed);
        object? mapped;
        try
        {
            mapped = CompilerTestHarness.InvokeMap(assembly, source);
        }
        catch (InvalidOperationException e)
        {
            // Throw PARITY is part of every relation (a kind-dependent throw is the I7 genre, and every
            // deliberate product throw in that family is InvalidOperationException); the ABSOLUTE
            // question "should this map throw at all?" belongs to K1's oracle leg. Any OTHER exception
            // type (NullReferenceException and friends) is not a documented product behaviour and
            // propagates as a loud red instead of becoming an outcome two variants could share. Only
            // the map invocation is inside the try — a fingerprint failure (H7 depth) stays loud too.
            return "THREW:" + e.GetType().Name + ":" + e.Message;
        }

        return ResultFingerprint.Compute(mapped);
    }

    private static void Count(string outcome, ref int executed, ref int refused)
    {
        if (outcome.StartsWith("REFUSED:", StringComparison.Ordinal))
            Interlocked.Increment(ref refused);
        else
            Interlocked.Increment(ref executed);
    }

    /// <summary>
    ///     Vacuity guard, K1's shape: a relation only has teeth when maps EXECUTE. A loose floor, not an
    ///     exact pin — the accept/refuse split of a random sample is a nondeterministic oracle and R4
    ///     forbids gating on one; the measured split is reported instead.
    /// </summary>
    private void AssertNotVacuous(int executed, int refused, string relation)
    {
        Assert.True(executed > 0,
            $"{relation}: no sampled graph executed ({refused} refusals, 0 executions) — the sampled "
            + "space has drifted into the refusal grammar and the relation is vacuous.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{relation}: sampled {executed + refused} pairs/sets: {executed} executed, {refused} refused"));
    }
}
