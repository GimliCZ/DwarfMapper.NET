// SPDX-License-Identifier: GPL-2.0-only

// DwarfMapper Gallery — a progression of mapping examples, simplest first.
// Each NN_*.cs file is a self-contained, annotated example. Run this project to execute them all:
//   dotnet run --project samples/DwarfMapper.Gallery
//
// The example list is DISCOVERED, not written down: every [DocExample] type is found by reflection and run in
// tier order. Adding a file adds a step; deleting one removes it. The same catalogue renders the index table
// in README.md, so the two cannot disagree.
//
// NOTE ON REFLECTION: this is the sample HARNESS, not the mapper. DwarfMapper itself performs no reflection —
// every map is resolved at compile time, which is what makes it AOT- and trim-safe. The Gallery is not an AOT
// target (samples/DwarfMapper.AotSample is, and it is the CI gate). Nothing here touches a mapping path.

using System.Reflection;
using DwarfMapper.Gallery;

Console.WriteLine("=== DwarfMapper Gallery — simple → advanced ===");
Console.WriteLine();

var examples = Assembly.GetExecutingAssembly().GetTypes()
    .Select(t => (Type: t, Attr: t.GetCustomAttribute<DocExampleAttribute>()))
    .Where(x => x.Attr is not null)
    .OrderBy(x => x.Attr!.Tier)
    .ThenBy(x => x.Attr!.Ordinal)
    .ToList();

if (examples.Count == 0)
    throw new InvalidOperationException(
        "No [DocExample] types found. The Gallery would print nothing and exit 0, which reads as success.");

Tier? tier = null;
foreach (var (type, attr) in examples)
{
    if (tier != attr!.Tier)
    {
        tier = attr.Tier;
        Console.WriteLine($"-- {TierName.Of(attr.Tier)} --");
    }

    var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
              ?? throw new InvalidOperationException($"{type.Name} has no public static Run().");
    run.Invoke(null, null);
}

Console.WriteLine();
Console.WriteLine($"=== {examples.Count} examples — open each NN_*.cs file for the annotated source ===");
