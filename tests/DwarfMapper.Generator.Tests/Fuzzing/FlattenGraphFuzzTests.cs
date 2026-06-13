// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DwarfMapper.Testing;
using Xunit;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// FlattenGraph reachability-oracle fuzz (Plan 22 test-methodology remediation).
/// Generates random homogeneous graphs over N seeds, maps via compiled [FlattenGraph] mapper,
/// then uses GraphOracleComparer.FlattenGraphDiff to assert:
///   (a) result node count == BFS-reachable count from source entry,
///   (b) every result node's navigation edges are null (topology degraded).
/// Deterministic seeds; failure messages include seed.
/// </summary>
public class FlattenGraphFuzzTests
{
    // ── Homo FlattenGraph source ──────────────────────────────────────────────

    private const string HomoFlattenSource = """
        using DwarfMapper;
        using System.Collections.Generic;
        namespace FlatFuzz;
        public class FNode {
            public int V { get; set; }
            public FNode? Left { get; set; }
            public FNode? Right { get; set; }
            public FNode? Back { get; set; }
        }
        public class FNodeDto {
            public int V { get; set; }
            public FNodeDto? Left { get; set; }
            public FNodeDto? Right { get; set; }
            public FNodeDto? Back { get; set; }
        }
        public class FRoot { public FNode? Entry { get; set; } }
        public class FRootDto { public List<FNodeDto> Nodes { get; set; } = new(); }
        [DwarfMapper]
        public partial class FlatMapper {
            [FlattenGraph("Entry", "Nodes")]
            public partial FRootDto Map(FRoot r);
        }
        """;

    // ── Hetero FlattenGraph source ────────────────────────────────────────────

    private const string HeteroFlattenSource = """
        using DwarfMapper;
        using System.Collections.Generic;
        namespace HetFuzz;
        public abstract class HNode { public int V { get; set; } public HNode? Parent { get; set; } }
        public class HFolder : HNode { public List<HNode> Children { get; set; } = new(); }
        public class HFile   : HNode { public long Size { get; set; } }
        public abstract class HNodeDto { public int V { get; set; } }
        public class HFolderDto : HNodeDto { public List<HNodeDto>? Children { get; set; } public HNodeDto? Parent { get; set; } }
        public class HFileDto   : HNodeDto { public long Size { get; set; } public HNodeDto? Parent { get; set; } }
        public class HRoot { public HNode? Entry { get; set; } }
        public class HRootDto { public List<HNodeDto> Nodes { get; set; } = new(); }
        [DwarfMapper]
        public partial class HetMapper {
            [FlattenGraph("Entry", "Nodes")]
            [MapDerivedType<HFolder, HFolderDto>]
            [MapDerivedType<HFile, HFileDto>]
            public partial HRootDto Map(HRoot r);
        }
        """;

    // ── Seed data ─────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> HomoSeeds() =>
        Enumerable.Range(0, 25).Select(i => new object[] { i });

    public static IEnumerable<object[]> HeteroSeeds() =>
        Enumerable.Range(0, 15).Select(i => new object[] { i });

    // ── Homo fuzz ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(HomoSeeds))]
    public void FlattenGraph_homo_reachability_oracle(int seed)
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(HomoFlattenSource);
        Assert.True(asm is not null,
            $"seed={seed} HomoFlattenSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var nodeType    = asm!.GetType("FlatFuzz.FNode")!;
        var rootType    = asm.GetType("FlatFuzz.FRoot")!;
        var mapperType  = asm.GetType("FlatFuzz.FlatMapper")!;
        var dtoType     = asm.GetType("FlatFuzz.FNodeDto")!;
        var rootDtoType = asm.GetType("FlatFuzz.FRootDto")!;
        var mapMethod   = mapperType.GetMethod("Map")!;
        var mapper      = Activator.CreateInstance(mapperType)!;

        // Build random graph
        var rng = new Random(seed);
        var nodes = Enumerable.Range(0, rng.Next(2, 7))
            .Select(i =>
            {
                var n = Activator.CreateInstance(nodeType)!;
                nodeType.GetProperty("V")!.SetValue(n, i);
                return n;
            })
            .ToArray();
        var edgeProps = new[] { "Left", "Right", "Back" };
        foreach (var n in nodes)
        {
            foreach (var prop in edgeProps)
            {
                if (rng.Next(3) != 0)  // 2/3 chance of setting edge
                    nodeType.GetProperty(prop)!.SetValue(n, nodes[rng.Next(nodes.Length)]);
            }
        }

        // Build root
        var root = Activator.CreateInstance(rootType)!;
        rootType.GetProperty("Entry")!.SetValue(root, nodes[0]);

        // Map
        var rootDto = mapMethod.Invoke(mapper, new[] { root })!;

        // Get result nodes
        var nodesProp    = rootDtoType.GetProperty("Nodes")!;
        var resultNodes  = nodesProp.GetValue(rootDto) as IEnumerable;

        // Oracle
        var violations = GraphOracleComparer.FlattenGraphDiff(
            nodes[0], resultNodes, nodeType, dtoType);

        Assert.True(violations.Count == 0,
            $"seed={seed} FlattenGraph homo reachability failed:\n" +
            GraphOracleComparer.RenderFlattenGraphDiff(violations));
    }

