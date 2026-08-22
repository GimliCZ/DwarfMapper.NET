// SPDX-License-Identifier: GPL-2.0-only

using CsCheck;

namespace DwarfMapper.CompilerTests.TypeGraphs;

/// <summary>
///     CsCheck generators over <see cref="GraphSpec" />. Seed replay and shrinking come from CsCheck itself.
///     <para>
///         The validity rules are implemented as CONSTRUCTION invariants — the sampled space contains only
///         specs that satisfy them — and <see cref="GraphSpec.Validate" /> runs as a post-condition on every
///         sample, so a generator bug that breaks an invariant is a loud red here, not invalid-input noise
///         in a downstream test. See the rule-by-rule comments in <see cref="GraphSpec.Validate" /> for the
///         false-positive class each rule removes.
///     </para>
/// </summary>
public static class TypeGraphGen
{
    /// <summary>
    ///     The scalar vocabulary. Primitives plus two struct BCL types, mirroring the RFC's list; enums are
    ///     a DECLARED gap (see <c>EnumCoverageRatchetTests</c>), not a silent one.
    /// </summary>
    private static readonly string[] Scalars =
    [
        "int", "long", "string", "decimal", "bool", "System.Guid", "System.DateTimeOffset", "byte", "double"
    ];

    private static readonly TypeKind[] AllKinds = Enum.GetValues<TypeKind>();
    private static readonly MemberShape[] AllShapes = Enum.GetValues<MemberShape>();
    private static readonly CollShape[] AllColls = Enum.GetValues<CollShape>();

    // Built from the proven house primitives (Gen.Int[a,b] + Select); CsCheck 4.7.0 has no Gen.Enum, and
    // the RFC's Gen.Enum<T>() sketch was never compiled.
    private static Gen<T> Pick<T>(IReadOnlyList<T> values)
    {
        return Gen.Int[0, values.Count - 1].Select(i => values[i]);
    }

    /// <summary>The structural roll for one member: everything except its name and nested wiring.</summary>
    private sealed record MemberRoll(string Scalar, bool Nullable, MemberShape Shape, CollShape Coll, int NestedPct);

    private static readonly Gen<MemberRoll> MemberGen =
        Gen.Select(Pick(Scalars), Gen.Bool, Pick(AllShapes), Pick(AllColls), Gen.Int[0, 99],
            (scalar, nullable, shape, coll, nestedPct) => new MemberRoll(scalar, nullable, shape, coll, nestedPct));

    /// <summary>One node's structural roll: member rolls plus the kind/base/nested-target randomness.</summary>
    private sealed record NodeRoll(
        MemberRoll[] Members,
        TypeKind SourceKind,
        TypeKind DestKind,
        int BasePct,
        int BaseTargetRoll,
        int[] NestedTargetRolls);

    private static Gen<NodeRoll> NodeGen(int maxMembers)
    {
        return Gen.Int[1, maxMembers].SelectMany(memberCount =>
            Gen.Select(
                MemberGen.Array[memberCount],
                Pick(AllKinds),
                Pick(AllKinds),
                Gen.Int[0, 99],
                Gen.Int[0, 999],
                Gen.Int[0, 999].Array[memberCount],
                (members, sourceKind, destKind, basePct, baseTarget, nestedTargets) =>
                    new NodeRoll(members, sourceKind, destKind, basePct, baseTarget, nestedTargets)));
    }

    /// <summary>
    ///     Mirrored pair: the dest subgraph is the source subgraph with kinds AND member shapes
    ///     independently re-rolled — the exact cell family the surface-matrix bugs (A11-F1/F2:
    ///     <c>[MapTo]</c>×struct, null-guarded value type, CS0037) came from, generalized. Members mirror by
    ///     NAME (name-keyed, per the audit's correction), so the same graph reordered stays the same mapping.
    /// </summary>
    public static Gen<GraphSpec> MirroredPair(int maxNodes = 4, int maxMembers = 5)
    {
        return Gen.Int[1, maxNodes]
            .SelectMany(n => Gen.Select(
                NodeGen(maxMembers).Array[n],
                Gen.Int[0, 999].Array[n], // dest-side member-shape re-roll seed, one per node
                (rolls, destRolls) => Assemble(rolls, destRolls)));
    }

