// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.CompilerTests.TypeGraphs;

/// <summary>
///     Renders a <see cref="GraphSpec" /> to compilable C# compilation units plus the mapper declaration.
///     <para>
///         Normally one unit; a node with <see cref="NodeSpec.SplitAcrossFiles" /> set contributes a second
///         unit holding the back half of its members (the P5 partial-file corpus shape). The renderer calls
///         <see cref="GraphSpec.Validate" /> first: an invalid spec is refused loudly here, never rendered
///         into C# that is known-invalid regardless of the mapper.
///     </para>
/// </summary>
public static class TypeGraphRenderer
{
    /// <summary>Render every compilation unit. Unit 0 carries the graph and the mapper declaration.</summary>
    public static IReadOnlyList<string> Render(GraphSpec graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        graph.Validate();

        var units = new List<string>();
        var main = new StringBuilder();
        main.AppendLine("using DwarfMapper;");
        main.AppendLine();
        main.AppendLine("namespace T;");

        // Bounded by Nodes.Count and each node's member count — the H7 variant is the loop counter itself;
        // NestedRef/BaseRef lookups never recurse (names are resolved by index, not by walking the graph).
        foreach (var node in graph.Nodes)
        {
            main.AppendLine();
            if (node.SplitAcrossFiles)
            {
                var half = (node.Members.Count + 1) / 2;
                RenderNode(main, graph, node, node.Members.Take(half).ToList(), partial: true, withCtor: true);
                var rest = new StringBuilder();
                rest.AppendLine("namespace T;");
                rest.AppendLine();
                RenderNode(rest, graph, node, node.Members.Skip(half).ToList(), partial: true, withCtor: false);
                units.Add(rest.ToString());
            }
            else
            {
                RenderNode(main, graph, node, node.Members, partial: false, withCtor: true);
            }
        }

        main.AppendLine();
        main.AppendLine("[DwarfMapper]");
        main.AppendLine("public partial class M");
        main.AppendLine("{");
        main.AppendLine(FormattableString.Invariant(
            $"    public partial {graph.RootDest} Map({graph.RootSource} s);"));
        main.AppendLine("}");

        units.Insert(0, main.ToString());
        return units;
    }

    /// <summary>
    ///     Renders one declaration of <paramref name="node" /> carrying <paramref name="members" /> — the
    ///     full member list normally, one half of it per partial declaration for a split node. The
    ///     constructor (which may assign CtorParam members declared in the OTHER half — legal for partial
    ///     types) is emitted with the first declaration only.
    /// </summary>
    private static void RenderNode(StringBuilder sb, GraphSpec graph, NodeSpec node,
        IReadOnlyList<MemberSpec> members, bool partial, bool withCtor)
    {
        var keyword = node.Kind switch
        {
            TypeKind.Class => "class",
            TypeKind.Record => "record",
            TypeKind.Struct => "struct",
            TypeKind.RecordStruct => "record struct",
            _ => throw new InvalidOperationException($"unknown TypeKind {node.Kind}")
        };
        var baseClause = node.BaseRef is int b ? " : " + graph.Nodes[b].Name : "";

        sb.Append("public ").Append(partial ? "partial " : "").Append(keyword).Append(' ')
            .Append(node.Name).Append(baseClause).AppendLine();
        sb.AppendLine("{");

        foreach (var member in members)
        {
            var type = TypeText(graph, member);
            sb.Append("    ");
            sb.AppendLine(member.Shape switch
            {
                MemberShape.AutoProp => $"public {type} {member.Name} {{ get; set; }}",
                MemberShape.InitOnly => $"public {type} {member.Name} {{ get; init; }}",
                MemberShape.Required => $"public required {type} {member.Name} {{ get; set; }}",
                MemberShape.CtorParam => $"public {type} {member.Name} {{ get; }}",
                MemberShape.Field => $"public {type} {member.Name};",
                _ => throw new InvalidOperationException($"unknown MemberShape {member.Shape}")
            });
        }

        if (withCtor)
        {
            // The ctor covers the node's WHOLE member list, not just the half rendered here: a partial
            // type's ctor may assign auto-props declared in the other file.
            var ctorParams = node.Members.Where(m => m.Shape == MemberShape.CtorParam).ToList();
            if (ctorParams.Count > 0)
            {
                sb.Append("    public ").Append(node.Name).Append('(');
                sb.Append(string.Join(", ", ctorParams.Select(m => TypeText(graph, m) + ' ' + ParamName(m))));
                sb.AppendLine(")");
                sb.AppendLine("    {");
                foreach (var m in ctorParams)
                    sb.Append("        ").Append(m.Name).Append(" = ").Append(ParamName(m)).AppendLine(";");
                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");
    }

    /// <summary>Member names are always "M…", so lower-casing the first letter cannot collide with them.</summary>
    private static string ParamName(MemberSpec member)
    {
        return char.ToLowerInvariant(member.Name[0]) + member.Name[1..];
    }

    private static string TypeText(GraphSpec graph, MemberSpec member)
    {
        var element = member.NestedRef is int j ? graph.Nodes[j].Name : member.ScalarType;
        if (member.Nullable) element += "?";
        return member.Coll switch
        {
            CollShape.None => element,
            CollShape.Array => element + "[]",
            CollShape.List => $"System.Collections.Generic.List<{element}>",
            CollShape.IReadOnlyList => $"System.Collections.Generic.IReadOnlyList<{element}>",
            CollShape.Dictionary => $"System.Collections.Generic.Dictionary<string, {element}>",
            CollShape.HashSet => $"System.Collections.Generic.HashSet<{element}>",
            _ => throw new InvalidOperationException($"unknown CollShape {member.Coll}")
        };
    }
}
