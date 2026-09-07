// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    public partial class SnapshotSuite
    {
        // ── Views: [GenerateView<S,T>] emits a nested readonly ref struct on the mapper ───────────
        // Snapshotted in a NULLABLE-ANNOTATED context, unlike most of this suite: a view's property signature
        // carries the destination member's own annotation, and the whole point of rendering it with a
        // nullable-aware display format is invisible in the oblivious world the harness defaults to.

        [Fact]
        public Task Snap_View_Flat()
        {
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int X { get; set; } public string? Name { get; set; } }
                               public class B { public int X { get; set; } public string? Name { get; set; } }
                               [DwarfMapper] [GenerateView<A, B>] public partial class M { }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            return Verify(generated);
        }

        /// <summary>A scalar converter: the view calls the same <c>private static</c> helper the map calls.</summary>
        [Fact]
        public Task Snap_View_Converter()
        {
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public enum Kind { A, B }
                               public class A { public Kind K { get; set; } }
                               public class B { public string K { get; set; } = ""; }
                               [DwarfMapper] [GenerateMap<A, B>] [GenerateView<A, B>] public partial class M { }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            return Verify(generated);
        }

        /// <summary>
        ///     A nested member becomes a nested VIEW rather than a call to the object helper, and the nullable
        ///     one yields <c>default</c> — a view whose <c>HasValue</c> is false — instead of constructing over
        ///     null.
        /// </summary>
        [Fact]
        public Task Snap_View_Nested()
        {
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public string Label { get; set; } = ""; }
                               public class InnerDto { public string Label { get; set; } = ""; }
                               public class A { public Inner Required { get; set; } = new(); public Inner? Optional { get; set; } }
                               public class B { public InnerDto Required { get; set; } = new(); public InnerDto? Optional { get; set; } }
                               [DwarfMapper] [GenerateView<A, B>] public partial class M { }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            return Verify(generated);
        }

        /// <summary>
        ///     The owner field, and why it exists: an INSTANCE converter is reached through the mapper (CS0120
        ///     unqualified) while a STATIC one is not (CS0176 through an instance). Both in one snapshot so the
        ///     two spellings sit side by side.
        /// </summary>
        [Fact]
        public Task Snap_View_InstanceAndStaticConverters()
        {
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int Inst { get; set; } public int Stat { get; set; } }
                               public class B { public string Inst { get; set; } = ""; public string Stat { get; set; } = ""; }
                               [DwarfMapper]
                               [MapProperty<A, B>(nameof(A.Inst), nameof(B.Inst), Use = nameof(ByInstance))]
                               [MapProperty<A, B>(nameof(A.Stat), nameof(B.Stat), Use = nameof(ByStatic))]
                               [GenerateView<A, B>]
                               public partial class M
                               {
                                   private string ByInstance(int v) => v.ToString();
                                   private static string ByStatic(int v) => v.ToString();
                               }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            return Verify(generated);
        }

        /// <summary>
        ///     <c>[MapProperty]</c> rename, <c>[MapIgnore]</c>, a <c>[MapValue]</c> constant and a
        ///     <c>NullSubstitute</c> — every directive that resolves to an expression, in one view.
        /// </summary>
        [Fact]
        public Task Snap_View_Directives()
        {
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public class A { public int X { get; set; } public string? Legacy { get; set; } }
                               public class B { public int X { get; set; } public string Modern { get; set; } = ""; public string Tag { get; set; } = ""; public string? Skipped { get; set; } }
                               [DwarfMapper]
                               [MapProperty<A, B>(nameof(A.Legacy), nameof(B.Modern), NullSubstitute = "(none)")]
                               [MapValue<B>(nameof(B.Tag), "api-v2")]
                               [MapIgnore<B>(nameof(B.Skipped))]
                               [GenerateView<A, B>]
                               public partial class M { }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            return Verify(generated);
        }
    }
}
