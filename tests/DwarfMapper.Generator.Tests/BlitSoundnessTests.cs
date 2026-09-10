// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The blit proof's soundness, held END-TO-END. Every shape here is one <c>BlittableProof</c> once ACCEPTED
    ///     while the runtime laid the two structs out differently — so the emitted <c>MemoryMarshal.Cast</c>
    ///     either threw ("destination is too short") or, casting narrower elements into a wider array, silently
    ///     filled a fraction of the destination and left the rest zeroed. The proof is the ONLY guard: the runtime
    ///     size check inside the copy was deleted in round 26 on the strength of it.
    ///     <para>
    ///         Seam-level tests (<c>BlittableProofCoverageTests</c>) pin the verdicts; these compile the pair, run
    ///         the generator, emit, invoke the mapper and read the values back, because "the proof refuses" is a
    ///         claim about the generator and "the values survive" is the claim the consumer actually relies on.
    ///         Each also asserts that no <c>__DwarfBlit_</c> helper was synthesized — the mapping went through the
    ///         scalar path, by name, which is the oracle the blit is measured against.
    ///     </para>
    /// </summary>
    public class BlitSoundnessTests
    {
        private const string PointFile = "namespace Demo { public partial struct SrcV { public int X; } }";
        private const string PointExtraFile = "namespace Demo { public partial struct SrcV { public long Y; } }";

        private const string PartialRestFile = """
                                               using DwarfMapper;
                                               namespace Demo
                                               {
                                                   public struct DstV { public long Y; public int X; }
                                                   public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                                   public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                                   [DwarfMapper] public partial class M { public partial B Map(A a); }
                                               }
                                               """;

        /// <summary>
        ///     The shape that produced swapped bytes in production: a struct whose fields are split across
        ///     <c>Point.cs</c> and <c>Point.Extra.cs</c>, compiled in the order MSBuild feeds them on Windows
        ///     (case-insensitive: <c>Point.cs</c> first). The proof used to re-sort the fields by ORDINAL path —
        ///     where <c>Point.Extra.cs</c> comes first — line the sorted list up by name with a twin declared in
        ///     that reversed order, and accept; the real layouts were each other's reverse, and
        ///     <c>P{X=1,Y=2}</c> came out as <c>Q{X=2,Y=1}</c>.
        /// </summary>
        [Fact]
        public void Partial_struct_split_across_files_maps_by_name_in_the_order_the_build_used()
        {
            var trees = new[]
            {
                CSharpSyntaxTree.ParseText(PointFile, path: "Point.cs"),
                CSharpSyntaxTree.ParseText(PointExtraFile, path: "Point.Extra.cs"),
                CSharpSyntaxTree.ParseText(PartialRestFile, path: "Rest.cs")
            };
            var compilation = GeneratorTestHarness.BuildCompilation("BlitSoundness_" + Guid.NewGuid().ToString("N"), trees);

            // Geometry, so the pass below cannot be a pass for a boring reason: the compiler's field order is the
            // file order [X, Y], the ordinal path order is the reverse, and the twin is declared [Y, X] — the
            // exact configuration under which the sorted list matched the twin and the real layouts did not.
            var srcV = compilation.GetTypeByMetadataName("Demo.SrcV")!;
            Assert.Equal(new[] { "X", "Y" }, srcV.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name));
            Assert.True(string.CompareOrdinal("Point.Extra.cs", "Point.cs") < 0,
                "fixture geometry broken: the ordinal path order must be the reverse of the compile order");

            var (asm, errors) = GeneratorTestHarness.EmitAssembly(compilation);
            Assert.True(asm is not null, string.Join("\n", errors));

            var mapped = MapArray(asm, ("X", 1), ("Y", 2L), ("X", 3), ("Y", 4L));
            Assert.Equal(new object[] { 1, 2L }, mapped[0]);
            Assert.Equal(new object[] { 3, 4L }, mapped[1]);
            AssertNoBlitHelper(asm);
        }

        [Fact]
        public void Explicit_StructLayout_Size_is_part_of_the_layout()
        {
            // 4-byte elements cast into a 32-byte element array: 3 SrcV are 12 bytes, which is ZERO DstV, so the
            // copy moved nothing and every mapped element stayed default — the silent direction. (The reverse
            // direction threw "destination is too short", which is at least loud.)
            const string s = """
                             using System.Runtime.InteropServices;
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; }
                             [StructLayout(LayoutKind.Sequential, Size = 32)]
                             public struct DstV { public int X; }
                             public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial B Map(A a); }
                             """;
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(s);
            Assert.True(asm is not null, string.Join("\n", errors));

            var mapped = MapArray(asm, ("X", 1), ("X", 2), ("X", 3));
            Assert.Equal(new object[] { 1 }, mapped[0]);
            Assert.Equal(new object[] { 2 }, mapped[1]);
            Assert.Equal(new object[] { 3 }, mapped[2]);
            AssertNoBlitHelper(asm);
        }

        [Fact]
        public void InlineArray_length_is_part_of_the_layout()
        {
            // An [InlineArray(4)] of int is 16 bytes with ONE field in the symbol model. Its plain twin is 4.
            const string s = """
                             using System.Runtime.CompilerServices;
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int E; }
                             [InlineArray(4)]
                             public struct DstV { public int E; }
                             public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial B Map(A a); }
                             """;
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(s);
            Assert.True(asm is not null, string.Join("\n", errors));

            var mapped = MapArray(asm, ("E", 7), ("E", 8), ("E", 9));
            Assert.Equal(new object[] { 7 }, mapped[0]);
            Assert.Equal(new object[] { 8 }, mapped[1]);
            Assert.Equal(new object[] { 9 }, mapped[2]);
            AssertNoBlitHelper(asm);
        }

        [Fact]
        public void Fixed_buffer_length_is_part_of_the_layout()
        {
            // Both buffers are `int*` to the type comparison; only the field knows it reserves 16 or 32 bytes.
            // Held at the generated-source level rather than by execution: refused, the pair falls to the scalar
            // path, and a fixed buffer cannot be assigned as a value there (CS1666 in the generated code — a
            // loud failure, and a separate defect of the scalar path, not of the proof). The executing half of
            // this rule is the equal-length control below.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int Tag; public unsafe fixed int Buf[4]; }
                             public struct DstV { public int Tag; public unsafe fixed int Buf[8]; }
                             public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial B Map(A a); }
                             """;
            var (_, generated) = GeneratorTestHarness.Run(s, allowUnsafe: true);
            Assert.NotEmpty(generated);
            Assert.DoesNotContain("MemoryMarshal.Cast<", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Fixed_buffers_of_equal_length_still_blit_and_the_values_survive()
        {
            // The rule is about LENGTH, not about fixed buffers: with the lengths equal the layouts are the same
            // 20 bytes, the pair blits, and — because the blit copies bytes rather than assigning members — this
            // is also the one way a struct with a fixed buffer maps at all today.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int Tag; public unsafe fixed int Buf[4]; }
                             public struct DstV { public int Tag; public unsafe fixed int Buf[4]; }
                             public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial B Map(A a); }
                             """;
            var compilation = GeneratorTestHarness.BuildCompilation("BlitSoundness_" + Guid.NewGuid().ToString("N"), s, allowUnsafe: true);
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(compilation);
            Assert.True(asm is not null, string.Join("\n", errors));

            var mapped = MapArray(asm, ("Tag", 1), ("Tag", 2), ("Tag", 3));
            Assert.Equal(new object[] { 1 }, mapped[0]);
            Assert.Equal(new object[] { 2 }, mapped[1]);
            Assert.Equal(new object[] { 3 }, mapped[2]);
            Assert.NotEmpty(BlitHelpers(asm));
        }

        /// <summary>
        ///     Builds <c>Demo.A</c> with one <c>SrcV</c> per element, each element's fields set from the
        ///     (name, value) pairs — pairs run consecutively into the next element when a name repeats — maps it
        ///     through <c>Demo.M.Map</c>, and returns each mapped <c>DstV</c> as the values of the fields named.
        /// </summary>
        private static List<object[]> MapArray(Assembly asm, params (string Field, object Value)[] fields)
        {
            var srcV = asm.GetType("Demo.SrcV")!;
            var dstV = asm.GetType("Demo.DstV")!;
            var a = asm.GetType("Demo.A")!;
            var m = asm.GetType("Demo.M")!;

            var names = fields.Select(f => f.Field).Distinct().ToList();
            var elements = new List<object>();
            for (var i = 0; i < fields.Length; i += names.Count)
            {
                var element = Activator.CreateInstance(srcV)!;
                foreach (var (field, value) in fields.Skip(i).Take(names.Count))
                    srcV.GetField(field)!.SetValue(element, value);

                elements.Add(element);
            }

            var array = Array.CreateInstance(srcV, elements.Count);
            for (var i = 0; i < elements.Count; i++)
                array.SetValue(elements[i], i);

            var input = Activator.CreateInstance(a)!;
            a.GetProperty("V")!.SetValue(input, array);

            var output = m.GetMethod("Map")!.Invoke(Activator.CreateInstance(m), new[] { input })!;
            var mapped = (Array)output.GetType().GetProperty("V")!.GetValue(output)!;
            Assert.Equal(elements.Count, mapped.Length);

            return mapped.Cast<object>()
                .Select(e => names.Select(n => dstV.GetField(n)!.GetValue(e)!).ToArray())
                .ToList();
        }

        [Fact]
        public void Optional_nested_struct_member_keeps_the_root_blit()
        {
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct Addr { public int Street, City, Zip, Country; }
                    public struct AddrDto { public int Street, City, Zip, Country; }
                    public struct Order { public long Id; public Addr? Ship; public long Amount; }
                    public struct OrderDto { public long Id; public AddrDto? Ship; public long Amount; }
                    public class Src { public Order[] Items { get; set; } = System.Array.Empty<Order>(); }
                    public class Dst { public OrderDto[] Items { get; set; } = System.Array.Empty<OrderDto>(); }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;
            var generated = GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable);
            Assert.Contains("__DwarfBlit_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("DWARF100", GeneratorTestHarness.Run(src).Diagnostics.Select(d => d.Id));
        }

        [Fact]
        public void Nullable_on_one_side_only_is_a_near_miss_that_names_the_member()
        {
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct Addr { public int Street, City, Zip, Country; }
                    public struct Order { public long Id; public Addr? Ship; }
                    public struct OrderDto { public long Id; public Addr Ship; }
                    public class Src { public Order[] Items { get; set; } = System.Array.Empty<Order>(); }
                    public class Dst { public OrderDto[] Items { get; set; } = System.Array.Empty<OrderDto>(); }
                    [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                }
                """;
            var d = GeneratorAssert.Reports(src, "DWARF100");
            Assert.Contains("Nullable<T> on one side only", Assert.Single(d).GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        // ── The proof never overrides the resolver (round 29, T0.2c) ─────────────────────────────────────
        // HandleCollectionConversion decides the array/list block copy at chain position 2 — before the arms
        // that adopt a user-declared element conversion, and before the element pair is resolved at all. Every
        // shape below used to blit past exactly what the mapper's author asked for, with no diagnostic. The
        // rule these pin is one sentence: a proof enables a fast path, it never changes semantics.

        /// <summary>The layout-identical element pair every case below shares, plus the array and list carriers.</summary>
        private const string GatePairs = """
                                         public struct SrcV { public int X; public int Y; }
                                         public struct DstV { public int X; public int Y; }
                                         public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                         public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                         public class LA { public System.Collections.Generic.List<SrcV> V { get; set; } = new(); }
                                         public class LB { public System.Collections.Generic.List<DstV> V { get; set; } = new(); }
                                         public class IB { public ImmutableArray<DstV> V { get; set; } }
                                         """;

        private static string GateSource(string classAttributes, string members, string storage = "array")
        {
            var map = storage switch
            {
                "list" => "public partial LB Map(LA a);\n",
                "immutable" => "public partial IB Map(A a);\n",
                _ => "public partial B Map(A a);\n"
            };
            return "using System.Collections.Immutable;\nusing DwarfMapper;\nnamespace T\n{\n" + GatePairs +
                   "\n[DwarfMapper]\n" + classAttributes + "public partial class M\n{\n" + members + "\n" +
                   map + "}\n}\n";
        }

        /// <summary>The blit was taken: some <c>__DwarfBlit_</c>/<c>__DwarfBlitL_</c> helper reinterprets the storage.</summary>
        private static void AssertBlitted(string generated)
        {
            Assert.Contains("MemoryMarshal.Cast<", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The element loop was kept AND it calls <paramref name="expectedCall" /> — both halves, because
        ///     "no blit" alone would also pass if the pair had simply failed to resolve.
        /// </summary>
        private static void AssertLoopCalls(string generated, string expectedCall)
        {
            Assert.DoesNotContain("__DwarfBlit_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfBlitL_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast<", generated, StringComparison.Ordinal);
            Assert.Contains(expectedCall, generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_user_declared_element_conversion_method_keeps_the_array_loop()
        {
            var src = GateSource("", "public static DstV Conv(SrcV s) => new DstV { X = s.X * 2, Y = s.Y };");
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "Conv(");
        }

        [Fact]
        public void A_user_declared_element_conversion_method_keeps_the_list_loop()
        {
            var src = GateSource("", "public static DstV Conv(SrcV s) => new DstV { X = s.X * 2, Y = s.Y };", "list");
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "Conv(");
        }

        [Fact]
        public void A_user_declared_element_conversion_method_keeps_the_immutable_array_loop()
        {
            var src = GateSource("", "public static DstV Conv(SrcV s) => new DstV { X = s.X * 2, Y = s.Y };", "immutable");
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "Conv(");
        }

        [Fact]
        public void A_user_declared_element_conversion_method_keeps_the_loop_for_an_enum_array()
        {
            // The enum half of the OR: CanReinterpretEnums accepts SrcE[] → DstE[] under ByValue and used to
            // decide the copy on its own, so a declared SrcE → DstE converter was bypassed as well.
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public enum SrcE { A = 0, B = 1 }
                    public enum DstE { A = 0, B = 1 }
                    public class A { public SrcE[] V { get; set; } = System.Array.Empty<SrcE>(); }
                    public class B { public DstE[] V { get; set; } = System.Array.Empty<DstE>(); }
                    [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                    public partial class M
                    {
                        public static DstE Conv(SrcE s) => DstE.B;
                        public partial B Map(A a);
                    }
                }
                """;
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "Conv(");
        }

        [Fact]
        public void A_user_defined_conversion_operator_keeps_the_loop_when_it_is_what_resolution_would_pick()
        {
            // [AutoNest(false)] is load-bearing, not decoration. The user-operator arm is the LAST in the
            // chain: with auto-nest ON the element pair resolves to a synthesized __DwarfMap_Obj_* and the
            // operator is not called on the scalar path either (pinned by the test below), so there is nothing
            // for the blit to bypass. With auto-nest off the operator IS the resolver's answer, and blitting
            // past it silently drops the *2.
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct SrcV { public int X; }
                    public struct DstV { public int X; public static implicit operator DstV(SrcV s) => new DstV { X = s.X * 2 }; }
                    public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                    public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                    [DwarfMapper] public partial class M { [AutoNest(false)] public partial B Map(A a); }
                }
                """;
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "__DwarfMap_UserConv_");
        }

        [Fact]
        public void A_user_defined_conversion_operator_the_resolver_would_not_pick_keeps_the_blit()
        {
            // The precedence half of the rule above: with auto-nest on, the scalar path is the by-name object
            // map, which the proof reproduces byte for byte. Refusing the blit here would cost the fast path
            // for a difference that does not exist.
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct SrcV { public int X; }
                    public struct DstV { public int X; public static implicit operator DstV(SrcV s) => new DstV { X = s.X * 2 }; }
                    public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                    public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                    [DwarfMapper] public partial class M { public partial B Map(A a); }
                }
                """;
            AssertBlitted(GeneratorAssert.CompilesClean(src));
        }

        [Theory]
        [InlineData("[MapIgnore<DstV>(\"Y\")]\n", "", "__DwarfMap_Obj_")]
        [InlineData("[MapProperty<SrcV, DstV>(\"X\", \"Y\")]\n", "", "__DwarfMap_Obj_")]
        [InlineData("[MapValue<DstV>(\"Y\", 42)]\n", "", "__DwarfMap_Obj_")]
        // The hook row anchors on Touch( rather than on the helper's name: a synthesized helper that exists
        // but silently fails to replicate the pair's Before/AfterMap hooks is a KNOWN hazard class in this
        // repository, so "the loop was kept" does not by itself prove "the hook runs".
        [InlineData("", "[AfterMap] public static void Touch(SrcV s, ref DstV d) { d.X += 1; }", "Touch(")]
        public void A_pair_scoped_directive_or_hook_on_the_element_pair_keeps_the_array_loop(
            string classAttribute,
            string member,
            string expectedCall)
        {
            // NestedMappingRegistry.GetOrReserve is keyed purely by the type pair, so whatever these customize
            // is baked into the ONE __DwarfMap_Obj_* helper the element pair gets. A block copy bypasses the
            // helper, and with it the directive — silently.
            var src = GateSource(classAttribute, member);
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("__DwarfBlit_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast<", generated, StringComparison.Ordinal);
            Assert.Contains(expectedCall, generated, StringComparison.Ordinal);

            // The semantic half, and the one that would still fail if the loop were kept for the wrong reason:
            // the directive is now APPLIED by the element pair's own helper, so it no longer "matches no pair".
            // Before the gate this same source blitted AND reported DWARF056. (The hook row carries no
            // pair-scoped attribute, so its DWARF056 assertion is vacuous and harmless.)
            GeneratorAssert.DoesNotReport(src, "DWARF056");

            // And no near-miss: the pair lines up by name, so TryExplainNearMiss has nothing to explain — which
            // is what makes it safe for the near-miss gate NOT to consult the customization half.
            GeneratorAssert.DoesNotReport(src, "DWARF100");
        }

        [Theory]
        [InlineData("list")]
        // ImmutableArray shares SynthesizeBlitListShape with List — the THIRD blit site — so this row is what
        // keeps that site's coverage durable rather than resting on a probe that no longer exists.
        [InlineData("immutable")]
        public void A_pair_scoped_directive_on_the_element_pair_keeps_the_list_family_loop(string storage)
        {
            var src = GateSource("[MapIgnore<DstV>(\"Y\")]\n", "", storage);
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("__DwarfBlitL_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MemoryMarshal.Cast<", generated, StringComparison.Ordinal);
            Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
            GeneratorAssert.DoesNotReport(src, "DWARF056");
            GeneratorAssert.DoesNotReport(src, "DWARF100");
        }

        [Fact]
        public void A_pair_scoped_MapConstructor_keeps_the_loop_and_the_factory_runs_per_element()
        {
            // The re-review of T0.2 judged PairConstructors byte-equivalent for a blittable pair and left them
            // out of the span gate. They are NOT byte-equivalent — this factory doubles X — but they need no
            // question of their own: a pair-scoped [MapConstructor] is honoured only for a pair some
            // [GenerateMap<S,T>] declares (DWARF056 refuses it otherwise), and a declared pair contributes a
            // candidate method, so the user-declared-conversion question already refuses the blit. Pinned here
            // so the reasoning cannot quietly stop being true.
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct SrcV { public int X; }
                    public struct DstV { public int X; public DstV(int x) { X = x * 2; } }
                    public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                    public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                    [DwarfMapper]
                    [GenerateMap<SrcV, DstV>]
                    [MapConstructor<SrcV, DstV>(nameof(Make))]
                    public partial class M
                    {
                        public static DstV Make(SrcV s) => new DstV(s.X);
                        public partial B Map(A a);
                    }
                }
                """;
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "Make(");
        }

        /// <summary>
        ///     The source of <see cref="A_pair_scoped_MapConstructor_keeps_the_loop_and_the_factory_runs_per_element" />,
        ///     parameterised by the reference mode — the mode is what decides which ROUTE the element pair takes
        ///     to the factory, and for two of the three modes that route is not the declared method.
        /// </summary>
        private static string PairConstructorSource(string mode)
        {
            return """
                using DwarfMapper;
                namespace T
                {
                    public struct SrcV { public int X; }
                    public struct DstV { public int X; public DstV(int x) { X = x * 2; } }
                    public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                    public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                    [DwarfMapper(MODE)]
                    [GenerateMap<SrcV, DstV>]
                    [MapConstructor<SrcV, DstV>(nameof(Make))]
                    public partial class M
                    {
                        public static DstV Make(SrcV s) => new DstV(s.X);
                        public partial B Map(A a);
                    }
                }
                """.Replace("MODE", mode, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ReferenceHandling = ReferenceHandlingStrategy.Preserve")]
        [InlineData("OnCycle = OnCycleStrategy.SetNull")]
        public void A_pair_scoped_MapConstructor_survives_the_modes_that_route_through_the_synthesized_helper(
            string mode)
        {
            // Round 29 T0.2c review fix 1. Under Preserve and SetNull the declared pair method is NOT the
            // element route: PrefersSynthesizedObjectMap hands the pair to the auto-nest arm, because a public
            // method cannot accept the shared DwarfRefContext. So the user-declared-conversion question answers
            // "no user conversion" and, before this fix, the blit was taken — while it is the SYNTHESIZED helper
            // that carries the pair-scoped factory in those modes (DrainNestedMappingQueue's nested-pair factory
            // wiring, scoped to declared pairs). Blitting past it skipped Make() exactly as it did in None mode,
            // which is the bypass the None-mode test above was written to prove closed. Closed for real now, by
            // ElementPairHasCustomization consulting PairConstructors rather than by the declared-method rule.
            var src = PairConstructorSource(mode);
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.DoesNotContain("MemoryMarshal.Cast<", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfBlit_", generated, StringComparison.Ordinal);
            Assert.Contains("Make(", generated, StringComparison.Ordinal);
            // The route these two modes actually take, named so a refactor that changes it is visible here.
            Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
        }

        [Theory]
        // The other half of the T0.2 re-review's claim, and this half holds: an unmanaged struct pair has no
        // nullable source member to skip, so the directive cannot change a byte. Deliberately NOT a reason to
        // refuse the fast path.
        [InlineData("[MapNullSkip<SrcV, DstV>]\n", "")]
        // Round 29 T0.2c review fix 2 — the SCOPING of the [MapConstructor] question, which was previously
        // unproven: delete the `decls.GenPairs.Exists(...)` half of that condition and nothing failed. A
        // pair-scoped [MapConstructor] naming a pair NO [GenerateMap<S,T>] declares is honoured by nobody
        // (DrainNestedMappingQueue's factory wiring is scoped the same way, and DWARF056 reports the directive
        // as matching no pair), so it changes no byte and must NOT cost the fast path — refusing the blit for
        // it would make DWARF056's "matched no pair" untrue. Note the factory itself does not adopt the pair
        // either: a method named by a pair-scoped directive is a RESERVED converter, so
        // FindUserDeclaredConversion skips it, which is what leaves this row measuring the scoping and nothing
        // else.
        [InlineData("[MapConstructor<SrcV, DstV>(nameof(Make))]\n",
            "public static DstV Make(SrcV s) => new DstV { X = s.X, Y = s.Y };")]
        public void A_pair_scoped_directive_that_changes_no_byte_keeps_the_blit(
            string classAttribute,
            string member)
        {
            AssertBlitted(GeneratorAssert.CompilesClean(GateSource(classAttribute, member)));
        }

        [Fact]
        public void A_plain_layout_identical_pair_still_blits_for_both_storages()
        {
            AssertBlitted(GeneratorAssert.CompilesClean(GateSource("", "")));
            AssertBlitted(GeneratorAssert.CompilesClean(GateSource("", "", "list")));
            AssertBlitted(GeneratorAssert.CompilesClean(GateSource("", "", "immutable")));
        }

        [Fact]
        public void Reinterpret_is_an_explicit_instruction_and_a_user_converter_does_not_override_it()
        {
            // The decision, pinned: [Reinterpret("V")] names THIS member and forces the block copy; an
            // auto-adopted converter is ambient (the same helper may exist for another member entirely), so it
            // does not revoke a member-scoped instruction — and refusing the build over it would be a false
            // positive for any mapper that uses the helper elsewhere. [Reinterpret] returns before this gate is
            // reached, which is also why the near-miss never speaks for it.
            var src = GateSource("", "public static DstV Conv(SrcV s) => new DstV { X = s.X * 2, Y = s.Y };")
                .Replace("public partial B Map(A a);", "[Reinterpret(\"V\")] public partial B Map(A a);", StringComparison.Ordinal);
            var generated = GeneratorAssert.CompilesClean(src);
            AssertBlitted(generated);
            Assert.DoesNotContain("Conv(", generated, StringComparison.Ordinal);

            // Review fix 3: winning is right, winning SILENTLY is not. The bypass is reported informationally,
            // naming the member, what is not being called, and how to get it called.
            var hint = Assert.Single(GeneratorAssert.Reports(src, "DWARF106"))
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                "[Reinterpret] on 'V' takes precedence over the declared conversion method 'Conv', so the block " +
                "copy fills 'V' without calling it; remove [Reinterpret] from 'V' to use it instead",
                hint);
        }

        [Fact]
        public void Reinterpret_reports_the_bypass_for_a_user_defined_operator_too()
        {
            // The operator half. It has no name to grep for, so the message describes the pair it converts
            // between rather than pretending to name a method.
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct SrcV { public int X; }
                    public struct DstV { public int X; public static implicit operator DstV(SrcV s) => new DstV { X = s.X * 2 }; }
                    public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                    public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                    [DwarfMapper] public partial class M { [AutoNest(false)] [Reinterpret("V")] public partial B Map(A a); }
                }
                """;
            AssertBlitted(GeneratorAssert.CompilesClean(src));
            var hint = Assert.Single(GeneratorAssert.Reports(src, "DWARF106"))
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                "[Reinterpret] on 'V' takes precedence over the user-defined conversion operator from 'SrcV' to " +
                "'DstV', so the block copy fills 'V' without calling it; remove [Reinterpret] from 'V' to use it " +
                "instead",
                hint);
        }

        [Theory]
        // One row per kind the customization rule knows, so "the message names the directive" is checked for
        // every arm of it rather than for whichever one happened to be written first.
        // A pair-scoped ATTRIBUTE is "declared for" the pair and is APPLIED; a hook is only "matching" it (its
        // parameter types accept the pair by implicit conversion) and is RUN. The whole clause is the expectation
        // so that distinction cannot quietly collapse into one wording.
        [InlineData("[MapIgnore<DstV>(\"Y\")]\n", "",
            "the pair-scoped [MapIgnore<T>] declared for 'SrcV' \u2192 'DstV'", "applying")]
        [InlineData("[MapProperty<SrcV, DstV>(\"X\", \"Y\")]\n", "",
            "the pair-scoped [MapProperty<S,T>] declared for 'SrcV' \u2192 'DstV'", "applying")]
        [InlineData("[MapValue<DstV>(\"Y\", 42)]\n", "",
            "the pair-scoped [MapValue<T>] declared for 'SrcV' \u2192 'DstV'", "applying")]
        [InlineData("", "[BeforeMap] public static void Pre(SrcV s) { }",
            "the [BeforeMap] hook matching 'SrcV' \u2192 'DstV'", "running")]
        [InlineData("", "[AfterMap] public static void Touch(SrcV s, ref DstV d) { d.X += 1; }",
            "the [AfterMap] hook matching 'SrcV' \u2192 'DstV'", "running")]
        public void Reinterpret_reports_the_bypass_for_a_pair_scoped_directive_or_hook_too(
            string classAttribute,
            string member,
            string expectedDirective,
            string expectedVerb)
        {
            // Round 29 T0.2d. DWARF106 used to return early unless the element pair resolved to a user
            // CONVERSION, so [Reinterpret] overriding a pair-scoped directive or a hook — the same intentional
            // bypass, with a consequence just as invisible (the directive is simply never applied, and the
            // output is a correct block copy either way) — was silent. Same id, second message shape.
            var src = GateSource(classAttribute, member)
                .Replace("public partial B Map(A a);", """[Reinterpret("V")] public partial B Map(A a);""", StringComparison.Ordinal);

            // The decision is unchanged: [Reinterpret] names this member explicitly and still wins.
            AssertBlitted(GeneratorAssert.CompilesClean(src));

            var hint = Assert.Single(GeneratorAssert.Reports(src, "DWARF106"))
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                $"[Reinterpret] on 'V' takes precedence over {expectedDirective}, so the block copy fills 'V' " +
                $"without {expectedVerb} it; remove [Reinterpret] from 'V' to use it instead",
                hint);
        }

        [Theory]
        [InlineData("ReferenceHandling = ReferenceHandlingStrategy.Preserve")]
        [InlineData("OnCycle = OnCycleStrategy.SetNull")]
        public void Reinterpret_reports_the_bypass_for_a_pair_scoped_MapConstructor(string mode)
        {
            // The sixth kind the customization rule knows, and the one that cannot be expressed in GateSource:
            // [MapConstructor] is scoped to DECLARED pairs, and a [GenerateMap<SrcV, DstV>] beside it gives the
            // pair a declared `DstV Map(SrcV)` — which the conversion arm then names instead. Under Preserve and
            // SetNull that declared method is not the element route (PrefersSynthesizedObjectMap hands the pair
            // to the synthesized helper, which is where the factory is wired), so the conversion arm answers
            // "none" and the directive shape is what the reader gets. Same two modes as
            // A_pair_scoped_MapConstructor_survives_the_modes_that_route_through_the_synthesized_helper, for the
            // same reason.
            var src = PairConstructorSource(mode)
                .Replace("public partial B Map(A a);", """[Reinterpret("V")] public partial B Map(A a);""", StringComparison.Ordinal);

            AssertBlitted(GeneratorAssert.CompilesClean(src));

            var hint = Assert.Single(GeneratorAssert.Reports(src, "DWARF106"))
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(
                "[Reinterpret] on 'V' takes precedence over the pair-scoped [MapConstructor<S,T>] declared for " +
                "'SrcV' \u2192 'DstV', so the block copy fills 'V' without applying it; remove [Reinterpret] " +
                "from 'V' to use it instead",
                hint);
        }

        [Fact]
        public void Reinterpret_reports_the_conversion_rather_than_the_directive_when_both_are_present()
        {
            // Two message shapes, one id — so which one fires when both apply has to be pinned rather than
            // left to the order the checks happen to sit in. The conversion is named: it is the arm the gate
            // answers first, and it is the more specific fact (a method the user can grep for by name).
            var src = GateSource("[MapIgnore<DstV>(\"Y\")]\n",
                    "public static DstV Conv(SrcV s) => new DstV { X = s.X * 2, Y = s.Y };")
                .Replace("public partial B Map(A a);", """[Reinterpret("V")] public partial B Map(A a);""", StringComparison.Ordinal);

            var hint = Assert.Single(GeneratorAssert.Reports(src, "DWARF106"))
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("'Conv'", hint, StringComparison.Ordinal);
            Assert.DoesNotContain("pair-scoped", hint, StringComparison.Ordinal);
        }

        [Fact]
        public void Reinterpret_with_no_conversion_in_sight_says_nothing()
        {
            // DWARF106 reports a CONFLICT, not the attribute. Without this control the Info would drift into
            // ambient noise on every ordinary [Reinterpret] — which is how an informational diagnostic gets
            // suppressed wholesale, taking the cases worth reading with it.
            var src = GateSource("", "")
                .Replace("public partial B Map(A a);", """[Reinterpret("V")] public partial B Map(A a);""", StringComparison.Ordinal);
            AssertBlitted(GeneratorAssert.CompilesClean(src));
            GeneratorAssert.DoesNotReport(src, "DWARF106");
        }

        [Fact]
        public void A_pair_directive_nothing_applies_is_still_reported_by_DWARF056()
        {
            // The non-mutating-lookup lock. The gate ASKS whether a pair-scoped directive matches the element
            // pair; asking must not mark it Consumed. Here the user converter owns the element pair, so the
            // [MapIgnore<DstV>] is applied by nobody — and DWARF056 has to say so. Using the mutating
            // MatchPairIgnores for the question would silence it.
            var src = GateSource("[MapIgnore<DstV>(\"Y\")]\n",
                "public static DstV Conv(SrcV s) => new DstV { X = s.X * 2, Y = s.Y };");
            AssertLoopCalls(GeneratorAssert.EmitsCompilableCode(src), "Conv(");
            GeneratorAssert.Reports(src, "DWARF056");
        }

        [Fact]
        public void A_near_miss_is_not_reported_when_a_user_converter_owns_the_element_pair()
        {
            // DWARF100 tells a caller they are one rename away from the block copy. With a converter bound to
            // the pair that is untrue — the rename would change nothing — so the near-miss stays quiet.
            const string src = """
                using DwarfMapper;
                namespace T
                {
                    public struct SrcV { public int X; public int Y; }
                    public struct DstV { public int X; public int Renamed; }
                    public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                    public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                    [DwarfMapper] public partial class M
                    {
                        public static DstV Conv(SrcV s) => new DstV { X = s.X, Renamed = s.Y };
                        public partial B Map(A a);
                    }
                }
                """;
            AssertLoopCalls(GeneratorAssert.CompilesClean(src), "Conv(");
            GeneratorAssert.DoesNotReport(src, "DWARF100");
        }

        private static void AssertNoBlitHelper(Assembly asm)
        {
            var helpers = BlitHelpers(asm);
            Assert.True(helpers.Count == 0, "the pair must not have been blitted, but a helper was synthesized: " + string.Join(", ", helpers));
        }

        /// <summary>The <c>__DwarfBlit_…</c> helpers synthesized into <c>Demo.M</c> — one per pair the proof accepted.</summary>
        private static List<string> BlitHelpers(Assembly asm)
        {
            return asm.GetType("Demo.M")!
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(mi => mi.Name.StartsWith("__DwarfBlit_", StringComparison.Ordinal))
                .Select(mi => mi.Name)
                .ToList();
        }
    }
}
