// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.CompilerTests.TypeGraphs;

namespace DwarfMapper.CompilerTests;

/// <summary>One pinned corpus row: a named <see cref="GraphSpec" /> with the reason it is pinned.</summary>
/// <param name="Id">Stable row id, referenced from issue records and cross-project comments.</param>
/// <param name="Reason">Why this exact shape is pinned — the finding or the cross-reference it serves.</param>
/// <param name="Graph">The spec, valid by <see cref="GraphSpec.Validate" />.</param>
/// <param name="KnownSilentCsIds">
///     Null for the normal contract (compile clean or refuse loudly). Non-null pins a KNOWN, filed
///     divergence: the generators are silent and the output has EXACTLY these CS error ids — the
///     DeclaredDivergences discipline (shrink-only, keyed to an issue id in the Reason). The pin goes red
///     when the product is fixed, which is the signal to flip the row to the normal contract and delete
///     any matching sampled-space exclusion in the same commit. Never a blanket skip.
/// </param>
/// <param name="ExpectedRefusalIds">
///     Null unless this row pins a REFUSAL contract (K1 leg 1): the generators must refuse LOUDLY with
///     exactly these error-severity DWARF ids. Such a row is the deterministic proof that the harness's
///     <c>RefusedLoudly</c> branch executes at all — K0 shipped with that branch never taken (0 refusals
///     across all sampling). Mutually exclusive with <paramref name="KnownSilentCsIds" /> (a run cannot be
///     both silent and loud); the corpus sweep enforces the exclusivity.
/// </param>
public sealed record CorpusRow(
    string Id,
    string Reason,
    GraphSpec Graph,
    IReadOnlyList<string>? KnownSilentCsIds = null,
    IReadOnlyList<string>? ExpectedRefusalIds = null);

