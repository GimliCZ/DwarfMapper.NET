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

    // DWARFR04 again, for the OTHER arity a caller can get wrong: not how many [MapProperty] attributes
    // were stacked, but how many values ONE of them carries. The member form takes a single name — the
    // destination this member supplies; the two-name form is the class model's METHOD form. Written here it
    // used to bind nothing: ParseDirectives reads a name only off a one-argument application, so the
    // directive became "no name at all" and the member quietly fell back to its own. The fixture is chosen so
    // that fallback SUCCEEDS (Dto.A is satisfied by Src.A), because a fixture where it fails reports DWARFR02
    // instead and the silence this pins would be invisible.
    [Fact]
    public void MapProperty_method_form_on_a_registry_member_reports_DWARFR04()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))]
                         public class Src
                         {
                             [MapProperty("A", "X")]
                             public int A { get; set; }
                         }
                         public class Dto { public int A { get; set; } }
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

    // DWARFR07 — an implicit but precision-losing conversion. The class model reports DWARF038 for exactly this;
    // the registry used to emit a silent direct assignment, so the SAME mapping was loud through [DwarfMapper]
    // and quiet through [MapTo]. Both engines now share NumericConverter.IsCrossCategoryLossy.
    [Theory]
    [InlineData("long", "double")]
    [InlineData("int", "float")]
    [InlineData("long", "decimal")]
    public void Lossy_implicit_numeric_conversion_reports_DWARFR07(string srcType, string dstType)
    {
        var s = $$"""
                  using DwarfMapper;
                  namespace Demo;
                  [MapTo(typeof(Dto))] public class Src { public {{srcType}} V { get; set; } }
                  public class Dto { public {{dstType}} V { get; set; } }
                  """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR07");
    }

    [Fact]
    public void Same_category_widening_does_not_report_DWARFR07()
    {
        // Guard against over-reach: int→long and float→double stay on the silent zero-cost path.
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int A { get; set; } public float B { get; set; } }
                         public class Dto { public long A { get; set; } public double B { get; set; } }
                         """;
        Assert.DoesNotContain(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR07");
    }

    // ISSUE-009 — the registry filtered on the PROPERTY's accessibility but never the ACCESSOR's, so
    // `public int X { get; private set; }` counted as writable and emitted `new Dto { X = … }` → CS0272, and a
    // `private get` emitted `source.X` → CS0271: raw compiler errors out of generated code. Such a member is now
    // simply not writable/readable, which the completeness gate turns into a proper DWARFR02.
    [Fact]
    public void Private_setter_on_the_target_is_never_assigned()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int A { get; set; } public int B { get; set; } }
                         public class Dto { public int A { get; set; } public int B { get; private set; } }
                         """;
        var (diags, generated) = GeneratorTestHarness.RunMapToWithSource(s);

        // A private-set property is not an assignable destination at all (same as the class model treats it), so
        // it is simply not a mapping target — the point is that the registry no longer EMITS `B = …`, which the
        // compiler rejected with CS0272 out of generated code.
        Assert.DoesNotContain("B =", generated, StringComparison.Ordinal);
        Assert.Contains("A =", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Private_getter_on_the_source_is_not_read()
    {
        // Src.B is unreadable, so Dto.B has no source → DWARFR02 rather than an emitted `source.B` (CS0271).
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int A { get; set; } public int B { private get; set; } }
                         public class Dto { public int A { get; set; } public int B { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR02");
    }

    // ISSUE-010 — two targets whose SIMPLE names collide both generate `ToOrder(this Src)` in one static class,
    // which the compiler rejects with CS0111 out of generated code.
    [Fact]
    public void Targets_with_the_same_simple_name_report_DWARFR08()
    {
        const string src = """
                           using DwarfMapper;
                           namespace A { public class Order { public int Id { get; set; } } }
                           namespace B { public class Order { public int Id { get; set; } } }
                           namespace Demo
                           {
                               [MapTo(typeof(A.Order), typeof(B.Order))]
                               public class Src { public int Id { get; set; } }
                           }
                           """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(src), d => d.Id == "DWARFR08");
    }

    // ISSUE-011 — the registry builds targets with `new T { … }`. A ctor-only target also has no writable
    // members, so the completeness gate stayed silent and the only signal was CS1729 out of generated code.
    [Fact]
    public void Target_without_a_parameterless_constructor_reports_DWARFR09()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public Dto(int id) { Id = id; } public int Id { get; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR09");
    }

    [Fact]
    public void A_struct_target_is_not_flagged_by_DWARFR09()
    {
        // Over-reach guard: every struct has a parameterless constructor.
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public struct Dto { public int Id { get; set; } }
                         """;
        Assert.DoesNotContain(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR09");
    }

    // DWARFR10 — the explicit-only trust boundary, asked of the front door that used to read no
    // assembly-level configuration at all. [assembly: DwarfMapperDefaults(AutoMatchMembers = false)] means
    // nothing is mapped unless the caller said so; the by-name wire here is exactly the mass-assignment
    // surface, and the completeness gate could never notice it because the member IS mapped.
    [Fact]
    public void Auto_matched_member_under_an_explicit_only_assembly_reports_DWARFR10()
    {
        const string s = """
                         using DwarfMapper;
                         [assembly: DwarfMapperDefaults(AutoMatchMembers = false)]
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public int Id { get; set; } }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR10");
    }

    // The other half of the boundary: a destination the caller NAMED is a decision, not a coincidence, so it
    // maps. Without this the guard could be "refuse everything" and still pass the fact above.
    [Fact]
    public void An_explicitly_named_destination_is_not_flagged_by_DWARFR10()
    {
        const string s = """
                         using DwarfMapper;
                         [assembly: DwarfMapperDefaults(AutoMatchMembers = false)]
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { [MapProperty("Id")] public int Id { get; set; } }
                         public class Dto { public int Id { get; set; } }
                         """;
        Assert.Empty(GeneratorTestHarness.RunMapTo(s).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // The hole a "did the caller write an attribute?" test would have left open. [MapProperty("")] and
    // [MapProperty(null)] both have arity one — so DWARFR04 does not refuse them — and both leave
    // MemberDirective.Name empty, which sends the binding back to the member's OWN name. That is an IMPLICIT
    // match wearing an attribute, and crediting the attribute's mere presence would let it walk straight
    // through the trust boundary. Keyed on whether the caller NAMED the destination, so it does not.
    //
    // Written because a committed doc claimed this edge was tested when nothing under tests/ mentioned
    // MapProperty(""). Verified to FAIL against the plausible wrong implementation (guarding on
    // Directives.Count == 0), not merely to pass against the right one.
    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void A_MapProperty_that_names_nothing_still_reports_DWARFR10(string argument)
    {
        var s = $$"""
                  using DwarfMapper;
                  [assembly: DwarfMapperDefaults(AutoMatchMembers = false)]
                  namespace Demo;
                  #pragma warning disable CS8625
                  [MapTo(typeof(Dto))] public class Src { [MapProperty({{argument}})] public int Id { get; set; } }
                  public class Dto { public int Id { get; set; } }
                  """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR10");
    }

    // The same two shapes must NOT become errors where no boundary was asked for: the fallback to the
    // member's own name is long-standing behaviour, and the guard above must not leak into an ordinary
    // assembly.
    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void A_MapProperty_that_names_nothing_is_silent_without_the_boundary(string argument)
    {
        var s = $$"""
                  using DwarfMapper;
                  namespace Demo;
                  #pragma warning disable CS8625
                  [MapTo(typeof(Dto))] public class Src { [MapProperty({{argument}})] public int Id { get; set; } }
                  public class Dto { public int Id { get; set; } }
                  """;
        Assert.Empty(GeneratorTestHarness.RunMapTo(s).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // No assembly default, no boundary: the ordinary by-name wire must stay silent, or every [MapTo] in every
    // assembly would now be an error.
    [Fact]
    public void DWARFR10_is_silent_when_the_assembly_says_nothing()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public int Id { get; set; } }
                         """;
        Assert.DoesNotContain(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR10");
    }

    // A refused member must NOT also draw DWARFR02. "Has no source member" would be false — it has one, and
    // declining to wire it is the whole point; two diagnostics about one member, one of them untrue, sends the
    // reader to the wrong fix.
    [Fact]
    public void A_member_refused_by_DWARFR10_is_not_also_reported_as_unmapped()
    {
        const string s = """
                         using DwarfMapper;
                         [assembly: DwarfMapperDefaults(AutoMatchMembers = false)]
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public int Id { get; set; } }
                         """;
        Assert.DoesNotContain(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR02");
    }

    // ── the accessibility of the emitted extension class ────────────────────────
    // Not a diagnostic, but the same defect at the same front door: the registry chose public whenever the
    // types allowed it and never read [assembly: DwarfMapperOptions(PublicExtensions = true)], so the assembly
    // default was honoured for a caller's [DwarfMapper] classes and overridden for their [MapTo] types. Both
    // directions asserted, because a one-sided check passes for an emitter that hard-codes either answer.
    [Fact]
    public void The_registry_extension_class_is_internal_unless_the_assembly_opts_in()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public int Id { get; set; } }
                         """;
        Assert.Contains("internal static class __DwarfRegistry_Src",
            GeneratorTestHarness.RunAll(s).GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_extension_class_is_public_when_the_assembly_opts_in()
    {
        const string s = """
                         using DwarfMapper;
                         [assembly: DwarfMapperOptions(PublicExtensions = true)]
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public int Id { get; set; } }
                         """;
        Assert.Contains("public static class __DwarfRegistry_Src",
            GeneratorTestHarness.RunAll(s).GeneratedSource, StringComparison.Ordinal);
    }

    // The opt-in is a ceiling, not a decision: a non-public type still gets an internal extension class,
    // because a public member over an internal type does not compile.
    [Fact]
    public void The_opt_in_does_not_make_an_internal_typed_registry_extension_public()
    {
        const string s = """
                         using DwarfMapper;
                         [assembly: DwarfMapperOptions(PublicExtensions = true)]
                         namespace Demo;
                         [MapTo(typeof(Dto))] internal class Src { public int Id { get; set; } }
                         internal class Dto { public int Id { get; set; } }
                         """;
        Assert.Contains("internal static class __DwarfRegistry_Src",
            GeneratorTestHarness.RunAll(s).GeneratedSource, StringComparison.Ordinal);
    }

    // ── DWARFR11 — the constructor directive this front door has never read ──────
    // [DwarfMapperConstructor] names the constructor DwarfMapper must build a target with. The registry
    // builds every target with `new T { … }` and calls ConstructorSelector nowhere, so the directive was
    // accepted, ignored and unreported at all of this generator's construction sites. Surface matrix A11-F2.

    [Fact]
    public void An_annotated_constructor_on_a_MapTo_target_reports_DWARFR11()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto
                         {
                             public Dto() { }
                             [DwarfMapperConstructor] public Dto(int id) { Id = id; }
                             public int Id { get; set; }
                         }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR11");
    }

    // The NESTED construction site. Reporting only at the target would have left the identical silence one
    // level down — the shape this branch has had to unpick repeatedly — so the nested-object helper carries
    // the same call. The outer target here is deliberately unannotated, so a passing assertion can only come
    // from the nested site.
    [Fact]
    public void An_annotated_constructor_on_a_NESTED_registry_target_reports_DWARFR11()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Leaf { public int X { get; set; } }
                         public class LeafDto
                         {
                             public LeafDto() { }
                             [DwarfMapperConstructor] public LeafDto(int x) { X = x; }
                             public int X { get; set; }
                         }
                         [MapTo(typeof(Dto))] public class Src { public Leaf Child { get; set; } = new(); }
                         public class Dto { public LeafDto Child { get; set; } = new(); }
                         """;
        Assert.Contains(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR11");
    }

    // Non-vacuity in the other direction: an ordinary registry pair, whose targets carry no annotation
    // anywhere, must stay quiet. Without this the guard could be "always report" and still pass both facts
    // above.
    [Fact]
    public void An_unannotated_registry_pair_is_not_flagged_by_DWARFR11()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         [MapTo(typeof(Dto))] public class Src { public int Id { get; set; } }
                         public class Dto { public Dto() { } public Dto(int id) { Id = id; } public int Id { get; set; } }
                         """;
        Assert.DoesNotContain(GeneratorTestHarness.RunMapTo(s), d => d.Id == "DWARFR11");
    }

    // The remedy the message prescribes, MEASURED rather than asserted: the same annotation on the same
    // target type, mapped through the [DwarfMapper] class model, selects the annotated constructor. A
    // refusal is only honest if the place it points at actually honours the directive.
    [Fact]
    public void The_DWARFR11_remedy_the_class_model_honours_the_annotated_constructor()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Id { get; set; } }
                         public class Dto
                         {
                             public Dto() { }
                             [DwarfMapperConstructor] public Dto(int id) { Id = id; }
                             public int Id { get; set; }
                         }
                         [DwarfMapper] public partial class M { public partial Dto Map(Src s); }
                         """;
        var generated = GeneratorAssert.CompilesClean(s);
        Assert.Contains("new global::Demo.Dto(", generated, StringComparison.Ordinal);
        Assert.Contains("id: s.Id", generated, StringComparison.Ordinal);
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
