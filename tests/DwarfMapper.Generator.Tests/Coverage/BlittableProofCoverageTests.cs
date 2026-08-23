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
        // remains the seam-level kill (the comparator-mutant geometry below needs BlittableProof directly);
        // the corpus row is the emission-level restatement, not a replacement.
        //
        // The geometry is engineered so every comparator mutant diverges:
        //  - "Alpha.cs" < "Beta.cs" ordinally, and the field declared in Alpha.cs is the one that must sort
        //    FIRST, so deleting the sort (or neutering the file-path key) breaks the reversed compile order;
        //  - the Alpha.cs field sits at a HIGHER source offset than the Beta.cs field (the padding comment
        //    below), so a mutant that compares POSITIONS across files ('byFile != 0' → '== 0') inverts the
        //    order even in the forward compile order.
        private const string PartialAlphaFile =
            "// Padding so that the field declared in this file sits at a HIGHER SourceSpan.Start than the\n" +
            "// field declared in Beta.cs — see the comparator-mutant geometry note above the fixture.\n" +
            "namespace T { public partial struct SplitSrc { public int First; } }";

        private const string PartialBetaFile =
            "namespace T { public partial struct SplitSrc { public int Second; } }";

        private const string WholeDstFile =
            "namespace T { public struct WholeDst { public int First; public int Second; } }";

        // Five: above the two- and three-element special cases in List<T>.Sort and below the seventeen-element
        // threshold where the quicksort partition refuses an inconsistent comparator — the band in which the
        // sort's insertion path actually depends on the comparator's position key. See
        // CanReinterpret_one_field_per_file_split_matches_its_single_file_twin.
        private const int WideFieldCount = 5;
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
                new[]
                {
                    tree
                },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
            var user = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
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
        ///     The determinism guarantee of <c>InstanceFields</c> (T3 kill-first #2): <c>GetMembers()</c> order
        ///     for a struct split across partial files depends on the order the compiler saw the files, and the
        ///     blit proof compares fields positionally — the sort by (file path, position) is the only thing
        ///     making the verdict build-order-independent. Deleting that sort outright survived the whole suite
        ///     until this fixture: same struct pair, both compile orders, same verdict, both directions.
        /// </summary>
        [Fact]
        public void CanReinterpret_partial_file_struct_verdict_is_file_order_independent()
        {
            foreach (var files in new[]
                     {
                         new[]
                         {
                             ("Alpha.cs", PartialAlphaFile), ("Beta.cs", PartialBetaFile), ("Dst.cs", WholeDstFile)
                         },
                         new[]
                         {
                             ("Beta.cs", PartialBetaFile), ("Alpha.cs", PartialAlphaFile), ("Dst.cs", WholeDstFile)
                         }
                     })
            {
                var (_, types) = CompileFiles(files);
                var order = string.Join(", ", files.Select(f => f.Item1));

                // Precondition of the L83 kill: the cross-file source positions really are inverted relative
                // to the sorted field order (First@Alpha.cs starts AFTER Second@Beta.cs).
                var split = types["SplitSrc"];
                var first = (IFieldSymbol)split.GetMembers("First").Single();
                var second = (IFieldSymbol)split.GetMembers("Second").Single();
                Assert.True(first.Locations[0].SourceSpan.Start > second.Locations[0].SourceSpan.Start,
                    "fixture geometry broken: First must sit at a higher offset than Second");

                Assert.True(BlittableProof.CanReinterpret(split, types["WholeDst"]),
                    $"SplitSrc → WholeDst must blit under compile order [{order}]");
                Assert.True(BlittableProof.CanReinterpret(types["WholeDst"], split),
                    $"WholeDst → SplitSrc must blit under compile order [{order}]");
            }
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

        /// <summary>
        ///     The WIDE half of the InstanceFields determinism guarantee: the same field list declared one field
        ///     per file on one side and all in one file on the other must reach the same order, hence the same
        ///     verdict.
        ///     <para>
        ///         <see cref="CanReinterpret_partial_file_struct_verdict_is_file_order_independent" /> pins the
        ///         file-path key, but its structs hold TWO fields each — and a two-element
        ///         <c>List&lt;T&gt;.Sort</c> is one <c>SwapIfGreater(keys[0], keys[1])</c> call, which a
        ///         comparator that has lost its POSITION key can still get right by accident. Four to sixteen
        ///         elements take the insertion-sort path instead (two and three are special-cased above it,
        ///         seventeen and up reach the quicksort partition, which rejects an inconsistent comparator
        ///         outright), and there a comparator whose left-hand position is stuck at zero claims every
        ///         candidate sorts before everything already placed — which REVERSES a same-file field list.
        ///     </para>
        ///     <para>
        ///         Hence the geometry: five fields, so the single-file side reverses while the one-per-file side
        ///         is ordered entirely by its file-path key and does not, and the two lists stop lining up by
        ///         name. Both compile orders and both directions, as the two-field fixture does.
        ///     </para>
        /// </summary>
        [Fact]
        public void CanReinterpret_one_field_per_file_split_matches_its_single_file_twin()
        {
            IReadOnlyList<(string Path, string Source)> forward = WideFiles();
            IReadOnlyList<(string Path, string Source)> backward = forward.Reverse()
                .ToList();

            foreach (var files in new[]
                     {
                         forward, backward
                     })
            {
                var (_, types) = CompileFiles(files);
                var order = string.Join(", ", files.Select(f => f.Path));
                var split = types["SplitWide"];
                var whole = types["WholeWide"];

                // Geometry 1: the split side really does put every field in its OWN file, so its order comes
                // from the file-path key alone and the position tie-break never speaks for it.
                Assert.Equal(
                    Enumerable.Range(1, WideFieldCount)
                        .Select(i => $"W{i}.cs"),
                    split.GetMembers()
                        .OfType<IFieldSymbol>()
                        .Select(f => f.Locations[0].SourceTree!.FilePath)
                        .OrderBy(p => p, StringComparer.Ordinal));

                // Geometry 2: the whole side really does put every field in ONE file at ascending, non-zero
                // offsets, so the position tie-break is the only thing ordering it.
                var wholeFields = whole.GetMembers()
                    .OfType<IFieldSymbol>()
                    .ToList();
                Assert.Equal(WideFieldCount, wholeFields.Count);
                Assert.All(wholeFields,
                    f => Assert.Equal("WholeWide.cs", f.Locations[0].SourceTree!.FilePath));
                var positions = wholeFields.Select(f => f.Locations[0].SourceSpan.Start)
                    .ToList();
                Assert.All(positions, p => Assert.True(p > 0, "fixture geometry broken: a zero source offset"));
                Assert.Equal(positions.OrderBy(p => p), positions);

                Assert.True(BlittableProof.CanReinterpret(split, whole),
                    $"SplitWide → WholeWide must blit under compile order [{order}]");
                Assert.True(BlittableProof.CanReinterpret(whole, split),
                    $"WholeWide → SplitWide must blit under compile order [{order}]");
            }
        }

        /// <summary>
        ///     The wide fixture's files: <c>W1.cs</c>…<c>W5.cs</c> each contributing one field to a partial
        ///     <c>SplitWide</c>, plus one file holding the whole twin. The file names sort ordinally in the same
        ///     order as the fields they declare, so the expected order is the obvious one in both compile orders.
        /// </summary>
        private static List<(string Path, string Source)> WideFiles()
        {
            var files = new List<(string Path, string Source)>();
            for (var i = 1; i <= WideFieldCount; i++)
                files.Add(($"W{i}.cs", $"namespace T {{ public partial struct SplitWide {{ public int F{i}; }} }}"));

            var body = string.Join(" ",
                Enumerable.Range(1, WideFieldCount)
                    .Select(i => $"public int F{i};"));
            files.Add(("WholeWide.cs", "namespace T { public struct WholeWide { " + body + " } }"));
            return files;
        }

        /// <summary>Multi-file variant of <see cref="Compile" />: each source gets its own tree with an explicit file path.</summary>
        private static (Compilation Compilation, IReadOnlyDictionary<string, INamedTypeSymbol> Types)
            CompileFiles(IReadOnlyList<(string Path, string Source)> files)
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

            return (compilation, types);
        }
    }
}
