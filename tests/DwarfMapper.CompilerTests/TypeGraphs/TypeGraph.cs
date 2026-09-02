// SPDX-License-Identifier: GPL-2.0-only

// enum members deliberately carry the BCL interface names they classify
// ReSharper disable InconsistentNaming
namespace DwarfMapper.CompilerTests.TypeGraphs
{
    /// <summary>The declaration kind of a graph node. Every kind the mapper can declare a pair over.</summary>
    public enum TypeKind
    {
        Class,
        Record,
        Struct,
        RecordStruct
    }

    /// <summary>
    ///     How a member is declared. ONE value per member by design: <see cref="Required" /> and
    ///     <see cref="CtorParam" /> are mutually exclusive values of the same enum, so the audit's
    ///     "forbid required×CtorParam on the same member" rule is discharged by construction — a member cannot
    ///     carry both, and no <see cref="GraphSpec.Validate" /> pass has to police what the type system already
    ///     forbids. (The false-positive class removed: the CS9035-family interplay of a <c>required</c> member
    ///     that is simultaneously constructor-supplied, which is a property of the DESCRIPTOR being ill-formed,
    ///     not of the mapper under test.)
    /// </summary>
    public enum MemberShape
    {
        /// <summary>Read-write auto-property: <c>public T N { get; set; }</c>.</summary>
        AutoProp,

        /// <summary>Init-only auto-property: <c>public T N { get; init; }</c>.</summary>
        InitOnly,

        /// <summary>Required read-write auto-property: <c>public required T N { get; set; }</c>.</summary>
        Required,

        /// <summary>Get-only auto-property assigned from a constructor parameter: <c>public T N { get; }</c>.</summary>
        CtorParam,

        /// <summary>Public field: <c>public T N;</c>.</summary>
        Field
    }

    /// <summary>
    ///     The collection wrapper around a member's element type. <see cref="None" /> means the member IS its
    ///     element type. The set is deliberately smaller than the generator's own collection taxonomy — the gap
    ///     is a DECLARED, exactly pinned blind-spot population in <c>EnumCoverageRatchetTests</c>, never a
    ///     silent one (the YARPGen "generator bias caps yield" lesson as a test).
    /// </summary>
    public enum CollShape
    {
        None,
        Array,
        List,
        IReadOnlyList,
        Dictionary,
        HashSet
    }

    /// <summary>
    ///     One member of a node. Pure data — transforms and CsCheck shrinking stay trivial.
    /// </summary>
    /// <param name="Name">Member name, unique per node (see <see cref="GraphSpec.Validate" /> rule V4).</param>
    /// <param name="ScalarType">The element's C# type when <paramref name="NestedRef" /> is null.</param>
    /// <param name="Nullable">Whether the ELEMENT type carries a <c>?</c> annotation.</param>
    /// <param name="Shape">How the member is declared on its node.</param>
    /// <param name="Coll">The collection wrapper, or <see cref="CollShape.None" />.</param>
    /// <param name="NestedRef">
    ///     Index into <see cref="GraphSpec.Nodes" /> of the node this member's element type is, or null for a
    ///     scalar member. An index rather than a name so the acyclicity rules are checked on integers instead of
    ///     resolved through a name table.
    /// </param>
    public sealed record MemberSpec(
        string Name,
        string ScalarType,
        bool Nullable,
        MemberShape Shape,
        CollShape Coll,
        int? NestedRef);

    /// <summary>One type declaration in the graph.</summary>
    /// <param name="Name">The type name as rendered.</param>
    /// <param name="Kind">Declaration kind.</param>
    /// <param name="Members">The node's own (non-inherited) members.</param>
    /// <param name="BaseRef">
    ///     Index into <see cref="GraphSpec.Nodes" /> of this node's base type, or null. Reference kinds only —
    ///     see <see cref="GraphSpec.Validate" /> rule V5.
    /// </param>
    /// <param name="SplitAcrossFiles">
    ///     When true the renderer declares this node <c>partial</c> and splits its members across TWO
    ///     compilation units. Exists so the P5 partial-file corpus row (the same struct pair split across two
    ///     files, compiled in both file orders, same verdict) is EXPRESSIBLE in this descriptor — see
    ///     <c>PinnedCorpus</c>. The CsCheck generators never set it; it is a pinned-corpus axis, not a sampled
    ///     one, until K1 decides otherwise.
    /// </param>
    public sealed record NodeSpec(
        string Name,
        TypeKind Kind,
        IReadOnlyList<MemberSpec> Members,
        int? BaseRef,
        bool SplitAcrossFiles = false);

