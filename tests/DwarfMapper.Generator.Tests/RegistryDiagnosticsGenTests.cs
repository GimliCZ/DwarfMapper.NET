// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The <c>[MapTo]</c> registry front door emits its own <c>DWARFR01</c>–<c>DWARFR06</c> family from
///     <see cref="RegistryDiagnostics" /> — a SEPARATE class that the DWARF0xx self-validation scans reflect
///     right past (by design: it is not AnalyzerReleases-tracked). Before these tests the entire error surface
///     of a public, shipping attribute (<c>[MapTo]</c>) had zero triggering tests and no completeness gate: a
///     seventh code, or a regression on the six, was caught by nothing. Each fact below drives one code from a
///     minimal source, and <see cref="Every_registry_diagnostic_is_triggered_and_documented" /> is the standing
///     gate — a new <c>DWARFR##</c> that isn't both triggered here and documented fails the build.
/// </summary>
public sealed class RegistryDiagnosticsGenTests
{
    // DWARFR01 — [MapTo] target that isn't a mappable class/struct (here: abstract).
    [Fact]
    public void Abstract_target_reports_DWARFR01()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int A { get; set; } }
                         public abstract class Dto { public int A { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR01");
    }

    // DWARFR02 — a writable destination member with no source (the registry's DWARF001 counterpart).
    [Fact]
    public void Unmapped_destination_reports_DWARFR02()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int A { get; set; } }
                         public class Dto { public int A { get; set; } public int Orphan { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR02");
    }

    // DWARFR03 — two sources both claim one destination member.
    [Fact]
    public void Conflicting_sources_report_DWARFR03()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))]
                         public class Src
                         {
                             [MapProperty("X")] public int A { get; set; }
                             [MapProperty("X")] public int B { get; set; }
                         }
                         public class Dto { public int X { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR03");
    }

    // DWARFR04 — more [MapProperty] directives on a member than there are [MapTo] targets.
    [Fact]
    public void MapProperty_arity_mismatch_reports_DWARFR04()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))]
                         public class Src
                         {
                             [MapProperty("X")]
                             [MapProperty("Y")]
                             public int A { get; set; }
                         }
                         public class Dto { public int X { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR04");
    }

    // DWARFR05 — mapped members whose types have no built-in conversion (object member -> int).
    [Fact]
    public void No_conversion_reports_DWARFR05()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public Payload P { get; set; } }
                         public class Payload { public int V { get; set; } }
                         public class Dto { public int P { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR05");
    }

    // DWARFR06 — a self-referential graph the front door can't thread a reference context through.
    [Fact]
    public void Recursive_nesting_reports_DWARFR06()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(NodeDto))]
                         public class Node { public int V { get; set; } public Node Next { get; set; } }
                         public class NodeDto { public int V { get; set; } public NodeDto Next { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR06");
    }

    // ── the standing completeness gate ──────────────────────────────────────────
    // Mirrors AssemblyScanTests Scan3 (every id tested) + Scan7 (every id documented), but over the registry's
    // OWN descriptor class, which those scans deliberately skip. It does NOT assert AnalyzerReleases sync — the
    // DWARFR family is intentionally not release-tracked (RegistryDiagnostics.cs), and forcing it into that
    // scheme is a separate, deferred design decision. This closes the "no gate at all" hole while respecting it.
    [Fact]
    public void Every_registry_diagnostic_is_triggered_and_documented()
    {
        var ids = typeof(RegistryDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => ((DiagnosticDescriptor)f.GetValue(null)!).Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // Non-vacuity: the family must actually reflect, or the two subset checks below pass over nothing.
        Assert.True(ids.Count >= 6,
            $"Only {ids.Count} DWARFR descriptors reflected from RegistryDiagnostics — the gate would be vacuous. "
            + "The class moved or its field shape changed.");

        var thisFileText = File.ReadAllText(SourcePath());
        var docsText = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "diagnostics.md"));

        var untested = ids.Where(id => !thisFileText.Contains($"\"{id}\"", StringComparison.Ordinal)).ToList();
        Assert.True(untested.Count == 0,
            "Registry diagnostic id(s) with NO triggering test in this file (assert `d.Id == \"DWARFR##\"`): "
            + string.Join(", ", untested)
            + ". Add a fact that drives the diagnostic from a minimal [MapTo] source.");

        var undocumented = ids.Where(id => !docsText.Contains(id, StringComparison.Ordinal)).ToList();
        Assert.True(undocumented.Count == 0,
            "Registry diagnostic id(s) missing from docs/diagnostics.md: " + string.Join(", ", undocumented)
            + ". Document each DWARFR code in the registry-diagnostics table.");
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(SourcePath())!);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