/// <summary>
///     The pinned corpus: shapes that earned a permanent row — a shrunk fuzz finding, or a shape another
///     test file explicitly forwards here. This is the shrink-target of the K0 smoke and K1 legs ("every
///     disagreement either shrinks to a pinned corpus row or documents an undocumented semantic"): a row is
///     append-mostly evidence, never deleted to make a gate pass.
/// </summary>
public static class PinnedCorpus
{
    /// <summary>
    ///     The P5→K0 forward reference, honoured: BlittableProofCoverageTests'
    ///     <c>CanReinterpret_partial_file_struct_verdict_is_file_order_independent</c> built this shape by
    ///     hand as a K0 corpus row in waiting — the same struct pair split across two files, compiled in
    ///     BOTH file orders, same verdict. Here it is expressed in the descriptor (the split axis exists for
    ///     this row) and asserted at K0's level: both file orders must produce the same accept/refuse
    ///     outcome, no silent CS errors, and BYTE-IDENTICAL generated source — the file-order-independence
    ///     claim the P5 mutants attacked, measured end-to-end rather than at the BlittableProof seam.
    /// </summary>
    public static CorpusRow PartialFileSplitStructPair { get; } = new(
        "P5-K0-partial-split-struct-pair",
        "P5's partial-file determinism kill, lifted into the corpus per its own forward-reference comment",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Struct,
                    [
                        new MemberSpec("M0_0", "int", Nullable: false, MemberShape.Field, CollShape.None, null),
                        new MemberSpec("M0_1", "int", Nullable: false, MemberShape.Field, CollShape.None, null)
                    ],
                    BaseRef: null, SplitAcrossFiles: true),
                new NodeSpec("D0", TypeKind.Struct,
                    [
                        new MemberSpec("M0_0", "int", Nullable: false, MemberShape.Field, CollShape.None, null),
                        new MemberSpec("M0_1", "int", Nullable: false, MemberShape.Field, CollShape.None, null)
                    ],
                    BaseRef: null)
            ],
            "S0", "D0"));

    /// <summary>
    ///     The founding bug family of the whole arc, pinned as its smallest member: a class source whose
    ///     mirrored dest is a STRUCT. A11-F1 shipped exactly this as <c>[MapTo]</c>×struct — a null guard
    ///     emitted for a value type, CS0037, generator silent — and the RFC's sharpest evidence claim is
    ///     that the must-compile leg would have caught it on the first struct-kind roll. This row IS that
    ///     first roll, deterministic.
    /// </summary>
    public static CorpusRow RepresentationMirrorClassToStruct { get; } = new(
        "A11-representation-mirror",
        "class source, struct dest — the A11-F1 CS0037 family's minimal shape, deterministic",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [
                        new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null),
                        new MemberSpec("M0_1", "string", Nullable: false, MemberShape.AutoProp, CollShape.None, null)
                    ],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Struct,
                    [
                        new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null),
                        new MemberSpec("M0_1", "string", Nullable: false, MemberShape.AutoProp, CollShape.None, null)
                    ],
                    BaseRef: null)
            ],
            "S0", "D0"));

    /// <summary>
    ///     The first smoke sample's REAL FINDING (2026-08-22, CsCheck-shrunk, then minimized by probe —
    ///     filed as TASKS.md I5): a collection-wrapped nullable element whose source element type is a user
    ///     STRUCT needing an element map. The generator was silent and emitted
    ///     <c>__DwarfMap_Obj_S_D(__item)</c> with <c>__item</c> of type <c>S?</c> — CS1503 in code the
    ///     consumer cannot edit, measured for every CollShape wrapper (List/Array/IReadOnlyList/HashSet/
    ///     Dictionary).
    ///     <para>
    ///     <b>FIXED 2026-08-23 (round 23 N1), and the row stays</b> — it has flipped from pinning the
    ///     divergence (<c>KnownSilentCsIds: ["CS1503"]</c>) to pinning the CORRECT behaviour under the
    ///     normal must-compile contract, which is what the K0/K1 sweep now enforces on it. Its runtime
    ///     counterpart — a null element in yields a null element out, across every wrapper and both
    ///     destination element kinds — is
    ///     <c>DifferentialOracleTests.I5_nullable_struct_element_lifts_null_to_null…</c>. The matching
    ///     sampled-space exclusion in <c>TypeGraphGen</c> died in the fixing commit, so the shape is now
    ///     reachable by sampling as well as by this pin.
    ///     </para>
    /// </summary>
    public static CorpusRow NullableStructElementMap { get; } = new(
        "I5-nullable-struct-element-map",
        "List<S?> -> List<D?> with struct S needing an element map: lifts null -> null (TASKS.md I5, fixed)",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.List, 1)],
                    BaseRef: null),
                new NodeSpec("S1", TypeKind.Struct,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.List, 3)],
                    BaseRef: null),
                new NodeSpec("D1", TypeKind.Struct,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0"));

    /// <summary>
    ///     I5's RE-KINDED half, pinned separately because it is the cell the CS1503 pin above could not
    ///     see: the same nullable struct element, but the mirrored destination element is a CLASS. Before
    ///     the fix this cell resolved through the nullable-source branch instead — <c>?? throw</c> composed
    ///     with the element map — so it would have started COMPILING and started THROWING the moment the
    ///     element loop learned to honour its null handling. Pinning it keeps the two halves of the same
    ///     fix from drifting apart: both must lift, and the destination element (<c>D1?</c>, a nullable
    ///     reference) can hold the null in this one exactly as <c>Nullable&lt;D1&gt;</c> can in the other.
    /// </summary>
    public static CorpusRow NullableStructElementRekindedDest { get; } = new(
        "I5-nullable-struct-element-rekinded-dest",
        "List<S?> -> List<D?> with struct S and CLASS D: lifts null -> null (TASKS.md I5, fixed)",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.List, 1)],
                    BaseRef: null),
                new NodeSpec("S1", TypeKind.Struct,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.List, 3)],
                    BaseRef: null),
                new NodeSpec("D1", TypeKind.Class,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0"));

    /// <summary>
    ///     K1's mandated refusal pin: a destination member with NO source counterpart hits the product's
    ///     headline completeness rule — <c>DWARF001</c> "Destination member is not mapped", documented in
    ///     <c>docs/diagnostics.md#dwarf001</c> as error severity and enforced by construction (the method
    ///     body is not generated). This is the deterministic proof that the <c>RefusedLoudly</c> branch of
    ///     the must-compile-or-refuse contract actually executes — K0 disclosed it never had (0 refusals in
    ///     all sampling, mirrored pairs are complete by construction). Note the row asserts the GENERATOR
    ///     ids only: an unimplemented partial method necessarily leaves CS errors (CS8795 family) in the
    ///     output, which is exactly why a refusal returns before the silent-miscompilation check.
    /// </summary>
    public static CorpusRow UnmappedDestinationMemberRefusal { get; } = new(
        "K1-refusal-unmapped-dest-member",
        "leg-1 refusal contract: dest member without source counterpart must refuse loudly with DWARF001",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [
                        new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null),
                        new MemberSpec("MX_0", "string", Nullable: false, MemberShape.AutoProp, CollShape.None, null)
                    ],
                    BaseRef: null)
            ],
            "S0", "D0"),
        ExpectedRefusalIds: ["DWARF001"]);

    /// <summary>
    ///     K1's FIRST-RUN FINDING (2026-08-22, CsCheck seed 0vihQF5Vee7b at 1,000 samples, then minimized
    ///     by a 6-cell kind-pair probe — filed as TASKS.md I7): a PLAIN nullable nested member across a
    ///     re-kinded pair, value-kind source × reference-kind dest. Compiles clean, but the emitted map
    ///     unwraps with <c>?? throw</c> ("Source member 'M0_0' was null") instead of lifting null → null,
    ///     although the destination member (<c>D1?</c>) is nullable-capable and the lossless emission
    ///     exists next door (the NullableProject ternary used by the same-kind diagonal, which the probe
    ///     measured lifting correctly: Struct→Struct and Class→Class both propagate null). Undocumented —
    ///     the <c>NullStrategy</c> doc row covers "nullable-value source → NON-nullable target" only. The
    ///     compile contract here is the NORMAL one; the runtime divergence itself is pinned red-on-fix in
    ///     <c>DifferentialOracleTests.I7_pinned_runtime_divergence…</c>, and the matching sampled-space
    ///     exclusion in TypeGraphGen is keyed to these rows and dies with them.
    /// </summary>
    public static CorpusRow NullableRekindValueToReference { get; } = new(
        "I7-nullable-rekind-value-to-reference",
        "S1? plain member, struct S1 -> class D1: runtime throw on null instead of null->null (TASKS.md I7)",
        PlainNullableRekindPair(TypeKind.Struct, TypeKind.Class));

    /// <summary>
    ///     I7's reverse genre, same filing: reference-kind source × value-kind dest throws
    ///     "Cannot map a null 'global::T.S1' to value-type 'global::T.D1'." although the destination
    ///     member is <c>Nullable&lt;D1&gt;</c> and could hold the null. Unreachable in sampling only
    ///     because the oracle population never nulls reference members (a DECLARED bias) — pinned here so
    ///     the genre has a deterministic executor anyway.
    /// </summary>
    public static CorpusRow NullableRekindReferenceToValue { get; } = new(
        "I7-nullable-rekind-reference-to-value",
        "S1? plain member, class S1 -> struct D1: runtime throw on null instead of null->null (TASKS.md I7)",
        PlainNullableRekindPair(TypeKind.Class, TypeKind.Struct));

    private static GraphSpec PlainNullableRekindPair(TypeKind sourceElement, TypeKind destElement)
    {
        return new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.None, 1)],
                    BaseRef: null),
                new NodeSpec("S1", sourceElement,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: true, MemberShape.AutoProp, CollShape.None, 3)],
                    BaseRef: null),
                new NodeSpec("D1", destElement,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0");
    }

    /// <summary>
    ///     K2's oracle-gap counterexample, preserved (2026-08-22, TASKS.md I11 — found by MR-3 at CsCheck
    ///     seed <c>9EqkJlF93ol7</c> back when the fast tier still drew randomly, then minimized by hand;
    ///     the seed string itself is dead now that <c>PinnedSampling</c> owns the case set, which is
    ///     exactly why the SHAPE is pinned instead of the seed). A <c>HashSet</c> member whose element type
    ///     is a graph node, populated at seed <see cref="SetOfStructuralElementsSeed" /> with two elements
    ///     whose only member (<c>long?</c>) draws null on both — so the two elements are structurally
    ///     equal and referentially distinct.
    ///     <para>
    ///         <b>This row pins CORRECT behaviour, not a defect.</b> Under the class representation the
    ///         source set holds 2 and the mapped set holds 2; under record and record struct both hold 1,
    ///         because record and struct equality is structural. Nothing in the mapper chooses this — the
    ///         collapse happens in the SOURCE, before any mapping. It is pinned because it is the evidence
    ///         behind <c>MetamorphicTests.ListifySetsOfStructuralElements</c>: delete the exclusion and
    ///         this shape is what comes back. Its executor is
    ///         <c>MetamorphicTests.Set_shaped_members_are_representation_dependent_by_design</c>; the
    ///         compile contract here is the NORMAL one (clean or loud), because there is nothing wrong
    ///         with the code.
    ///     </para>
    /// </summary>
    public static CorpusRow SetOfStructuralElements { get; } = new(
        "I11-set-of-structural-elements",
        "HashSet<S1> with two structurally-equal elements: cardinality is representation-dependent BY "
        + "DESIGN (record equality is structural) — MR-3's declared precondition (TASKS.md I11)",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_1", "int", Nullable: false, MemberShape.AutoProp, CollShape.HashSet, 1)],
                    BaseRef: null),
                new NodeSpec("S1", TypeKind.Class,
                    [new MemberSpec("M1_0", "long", Nullable: true, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_1", "int", Nullable: false, MemberShape.AutoProp, CollShape.HashSet, 3)],
                    BaseRef: null),
                new NodeSpec("D1", TypeKind.Class,
                    [new MemberSpec("M1_0", "long", Nullable: true, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0"));

    /// <summary>
    ///     The population seed <see cref="SetOfStructuralElements" /> is pinned at — the one the original
    ///     MR-3 red drew. Name-keyed population makes it portable: member <c>M0_1</c> keys the set to
    ///     count 2 and both elements' <c>M1_0</c> to null regardless of what else the node declares, which
    ///     is why the hand-minimized shape reproduces the sampled one exactly.
    /// </summary>
    public const int SetOfStructuralElementsSeed = 269828994;

    /// <summary>
    ///     I11's second route to the same precondition, pinned separately because it reaches it WITHOUT
    ///     re-kinding: MR-2's injected unmapped member. The source element node is a RECORD (structural
    ///     equality) and the destination element node a CLASS (referential), so the baseline maps a
    ///     one-element set while the fattened variant — whose injected <c>X1</c> differs per element and
    ///     therefore splits the tie — maps a two-element one. Again both are correct; the relation's
    ///     premise is what fails, which is why MR-2 normalizes structural-element sets too. Executor:
    ///     <c>MetamorphicTests.Set_shaped_members_are_unmapped_member_dependent_by_design</c>.
    /// </summary>
    public static CorpusRow SetOfStructuralElementsRecordToClass { get; } = new(
        "I11-set-of-structural-elements-mr2",
        "HashSet<S1> with record source element and class dest element: an injected unmapped member "
        + "splits a structural tie and changes set cardinality — MR-2's declared precondition (I11)",
        new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_1", "int", Nullable: false, MemberShape.AutoProp, CollShape.HashSet, 1)],
                    BaseRef: null),
                new NodeSpec("S1", TypeKind.Record,
                    [new MemberSpec("M1_0", "long", Nullable: true, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_1", "int", Nullable: false, MemberShape.AutoProp, CollShape.HashSet, 3)],
                    BaseRef: null),
                new NodeSpec("D1", TypeKind.Class,
                    [new MemberSpec("M1_0", "long", Nullable: true, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0"));

    /// <summary>Every pinned row, for the corpus test's sweep. Grows append-mostly; never shrinks to pass.</summary>
    public static IReadOnlyList<CorpusRow> Rows { get; } =
    [
        PartialFileSplitStructPair,
        RepresentationMirrorClassToStruct,
        NullableStructElementMap,
        NullableStructElementRekindedDest,
        UnmappedDestinationMemberRefusal,
        NullableRekindValueToReference,
        NullableRekindReferenceToValue,
        SetOfStructuralElements,
        SetOfStructuralElementsRecordToClass
    ];
}
