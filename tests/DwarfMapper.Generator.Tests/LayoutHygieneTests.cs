// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>LayoutHygiene.Measure</c> and <c>DWARF101</c> — the struct-padding hint (round 29, T0.3).
    ///     <para>
    ///         Half of these tests assert SILENCE, and they are the half that keeps the diagnostic worth
    ///         having. An informational hint that fires on ordinary transfer-model structs gets suppressed
    ///         wholesale by the first consumer who meets it, taking the cases worth reading with it — the
    ///         lesson <c>BlittableProof.TryExplainNearMiss</c>'s doc comment already records for
    ///         <c>DWARF100</c>. So both thresholds are pinned from BOTH sides: a struct one byte under the
    ///         padding floor, and one percentage point under the quarter, must stay quiet.
    ///     </para>
    ///     <para>
    ///         Every expected SIZE below was verified against the runtime with <c>Unsafe.SizeOf&lt;T&gt;</c>
    ///         before it was written down (round 29, T0.3): the numbers are measurements, not a restatement
    ///         of the model being tested.
    ///     </para>
    /// </summary>
    public class LayoutHygieneTests
    {
        /// <summary>
        ///     Compiles <paramref name="source" /> and returns every named type declared in it, keyed by name.
        ///     Same shape as <c>BlittableProofCoverageTests.Compile</c> — a symbol-level unit test needs a
        ///     compilation and nothing else.
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
                "LayoutTestAsm_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    tree
                },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                if (model.GetDeclaredSymbol(decl) is { } named)
                {
                    types[named.Name] = named;
                }

            foreach (var decl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
                if (model.GetDeclaredSymbol(decl) is { } named)
                {
                    types[named.Name] = named;
                }

            return (compilation, types);
        }

        private static LayoutHygiene.Layout MeasureType(string source, string typeName, bool allowUnsafe = false)
        {
            var (_, types) = Compile(source, allowUnsafe);
            var measured = LayoutHygiene.Measure(types[typeName]);
            Assert.NotNull(measured);
            return measured!.Value;
        }

        private static LayoutHygiene.Layout? TryMeasureType(string source, string typeName, bool allowUnsafe = false)
        {
            var (_, types) = Compile(source, allowUnsafe);
            return LayoutHygiene.Measure(types[typeName]);
        }

        // ─── Measure: the numbers ────────────────────────────────────────────────

        /// <summary>
        ///     The plan's headline fixture. Verified against the runtime: <c>Unsafe.SizeOf</c> is 40, and
        ///     <c>Marshal.OffsetOf</c> puts the five fields at 0 / 8 / 16 / 24 / 32.
        ///     <para>
        ///         The brief said "padding 15". It is 20, and 15 matches no definition of the word: the payload
        ///         is 1 + 8 + 1 + 8 + 2 = 20 bytes in a 40-byte struct, interior-only padding is 14, and
        ///         size-minus-packed is 16. The measured number is what this pins.
        ///     </para>
        ///     <para>
        ///         The packed order is a STABLE sort by alignment descending, so the two one-byte fields keep
        ///         their declaration order: <c>A</c> (bool) is declared before <c>C</c> (byte). The brief's
        ///         "…short, byte, bool" is not a stable sort of this declaration.
        ///     </para>
        /// </summary>
        [Fact]
        public void Measure_reports_size_padding_and_the_packed_field_order()
        {
            var layout = MeasureType(
                "namespace T { public struct S { public bool A; public long B; public byte C; public double D; public short E; } }",
                "S");

            Assert.Equal(40, layout.Size);
            Assert.Equal(20, layout.Padding);
            Assert.Equal(24, layout.PackedSize);
            Assert.Equal("B, D, E, A, C", layout.PackedOrderText);
        }

        /// <summary>A struct that is already packed wastes nothing and reports its own order back.</summary>
        [Fact]
        public void Measure_reports_no_padding_for_a_struct_that_is_already_packed()
        {
            var layout = MeasureType(
                "namespace T { public struct Vec3 { public float X; public float Y; public float Z; } }",
                "Vec3");

            Assert.Equal(12, layout.Size);
            Assert.Equal(0, layout.Padding);
            Assert.Equal(12, layout.PackedSize);
            Assert.Equal("X, Y, Z", layout.PackedOrderText);
        }

        /// <summary>
        ///     <c>Nullable&lt;T&gt;</c> is <c>{bool hasValue; T value}</c> laid out sequentially: 1 byte, padded
        ///     to T's alignment, then T. Measured at 20 for a 16-byte, 4-aligned T.
        /// </summary>
        [Fact]
        public void Measure_lays_out_Nullable_as_a_flag_padded_to_the_value()
        {
            var (compilation, types) = Compile(
                "namespace T { public struct Addr16 { public uint A; public uint B; public uint C; public uint D; } }");
            var nullable = compilation.GetTypeByMetadataName("System.Nullable`1")!.Construct(types["Addr16"]);

            var layout = LayoutHygiene.Measure(nullable);

            Assert.NotNull(layout);
            Assert.Equal(20, layout!.Value.Size);
            Assert.Equal(4, layout.Value.Alignment);
        }

        /// <summary>
        ///     A nested struct is measured recursively and then counted as ONE field at its full size — its own
        ///     internal padding belongs to its own declaration, never to the enclosing type's remedy.
        /// </summary>
        [Fact]
        public void Measure_counts_a_nested_struct_as_one_field_of_its_measured_size()
        {
            var layout = MeasureType(
                """
                namespace T
                {
                    public struct Vec3 { public float X; public float Y; public float Z; }
                    public struct Nested { public byte A; public Vec3 V; public byte B; }
                }
                """,
                "Nested");

            // byte@0, Vec3(12, align 4)@4, byte@16, rounded to 20. Payload 1 + 12 + 1 = 14.
            Assert.Equal(20, layout.Size);
            Assert.Equal(6, layout.Padding);
            Assert.Equal("V, A, B", layout.PackedOrderText);
        }

        /// <summary>
        ///     The enclosing type's padding never absorbs a nested type's own waste: reordering the outer fields
        ///     could not recover it, so attributing it there would print a remedy that does not work.
        /// </summary>
        [Fact]
        public void Measure_does_not_charge_the_outer_struct_for_a_nested_types_own_padding()
        {
            var layout = MeasureType(
                """
                namespace T
                {
                    public struct Inner { public byte A; public long B; }
                    public struct Outer { public Inner I; public long L; }
                }
                """,
                "Outer");

            // Inner is 16 bytes (7 of them its own padding); Outer is 16 + 8 with nothing wasted at its level.
            Assert.Equal(24, layout.Size);
            Assert.Equal(0, layout.Padding);
        }

        /// <summary>An enum field is measured as its underlying primitive.</summary>
        [Fact]
        public void Measure_reads_an_enum_field_as_its_underlying_primitive()
        {
            var layout = MeasureType(
                """
                namespace T
                {
                    public enum E8 : long { X }
                    public struct WithEnum { public byte A; public E8 B; }
                }
                """,
                "WithEnum");

            Assert.Equal(16, layout.Size);
            Assert.Equal(7, layout.Padding);
        }

        /// <summary>
        ///     An auto-property's backing field is named <c>&lt;Value&gt;k__BackingField</c>, which is not
        ///     something a consumer can type — and a transfer model written in properties is the common case,
        ///     not an exotic one. The remedy names the PROPERTY they would move; the layout is the backing
        ///     field's, and it is laid out where the property is declared.
        /// </summary>
        [Fact]
        public void Measure_names_the_property_rather_than_its_backing_field()
        {
            var layout = MeasureType(
                """
                namespace T
                {
                    public struct S
                    {
                        public bool Ok { get; set; }
                        public long Id { get; set; }
                        public byte Kind { get; set; }
                        public double Value { get; set; }
                        public short Code { get; set; }
                    }
                }
                """,
                "S");

            Assert.Equal(40, layout.Size);
            Assert.Equal(20, layout.Padding);
            Assert.Equal("Id, Value, Code, Ok, Kind", layout.PackedOrderText);
        }

        // ─── Measure: the refusals ───────────────────────────────────────────────

        /// <summary>
        ///     The native-sized refusal, inherited from <c>BlittableProof.PrimitiveSize</c>: <c>IntPtr</c> and
        ///     <c>UIntPtr</c> are as wide as the platform, so a size measured here would describe the build
        ///     machine and not the machine the consumer runs on. There is no number to report, so nothing is.
        /// </summary>
        [Theory]
        [InlineData("System.IntPtr")]
        [InlineData("System.UIntPtr")]
        [InlineData("nint")]
        [InlineData("nuint")]
        public void Measure_refuses_a_struct_with_a_native_sized_field(string fieldType)
        {
            Assert.Null(TryMeasureType(
                $"namespace T {{ public struct S {{ public byte A; public {fieldType} P; }} }}",
                "S"));
        }

        /// <summary>
        ///     Every shape whose bytes this generator may not claim to know. Each would otherwise produce a
        ///     confident number that is wrong, and a remedy the consumer cannot act on.
        /// </summary>
        [Theory]
        // A metadata struct: an absent [StructLayout] cannot be read as Sequential, and the consumer cannot
        // reorder a type they do not declare.
        [InlineData("Metadata", "namespace T { public struct Holder { public System.Guid G; } }", "System.Guid")]
        // Non-Sequential layout: the runtime is free to reorder.
        [InlineData("Auto",
            "using System.Runtime.InteropServices; namespace T { [StructLayout(LayoutKind.Auto)] public struct S { public byte A; public long B; } }",
            "S")]
        [InlineData("Explicit",
            "using System.Runtime.InteropServices; namespace T { [StructLayout(LayoutKind.Explicit)] public struct S { [FieldOffset(0)] public byte A; [FieldOffset(8)] public long B; } }",
            "S")]
        // An explicit Pack changes every offset the natural-alignment model computes.
        [InlineData("Pack",
            "using System.Runtime.InteropServices; namespace T { [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public byte A; public long B; } }",
            "S")]
        // An explicit Size is a floor the runtime pads up to: the fields no longer decide the bytes.
        [InlineData("Size",
            "using System.Runtime.InteropServices; namespace T { [StructLayout(LayoutKind.Sequential, Size = 64)] public struct S { public byte A; public long B; } }",
            "S")]
        // [InlineArray] repeats its single field n times; the field list is not the layout.
        [InlineData("InlineArray",
            "using System.Runtime.CompilerServices; namespace T { [InlineArray(8)] public struct S { public long E; } }",
            "S")]
        // CS0282: the compiler defines no field order across partial declarations, so there is none to restate.
        [InlineData("SplitPartials",
            "namespace T { public partial struct S { public byte A; } public partial struct S { public long B; } }",
            "S")]
        // An empty struct has no field order to report and no padding to save.
        [InlineData("Empty", "namespace T { public struct S { } }", "S")]
        // A managed field: not an unmanaged struct, so the blit question this serves does not arise.
        [InlineData("Managed", "namespace T { public struct S { public byte A; public string B; } }", "S")]
        // A class is not laid out by these rules at all.
        [InlineData("Class", "namespace T { public class S { public byte A; public long B; } }", "S")]
        // decimal has no width PrimitiveSize will claim, and its own layout is metadata's business.
        [InlineData("Decimal", "namespace T { public struct S { public byte A; public decimal B; } }", "S")]
        public void Measure_refuses_a_shape_whose_bytes_it_cannot_prove(string shape, string source, string typeName)
        {
            var (compilation, types) = Compile(source);
            var symbol = types.TryGetValue(typeName, out var declared)
                ? declared
                : compilation.GetTypeByMetadataName(typeName)!;

            Assert.True(LayoutHygiene.Measure(symbol) is null, shape + " was measured, and must not be");
        }

        /// <summary>A fixed-size buffer's length is what the runtime reserves; the field type is only a pointer.</summary>
        [Fact]
        public void Measure_refuses_a_fixed_size_buffer()
        {
            Assert.Null(TryMeasureType(
                "namespace T { public struct S { public int Tag; public unsafe fixed int Buf[8]; } }",
                "S",
                true));
        }

        // ─── The thresholds, pinned from both sides ──────────────────────────────

        /// <summary>
        ///     Exactly on both edges: 8 bytes of padding in 32 bytes is a quarter. Both thresholds are
        ///     inclusive, so this is the smallest struct the hint speaks about.
        /// </summary>
        [Fact]
        public void The_hint_fires_on_a_struct_exactly_at_both_thresholds()
        {
            var layout = MeasureType(
                "namespace T { public struct S { public int A; public long B; public int C; public long D; } }",
                "S");

            Assert.Equal(32, layout.Size);
            Assert.Equal(8, layout.Padding);
            Assert.True(LayoutHygiene.WastesAQuarter(layout));
        }

        /// <summary>
        ///     Over the quarter, one byte under the floor. This is the ORDINARY struct — a flag beside an
        ///     identifier — and it is the single most important silence in this file: a hint that fired here
        ///     would fire on half the transfer models in existence.
        /// </summary>
        [Fact]
        public void The_hint_stays_silent_one_byte_under_the_padding_floor()
        {
            var layout = MeasureType(
                "namespace T { public struct S { public byte A; public long B; } }",
                "S");

            Assert.Equal(16, layout.Size);
            Assert.Equal(7, layout.Padding);
            Assert.False(LayoutHygiene.WastesAQuarter(layout));
        }

        /// <summary>Well over the byte floor, just under the quarter: 14 bytes in 64 is 21.9%.</summary>
        [Fact]
        public void The_hint_stays_silent_just_under_the_quarter()
        {
            var layout = MeasureType(
                """
                namespace T
                {
                    public struct S
                    {
                        public byte A; public long B; public byte C; public long D;
                        public long E; public long F; public long G; public long H;
                    }
                }
                """,
                "S");

            Assert.Equal(64, layout.Size);
            Assert.Equal(14, layout.Padding);
            Assert.False(LayoutHygiene.WastesAQuarter(layout));
        }

        // ─── DWARF101 at the mapping site ────────────────────────────────────────

        private const string PaddedPair = """
                                          using DwarfMapper;
                                          namespace Demo;
                                          public struct Sample
                                          {
                                              public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                                          }
                                          public class C { public Sample[] V { get; set; } = System.Array.Empty<Sample>(); }
                                          public class D { public Sample[] V { get; set; } = System.Array.Empty<Sample>(); }
                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                          """;

        private static List<Diagnostic> Run(string source, bool allowUnsafe = false)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source, allowUnsafe: allowUnsafe);
            return diagnostics.Where(d => d.Id == "DWARF101").ToList();
        }

        /// <summary>
        ///     The reporting case, message and all. Asserted whole rather than by substring: the wording is
        ///     pinned in <c>docs/diagnostics.md</c> and in the NegativeCases row, and a substring assertion
        ///     could not have caught a reworded remedy.
        /// </summary>
        [Fact]
        public void A_padded_element_struct_is_named_with_the_field_order_that_packs_it()
        {
            var reported = Run(PaddedPair);

            var one = Assert.Single(reported);
            Assert.Equal(
                "'Demo.Sample' is 40 bytes with 20 bytes of padding; declaring its fields as " +
                "Id, Value, Code, Ok, Kind makes it 24 bytes — smaller arrays, and a layout-identical twin " +
                "can take the blit",
                one.GetMessage(CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     Two members of the same padded type is ONE report, not two. An Info repeated per member is the
        ///     shape consumers suppress wholesale.
        /// </summary>
        [Fact]
        public void The_same_padded_struct_is_named_once_however_many_members_reach_it()
        {
            var reported = Run("""
                               using DwarfMapper;
                               namespace Demo;
                               public struct Sample
                               {
                                   public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                               }
                               public class C
                               {
                                   public Sample[] First { get; set; } = System.Array.Empty<Sample>();
                                   public Sample[] Second { get; set; } = System.Array.Empty<Sample>();
                                   public System.Collections.Generic.List<Sample> Third { get; set; } = new();
                               }
                               public class D
                               {
                                   public Sample[] First { get; set; } = System.Array.Empty<Sample>();
                                   public Sample[] Second { get; set; } = System.Array.Empty<Sample>();
                                   public System.Collections.Generic.List<Sample> Third { get; set; } = new();
                               }
                               [DwarfMapper] public partial class M { public partial D Map(C c); }
                               """);

            Assert.Single(reported);
        }

        /// <summary>
        ///     A pair that DOES blit is reported too. The hint is about the bytes the array carries, not about
        ///     a refusal — and the fast path copies the padding along with everything else.
        /// </summary>
        [Fact]
        public void A_pair_that_takes_the_block_copy_is_still_told_about_its_padding()
        {
            var reported = Run("""
                               using DwarfMapper;
                               namespace Demo;
                               public struct SrcV
                               {
                                   public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                               }
                               public struct DstV
                               {
                                   public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                               }
                               public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                               public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                               [DwarfMapper] public partial class M { public partial D Map(C c); }
                               """);

            // Both sides are padded and both are the consumer's to reorder, so both are named — reordering
            // only one of a blitting pair would break the twin and lose the block copy in silence.
            Assert.Equal(2, reported.Count);
            Assert.Contains(reported, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'Demo.SrcV'", StringComparison.Ordinal));
            Assert.Contains(reported, d => d.GetMessage(CultureInfo.InvariantCulture).Contains("'Demo.DstV'", StringComparison.Ordinal));
        }

        /// <summary>An ordinary, already-tight transfer model says nothing at all.</summary>
        [Fact]
        public void An_ordinary_element_struct_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             namespace Demo;
                             public struct Point { public int X; public int Y; }
                             public class C { public Point[] V { get; set; } = System.Array.Empty<Point>(); }
                             public class D { public Point[] V { get; set; } = System.Array.Empty<Point>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A struct one byte under the padding floor at the MAPPING site, not just in the predicate — the
        ///     threshold has to be the one the report site actually applies.
        /// </summary>
        [Fact]
        public void An_element_struct_under_the_padding_floor_reports_nothing_at_the_mapping_site()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             namespace Demo;
                             public struct Flagged { public byte Kind; public long Id; }
                             public class C { public Flagged[] V { get; set; } = System.Array.Empty<Flagged>(); }
                             public class D { public Flagged[] V { get; set; } = System.Array.Empty<Flagged>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A metadata element type is never named: the consumer cannot reorder a struct they do not
        ///     declare, so the remedy would be unusable even where the number is right.
        /// </summary>
        [Fact]
        public void A_metadata_element_struct_is_never_named()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             namespace Demo;
                             public class C { public System.Guid[] V { get; set; } = System.Array.Empty<System.Guid>(); }
                             public class D { public System.Guid[] V { get; set; } = System.Array.Empty<System.Guid>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A padded struct mapped as a SCALAR member is not this diagnostic's business. The hint's claim is
        ///     about arrays — "smaller arrays" is in the message — and a one-off member wastes 20 bytes once.
        /// </summary>
        [Fact]
        public void A_padded_struct_mapped_as_a_scalar_member_is_not_reported()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             namespace Demo;
                             public struct Sample
                             {
                                 public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                             }
                             public class C { public Sample V { get; set; } }
                             public class D { public Sample V { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }
    }
}
