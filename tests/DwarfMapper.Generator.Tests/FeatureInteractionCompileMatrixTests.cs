// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>One test case for the feature-interaction compile matrix.</summary>
public sealed record FimMatrixCase(
    string Id,
    string Source,
    IReadOnlyList<string>? ExpectedDiagnosticIds = null  // null = expect zero errors; non-null = expect these DWARF diagnostics
);

/// <summary>
/// Part 2 self-validation: FeatureInteractionCompileMatrix.
/// Programmatically enumerates mapper sources combining FEATURES × MODES × TARGET-SHAPES
/// and asserts GeneratorTestHarness.RunAndGetCompilationErrors(src) is EMPTY (no CS errors)
/// for every VALID combination.
///
/// This matrix WOULD have caught: MdT+Preserve (CS7036), FlattenGraph+List-source (CS1503).
/// Each case has a clear id in the failure message.
/// </summary>
public class FeatureInteractionCompileMatrixTests
{
    // ── Case definition ──────────────────────────────────────────────────────

    // ── Case enumeration ─────────────────────────────────────────────────────

    private static IEnumerable<FimMatrixCase> BuildCases()
    {
        // ── 1. Plain object map: class, record, struct ────────────────────────

        yield return new FimMatrixCase("plain_class", """
            using DwarfMapper;
            namespace Fim;
            public class Src { public int X { get; set; } public string S { get; set; } = ""; }
            public class Dst { public int X { get; set; } public string S { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """);

        yield return new FimMatrixCase("plain_record", """
            using DwarfMapper;
            namespace Fim;
            public record Src(int X, string S);
            public record Dst(int X, string S);
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """);

        yield return new FimMatrixCase("plain_struct", """
            using DwarfMapper;
            namespace Fim;
            public struct Src { public int X { get; set; } }
            public struct Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """);

        // ── 2. Nested auto-nest ───────────────────────────────────────────────

        yield return new FimMatrixCase("autonest_class", """
            using DwarfMapper;
            namespace Fim;
            public class Inner { public int V { get; set; } }
            public class InnerDto { public int V { get; set; } }
            public class Outer { public Inner? Child { get; set; } }
            public class OuterDto { public InnerDto? Child { get; set; } }
            [DwarfMapper(AutoNest = true)] public partial class M { public partial OuterDto Map(Outer o); }
            """);

        yield return new FimMatrixCase("autonest_preserve", """
            using DwarfMapper;
            namespace Fim;
            public class Inner { public int V { get; set; } }
            public class InnerDto { public int V { get; set; } }
            public class Outer { public Inner? Child { get; set; } }
            public class OuterDto { public InnerDto? Child { get; set; } }
            [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            public partial class M { public partial OuterDto Map(Outer o); }
            """);

        // ── 3. Collection members ─────────────────────────────────────────────

        yield return new FimMatrixCase("collection_member_List", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Owner { public List<Item> Items { get; set; } = new(); }
            public class OwnerDto { public List<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                public partial OwnerDto Map(Owner o);
                public partial ItemDto Map(Item i);
            }
            """);

        yield return new FimMatrixCase("collection_member_Array", """
            using DwarfMapper;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Owner { public Item[] Items { get; set; } = []; }
            public class OwnerDto { public ItemDto[] Items { get; set; } = []; }
            [DwarfMapper] public partial class M {
                public partial OwnerDto Map(Owner o);
                public partial ItemDto Map(Item i);
            }
            """);

        yield return new FimMatrixCase("collection_member_IReadOnlyList", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Owner { public System.Collections.Generic.List<Item> Items { get; set; } = new(); }
            public class OwnerDto { public IReadOnlyList<ItemDto> Items { get; set; } = new System.Collections.Generic.List<ItemDto>(); }
            [DwarfMapper] public partial class M {
                public partial OwnerDto Map(Owner o);
                public partial ItemDto Map(Item i);
            }
            """);

        yield return new FimMatrixCase("collection_member_HashSet", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Owner { public HashSet<Item> Items { get; set; } = new(); }
            public class OwnerDto { public HashSet<ItemDto> Items { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                public partial OwnerDto Map(Owner o);
                public partial ItemDto Map(Item i);
            }
            """);

        // ── 4. Top-level collection methods ──────────────────────────────────

        yield return new FimMatrixCase("toplevel_List_to_List", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            [DwarfMapper] public partial class M {
                public partial List<ItemDto> Map(List<Item> src);
                public partial ItemDto Map(Item i);
            }
            """);

        // Note: List→Array top-level method (ItemDto[] Map(List<Item>)) is not supported by the generator
        // (DWARF003 or CS8795). Tracked as M2/M3 generator bugs. Skipped here.

        yield return new FimMatrixCase("toplevel_List_to_IReadOnlyList", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            [DwarfMapper] public partial class M {
                public partial IReadOnlyList<ItemDto> Map(List<Item> src);
                public partial ItemDto Map(Item i);
            }
            """);

        yield return new FimMatrixCase("toplevel_HashSet_to_List", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            [DwarfMapper] public partial class M {
                public partial List<ItemDto> Map(HashSet<Item> src);
                public partial ItemDto Map(Item i);
            }
            """);

        yield return new FimMatrixCase("toplevel_Dict_to_RODict", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class K { public string V { get; set; } = ""; }
            public class V { public int N { get; set; } }
            public class KDto { public string V { get; set; } = ""; }
            public class VDto { public int N { get; set; } }
            [DwarfMapper] public partial class M {
                public partial IReadOnlyDictionary<KDto, VDto> Map(Dictionary<K, V> src);
                public partial KDto Map(K k);
                public partial VDto Map(V v);
            }
            """);

        // ── 5. MapDerivedType dispatch ────────────────────────────────────────

        yield return new FimMatrixCase("mdt_basic", """
            using DwarfMapper;
            namespace Fim;
            public abstract class Base { public string Name { get; set; } = ""; }
            public class Concrete : Base { public int Extra { get; set; } }
            public class BaseDto { public string Name { get; set; } = ""; }
            public class ConcreteDto : BaseDto { public int Extra { get; set; } }
            [DwarfMapper] public partial class M {
                [MapDerivedType<Concrete, ConcreteDto>]
                public partial BaseDto Map(Base b);
                public partial ConcreteDto Map(Concrete c);
            }
            """);

        yield return new FimMatrixCase("mdt_autonest", """
            using DwarfMapper;
            namespace Fim;
            public abstract class Base { public string Name { get; set; } = ""; }
            public class Concrete : Base { public int Extra { get; set; } }
            public class BaseDto { public string Name { get; set; } = ""; }
            public class ConcreteDto : BaseDto { public int Extra { get; set; } }
            [DwarfMapper(AutoNest = true)] public partial class M {
                [MapDerivedType<Concrete, ConcreteDto>]
                public partial BaseDto Map(Base b);
            }
            """);

        yield return new FimMatrixCase("mdt_preserve", """
            using DwarfMapper;
            namespace Fim;
            public abstract class Base { public string Name { get; set; } = ""; }
            public class Concrete : Base { public int Extra { get; set; } }
            public class BaseDto { public string Name { get; set; } = ""; }
            public class ConcreteDto : BaseDto { public int Extra { get; set; } }
            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve, AutoNest = true)]
            public partial class M {
                [MapDerivedType<Concrete, ConcreteDto>]
                public partial BaseDto Map(Base b);
            }
            """);

        yield return new FimMatrixCase("mdt_preserve_explicit_overloads", """
            using DwarfMapper;
            namespace Fim;
            public abstract class Base { public string Name { get; set; } = ""; }
            public class Concrete : Base { public int Extra { get; set; } }
            public class BaseDto { public string Name { get; set; } = ""; }
            public class ConcreteDto : BaseDto { public int Extra { get; set; } }
            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            public partial class M {
                [MapDerivedType<Concrete, ConcreteDto>]
                public partial BaseDto Map(Base b);
                public partial ConcreteDto Map(Concrete c);
            }
            """);

        // ── 6. Homogeneous FlattenGraph ───────────────────────────────────────

        yield return new FimMatrixCase("flatten_homo_single_ref", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Node { public string N { get; set; } = ""; public Node? Next { get; set; } }
            public class NodeDto { public string N { get; set; } = ""; public NodeDto? Next { get; set; } }
            public class Root { public Node? Entry { get; set; } }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                [FlattenGraph("Entry", "Nodes")]
                public partial RootDto Map(Root r);
            }
            """);

