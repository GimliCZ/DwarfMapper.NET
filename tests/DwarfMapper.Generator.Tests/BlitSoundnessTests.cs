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