    /// <summary>
    ///     A whole generated compilation: the type graph plus the root pair the mapper is declared over.
    ///     <para>
    ///         The validity rules in <see cref="Validate" /> ARE the grammar (Csmith's core lesson: the
    ///         generator's whole value is that its output is VALID — otherwise findings drown in invalid-input
    ///         noise). Every rule below names the false-positive class it removes. The CsCheck generators build
    ///         specs that satisfy the rules by construction and call <see cref="Validate" /> as a post-condition,
    ///         so the invariants are proven ON the sampled space rather than merely intended.
    ///     </para>
    /// </summary>
    /// <param name="Nodes">All declared nodes, source subgraph first, then the mirrored dest subgraph.</param>
    /// <param name="RootSource">Name of the mapper's source parameter type.</param>
    /// <param name="RootDest">Name of the mapper's return type.</param>
    public sealed record GraphSpec(IReadOnlyList<NodeSpec> Nodes, string RootSource, string RootDest)
    {
        /// <summary>
        ///     Throws when any validity rule is violated. Called by the renderer before emitting (an invalid
        ///     spec is refused loudly, never rendered into known-invalid C#) and by the generators as a
        ///     post-condition on every sampled spec.
        /// </summary>
        public void Validate()
        {
            // V1 — the roots resolve. False-positive class removed: CS0246 (unknown type) in the mapper's own
            // partial-method signature, which would be a defect of the DESCRIPTOR, not of the generator.
            Demand(Nodes.Count > 0, "V1", "a graph must declare at least one node");
            Demand(Nodes.Any(n => n.Name == RootSource), "V1", $"RootSource '{RootSource}' names no node");
            Demand(Nodes.Any(n => n.Name == RootDest), "V1", $"RootDest '{RootDest}' names no node");

            // Node names must be unique or two declarations collide (CS0101) — a precondition of every
            // index-based rule below, stated before they run.
            Demand(Nodes.Select(n => n.Name).Distinct(StringComparer.Ordinal).Count() == Nodes.Count,
                "V1",
                "node names must be unique (CS0101: duplicate type declaration)");

            for (var i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];

                // V4 — per-node member-name dedupe (the audit's correction: PER NODE, not global). False-positive
                // class removed: CS0102 (duplicate member definition inside one type).
                Demand(node.Members.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() == node.Members.Count,
                    "V4",
                    $"node '{node.Name}' declares a duplicate member name (CS0102)");

                // V2 — NestedRef wiring is acyclic BY INDEX: node i may reference j > i only, so the whole
                // containment graph is a DAG. False-positive classes removed: circular containment (the CS0523
                // family when the chain is all-struct) and unbounded recursion in rendering/population — this
                // rule is also the H7 termination variant for every loop that walks the graph (each step
                // strictly increases the node index, which is bounded by Nodes.Count).
                foreach (var member in node.Members)
                {
                    if (member.NestedRef is not { } j)
                    {
                        continue;
                    }

                    Demand(j >= 0 && j < Nodes.Count,
                        "V2",
                        $"node '{node.Name}' member '{member.Name}' references node index {j}, out of range");
                    Demand(j > i,
                        "V2",
                        $"node '{node.Name}' (index {i}) member '{member.Name}' references index {j} — " + "NestedRef must reference a STRICTLY LATER node so the containment graph is a DAG");
                }

                // V5 — BaseRef is for reference kinds only, same-kind, strictly ordered. False-positive classes
                // removed: CS0527 (a struct/record struct cannot declare a base type), CS8865/CS8864 (records
                // and classes cannot cross-inherit), CS0146 (circular base — impossible under strict index
                // order, same argument as V2).
                if (node.BaseRef is { } b)
                {
                    Demand(node.Kind is TypeKind.Class or TypeKind.Record,
                        "V5",
                        $"node '{node.Name}' is a {node.Kind} and cannot have a base type (CS0527)");
                    Demand(b >= 0 && b < Nodes.Count,
                        "V5",
                        $"node '{node.Name}' BaseRef {b} is out of range");
                    Demand(b > i,
                        "V5",
                        $"node '{node.Name}' (index {i}) BaseRef {b} — the base must be a strictly later node " + "so inheritance is acyclic (CS0146)");
                    Demand(Nodes[b].Kind == node.Kind,
                        "V5",
                        $"node '{node.Name}' ({node.Kind}) inherits '{Nodes[b].Name}' ({Nodes[b].Kind}) — " + "classes inherit classes and records inherit records (CS8864/CS8865)");

                    // V5b — a base node must keep its implicit parameterless constructor. A CtorParam member
                    // makes the renderer emit a parameters-only ctor, and every derived declaration would then
                    // fail with CS1729/CS7036 ('base does not contain a constructor that takes 0 arguments') —
                    // a defect of the descriptor, not of the mapper. (Not in the audit's enumerated list; found
                    // by deriving the rules against the renderer, which is exactly what "the validity rules ARE
                    // the grammar" demands.)
                    Demand(Nodes[b].Members.All(m => m.Shape != MemberShape.CtorParam),
                        "V5b",
                        $"base node '{Nodes[b].Name}' has a CtorParam member — its explicit ctor would leave " + "no parameterless ctor for derived declarations to chain to (CS1729)");
                }
            }

