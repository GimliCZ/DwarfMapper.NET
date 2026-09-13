// SPDX-License-Identifier: GPL-2.0-only

using System.Numerics;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Coverage suite for DwarfMapper.Generator.Pipeline.BlittableProof.
// Techniques: unit (Roslyn symbol-based), adversary, deterministic, defensive, fuzzy (seeded), fixture.
namespace DwarfMapper.Generator.Tests.Coverage
{
    /// <summary>
    ///     Unit + integration tests for <c>BlittableProof.CanReinterpret</c> / <c>LayoutIdentical</c>
    ///     (the latter exercised indirectly via CanReinterpret).
    /// </summary>
    public class BlittableProofCoverageTests
    {
        // K0 CROSS-REFERENCE (round-22, P5 → K0, landed): this partial-file fixture was deliberately written
        // as a K0 corpus row in waiting — the same struct pair split across two files, compiled in BOTH file
        // orders, same CanReinterpret verdict. K0 lifted the shape rather than re-inventing it: the descriptor
        // expresses it via NodeSpec.SplitAcrossFiles, and it is pinned END-TO-END (same accept/refuse outcome
        // AND byte-identical generated source in both file orders) as PinnedCorpus row
        // 'P5-K0-partial-split-struct-pair' in tests/DwarfMapper.CompilerTests (PinnedCorpusTests). This test
        // remains the seam-level kill; the corpus row is the emission-level restatement, not a replacement.
        //
        // The verdict the shape gets is REFUSE, in both orders. It was once ACCEPT in both: the proof re-sorted
        // the fields by (ordinal file path, position) so that the compile order could not move the verdict —
        // and that was the defect. The compiler lays a Sequential struct out in the order it received the
        // files, the sort put them in another, and against a twin declared in the sorted order the proof
        // accepted a MemoryMarshal.Cast whose bytes came back swapped (BlitSoundnessTests executes the shape).
        // What the compile order must not move is now "refused": a struct whose instance fields span more
        // than one partial declaration is the shape the compiler itself declines to order (CS0282), and the
        // proof declines with it.
        //
        // The geometry is engineered so the refusal cannot be for a boring reason: in the forward compile
        // order the split struct's field list is POSITIONALLY IDENTICAL to the whole twin's (First, Second),
        // so nothing but the rule stands between the pair and a blit; in the reversed order it is the twin's
        // reverse — the layout the sort used to paper over.
        private const string PartialAlphaFile =
            "namespace T { public partial struct SplitSrc { public int First; } }";

        private const string PartialBetaFile =
            "namespace T { public partial struct SplitSrc { public int Second; } }";

        private const string WholeDstFile =
            "namespace T { public struct WholeDst { public int First; public int Second; } }";

        // ─── Compilation helper ───────────────────────────────────────────────────

        /// <summary>
        ///     Compiles <paramref name="source" /> and returns the compilation + all named types defined in it.
        ///     Same reference set as GeneratorTestHarness.
        /// </summary>
        private static (Compilation Compilation, IReadOnlyDictionary<string, INamedTypeSymbol> Types)
            Compile(string source, bool allowUnsafe = false)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "BlitTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (var decl in root.DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                var sym = model.GetDeclaredSymbol(decl);
                if (sym is { } named)
                {
                    types[named.Name] = named;
                }
            }

