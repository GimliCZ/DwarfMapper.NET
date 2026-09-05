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
        ///     to T's alignment, then T. Measured at 20 for a 16-byte, 4-aligned T (<c>Unsafe.SizeOf</c> agrees).
        ///     <para>
        ///         Its 3 bytes of padding are real and NOT recoverable — the two fields are the runtime's, in the
        ///         runtime's order — so it reports no field order and its packed size is its size. What actually
        ///         keeps it out of the diagnostic is the 8-byte floor, since a <c>Nullable&lt;T&gt;</c>'s padding
        ///         is <c>alignof T − 1</c> and so at most 7; the empty order is the second, independent reason,
        ///         asserted separately below.
        ///     </para>
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
            Assert.Equal(3, layout.Value.Padding);
            Assert.Equal(20, layout.Value.PackedSize);
            Assert.Empty(layout.Value.PackedOrder);
            Assert.False(LayoutHygiene.WastesAQuarter(layout.Value));
        }

        /// <summary>
        ///     The empty-order clause of <c>WastesAQuarter</c>, asserted directly because no shape
        ///     <c>Measure</c> can produce today reaches it: the only empty order belongs to
        ///     <c>Nullable&lt;T&gt;</c>, whose padding never clears the floor, so the clause would be a dead
        ///     guard that a mutant could delete unnoticed. It states a contract worth keeping either way — the
        ///     diagnostic's entire payload is a field order the consumer retypes, so a layout that has none is
        ///     never worth reporting, whatever its arithmetic says.
        /// </summary>
        [Fact]
        public void WastesAQuarter_refuses_a_layout_with_no_field_order_to_restate()
        {
            var wasteful = new LayoutHygiene.Layout(64, 8, 16, 48, Array.Empty<string>());

            Assert.True(wasteful.Padding >= LayoutHygiene.MinimumWastedBytes && wasteful.Padding * 4 >= wasteful.Size,
                "the fixture must clear both arithmetic thresholds, or it proves nothing about the clause");
            Assert.False(LayoutHygiene.WastesAQuarter(wasteful));
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

        /// <summary>
        ///     Plain fields and auto-properties interleaved. <c>BlittableProof.InstanceFields</c> asserts that
        ///     <c>GetMembers()</c> order IS the layout, and this is the first caller that PRINTS a number from
        ///     that assumption rather than comparing two lists under it — so the assumption is pinned here
        ///     against the runtime rather than inherited. Verified: <c>Unsafe.SizeOf</c> 32, and
        ///     <c>Marshal.OffsetOf</c> puts <c>A@0, &lt;B&gt;k__BackingField@8, C@16, D@24</c>.
        /// </summary>
        [Fact]
        public void Measure_reads_fields_and_auto_properties_in_one_declaration_order()
        {
            var layout = MeasureType(
                """
                namespace T
                {
                    public struct Mixed
                    {
                        public byte A;
                        public long B { get; set; }
                        public byte C;
                        public double D;
                    }
                }
                """,
                "Mixed");

            Assert.Equal(32, layout.Size);
            Assert.Equal(14, layout.Padding);
            Assert.Equal(24, layout.PackedSize);
            Assert.Equal("B, D, A, C", layout.PackedOrderText);
            Assert.True(LayoutHygiene.WastesAQuarter(layout));
        }

        /// <summary>
        ///     A positional <c>record struct</c> lays its parameters out as backing fields in parameter order —
        ///     measured at 24 for <c>(byte, long, short)</c>, with the fields emitted
        ///     <c>&lt;Kind&gt;k__BackingField, &lt;Id&gt;k__BackingField, &lt;Code&gt;k__BackingField</c>. The
        ///     remedy names the positional parameters, which is what a consumer would reorder.
        /// </summary>
        [Fact]
        public void Measure_reads_a_positional_record_struct_in_parameter_order()
        {
            var layout = MeasureType(
                "namespace T { public record struct RS(byte Kind, long Id, short Code); }",
                "RS");

            Assert.Equal(24, layout.Size);
            Assert.Equal(13, layout.Padding);
            Assert.Equal(16, layout.PackedSize);
            Assert.Equal("Id, Code, Kind", layout.PackedOrderText);
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
        ///     The squiggle lands on the STRUCT's declaration, not on the member whose mapping reached it. The
        ///     message asks for that type's fields to be reordered, and the user's standing rule is that a
        ///     refusal (or a hint) points at the exact line to edit. It also removes an arbitrary choice: with
        ///     two members reaching one padded type, anchoring on the member made the survivor depend on
        ///     declaration order.
        /// </summary>
        [Fact]
        public void The_report_lands_on_the_struct_declaration_rather_than_on_a_member()
        {
            var reported = Assert.Single(Run(PaddedPair));

            var lines = PaddedPair.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var declaration = Array.FindIndex(lines, l => l.Contains("public struct Sample", StringComparison.Ordinal));
            Assert.True(declaration >= 0, "the fixture no longer declares the struct this test is looking for");

            var span = reported.Location.GetLineSpan();
            Assert.Equal(declaration, span.StartLinePosition.Line);

            // Not the member, and not the mapper: both are further down the same file, so a stale anchor would
            // still have produced a line number — just the wrong one.
            Assert.NotEqual(Array.FindIndex(lines, l => l.Contains("public Sample[] V", StringComparison.Ordinal)),
                span.StartLinePosition.Line);
        }

        /// <summary>
        ///     A struct emitted by ANOTHER source generator is never named. It passes every measurement test —
        ///     it is in source, Sequential, and as padded as any hand-written one — and fails the only question
        ///     a report has to answer: the consumer cannot reorder fields they did not write, and cannot
        ///     suppress a diagnostic raised inside a <c>.g.cs</c> either.
        ///     <para>
        ///         The control half is the point: the SAME source in a file that is not <c>.g.cs</c> reports, so
        ///         this pins the file path as the discriminator rather than passing for some unrelated reason.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_struct_another_generator_emitted_is_never_named()
        {
            const string mapper = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class C { public Sample[] V { get; set; } = System.Array.Empty<Sample>(); }
                                  public class D { public Sample[] V { get; set; } = System.Array.Empty<Sample>(); }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;
            const string dto = """
                               namespace Demo;
                               public struct Sample
                               {
                                   public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                               }
                               """;

            Assert.Single(RunAcross(mapper, dto, "Sample.cs"));
            Assert.Empty(RunAcross(mapper, dto, "Sample.g.cs"));
        }

        /// <summary>
        ///     Runs the generator over two files, the second under <paramref name="secondPath" />, and returns
        ///     the DWARF101s. The path is the whole point — <c>GeneratedSourceExtensions.IsGeneratorAuthored</c>
        ///     reads it — so the trees are built here rather than through the single-source harness entry.
        /// </summary>
        private static List<Diagnostic> RunAcross(string first, string second, string secondPath)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapperTestAsm",
                new[]
                {
                    CSharpSyntaxTree.ParseText(first, path: "Mapper.cs"),
                    CSharpSyntaxTree.ParseText(second, path: secondPath)
                });

            CSharpGeneratorDriver.Create(new DwarfGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

            return diagnostics.Where(d => d.Id == "DWARF101").ToList();
        }

        /// <summary>
        ///     The span-map arm is silent. Structurally so — the report lives in the collection arm, and a span
        ///     map maps into a buffer the caller already owns, which is what "smaller arrays" does not describe
        ///     — but pinned, so that a later task wiring this arm records the scope change instead of making it
        ///     silently.
        /// </summary>
        [Fact]
        public void A_span_map_over_a_padded_element_reports_nothing()
        {
            Assert.Empty(Run("""
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             public struct Sample
                             {
                                 public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                             }
                             public struct SampleDto
                             {
                                 public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                             }
                             [DwarfMapper] public partial class M { public partial void Map(ReadOnlySpan<Sample> s, Span<SampleDto> d); }
                             """));
        }

        /// <summary>
        ///     The dictionary arm is silent, for the same structural reason and pinned for the same one: a
        ///     <c>Dictionary&lt;K, Padded&gt;</c> multiplies the padding as surely as an array does, so if a
        ///     later task decides to say so, this test is where that decision gets recorded.
        /// </summary>
        [Fact]
        public void A_dictionary_value_that_is_padded_reports_nothing()
        {
            Assert.Empty(Run("""
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public struct Sample
                             {
                                 public bool Ok; public long Id; public byte Kind; public double Value; public short Code;
                             }
                             public class C { public Dictionary<int, Sample> V { get; set; } = new(); }
                             public class D { public Dictionary<int, Sample> V { get; set; } = new(); }
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