            // V3 — NO value-type cycles through NestedRef, checked INDEPENDENTLY of V2 even though the strict
            // index order already forbids all cycles: the audit lists this as its own binding correction, and an
            // independent check survives a future relaxation of V2 (e.g. allowing class back-edges for cyclic
            // reference graphs) without silently re-admitting the one cycle class that is uncompilable REGARDLESS
            // of the mapper. False-positive class removed: CS0523 (struct member causes a cycle in the struct
            // layout). A containment edge is a CollShape.None NestedRef between two struct-kind nodes — NULLABLE
            // INCLUDED, because Nullable<T> contains T by value and 'struct A { A? X; }' is still CS0523; a
            // collection wrapper or a reference-kind node on the path breaks layout containment.
            DemandNoValueTypeCycle();

            // V6 — required×CtorParam: discharged by construction. MemberShape is a single-valued enum, so no
            // member can be both Required and CtorParam; see the MemberShape doc-comment for the false-positive
            // class. There is deliberately no runtime check here — a check that cannot fail is dead code, and
            // the enum's shape IS the enforcement.
        }

        private void DemandNoValueTypeCycle()
        {
            // Iterative DFS with an explicit colour array. Termination (H7): each node is pushed at most twice
            // (enter + exit marker); the stack length is bounded by 2 × Nodes.Count × max member count, and
            // every iteration pops one entry — a strictly decreasing variant.
            var colour = new int[Nodes.Count]; // 0 = white, 1 = on stack, 2 = done
            for (var start = 0; start < Nodes.Count; start++)
            {
                if (colour[start] != 0)
                {
                    continue;
                }

                var stack = new Stack<(int Node, bool Exit)>();
                stack.Push((start, false));
                while (stack.Count > 0)
                {
                    var (n, exit) = stack.Pop();
                    if (exit)
                    {
                        colour[n] = 2;
                        continue;
                    }

                    if (colour[n] == 2)
                    {
                        continue;
                    }

                    Demand(colour[n] != 1,
                        "V3",
                        $"value-type containment cycle through node '{Nodes[n].Name}' (CS0523)");
                    colour[n] = 1;
                    stack.Push((n, true));
                    foreach (var edge in ValueContainmentEdges(n)) stack.Push((edge, false));
                }
            }
        }

        private IEnumerable<int> ValueContainmentEdges(int i)
        {
            if (Nodes[i].Kind is not (TypeKind.Struct or TypeKind.RecordStruct))
            {
                yield break;
            }

            foreach (var member in Nodes[i].Members)
                if (member is { Coll: CollShape.None, NestedRef: { } j } && j >= 0 && j < Nodes.Count && Nodes[j].Kind is TypeKind.Struct or TypeKind.RecordStruct)
                {
                    yield return j;
                }
        }

        private static void Demand(bool condition, string rule, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"GraphSpec validity rule {rule} violated: {message}");
            }
        }

        /// <summary>A stable one-line description, so a shrunk repro can be pasted into an issue.</summary>
        public string Describe()
        {
            return $"GraphSpec({RootSource}->{RootDest}; " +
                   string.Join("; ",
                       Nodes.Select(n =>
                           $"{n.Name}:{n.Kind}" +
                           (n.BaseRef is { } b ? $":base={b}" : "") +
                           (n.SplitAcrossFiles ? ":split" : "") +
                           "[" +
                           string.Join(",",
                               n.Members.Select(m =>
                                   $"{m.Name}:{m.Shape}:{(m.NestedRef is { } j ? "->" + j : m.ScalarType)}" + (m.Nullable ? "?" : "") + (m.Coll == CollShape.None ? "" : ":" + m.Coll))) +
                           "]")) +
                   ")";
        }
    }
}