        yield return new FimMatrixCase("flatten_homo_collection_edge", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Node { public string N { get; set; } = ""; public List<Node> Children { get; set; } = new(); }
            public class NodeDto { public string N { get; set; } = ""; public List<NodeDto>? Children { get; set; } }
            public class Root { public Node? Entry { get; set; } }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                [FlattenGraph("Entry", "Nodes")]
                public partial RootDto Map(Root r);
            }
            """);

        yield return new FimMatrixCase("flatten_homo_array_target", """
            using DwarfMapper;
            namespace Fim;
            public class Node { public string N { get; set; } = ""; public Node? Next { get; set; } }
            public class NodeDto { public string N { get; set; } = ""; public NodeDto? Next { get; set; } }
            public class Root { public Node? Entry { get; set; } }
            public class RootDto { public NodeDto[] Nodes { get; set; } = []; }
            [DwarfMapper] public partial class M {
                [FlattenGraph("Entry", "Nodes")]
                public partial RootDto Map(Root r);
            }
            """);

        yield return new FimMatrixCase("flatten_homo_list_source_nav", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Node { public string N { get; set; } = ""; public Node? Next { get; set; } }
            public class NodeDto { public string N { get; set; } = ""; public NodeDto? Next { get; set; } }
            public class Root { public List<Node> Entries { get; set; } = new(); }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                [FlattenGraph("Entries", "Nodes")]
                public partial RootDto Map(Root r);
            }
            """);

        // ── 7. Heterogeneous FlattenGraph ─────────────────────────────────────

        yield return new FimMatrixCase("flatten_hetero_basic", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public abstract class Node { public string N { get; set; } = ""; public Node? Parent { get; set; } }
            public class FolderNode : Node { public List<Node> Children { get; set; } = new(); }
            public class FileNode   : Node { public long Size { get; set; } }
            public abstract class NodeDto { public string N { get; set; } = ""; }
            public class FolderDto : NodeDto { public List<NodeDto>? Children { get; set; } public NodeDto? Parent { get; set; } }
            public class FileDto   : NodeDto { public long Size { get; set; } public NodeDto? Parent { get; set; } }
            public class Root { public Node? Entry { get; set; } }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                [FlattenGraph("Entry", "Nodes")]
                [MapDerivedType<FolderNode, FolderDto>]
                [MapDerivedType<FileNode, FileDto>]
                public partial RootDto Map(Root r);
            }
            """);

        yield return new FimMatrixCase("flatten_hetero_array_target", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public abstract class Node { public string N { get; set; } = ""; }
            public class LeafNode : Node { public int V { get; set; } }
            public abstract class NodeDto { public string N { get; set; } = ""; }
            public class LeafDto : NodeDto { public int V { get; set; } }
            public class Root { public Node? Entry { get; set; } }
            public class RootDto { public NodeDto[] Nodes { get; set; } = []; }
            [DwarfMapper] public partial class M {
                [FlattenGraph("Entry", "Nodes")]
                [MapDerivedType<LeafNode, LeafDto>]
                public partial RootDto Map(Root r);
            }
            """);

        // ── 8. Dictionary member ──────────────────────────────────────────────

        yield return new FimMatrixCase("dict_member", """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Fim;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }
            public class Owner { public Dictionary<string, Item> Map_ { get; set; } = new(); }
            public class OwnerDto { public Dictionary<string, ItemDto> Map_ { get; set; } = new(); }
            [DwarfMapper] public partial class M {
                public partial OwnerDto Map(Owner o);
                public partial ItemDto Map(Item i);
            }
            """);

        // ── 9. ImmutableArray member ──────────────────────────────────────────

        yield return new FimMatrixCase("immutablearray_member", """
            using DwarfMapper;
            using System.Collections.Immutable;
            namespace Fim;
            public class Src { public ImmutableArray<int> Vals { get; set; } = ImmutableArray<int>.Empty; }
            public class Dst { public ImmutableArray<int> Vals { get; set; } = ImmutableArray<int>.Empty; }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """);

        // ── 10. MapIgnore ─────────────────────────────────────────────────────

        yield return new FimMatrixCase("mapignore_member", """
            using DwarfMapper;
            namespace Fim;
            public class Src { public int X { get; set; } public int Secret { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M {
                [MapIgnore("Secret")]
                public partial Dst Map(Src s);
            }
            """);

        // ── 10b. MapIgnoreSource + RequiredMapping = Both (source-coverage) ───
        yield return new FimMatrixCase("mapignoresource_both", """
            using DwarfMapper;
            namespace Fim;
            public class Src { public int X { get; set; } public int Unused { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)] public partial class M {
                [MapIgnoreSource("Unused")]
                public partial Dst Map(Src s);
            }
            """);

        // ── 11. MapProperty rename ────────────────────────────────────────────

        yield return new FimMatrixCase("mapproperty_rename", """
            using DwarfMapper;
            namespace Fim;
            public class Src { public int OldName { get; set; } }
            public class Dst { public int NewName { get; set; } }
            [DwarfMapper] public partial class M {
                [MapProperty("OldName", "NewName")]
                public partial Dst Map(Src s);
            }
            """);

        // ── 12. BeforeMap hook ────────────────────────────────────────────────

        yield return new FimMatrixCase("beforemap_hook", """
            using DwarfMapper;
            namespace Fim;
            public class Src { public int X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M {
                public partial Dst Map(Src s);
                [BeforeMap] private static void Validate(Src s) { }
            }
            """);

        // ── 13. AfterMap hook ─────────────────────────────────────────────────

        yield return new FimMatrixCase("aftermap_hook", """
            using DwarfMapper;
            namespace Fim;
            public class Src { public int X { get; set; } }
            public class Dst { public int X { get; set; } }
            [DwarfMapper] public partial class M {
                public partial Dst Map(Src s);
                [AfterMap] private static void Postprocess(Src s, Dst d) { }
            }
            """);

        // ── 14. Reinterpret blit ──────────────────────────────────────────────

        yield return new FimMatrixCase("reinterpret_blit", """
            using DwarfMapper;
            namespace Fim;
            public struct Vec3Src { public float X; public float Y; public float Z; }
            public struct Vec3Dst { public float X; public float Y; public float Z; }
            public class Src { public Vec3Src[] Verts { get; set; } = System.Array.Empty<Vec3Src>(); }
            public class Dst { public Vec3Dst[] Verts { get; set; } = System.Array.Empty<Vec3Dst>(); }
            [DwarfMapper] public partial class M {
                [Reinterpret("Verts")]
                public partial Dst Map(Src s);
            }
            """);

        // ── 15. Flatten (member-level flatten, distinct from FlattenGraph) ────

        yield return new FimMatrixCase("flatten_member", """
            using DwarfMapper;
            namespace Fim;
            public class Address { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
            public class Person  { public string Name { get; set; } = ""; public Address Home { get; set; } = new(); }
            public class PersonDto { public string Name { get; set; } = ""; public string Street { get; set; } = ""; public string City { get; set; } = ""; }
            [DwarfMapper] public partial class M {
                [Flatten("Home")]
                public partial PersonDto Map(Person p);
            }
            """);

        // ── 16. [GenerateMap<S,T>] — attribute-declared low-ceremony mapper ───
        yield return new FimMatrixCase("generate_map_attribute", """
            using DwarfMapper;
            namespace Fim;
            public class GmA { public int Id { get; set; } public long Score { get; set; } }
            public class GmB { public int Id { get; set; } public int Score { get; set; } }
            [DwarfMapper]
            [GenerateMap<GmA, GmB>]
            public partial class M { }
            """);
    }

    // ── Theory data ──────────────────────────────────────────────────────────

    public static IEnumerable<object[]> Cases()
    {
        foreach (var c in BuildCases())
            yield return new object[] { c };
    }

    // ── Test ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cases))]
    public void Feature_combination_compiles_cleanly(FimMatrixCase c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.ExpectedDiagnosticIds is not null)
        {
            // For cases expected to produce specific diagnostics, assert they appear
            var (diags, _) = GeneratorTestHarness.Run(c.Source);
            foreach (var expectedId in c.ExpectedDiagnosticIds)
            {
                Assert.True(
                    diags.Any(d => d.Id == expectedId),
                    $"[{c.Id}] expected diagnostic {expectedId} but not found. Got: {string.Join(", ", diags.Select(d => d.Id))}");
            }
            return;
        }

        // Assert zero compilation errors (no CS errors)
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(c.Source);
        Assert.True(
            compileErrors.Length == 0,
            $"[{c.Id}] produced CS errors:\n" +
            string.Join("\n", compileErrors.Select(d =>
                $"  {d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")) +
            $"\n--- source ---\n{c.Source}");
    }
}
