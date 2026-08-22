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
    ///     STRUCT needing an element map. The generator is silent and emits
    ///     <c>__DwarfMap_Obj_S_D(__item)</c> with <c>__item</c> of type <c>S?</c> — CS1503 in code the
    ///     consumer cannot edit. Measured: every CollShape wrapper diverges (List/Array/IReadOnlyList/
    ///     HashSet/Dictionary); class elements, non-nullable struct elements and non-collection nullable
    ///     struct members are all fine; the trigger is the SOURCE side (struct source × class dest still
    ///     diverges, class source × struct dest does not). Expected-divergent until I5 is fixed; the
    ///     matching sampled-space exclusion lives in TypeGraphGen and dies with this pin.
    /// </summary>
    public static CorpusRow NullableStructElementMap { get; } = new(
        "I5-nullable-struct-element-map",
        "List<S?> -> List<D?> with struct S needing an element map: silent CS1503 (TASKS.md I5)",
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
            "S0", "D0"),
        KnownSilentCsIds: ["CS1503"]);

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

    /// <summary>Every pinned row, for the corpus test's sweep. Grows append-mostly; never shrinks to pass.</summary>
    public static IReadOnlyList<CorpusRow> Rows { get; } =
    [
        PartialFileSplitStructPair,
        RepresentationMirrorClassToStruct,
        NullableStructElementMap,
        UnmappedDestinationMemberRefusal,
        NullableRekindValueToReference,
        NullableRekindReferenceToValue
    ];
}
