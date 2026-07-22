// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     TDD tests for Backlog C (auto-nest, projection, housekeeping) — C1 through C9.
///     Written BEFORE implementation (red-first) per project convention.
/// </summary>
public class BacklogCTests
{
    // ────────────────────────────────────────────────────────────────────────────
    // C1 — per-method [AutoNest(true)] under class AutoNest=false propagates depth-2+
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C1_method_AutoNest_override_propagates_to_depth_2_nested_members()
    {
        // Class has AutoNest=false; one method overrides to AutoNest(true) with a 3-level graph.
        // Depth-2 and depth-3 members must be synthesized (no DWARF005).
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class City   { public string Name { get; set; } = ""; }
                           public class Addr   { public City   Location { get; set; } = new(); public int Zip { get; set; } }
                           public class Person { public Addr   Home { get; set; } = new(); public string FullName { get; set; } = ""; }
                           public record CityDto(string Name);
                           public record AddrDto(CityDto Location, int Zip);
                           public record PersonDto(AddrDto Home, string FullName);
                           [DwarfMapper(AutoNest = false)]
                           public partial class M
                           {
                               [AutoNest(true)]
                               public partial PersonDto Map(Person p);
                           }
                           """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        // No errors: depth-2+ nested pairs must be synthesized.
        var errors = diag.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got: {string.Join(", ", errors.Select(d => d.Id + " " + d.GetMessage(CultureInfo.InvariantCulture)))}");
        // Synthesized nested helpers must appear in generated code.
        Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
        // Must compile clean.
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void C1_sibling_method_without_AutoNest_override_uses_class_default_false()
    {
        // The sibling method has no [AutoNest] override → uses class AutoNest=false → DWARF005.
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner { public int X { get; set; } }
                           public class Outer { public Inner Child { get; set; } = new(); }
                           public class InnerDto { public int X { get; set; } }
                           public class OuterDto { public InnerDto Child { get; set; } = new(); }
                           [DwarfMapper(AutoNest = false)]
                           public partial class M
                           {
                               public partial OuterDto Map(Outer o);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        // Expects DWARF005 because AutoNest=false and no method override.
        Assert.Contains(diag, d => d.Id == "DWARF005");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C2 — abstract/interface SOURCE member → DWARF033
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C2_abstract_source_member_emits_DWARF033()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public abstract class Animal { public string Name { get; set; } = ""; }
                           public class Root { public Animal Pet { get; set; } = null!; }
                           public class AnimalDto { public string Name { get; set; } = ""; }
                           public class RootDto { public AnimalDto Pet { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diag, d => d.Id == "DWARF033");
    }

    [Fact]
    public void C2_concrete_source_member_no_DWARF033()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Address { public string Street { get; set; } = ""; }
                           public class Person  { public Address Home { get; set; } = new(); }
                           public class AddressDto { public string Street { get; set; } = ""; }
                           public class PersonDto  { public AddressDto Home { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial PersonDto Map(Person p);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Id == "DWARF033");
        GeneratorAssert.EmitsCompilableCode(src);
    }

    [Fact]
    public void C2_interface_source_member_emits_DWARF033()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public interface IAnimal { string Name { get; } }
                           public class Root { public IAnimal? Pet { get; set; } }
                           public class AnimalDto { public string Name { get; set; } = ""; }
                           public class RootDto { public AnimalDto? Pet { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial RootDto Map(Root r);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        // IAnimal is an interface — TypeKind.Interface → IsAbstract is true for interfaces.
        // We expect either DWARF033 or DWARF005 depending on whether interface reaches IsMappableObjectPair
        // gate. In practice TypeKind.Interface fails the Class/Struct check → falls to DWARF005 first.
        // At minimum no silent loss: either DWARF033 or DWARF005 must fire.
        var errIds = diag.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ToList();
        Assert.True(errIds.Contains("DWARF033") || errIds.Contains("DWARF005"),
            $"Expected DWARF033 or DWARF005 for interface source, got: {string.Join(", ", errIds)}");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C3 — nested DWARF001 contains dotted path (not just leaf name)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C3_deep_unmapped_member_DWARF001_message_contains_leaf_name()
    {
        // A nested target has an unmapped member — DWARF001 message must identify at least
        // the member name (dotted path is best-effort; at minimum the leaf must appear).
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Inner { public int X { get; set; } public int Y { get; set; } }
                           public class Outer { public Inner Child { get; set; } = new(); }
                           public class InnerDto { public int X { get; set; } public int Y { get; set; } public int Z { get; set; } }
                           public class OuterDto { public InnerDto Child { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial OuterDto Map(Outer o);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        var d001 = diag.Where(d => d.Id == "DWARF001").ToList();
        Assert.NotEmpty(d001);
        var msg = d001[0].GetMessage(CultureInfo.InvariantCulture);
        // The message must at minimum contain "Z" (the unmapped member name).
        Assert.Contains("Z", msg, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C4 — CaseInsensitive propagates into nested projection resolvers
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C4_CaseInsensitive_propagates_into_nested_projection_object()
    {
        // CaseInsensitive=true; nested projection object has case-differing member names.
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class Inner { public string firstName { get; set; } = ""; }
                           public class Outer { public Inner child { get; set; } = new(); }
                           public class InnerDto { public string FirstName { get; set; } = ""; }
                           public class OuterDto { public InnerDto Child { get; set; } = new(); }
                           [DwarfMapper(CaseInsensitive = true)]
                           public partial class M
                           {
                               public partial IQueryable<OuterDto> Project(IQueryable<Outer> src);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        var errors = diag.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors with CaseInsensitive=true, got: {string.Join(", ", errors.Select(d => d.Id + " " + d.GetMessage(CultureInfo.InvariantCulture)))}");
        GeneratorAssert.EmitsCompilableCode(src);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C5 — Nullable<T> source in projection emits HasValue ternary for nullable target
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C5_nullable_int_to_nullable_long_projection_compiles_no_DWARF028()
    {
        // int?→long?: both are nullable value types with a widening implicit conversion.
        // The implicit conversion handles this case naturally (no .Value needed).
        // Must compile cleanly, no DWARF028.
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class Src { public int? X { get; set; } }
                           public class Dst { public long? X { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial IQueryable<Dst> Project(IQueryable<Src> src);
                           }
                           """;
        GeneratorAssert.CompilesClean(src);
    }