            foreach (var decl in root.DescendantNodes()
                         .OfType<EnumDeclarationSyntax>())
            {
                var sym = model.GetDeclaredSymbol(decl);
                if (sym is { } named)
                {
                    types[named.Name] = named;
                }
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
                "public struct S1 { public int A; }", "public struct S2 { public float X; public float Y; }", "public struct S3 { public byte B1; public byte B2; public short S; }"
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
                ("namespace T { public struct Sa { public int A; } public struct Sb { public int A; } }", "Sa", "Sb"), ("namespace T { public struct Sa { public float X; public float Y; float Z; } public struct Sb { public float X; public float Y; float Z; } }",
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
                new[]
                {
                    tree
                },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (var decl in root.DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                var sym = model.GetDeclaredSymbol(decl);
                if (sym is { } named)
                {
                    types[named.Name] = named;
                }
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

        // ─── Round-22 P5 mutation kills ───────────────────────────────────────────

        /// <summary>
        ///     The auto-blit safety gate (T3 kill-first #1): a metadata (BCL) struct carries no guarantee that
        ///     an absent <c>[StructLayout]</c> means the C# default, so <c>IsSourceSequential</c> demands a
        ///     SOURCE declaration. The pre-existing BCL test used a pair that ALSO differed in field names, so
        ///     mutating <c>l.IsInSource</c> to <c>true</c> survived — this pair is deliberately FIELD-COMPATIBLE
        ///     with <c>System.Numerics.Vector2</c> (public float X, Y; no [StructLayout] in metadata), the exact
        ///     shape where that mutant returns an unsafe ACCEPT.
        /// </summary>
        [Fact]
        public void CanReinterpret_field_compatible_bcl_struct_is_still_refused()
        {
            // A dedicated two-reference compilation so GetTypeByMetadataName cannot go null-on-ambiguity
            // the way it can against the whole AppDomain reference sweep.
            var tree = CSharpSyntaxTree.ParseText(
                "namespace T { public struct Vec2User { public float X; public float Y; } }");
            var compilation = CSharpCompilation.Create(
                "BlitBclTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                new MetadataReference[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Vector2).Assembly.Location)
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var decl = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
            var user = model.GetDeclaredSymbol(decl)!;
            var vector2 = compilation.GetTypeByMetadataName("System.Numerics.Vector2");

            Assert.NotNull(vector2); // a null here would make the refusal assertions vacuous
            Assert.Equal(TypeKind.Struct, vector2.TypeKind);
            // Precondition of the kill: the pair really is field-compatible (names + primitive types align),
            // so the ONLY thing standing between it and a blit verdict is the source-declaration gate.
            Assert.Equal(
                user.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name),
                vector2.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).Select(f => f.Name));

            Assert.False(BlittableProof.CanReinterpret(user, vector2));
            Assert.False(BlittableProof.CanReinterpret(vector2, user));
        }

        /// <summary>
        ///     A struct whose instance fields span two partial declarations is refused in BOTH compile orders
        ///     and both directions — and under <c>[Reinterpret]</c> too. The refusal IS the determinism
        ///     guarantee: the compiler orders such a struct's fields by the order it happened to receive the
        ///     files (CS0282 says as much), so its layout is not a fact of the source and nothing about it is
        ///     provable at generation time. The forward order is the kill: there the split list reads
        ///     (First, Second) exactly like the twin, and a proof that compares the two lists positionally
        ///     without asking where the fields were declared accepts it.
        /// </summary>
        [Fact]
        public void CanReinterpret_partial_struct_with_fields_in_two_declarations_is_refused_in_both_compile_orders()
        {
            foreach (var (files, expectedOrder) in new[]
                     {
                         (new[]
                         {
                             ("Alpha.cs", PartialAlphaFile), ("Beta.cs", PartialBetaFile), ("Dst.cs", WholeDstFile)
                         }, new[]
                         {
                             "First", "Second"
                         }),
                         (new[]
                         {
                             ("Beta.cs", PartialBetaFile), ("Alpha.cs", PartialAlphaFile), ("Dst.cs", WholeDstFile)
                         }, new[]
                         {
                             "Second", "First"
                         })
                     })
            {
                var types = CompileFiles(files);
                var order = string.Join(", ", files.Select(f => f.Item1));
                var split = types["SplitSrc"];
                var whole = types["WholeDst"];

                // Geometry: the compiler's field order follows the compile order. Forward, the split struct is
                // positionally identical to its twin and only the rule refuses it; reversed, it is the twin's
                // reverse — the layout a name-aligned sort could not see.
                Assert.Equal(expectedOrder, split.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name));
                Assert.Equal(2, split.DeclaringSyntaxReferences.Length);

                Assert.False(BlittableProof.CanReinterpret(split, whole),
                    $"SplitSrc → WholeDst must be refused under compile order [{order}]");
                Assert.False(BlittableProof.CanReinterpret(whole, split),
                    $"WholeDst → SplitSrc must be refused under compile order [{order}]");
                Assert.False(BlittableProof.SameBytesIgnoringNames(split, whole),
                    $"[Reinterpret] asserts the bytes may be read positionally, and a struct with no defined field order has none to assert about (compile order [{order}])");
            }
        }

        /// <summary>
        ///     The rule is "instance fields span declarations", not "is partial": a partial struct that keeps
        ///     every instance field in ONE declaration — a constant, a static and a method in the other — has
        ///     one defined field order whatever the compile order, and blits against its whole twin as before.
        /// </summary>
        [Fact]
        public void CanReinterpret_partial_struct_with_all_instance_fields_in_one_declaration_still_blits()
        {
            const string fieldsFile = "namespace T { public partial struct SplitSrc { public int First; public int Second; } }";
            const string restFile =
                "namespace T { public partial struct SplitSrc { public const int Limit = 3; public static int Counter; public int Sum() => First + Second; } }";

            foreach (var files in new[]
                     {
                         new[]
                         {
                             ("Fields.cs", fieldsFile), ("Rest.cs", restFile), ("Dst.cs", WholeDstFile)
                         },
                         new[]
                         {
                             ("Rest.cs", restFile), ("Fields.cs", fieldsFile), ("Dst.cs", WholeDstFile)
                         }
                     })
            {
                var types = CompileFiles(files);
                var order = string.Join(", ", files.Select(f => f.Item1));
                var split = types["SplitSrc"];

                // Precondition: it IS the partial shape — two declarations, the second holding members of every
                // other kind — so the refusal above is shown to be a refusal of spanning fields, not of "partial".
                Assert.Equal(2, split.DeclaringSyntaxReferences.Length);
                Assert.Equal(new[]
                    {
                        "First", "Second"
                    },
                    split.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst).Select(f => f.Name));

                Assert.True(BlittableProof.CanReinterpret(split, types["WholeDst"]),
                    $"SplitSrc → WholeDst must blit under compile order [{order}]");
                Assert.True(BlittableProof.CanReinterpret(types["WholeDst"], split),
                    $"WholeDst → SplitSrc must blit under compile order [{order}]");
            }
        }

