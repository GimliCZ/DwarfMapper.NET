// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Coverage suite for DwarfMapper.Generator.Pipeline.BlittableProof.
// Techniques: unit (Roslyn symbol-based), adversary, deterministic, defensive, fuzzy (seeded), fixture.
namespace DwarfMapper.Generator.Tests.Coverage;

/// <summary>
///     Unit + integration tests for <c>BlittableProof.CanReinterpret</c> / <c>LayoutIdentical</c>
///     (the latter exercised indirectly via CanReinterpret).
/// </summary>
public class BlittableProofCoverageTests
{
    // ─── Compilation helper ───────────────────────────────────────────────────

    /// <summary>
    ///     Compiles <paramref name="source" /> and returns the compilation + all named types defined in it.
    ///     Same reference set as GeneratorTestHarness.
    /// </summary>
    private static (Compilation Compilation, IReadOnlyDictionary<string, INamedTypeSymbol> Types)
        Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "BlitTestAsm_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var decl in root.DescendantNodes()
                     .OfType<TypeDeclarationSyntax>())
        {
            var sym = model.GetDeclaredSymbol(decl);
            if (sym is INamedTypeSymbol named)
                types[named.Name] = named;
        }

        foreach (var decl in root.DescendantNodes()
                     .OfType<EnumDeclarationSyntax>())
        {
            var sym = model.GetDeclaredSymbol(decl);
            if (sym is INamedTypeSymbol named)
                types[named.Name] = named;
        }

        return (compilation, types);
    }

    // ─── CanReinterpret — identity → false ───────────────────────────────────

    [Fact]
    public void CanReinterpret_same_type_returns_false()
    {
        var src = "namespace T { public struct V { public float X; } }";
        var (_, types) = Compile(src);
        var v = types["V"];
        Assert.False(BlittableProof.CanReinterpret(v, v));
    }

    // ─── Both must be unmanaged ───────────────────────────────────────────────

    [Fact]
    public void CanReinterpret_managed_type_returns_false()
    {
        var src = """
                  namespace T {
                      public struct Src { public float X; }
                      public class Dst { public float X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["Src"], types["Dst"]));
    }

    [Fact]
    public void CanReinterpret_struct_with_string_field_returns_false()
    {
        var src = """
                  namespace T {
                      public struct SrcM { public float X; public string S; }
                      public struct DstM { public float X; public string S; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcM"], types["DstM"]));
    }

    // ─── Primitive same SpecialType → true ───────────────────────────────────

    [Fact]
    public void CanReinterpret_same_primitive_special_type_struct_returns_true()
    {
        var src = """
                  namespace T {
                      public struct SrcF { public float X; }
                      public struct DstF { public float X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["SrcF"], types["DstF"]));
    }

    [Fact]
    public void CanReinterpret_different_primitive_special_types_returns_false()
    {
        var src = """
                  namespace T {
                      public struct SrcFI { public float X; }
                      public struct DstFI { public int X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcFI"], types["DstFI"]));
    }

    // ─── TypeKind checks ──────────────────────────────────────────────────────

    [Fact]
    public void CanReinterpret_same_enum_field_type_same_layout_returns_true()
    {
        // Two structs each holding a field of the SAME enum type.
        // LayoutIdentical(Color, Color) → SymbolEqualityComparer same → return true (trivially same layout).
        var src = """
                  namespace T {
                      public enum Color { R, G, B }
                      public struct SrcE { public Color C; }
                      public struct DstE { public Color C; }
                  }
                  """;
        var (_, types) = Compile(src);
        var result = BlittableProof.CanReinterpret(types["SrcE"], types["DstE"]);
        Assert.True(result); // same-type enum field → same layout → true
    }

    [Fact]
    public void CanReinterpret_struct_vs_class_returns_false()
    {
        var src = """
                  namespace T {
                      public class SrcC { public int X; }
                      public struct DstS { public int X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcC"], types["DstS"]));
    }

    // ─── StructLayout: Explicit → false ──────────────────────────────────────

    [Fact]
    public void CanReinterpret_explicit_layout_returns_false()
    {
        var src = """
                  using System.Runtime.InteropServices;
                  namespace T {
                      [StructLayout(LayoutKind.Explicit)]
                      public struct SrcExp { [FieldOffset(0)] public int X; }
                      public struct DstSeq { public int X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcExp"], types["DstSeq"]));
    }

    [Fact]
    public void CanReinterpret_auto_layout_returns_false()
    {
        var src = """
                  using System.Runtime.InteropServices;
                  namespace T {
                      [StructLayout(LayoutKind.Auto)]
                      public struct SrcAuto { public int X; }
                      public struct DstSeq { public int X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcAuto"], types["DstSeq"]));
    }

    // ─── Pack mismatch → false ────────────────────────────────────────────────

    [Fact]
    public void CanReinterpret_different_pack_returns_false()
    {
        var src = """
                  using System.Runtime.InteropServices;
                  namespace T {
                      [StructLayout(LayoutKind.Sequential, Pack = 1)]
                      public struct SrcPack1 { public byte A; public int B; }
                      [StructLayout(LayoutKind.Sequential, Pack = 4)]
                      public struct DstPack4 { public byte A; public int B; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcPack1"], types["DstPack4"]));
    }

    [Fact]
    public void CanReinterpret_same_explicit_pack_continues_to_field_check()
    {
        var src = """
                  using System.Runtime.InteropServices;
                  namespace T {
                      [StructLayout(LayoutKind.Sequential, Pack = 4)]
                      public struct SrcP4 { public int X; public int Y; }
                      [StructLayout(LayoutKind.Sequential, Pack = 4)]
                      public struct DstP4 { public int X; public int Y; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["SrcP4"], types["DstP4"]));
    }

    // ─── Field count / name / order mismatch → false ─────────────────────────

    [Fact]
    public void CanReinterpret_empty_struct_returns_false()
    {
        // Empty struct: fa.Count == 0 → false.
        var src = """
                  namespace T {
                      public struct SrcEmpty { }
                      public struct DstEmpty { }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcEmpty"], types["DstEmpty"]));
    }

    [Fact]
    public void CanReinterpret_different_field_count_returns_false()
    {
        var src = """
                  namespace T {
                      public struct Src2 { public int A; public int B; }
                      public struct Dst3 { public int A; public int B; public int C; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["Src2"], types["Dst3"]));
    }

    [Fact]
    public void CanReinterpret_different_field_names_returns_false()
    {
        var src = """
                  namespace T {
                      public struct SrcAB { public float A; public float B; }
                      public struct DstXY { public float X; public float Y; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcAB"], types["DstXY"]));
    }

    [Fact]
    public void CanReinterpret_reordered_fields_returns_false()
    {
        var src = """
                  namespace T {
                      public struct SrcBA { public int B; public int A; }
                      public struct DstAB { public int A; public int B; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["SrcBA"], types["DstAB"]));
    }

    // ─── Nested struct recursion ──────────────────────────────────────────────

    [Fact]
    public void CanReinterpret_nested_layout_identical_returns_true()
    {
        var src = """
                  namespace T {
                      public struct Inner1 { public float A; public float B; }
                      public struct Inner2 { public float A; public float B; }
                      public struct Outer1 { public Inner1 I; public int N; }
                      public struct Outer2 { public Inner2 I; public int N; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["Outer1"], types["Outer2"]));
    }

    [Fact]
    public void CanReinterpret_nested_inner_field_name_mismatch_returns_false()
    {
        var src = """
                  namespace T {
                      public struct InnA { public float A; }
                      public struct InnZ { public float Z; }
                      public struct Outer1 { public InnA I; }
                      public struct Outer2 { public InnZ I; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["Outer1"], types["Outer2"]));
    }

    [Fact]
    public void CanReinterpret_nested_inner_type_mismatch_returns_false()
    {
        var src = """
                  namespace T {
                      public struct InnFloat { public float A; }
                      public struct InnInt   { public int   A; }
                      public struct Outer1 { public InnFloat I; }
                      public struct Outer2 { public InnInt   I; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["Outer1"], types["Outer2"]));
    }

    [Fact]
    public void CanReinterpret_deeply_nested_identical_returns_true()
    {
        var src = """
                  namespace T {
                      public struct L1a { public int X; }
                      public struct L1b { public int X; }
                      public struct L2a { public L1a I; public int N; }
                      public struct L2b { public L1b I; public int N; }
                      public struct L3a { public L2a I; public float F; }
                      public struct L3b { public L2b I; public float F; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["L3a"], types["L3b"]));
    }

    // ─── Non-source struct (BCL) falls through IsPrimitive ────────────────────

    [Fact]
    public void CanReinterpret_bcl_int32_vs_uint32_no_crash()
    {
        // BCL types have no source locations → IsSourceSequential returns false.
        // Both are primitives (different SpecialType) → IsPrimitive check triggers first.
        var source = "namespace T { public class D {} }";
        var (compilation, _) = Compile(source);
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
        var uint32 = compilation.GetSpecialType(SpecialType.System_UInt32);
        // Both are primitives, different SpecialType → false.
        Assert.False(BlittableProof.CanReinterpret(int32, uint32));
    }

    [Fact]
    public void CanReinterpret_bcl_int32_vs_int32_same_is_false()
    {
        var source = "namespace T { public class D {} }";
        var (compilation, _) = Compile(source);
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
        // Identity → false.
        Assert.False(BlittableProof.CanReinterpret(int32, int32));
    }

    // ─── Decimal field (not unmanaged) ────────────────────────────────────────

    [Fact]
    public void CanReinterpret_struct_with_decimal_field_same_layout_returns_true()
    {
        // decimal is an unmanaged value type in .NET 5+ (it's a struct with no managed fields).
        // Two structs each with a single decimal field having the same field name → layout-identical.
        var src = """
                  namespace T {
                      public struct SrcDec { public decimal D; }
                      public struct DstDec { public decimal D; }
                  }
                  """;
        var (_, types) = Compile(src);
        // decimal IS unmanaged in .NET → these structs are layout-identical.
        // IsPrimitive(decimal) = true (SpecialType.System_Decimal), so same SpecialType → true.
        Assert.True(BlittableProof.CanReinterpret(types["SrcDec"], types["DstDec"]));
    }

    // ─── Seeded property-based fuzz ───────────────────────────────────────────

    [Fact]
    public void Fuzz_seeded_CanReinterpret_T_T_is_always_false()
    {
        // Invariant: CanReinterpret(T, T) must always be false (identity case).
        var templates = new[]
        {
            "public struct S1 { public int A; }",
            "public struct S2 { public float X; public float Y; }",
            "public struct S3 { public byte B1; public byte B2; public short S; }"
        };
        foreach (var template in templates)
        {
            var src = $"namespace T {{ {template} }}";
            var (_, types) = Compile(src);
            var t = types.Values.First();
            Assert.False(BlittableProof.CanReinterpret(t, t),
                $"CanReinterpret(T,T) must be false for {t.Name}");
        }
    }

    [Fact]
    public void Fuzz_seeded_symmetric_layout_identical_struct_pairs_return_true()
    {
        var pairs = new[]
        {
            ("namespace T { public struct Sa { public int A; } public struct Sb { public int A; } }", "Sa", "Sb"),
            ("namespace T { public struct Sa { public float X; public float Y; float Z; } public struct Sb { public float X; public float Y; float Z; } }",
                "Sa", "Sb"),
            ("namespace T { public struct Sa { public long L; public int I; } public struct Sb { public long L; public int I; } }",
                "Sa", "Sb")
        };

        foreach (var (src, nameA, nameB) in pairs)
        {
            var (_, types) = Compile(src);
            Assert.True(BlittableProof.CanReinterpret(types[nameA], types[nameB]),
                $"Expected true for {nameA}/{nameB}");
            Assert.True(BlittableProof.CanReinterpret(types[nameB], types[nameA]),
                $"Expected true for {nameB}/{nameA} (symmetry)");
        }
    }

    // ─── Deterministic: same input produces same result across two calls ──────

    [Fact]
    public void CanReinterpret_result_is_deterministic_across_calls()
    {
        var src = """
                  namespace T {
                      public struct Sa { public int X; public float Y; }
                      public struct Sb { public int X; public float Y; }
                  }
                  """;
        var (_, types) = Compile(src);
        var r1 = BlittableProof.CanReinterpret(types["Sa"], types["Sb"]);
        var r2 = BlittableProof.CanReinterpret(types["Sa"], types["Sb"]);
        Assert.Equal(r1, r2);
        Assert.True(r1);
    }

    // ─── Adversary: single-field struct ──────────────────────────────────────

    [Fact]
    public void CanReinterpret_single_field_identical_struct()
    {
        var src = """
                  namespace T {
                      public struct Sa { public int X; }
                      public struct Sb { public int X; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["Sa"], types["Sb"]));
    }

    [Fact]
    public void CanReinterpret_single_field_different_name_returns_false()
    {
        var src = """
                  namespace T {
                      public struct Sa { public int X; }
                      public struct Sb { public int Y; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["Sa"], types["Sb"]));
    }

    // ─── Fixture: known golden cases ─────────────────────────────────────────

    [Fact]
    public void Fixture_three_float_vec_blits()
    {
        var src = """
                  namespace T {
                      public struct Vec3Src { public float X; public float Y; public float Z; }
                      public struct Vec3Dst { public float X; public float Y; public float Z; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["Vec3Src"], types["Vec3Dst"]));
    }

    [Fact]
    public void Fixture_mixed_int_float_same_names_blits()
    {
        var src = """
                  namespace T {
                      public struct MixedSrc { public int Id; public float Value; }
                      public struct MixedDst { public int Id; public float Value; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.True(BlittableProof.CanReinterpret(types["MixedSrc"], types["MixedDst"]));
    }

    [Fact]
    public void Fixture_different_layout_does_not_blit()
    {
        var src = """
                  namespace T {
                      public struct NotBlitA { public int A; public float B; }
                      public struct NotBlitB { public float A; public int B; }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["NotBlitA"], types["NotBlitB"]));
    }

    // ─── Cover lines 52-53: nb.TypeKind != Struct (e.g., src=struct, dst=enum) ──

    [Fact]
    public void CanReinterpret_struct_vs_enum_returns_false()
    {
        // na=Struct, nb=Enum → nb.TypeKind != TypeKind.Struct → false (lines 52-53).
        var src = """
                  namespace T {
                      public struct MyStruct { public int X; }
                      public enum MyEnum { A, B, C }
                  }
                  """;
        var (_, types) = Compile(src);
        Assert.False(BlittableProof.CanReinterpret(types["MyStruct"], types["MyEnum"]));
    }

    // ─── Cover lines 43-44: a is not INamedTypeSymbol (pointer type = IPointerTypeSymbol) ──
    // Also cover lines 109-110: BCL struct with no source location ─────────────

    [Fact]
    public void CanReinterpret_structs_with_different_pointer_field_types_returns_false()
    {
        // Two structs with pointer fields of DIFFERENT element types (int* vs float*).
        // When LayoutIdentical recurses to compare field types, it compares int* vs float*:
        //   - Different (not identity)
        //   - Both unmanaged (true)
        //   - Neither is primitive (SpecialType.None)
        //   - Neither is INamedTypeSymbol (they are IPointerTypeSymbol) → lines 43-44: return false.
        var source = """
                     namespace T {
                         public unsafe struct SrcP { public int*   P; }
                         public unsafe struct DstP { public float* P; }
                     }
                     """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();
        var compilation = CSharpCompilation.Create(
            "PtrTestAsm_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var decl in root.DescendantNodes()
                     .OfType<TypeDeclarationSyntax>())
        {
            var sym = model.GetDeclaredSymbol(decl);
            if (sym is INamedTypeSymbol named)
                types[named.Name] = named;
        }

        Assert.True(types.ContainsKey("SrcP"), "SrcP type not found");
        Assert.True(types.ContainsKey("DstP"), "DstP type not found");
        // SrcP.P = int*, DstP.P = float* → field types are different pointer types
        // → LayoutIdentical hits lines 43-44: a is not INamedTypeSymbol → return false.
        Assert.False(BlittableProof.CanReinterpret(types["SrcP"], types["DstP"]));
    }

    [Fact]
    public void CanReinterpret_bcl_non_primitive_struct_returns_false_no_source()
    {
        // System.Guid is a struct (not primitive), has no source location.
        // IsSourceSequential returns false (no IsInSource location) → LayoutIdentical returns false.
        var source = "namespace T { public class D {} }";
        var (compilation, _) = Compile(source);
        var guid = compilation.GetTypeByMetadataName("System.Guid")!;
        // guid vs guid: identity check → false immediately (line 17-18).
        Assert.False(BlittableProof.CanReinterpret(guid, guid));
    }

    [Fact]
    public void CanReinterpret_user_struct_vs_bcl_non_primitive_returns_false()
    {
        // User struct vs System.Guid: user has source, BCL does not → IsSourceSequential(guid) = false.
        var source = "namespace T { public struct S { public int X; } }";
        var (compilation, types) = Compile(source);
        var guid = compilation.GetTypeByMetadataName("System.Guid")!;
        Assert.False(BlittableProof.CanReinterpret(types["S"], guid));
    }

    // ─── Cover line 129: [StructLayout(Sequential)] without Pack named arg ──────

    [Fact]
    public void CanReinterpret_explicit_sequential_no_pack_arg_defaults_to_pack_zero()
    {
        // [StructLayout(LayoutKind.Sequential)] without Pack= → IsSourceSequential returns true,
        // pack = 0. Two such structs with matching fields should blit.
        var src = """
                  using System.Runtime.InteropServices;
                  namespace T {
                      [StructLayout(LayoutKind.Sequential)]
                      public struct SrcSeq { public int X; public int Y; }
                      [StructLayout(LayoutKind.Sequential)]
                      public struct DstSeq { public int X; public int Y; }
                  }
                  """;
        var (_, types) = Compile(src);
        // Both are Sequential pack-0 with matching fields → should blit.
        Assert.True(BlittableProof.CanReinterpret(types["SrcSeq"], types["DstSeq"]));
    }
}