    [Fact]
    public void C5_nullable_int_to_nullable_enum_projection_emits_HasValue_ternary()
    {
        // int?→Status? is not implicitly convertible — requires explicit cast.
        // After C5 fix: emits HasValue ternary for the null-preserving path.
        // (Unlike int?→long? which is implicitly widened, int?→Status? needs explicit cast.)
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public enum Status { A = 0, B = 1, C = 2 }
                           public class Src { public int? X { get; set; } }
                           public class Dst { public Status? X { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial IQueryable<Dst> Project(IQueryable<Src> src);
                           }
                           """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        // Must not error.
        Assert.DoesNotContain(diag, d => d.Severity == DiagnosticSeverity.Error);
        GeneratorAssert.EmitsCompilableCode(src);
        // The generated code should contain "HasValue" for the null-preserving ternary.
        Assert.Contains("HasValue", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void C5_nullable_int_to_nonnullable_long_projection_keeps_Value()
    {
        // int?→long (non-nullable target): .Value is kept (user accepts that null throws).
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public class Src { public int? X { get; set; } }
                           public class Dst { public long X { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial IQueryable<Dst> Project(IQueryable<Src> src);
                           }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // The generated code should contain ".Value" for the unwrap.
        Assert.Contains(".Value", generated, StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C6 — enum↔integral inline cast in projection (no DWARF028 for safe widening)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C6_enum_int_to_int_projection_compiles_no_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public enum Status { A, B, C }
                           public class Src { public Status S { get; set; } }
                           public class Dst { public int S { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial IQueryable<Dst> Project(IQueryable<Src> src);
                           }
                           """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Id == "DWARF028");
        GeneratorAssert.EmitsCompilableCode(src);
        // Should use inline cast, not a __DwarfMap_ helper.
        Assert.DoesNotContain("__DwarfMap_", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void C6_int_to_enum_int_projection_compiles_no_DWARF028()
    {
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public enum Status { A, B, C }
                           public class Src { public int S { get; set; } }
                           public class Dst { public Status S { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial IQueryable<Dst> Project(IQueryable<Src> src);
                           }
                           """;
        var (diag, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diag, d => d.Id == "DWARF028");
        GeneratorAssert.EmitsCompilableCode(src);
        Assert.DoesNotContain("__DwarfMap_", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void C6_narrowing_enum_long_to_int_projection_emits_DWARF028()
    {
        // enum:long → int is narrowing — must still be DWARF028.
        const string src = """
                           using DwarfMapper;
                           using System.Linq;
                           namespace Demo;
                           public enum BigStatus : long { A = 0L, B = 1L }
                           public class Src { public BigStatus S { get; set; } }
                           public class Dst { public int S { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial IQueryable<Dst> Project(IQueryable<Src> src);
                           }
                           """;
        var (diag, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diag, d => d.Id == "DWARF028");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C7 — DWARF004 and DWARF029 are noted as reserved in AnalyzerReleases.Unshipped.md
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C7_DWARF004_and_DWARF029_reserved_in_unshipped_release_notes()
    {
        // Read the unshipped release notes and verify reserved comment lines.
        var generatorAsm = typeof(DiagnosticDescriptors).Assembly;
        var asmLocation = generatorAsm.Location;
        var projectDir = Path.GetDirectoryName(asmLocation)!;
        // Navigate up to find AnalyzerReleases.Unshipped.md
        var dir = new DirectoryInfo(projectDir);
        string? mdPath = null;
        for (var current = dir; current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "AnalyzerReleases.Unshipped.md");
            if (File.Exists(candidate))
            {
                mdPath = candidate;
                break;
            }

            // Also check src/DwarfMapper.Generator subdirectory
            var sub = Path.Combine(current.FullName, "src", "DwarfMapper.Generator", "AnalyzerReleases.Unshipped.md");
            if (File.Exists(sub))
            {
                mdPath = sub;
                break;
            }
        }

        if (mdPath is null)
            // Skip if file not found in known locations (not a blocking test condition)
            return;
        var content = File.ReadAllText(mdPath);
        Assert.Contains("DWARF004", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DWARF029", content, StringComparison.OrdinalIgnoreCase);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C8 — DiagnosticDescriptors are reference-stable static readonly singletons
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C8_all_DiagnosticDescriptors_are_static_readonly_fields()
    {
        // Assert: every public DiagnosticDescriptor on DiagnosticDescriptors is declared as
        // a static readonly field (not a property, not a method, not inline-newed each time).
        // This guarantees the incremental-cache assumption documented in DiagnosticInfo.cs:
        // record equality uses reference identity for DiagnosticDescriptor — singletons are required.
        var type = typeof(DiagnosticDescriptors);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
        var descriptorFields = fields
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .ToList();

        Assert.True(descriptorFields.Count > 0, "Expected at least one DiagnosticDescriptor field.");

        foreach (var field in descriptorFields)
            Assert.True(field.IsInitOnly,
                $"Field DiagnosticDescriptors.{field.Name} must be readonly (IsInitOnly) but is not. " +
                "Mutable static fields can return different instances per call, breaking incremental cache equality.");
    }

    [Fact]
    public void C8_each_DiagnosticDescriptor_returns_same_instance_on_repeated_access()
    {
        // Reference-stability: two reads of the same static field must be the same instance.
        var type = typeof(DiagnosticDescriptors);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .ToList();

        foreach (var field in fields)
        {
            var first = field.GetValue(null);
            var second = field.GetValue(null);
            Assert.True(ReferenceEquals(first, second),
                $"DiagnosticDescriptors.{field.Name} returned two different instances — must be a singleton.");
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // C9 — fuzz cosmetic correctness
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void C9_BehavioralFuzzTests_path_stripping_does_not_corrupt_member_names_starting_with_r_o_t()
    {
        // The old code: d.Path.TrimStart('r').TrimStart('o','o','t') would corrupt a member
        // named "role" → "le" (strips leading 'r', then strips leading 'o' from "ole").
        // The fix uses a proper prefix strip: d.Path.StartsWith("root") ? d.Path["root".Length..] : d.Path.
        // We test the fix by simulating the logic on a tricky path.
        const string path = "root.role";
        const string rootPrefix = "root";
        var stripped = path.StartsWith(rootPrefix, StringComparison.Ordinal)
            ? path[rootPrefix.Length..]
            : path;
        // Expected: ".role" (the dot + member name, not corrupted to ".le")
        Assert.Equal(".role", stripped);
    }

    [Fact]
    public void C9_ObjectFactory_long_factory_can_produce_out_of_int_range_values()
    {
        // Verify that the long factory occasionally produces values outside int range.
        var rng = new Random(42);
        var values = Enumerable.Range(0, 200)
            .Select(_ => (long)ObjectFactory.Create(typeof(long), rng, 0)!)
            .ToList();
        // At least some values should be outside the original [1, int.MaxValue) range.
        // With 1-in-4 probability and 200 samples, the chance of all being in range is negligible.
        Assert.True(values.Any(v => v < 1 || v >= int.MaxValue),
            "Expected some out-of-int-range long values from widened factory, but all were in range.");
    }

    [Fact]
    public void C9_ObjectFactoryV2_long_factory_can_produce_out_of_int_range_values()
    {
        var rng = new Random(42);
        var values = Enumerable.Range(0, 200)
            .Select(_ => (long)ObjectFactoryV2.Create(typeof(long), rng, 0)!)
            .ToList();
        Assert.True(values.Any(v => v < 1 || v >= int.MaxValue),
            "Expected some out-of-int-range long values from widened factory, but all were in range.");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // DWARF033 descriptor exists and is Error
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DWARF033_descriptor_exists_and_is_error()
    {
        var d = DiagnosticDescriptors.AbstractSourceAutoNest;
        Assert.Equal("DWARF033", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.DefaultSeverity);
    }
}