        /// <summary>
        ///     Decided per DECLARATION, not per file: two parts in one file are two declarations to the compiler
        ///     as well, and its warning (CS0282) is about declarations. A per-file rule would accept this pair.
        /// </summary>
        [Fact]
        public void CanReinterpret_partial_struct_split_within_one_file_is_refused()
        {
            var src = """
                      namespace T {
                          public partial struct SplitSrc { public int First; }
                          public partial struct SplitSrc { public int Second; }
                          public struct WholeDst { public int First; public int Second; }
                      }
                      """;
            var (_, types) = Compile(src);
            var split = types["SplitSrc"];
            Assert.Equal(2, split.DeclaringSyntaxReferences.Length);
            Assert.Single(split.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Distinct());
            Assert.Equal(new[]
                {
                    "First", "Second"
                },
                split.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name));

            Assert.False(BlittableProof.CanReinterpret(split, types["WholeDst"]));
            Assert.False(BlittableProof.CanReinterpret(types["WholeDst"], split));
        }

        /// <summary>
        ///     A backing field has no syntax of its own; it is placed through the member it backs. Two
        ///     auto-properties in two declarations are two backing fields in two declarations — refused — while
        ///     the same two in one declaration, with a method in the other, are placeable and accepted.
        /// </summary>
        [Fact]
        public void CanReinterpret_places_backing_fields_through_the_member_they_back()
        {
            var spanning = """
                           namespace T {
                               public partial struct SplitSrc { public int First { get; set; } }
                               public partial struct SplitSrc { public int Second { get; set; } }
                               public struct WholeDst { public int First { get; set; } public int Second { get; set; } }
                           }
                           """;
            var (_, types) = Compile(spanning);
            Assert.All(types["SplitSrc"].GetMembers().OfType<IFieldSymbol>(), f => Assert.Empty(f.DeclaringSyntaxReferences));
            Assert.False(BlittableProof.CanReinterpret(types["SplitSrc"], types["WholeDst"]));
            Assert.False(BlittableProof.CanReinterpret(types["WholeDst"], types["SplitSrc"]));

            var together = """
                           namespace T {
                               public partial struct SplitSrc { public int First { get; set; } public int Second { get; set; } }
                               public partial struct SplitSrc { public int Sum() => First + Second; }
                               public struct WholeDst { public int First { get; set; } public int Second { get; set; } }
                           }
                           """;
            (_, types) = Compile(together);
            Assert.All(types["SplitSrc"].GetMembers().OfType<IFieldSymbol>(), f => Assert.Empty(f.DeclaringSyntaxReferences));
            Assert.True(BlittableProof.CanReinterpret(types["SplitSrc"], types["WholeDst"]));
            Assert.True(BlittableProof.CanReinterpret(types["WholeDst"], types["SplitSrc"]));
        }

        /// <summary>
        ///     A field that cannot be placed at all — a primary-constructor capture has neither syntax nor an
        ///     owning member — counts as spanning: "cannot tell" is a refusal, never a guess. A single-declaration
        ///     struct is exempt from the question, so the twin here is exactly that, capture and all.
        /// </summary>
        [Fact]
        public void CanReinterpret_refuses_a_partial_struct_whose_field_cannot_be_placed()
        {
            var src = """
                      namespace T {
                          public partial struct SplitSrc(int seed) { public int First = seed; public int Seed => seed; }
                          public partial struct SplitSrc { public int Sum() => First + Seed; }
                          public struct WholeDst(int seed) { public int First = seed; public int Seed => seed; }
                      }
                      """;
            var (_, types) = Compile(src);
            var split = types["SplitSrc"];
            var whole = types["WholeDst"];
            var capture = split.GetMembers().OfType<IFieldSymbol>().Single(f => f.Name != "First");
            Assert.Empty(capture.DeclaringSyntaxReferences);
            Assert.Null(capture.AssociatedSymbol);

            // Precondition: the two field lists line up — capture fields included, since both captures carry the
            // same parameter name — so the unplaceable capture is the only thing refusing the pair.
            Assert.Equal(
                whole.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name),
                split.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name));

            Assert.False(BlittableProof.CanReinterpret(split, whole));
            Assert.False(BlittableProof.CanReinterpret(whole, split));
        }

        // ─── The field list is not the whole layout: Size, [InlineArray], fixed buffers ───

        /// <summary>
        ///     An explicit <c>[StructLayout] Size</c> pads the struct without adding a field: 4 bytes of fields
        ///     become 32 bytes of struct, and the proof once saw only the 4. Refused unless both sides declare
        ///     the same one — and accepted when they do, because then it is the same padding.
        /// </summary>
        [Theory]
        [InlineData("", "[StructLayout(LayoutKind.Sequential, Size = 32)]", false)]
        [InlineData("[StructLayout(LayoutKind.Sequential, Size = 32)]", "", false)]
        [InlineData("[StructLayout(LayoutKind.Sequential, Size = 16)]", "[StructLayout(LayoutKind.Sequential, Size = 32)]", false)]
        [InlineData("[StructLayout(LayoutKind.Sequential, Size = 32)]", "[StructLayout(LayoutKind.Sequential, Size = 32)]", true)]
        public void CanReinterpret_compares_explicit_StructLayout_Size(string srcLayout, string dstLayout, bool expected)
        {
            var src = $$"""
                        using System.Runtime.InteropServices;
                        namespace T {
                            {{srcLayout}} public struct SrcS { public int X; }
                            {{dstLayout}} public struct DstS { public int X; }
                        }
                        """;
            var (_, types) = Compile(src);
            Assert.Equal(expected, BlittableProof.CanReinterpret(types["SrcS"], types["DstS"]));
            Assert.Equal(expected, BlittableProof.CanReinterpret(types["DstS"], types["SrcS"]));
            Assert.Equal(expected, BlittableProof.SameBytesIgnoringNames(types["SrcS"], types["DstS"]));
        }

        /// <summary>
        ///     An <c>[InlineArray(n)]</c> struct repeats its ONE field <c>n</c> times in the runtime layout while
        ///     the symbol model still shows one field, so a plain twin and an inline array of the same element
        ///     — or two inline arrays of different lengths — read as identical to a field-list comparison.
        /// </summary>
        [Theory]
        [InlineData("", "[InlineArray(4)]", false)]
        [InlineData("[InlineArray(4)]", "", false)]
        [InlineData("[InlineArray(4)]", "[InlineArray(8)]", false)]
        [InlineData("[InlineArray(4)]", "[InlineArray(4)]", true)]
        public void CanReinterpret_compares_InlineArray_length(string srcAttr, string dstAttr, bool expected)
        {
            var src = $$"""
                        using System.Runtime.CompilerServices;
                        namespace T {
                            {{srcAttr}} public struct SrcI { public int E; }
                            {{dstAttr}} public struct DstI { public int E; }
                        }
                        """;
            var (_, types) = Compile(src);
            Assert.Equal(expected, BlittableProof.CanReinterpret(types["SrcI"], types["DstI"]));
            Assert.Equal(expected, BlittableProof.CanReinterpret(types["DstI"], types["SrcI"]));
            Assert.Equal(expected, BlittableProof.SameBytesIgnoringNames(types["SrcI"], types["DstI"]));
        }

        /// <summary>
        ///     A fixed buffer's symbol type is the element POINTER type — <c>fixed int Buf[4]</c>,
        ///     <c>fixed int Buf[8]</c> and a genuine <c>int* Buf</c> are all <c>int*</c> to the type comparison —
        ///     while its length, the bytes the runtime reserves, lives on the field. Refused unless both fields
        ///     agree on being a fixed buffer and on its length.
        /// </summary>
        [Theory]
        [InlineData("public unsafe fixed int Buf[4];", "public unsafe fixed int Buf[8];", false)]
        [InlineData("public unsafe fixed int Buf[8];", "public unsafe fixed int Buf[4];", false)]
        [InlineData("public unsafe fixed int Buf[4];", "public unsafe int* Buf;", false)]
        [InlineData("public unsafe int* Buf;", "public unsafe fixed int Buf[4];", false)]
        [InlineData("public unsafe fixed int Buf[4];", "public unsafe fixed int Buf[4];", true)]
        public void CanReinterpret_compares_fixed_buffer_length(string srcField, string dstField, bool expected)
        {
            var src = $$"""
                        namespace T {
                            public struct SrcF { public int Tag; {{srcField}} }
                            public struct DstF { public int Tag; {{dstField}} }
                        }
                        """;
            var (compilation, types) = Compile(src, allowUnsafe: true);
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

            // Precondition: the two fields really are the same type to the type comparison, so the field's own
            // fixed-buffer facts are the only thing that can tell them apart.
            var srcBuf = (IFieldSymbol)types["SrcF"].GetMembers("Buf").Single();
            var dstBuf = (IFieldSymbol)types["DstF"].GetMembers("Buf").Single();
            Assert.True(SymbolEqualityComparer.Default.Equals(srcBuf.Type, dstBuf.Type));

            Assert.Equal(expected, BlittableProof.CanReinterpret(types["SrcF"], types["DstF"]));
            Assert.Equal(expected, BlittableProof.SameBytesIgnoringNames(types["SrcF"], types["DstF"]));
        }

        // ─── Symmetric enum coverage: the na-side TypeKind check (P5 NoCoverage sweep) ───

        [Fact]
        public void CanReinterpret_enum_vs_struct_returns_false()
        {
            // The mirror of CanReinterpret_struct_vs_enum_returns_false: na=Enum drives the FIRST TypeKind
            // check's refusal branch, which no test reached (only the nb-side one was covered).
            var src = """
                      namespace T {
                          public struct MyStruct { public int X; }
                          public enum MyEnum { A, B, C }
                      }
                      """;
            var (_, types) = Compile(src);
            Assert.False(BlittableProof.CanReinterpret(types["MyEnum"], types["MyStruct"]));
        }

        // ─── Round-24 (blit-rest) mutation kills ─────────────────────────────

        /// <summary>
        ///     The source-declaration gate at the one input <see cref="CanReinterpret_field_compatible_bcl_struct_is_still_refused" />
        ///     cannot express: a struct Roslyn reports with ZERO locations.
        ///     <para>
        ///         The gate is spelled <c>!t.Locations.Any(l =&gt; l.IsInSource)</c>, and <c>Any</c> and <c>All</c>
        ///         agree on every NON-empty location array — a metadata struct like <c>Vector2</c> carries one
        ///         metadata location, so it cannot tell the two apart. A <c>ValueTuple&lt;,&gt;</c> written by
        ///         name rather than with tuple syntax has no syntax to point at and comes back with an EMPTY
        ///         array, where <c>All</c> is vacuously TRUE while <c>Any</c> is false — i.e. exactly the shape
        ///         that turns the gate inside out and lets a location-less struct be blitted.
        ///     </para>
        ///     <para>
        ///         The pair is deliberately field-compatible (<c>Item1</c>, <c>Item2</c>, both <c>int</c>, no
        ///         <c>[StructLayout]</c> on either side, both pack 0), so the source-declaration gate is the only
        ///         thing standing between it and an accept — the same non-vacuity discipline as the Vector2 fixture.
        ///     </para>
        /// </summary>
        [Fact]
        public void CanReinterpret_struct_with_no_declaration_location_at_all_is_refused()
        {
            var src = """
                      namespace T {
                          public struct VtHolder { public System.ValueTuple<int, int> Pair; }
                          public struct VtTwin { public int Item1; public int Item2; }
                      }
                      """;
            var (_, types) = Compile(src);
            var valueTuple = types["VtHolder"]
                .GetMembers("Pair")
                .OfType<IFieldSymbol>()
                .Single()
                .Type;

            // Fixture geometry. Without these the refusal below could be true for a boring reason, and the
            // zero-location shape is the whole point — if a future Roslyn starts handing this type a location,
            // this test must say so rather than keep passing over a different input.
            Assert.True(valueTuple.Locations.IsEmpty,
                "fixture geometry broken: this type must report NO locations at all — that empty array, not a " + "metadata location, is what separates Any from All");
            Assert.Equal(TypeKind.Struct, valueTuple.TypeKind);
            Assert.True(valueTuple.IsUnmanagedType, "fixture geometry broken: the tuple must be unmanaged");
            Assert.Equal(
                new[]
                {
                    "Item1", "Item2"
                },
                ((INamedTypeSymbol)valueTuple).GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => !f.IsStatic && !f.IsConst)
                .Select(f => f.Name));
            Assert.Equal(
                new[]
                {
                    "Item1", "Item2"
                },
                types["VtTwin"]
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(f => !f.IsStatic && !f.IsConst)
                    .Select(f => f.Name));

            Assert.False(BlittableProof.CanReinterpret(valueTuple, types["VtTwin"]),
                "a struct with no source declaration must be refused even when it has no locations at all");
            Assert.False(BlittableProof.CanReinterpret(types["VtTwin"], valueTuple),
                "the refusal must hold in both directions");
        }

        /// <summary>Multi-file variant of <see cref="Compile" />: each source gets its own tree with an explicit file path.</summary>
        private static Dictionary<string, INamedTypeSymbol> CompileFiles(IReadOnlyList<(string Path, string Source)> files)
        {
            var trees = files
                .Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path))
                .ToArray();
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "BlitPartialTestAsm_" + Guid.NewGuid().ToString("N"),
                trees,
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
                    if (model.GetDeclaredSymbol(decl) is { } named)
                    {
                        types[named.Name] = named;
                    }
            }

            return types;
        }

        // ─── Arms reached only through these fixtures ─────────────────────────────

        [Fact]
        public void TryExplainNearMiss_two_empty_structs_are_not_a_near_miss()
        {
            // Distinct, unmanaged, same kind, and no instance fields at all: nothing about the pair suggests a blit.
            var (_, types) = Compile("namespace T { public struct E1 { } public struct E2 { } }");

            Assert.False(BlittableProof.TryExplainNearMiss(types["E1"], types["E2"], out var reason));
            Assert.Equal(string.Empty, reason);
        }

        [Fact]
        public void IsSourceSequential_short_layout_overload_is_sequential()
        {
            // StructLayoutAttribute(short) carries its kind as a short, not an int; 0 is Sequential.
            var (_, types) = Compile("using System.Runtime.InteropServices; namespace T { [StructLayout((short)0)] public struct S { public int X; } }");

            Assert.True(BlittableProof.IsSourceSequential(types["S"], out var pack, out var size));
            Assert.Equal(0, pack);
            Assert.Equal(0, size);
        }

        [Fact]
        public void IsSourceSequential_layout_attribute_without_arguments_is_sequential()
        {
            // [StructLayout] with no kind is CS7036 in the consumer's source; with no kind to read, the struct keeps the
            // C# default.
            var (_, types) = Compile("using System.Runtime.InteropServices; namespace T { [StructLayout] public struct S { public int X; } }");

            Assert.True(BlittableProof.IsSourceSequential(types["S"], out var pack, out var size));
            Assert.Equal(0, pack);
            Assert.Equal(0, size);
        }

        [Fact]
        public void EnumUnderlying_answers_an_enum_its_backing_type_and_anything_else_none()
        {
            var (compilation, types) = Compile("namespace T { public enum Kind : long { A } public class C { } }");

            Assert.Equal(SpecialType.System_Int64, BlittableProof.EnumUnderlying(types["Kind"]));
            Assert.Equal(SpecialType.None, BlittableProof.EnumUnderlying(types["C"]));
            Assert.Equal(SpecialType.None,
                BlittableProof.EnumUnderlying(compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Int32))));
        }

        [Fact]
        public void SameSpecialType_requires_the_same_real_special_type()
        {
            Assert.True(BlittableProof.SameSpecialType(SpecialType.System_Int32, SpecialType.System_Int32));
            Assert.False(BlittableProof.SameSpecialType(SpecialType.System_Int32, SpecialType.System_Int64));
            Assert.False(BlittableProof.SameSpecialType(SpecialType.None, SpecialType.None));
        }

        [Fact]
        public void IsSourceSequential_pack_and_size_that_are_not_ints_are_ignored()
        {
            // Pack = "x" and Size = "y" are CS0029 in the consumer's source. The compiler still records both named
            // arguments, as error constants with no value, so neither is read as a pack or a size.
            var (_, types) = Compile("""
                                     using System.Runtime.InteropServices;
                                     namespace T { [StructLayout(LayoutKind.Sequential, Pack = "x", Size = "y")] public struct S { public int X; } }
                                     """);

            Assert.True(BlittableProof.IsSourceSequential(types["S"], out var pack, out var size));
            Assert.Equal(0, pack);
            Assert.Equal(0, size);
        }

        [Fact]
        public void DeclaringPart_of_an_enum_member_is_none()
        {
            // An enum member is a field declared inside an enum declaration, which is not a type declaration.
            var (_, types) = Compile("namespace T { public enum Kind { A } }");
            var member = types["Kind"].GetMembers("A").OfType<IFieldSymbol>().Single();

            Assert.Null(BlittableProof.DeclaringPart(member));
        }
    }
}
