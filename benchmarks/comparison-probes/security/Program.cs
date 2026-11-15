// SPDX-License-Identifier: GPL-2.0-only
// Round 35 T25 security probe (benchmarks/comparison-probes/README.md). One process per cell: `probe-security <DWARF|MAPPERLY|MAPSTER> <case> [n]`.
// A stack overflow ends the process and cannot be caught, so the cell is how the process ENDED (exit code + output).
#nullable enable
using System;
using DwarfMapper;
using Mapster;

namespace Probe;

public class Node { public int Value { get; set; } public Node? Next { get; set; } }
public class NodeDto { public int Value { get; set; } public NodeDto? Next { get; set; } }

public class Tree { public int Value { get; set; } public Tree? Child { get; set; } }
public class TreeDto { public int Value { get; set; } public TreeDto? Child { get; set; } }

public class Flat { public int Id { get; set; } public string Name { get; set; } = ""; }
public class FlatDto { public int Id { get; set; } public string Name { get; set; } = ""; }

// ── DwarfMapper, every option at its default ──────────────────────────────────────────────────────────────────────
[DwarfMapper.DwarfMapper]
public partial class DwarfM
{
    public partial NodeDto Map(Node n);
    public partial FlatDto Map(Flat f);
    // Re-entry through user code: the converter calls the mapper again.
    [MapProperty(nameof(Tree.Child), nameof(TreeDto.Child), Use = nameof(ConvertChild))]
    public partial TreeDto Map(Tree t);
    private TreeDto? ConvertChild(Tree? child) => child is null ? null : Map(child);
}

// ── Mapperly, every option at its default ─────────────────────────────────────────────────────────────────────────
[Riok.Mapperly.Abstractions.Mapper]
public partial class MapperlyM
{
    public partial NodeDto Map(Node n);
    public partial FlatDto Map(Flat f);
    [Riok.Mapperly.Abstractions.MapProperty(nameof(Tree.Child), nameof(TreeDto.Child), Use = nameof(ConvertChild))]
    public partial TreeDto Map(Tree t);
    private TreeDto? ConvertChild(Tree? child) => child is null ? null : Map(child);
}

public static class Program
{
    public static int Main(string[] args)
    {
        var lib = args[0];
        var kind = args[1];
        var n = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 100_000;
        try
        {
            var result = kind switch
            {
                "FLAT" => Flat(lib),
                "DEEP" => Deep(lib, n),
                "CYCLE" => Cycle(lib),
                "REENTRY" => Reentry(lib, n),
                _ => throw new ArgumentException(kind),
            };
            Console.WriteLine("PROBE-SECURITY " + result);
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"PROBE-SECURITY THROWS {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            return 3;
        }
    }

    private static string Flat(string lib)
    {
        var src = new Flat { Id = 42, Name = "Ada" };
        var dto = lib switch
        {
            "DWARF" => new DwarfM().Map(src),
            "MAPPERLY" => new MapperlyM().Map(src),
            _ => src.Adapt<FlatDto>(),
        };
        return $"mapped Id={dto.Id} Name={dto.Name}";
    }

    private static string Deep(string lib, int n)
    {
        var head = new Node();
        var cur = head;
        for (var i = 1; i < n; i++) { cur.Next = new Node { Value = i }; cur = cur.Next; }
        var dto = lib switch
        {
            "DWARF" => new DwarfM().Map(head),
            "MAPPERLY" => new MapperlyM().Map(head),
            _ => head.Adapt<NodeDto>(),
        };
        var depth = 0;
        for (var d = dto; d is not null; d = d.Next) depth++;
        return $"mapped a chain of {depth} of {n}";
    }

    private static string Cycle(string lib)
    {
        var a = new Node { Value = 1 };
        a.Next = a;
        var dto = lib switch
        {
            "DWARF" => new DwarfM().Map(a),
            "MAPPERLY" => new MapperlyM().Map(a),
            _ => a.Adapt<NodeDto>(),
        };
        return "mapped; " + (ReferenceEquals(dto.Next, dto) ? "the cycle kept" : dto.Next is null ? "the cycle cut to null" : "the cycle unrolled");
    }

    private static string Reentry(string lib, int n)
    {
        var root = new Tree();
        var cur = root;
        for (var i = 1; i < n; i++) { cur.Child = new Tree { Value = i }; cur = cur.Child; }
        TreeDto dto;
        if (lib == "MAPSTER")
        {
            var config = new TypeAdapterConfig();
            config.NewConfig<Tree, TreeDto>().Map(d => d.Child, s => s.Child == null ? null : s.Child.Adapt<TreeDto>(config));
            dto = root.Adapt<TreeDto>(config);
        }
        else
        {
            dto = lib == "DWARF" ? new DwarfM().Map(root) : new MapperlyM().Map(root);
        }

        var depth = 0;
        for (var d = dto; d is not null; d = d.Child) depth++;
        return $"mapped a chain of {depth} of {n}";
    }
}