    // ── Hetero fuzz ────────────────────────────────────────────────────────────

    [Trait("tier", "exhaustive")]
    [Theory]
    [MemberData(nameof(HeteroSeeds))]
    public void FlattenGraph_hetero_reachability_oracle(int seed)
    {
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(HeteroFlattenSource);
        Assert.True(asm is not null,
            $"seed={seed} HeteroFlattenSource failed to emit: {string.Join(", ", errors.Select(e => e.Id))}");

        var folderType  = asm!.GetType("HetFuzz.HFolder")!;
        var fileType    = asm.GetType("HetFuzz.HFile")!;
        var nodeType    = asm.GetType("HetFuzz.HNode")!;     // abstract base
        var rootType    = asm.GetType("HetFuzz.HRoot")!;
        var mapperType  = asm.GetType("HetFuzz.HetMapper")!;
        var dtoBaseType = asm.GetType("HetFuzz.HNodeDto")!;
        var rootDtoType = asm.GetType("HetFuzz.HRootDto")!;
        var mapMethod   = mapperType.GetMethod("Map")!;
        var mapper      = Activator.CreateInstance(mapperType)!;

        var rng = new Random(seed);

        // Build random mixed graph of Folders and Files
        var concTypes = new[] { folderType, fileType };
        object MakeNode(int v)
        {
            var t = concTypes[rng.Next(concTypes.Length)];
            var n = Activator.CreateInstance(t)!;
            nodeType.GetProperty("V")!.SetValue(n, v);
            if (t == fileType) fileType.GetProperty("Size")!.SetValue(n, (long)(v * 100));
            return n;
        }

        var nodes = Enumerable.Range(0, rng.Next(2, 7)).Select(MakeNode).ToArray();

        // Wire random edges: Parent (single-ref on all) and Children (collection on Folder)
        foreach (var n in nodes)
        {
            if (rng.Next(2) == 1)
                nodeType.GetProperty("Parent")!.SetValue(n, nodes[rng.Next(nodes.Length)]);
            if (n.GetType() == folderType && rng.Next(2) == 1)
            {
                var childrenProp = folderType.GetProperty("Children")!;
                var childList    = childrenProp.GetValue(n)!;
                var addMethod    = childList.GetType().GetMethod("Add")!;
                var child        = nodes[rng.Next(nodes.Length)];
                addMethod.Invoke(childList, new[] { child });
            }
        }

        var root = Activator.CreateInstance(rootType)!;
        rootType.GetProperty("Entry")!.SetValue(root, nodes[0]);

        object rootDto;
        try
        {
            rootDto = mapMethod.Invoke(mapper, new[] { root })!;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is ArgumentException)
        {
            Assert.Fail($"seed={seed} unexpected ArgumentException: {tie.InnerException.Message}");
            return;
        }

        var nodesProp   = rootDtoType.GetProperty("Nodes")!;
        var resultNodes = nodesProp.GetValue(rootDto) as IEnumerable;

        var violations = GraphOracleComparer.FlattenGraphDiff(
            nodes[0], resultNodes, nodeType, dtoBaseType);

        Assert.True(violations.Count == 0,
            $"seed={seed} FlattenGraph hetero reachability failed:\n" +
            GraphOracleComparer.RenderFlattenGraphDiff(violations));
    }
}
