// SPDX-License-Identifier: GPL-2.0-only

// 20 — Self-referential and mutually-recursive graphs. The generator detects which (src,tgt) pairs can
// actually re-enter and only those carry the extra machinery; acyclic pairs stay zero-overhead.
//
// Three behaviours, chosen per mapper — they are alternatives, not a stack ([DwarfMapper] is
// AllowMultiple = false):
//   ReferenceHandling = None (default) — depth-guarded, zero allocation. A cycle throws a catchable
//                                        DwarfMappingDepthException at MaxDepth (default 64).
//   OnCycle = SetNull                  — break the cycle by nulling the back-reference (what
//                                        System.Text.Json's IgnoreCycles does).
//   ReferenceHandling = Preserve       — reconstruct the full topology, shared references and all.
// The loud default matters: without it a cycle is a StackOverflowException, which you cannot catch.

namespace DwarfMapper.Gallery.Ex20;

public sealed class Dwarf
{
    public string Name { get; set; } = "";
    public Dwarf? Mentor { get; set; }
}

public sealed class DwarfDto
{
    public string Name { get; set; } = "";
    public DwarfDto? Mentor { get; set; }
}

// <snippet: reference-cycles>
[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class Mapper
{
    public partial DwarfDto ToDto(Dwarf d);
}
// </snippet>

[DocExample(20, Tier.Advanced, "Reference cycles",
    Shows = "`OnCycle = SetNull` breaks a cycle instead of overflowing the stack")]
public static class Example
{
    public static void Run()
    {
        // A mentors B, and B mentors A — a cycle that would recurse forever if nothing guarded it.
        var balin = new Dwarf { Name = "Balin" };
        var dwalin = new Dwarf { Name = "Dwalin", Mentor = balin };
        balin.Mentor = dwalin;

        var dto = new Mapper().ToDto(balin);

        Console.WriteLine(
            $"20 Reference cycles   -> {dto.Name} <- {dto.Mentor?.Name}; cycle closed with "
            + $"{(dto.Mentor?.Mentor is null ? "null" : "a loop")}");
    }
}
