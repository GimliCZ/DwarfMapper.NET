// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Audit R7 (Critical): the synthesized <c>__DwarfMap_FlatNode_*</c> helper assigned a nullable-reference
///     leaf into a non-nullable DTO member without the null-forgiving <c>!</c>, emitting CS8601 from inside the
///     always-<c>#nullable enable</c> generated file — a hard build break under TreatWarningsAsErrors, in code
///     the user cannot edit. Same class as the nested-nullable CS8604 bug, in the [FlattenGraph] path, and a
///     corpus hole (existing FlattenGraph tests use non-nullable <c>string Name = ""</c> leaves).
/// </summary>
public class FlattenNullableLeafTests
{
    private const string NullableLeaf = """
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public class Node    { public string? Name { get; set; } public Node? Next { get; set; } }
                                        public class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                                        public class Root    { public IReadOnlyList<Node> Entries { get; set; } = new List<Node>(); }
                                        public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                                        [DwarfMapper]
                                        public partial class M
                                        {
                                            [FlattenGraph("Entries", "Nodes")]
                                            public partial RootDto Map(Root r);
                                        }
                                        """;

    [Fact]
    public void FlattenGraph_nullable_ref_leaf_into_non_nullable_dto_member_emits_no_CS8601()
    {
        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(NullableLeaf);

        Assert.DoesNotContain(warnings, d => d.Id == "CS8601");
    }
}