    private static GraphSpec Assemble(NodeRoll[] rolls, int[] destRolls)
    {
        var n = rolls.Length;

        // Structural edges first, kinds second: BaseRef endpoints constrain the kind roll, so the wiring
        // must exist before kinds are finalized. All loops are bounded by n or the member count (H7).
        var baseRef = new int?[n];
        var isBaseTarget = new bool[n];
        for (var i = 0; i < n - 1; i++)
        {
            // ~15 % of non-terminal nodes inherit; the target is a strictly later node (rule V5's order).
            if (rolls[i].BasePct < 15)
            {
                var b = i + 1 + rolls[i].BaseTargetRoll % (n - 1 - i);
                baseRef[i] = b;
                isBaseTarget[b] = true;
            }
        }

        // Kinds per side. A node that participates in inheritance (either end) must be a reference kind,
        // and derived must match base (rule V5). Deriveds are processed DESCENDING so the base's kind is
        // final before any of its deriveds copy it — bases always sit at higher indexes than deriveds.
        var sourceKinds = FinalizeKinds(rolls.Select(r => r.SourceKind).ToArray(), baseRef);
        var destKinds = FinalizeKinds(rolls.Select(r => r.DestKind).ToArray(), baseRef);

        var sourceNodes = new NodeSpec[n];
        var destNodes = new NodeSpec[n];
        for (var i = 0; i < n; i++)
        {
            var members = new MemberSpec[rolls[i].Members.Length];
            var destMembers = new MemberSpec[rolls[i].Members.Length];
            for (var k = 0; k < members.Length; k++)
            {
                var roll = rolls[i].Members[k];

                // ~25 % of members on non-terminal nodes are nested edges to a strictly later node
                // (rule V2's DAG order — the graph-walk termination variant).
                int? nested = i < n - 1 && roll.NestedPct < 25
                    ? i + 1 + rolls[i].NestedTargetRolls[k] % (n - 1 - i)
                    : null;

                // (The I5 exclusion that used to sit here — collection-wrapped nullable elements over a
                // VALUE-kind source element node, held out of the sampled space because the synthesized
                // element maps did not lift over Nullable<T> — was DELETED with the fix in round 23 N1.
                // The sampled space is wider by exactly that cell family again; the shapes stay pinned as
                // PinnedCorpus rows 'I5-nullable-struct-element-map' and
                // 'I5-nullable-struct-element-rekinded-dest', now under the normal must-compile contract.)
                var nullable = roll.Nullable;

                // (The I7 exclusion that used to sit here — plain nullable nested members whose source node
                // is a VALUE kind and whose mirrored dest node re-kinded to a REFERENCE kind, held out
                // because the map THREW on a null instead of lifting it — was DELETED with the fix in
                // round 23 N2. All six kind-pairs lift now, pinned cell by cell in
                // DifferentialOracleTests.I7_null_across_a_rekinded_pair_lifts_for_every_kind_pair. The
                // reverse genre stays unreachable by SAMPLING for a reason that has nothing to do with the
                // product — the oracle's population never nulls reference members, a declared bias in the
                // ReflectionOracle header — which is why its pin is a deterministic executor.)

                // Names are unique per node index ("M{i}_{k}"), which makes rule V4 hold by construction
                // AND keeps inherited members from shadowing derived ones (CS0108 would be warning-only
                // noise the errors-only compile leg cannot see — removed structurally instead).
                var name = FormattableString.Invariant($"M{i}_{k}");

                // Rule V5b structurally: a base target keeps its implicit parameterless ctor, so CtorParam
                // shapes on it are remapped to AutoProp (on BOTH sides — the dest mirror shares baseRef).
                var shape = isBaseTarget[i] && roll.Shape == MemberShape.CtorParam
                    ? MemberShape.AutoProp
                    : roll.Shape;
                members[k] = new MemberSpec(name, roll.Scalar, nullable, shape, roll.Coll, nested);

                // The dest member mirrors name, scalar, nullability, collection and nested wiring (offset
                // into the dest subgraph) but RE-ROLLS the shape — representation variance on the member
                // axis as well as the node axis. The per-node seed is mixed with the member index by a
                // constant coprime to the shape count, so one seed still varies every member's shape.
                var destShape = AllShapes[(destRolls[i] + k * 7) % AllShapes.Length];
                if (isBaseTarget[i] && destShape == MemberShape.CtorParam) destShape = MemberShape.AutoProp;
                destMembers[k] = members[k] with { Shape = destShape, NestedRef = nested + n };
            }

            sourceNodes[i] = new NodeSpec(
                FormattableString.Invariant($"S{i}"), sourceKinds[i], members,
                baseRef[i]);
            destNodes[i] = new NodeSpec(
                FormattableString.Invariant($"D{i}"), destKinds[i], destMembers,
                baseRef[i] is int b ? b + n : null);
        }

        var spec = new GraphSpec([.. sourceNodes, .. destNodes], "S0", "D0");
        spec.Validate(); // post-condition: the invariants hold ON the sampled space, not just by intent
        return spec;
    }

    /// <summary>
    ///     Applies rule V5's kind constraints to a raw kind roll: every inheritance participant becomes a
    ///     reference kind (struct-kinds remap value→reference preserving record-ness), and each derived
    ///     copies its base's final kind. Descending order terminates because BaseRef always points to a
    ///     strictly higher index (H7: the loop variable strictly decreases and each step reads only
    ///     already-final entries).
    /// </summary>
    private static TypeKind[] FinalizeKinds(TypeKind[] kinds, int?[] baseRef)
    {
        var participates = new bool[kinds.Length];
        for (var i = 0; i < kinds.Length; i++)
        {
            if (baseRef[i] is int b)
            {
                participates[i] = true;
                participates[b] = true;
            }
        }

        for (var i = kinds.Length - 1; i >= 0; i--)
        {
            if (participates[i])
            {
                kinds[i] = kinds[i] switch
                {
                    TypeKind.Struct => TypeKind.Class,
                    TypeKind.RecordStruct => TypeKind.Record,
                    _ => kinds[i]
                };
            }

            if (baseRef[i] is int bb) kinds[i] = kinds[bb];
        }

        return kinds;
    }
}
