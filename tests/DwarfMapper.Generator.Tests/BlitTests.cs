// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class BlitTests
{
    [Fact]
    public void Reinterpret_unknown_member_reports_DWARF022()
    {
        // A typo'd [Reinterpret] target must not be silently ignored.
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class C { public int V { get; set; } }
            public class D { public int V { get; set; } }
            [DwarfMapper] public partial class M
            {
                [Reinterpret("Nope")]
                public partial D Map(C c);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF022" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
    }


    [Fact]
    public void Layout_identical_structs_blit()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public struct SrcV { public float X; public float Y; public float Z; }
            public struct DstV { public float X; public float Y; public float Z; }
            public class A { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
            public class B { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        Assert.Contains("Unsafe.SizeOf<", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_field_names_do_not_blit()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public struct SrcV { public float A; public float B; }
            public struct DstV { public float X; public float Y; }
            public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
            public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
            [DwarfMapper] public partial class M { public partial D Map(C c); }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        // names differ -> not provable -> no blit -> no element conversion -> DWARF005
        Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        Assert.Contains(diagnostics, d => d.Id == "DWARF005");
    }

    [Fact]
    public void Managed_element_does_not_blit()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public struct SrcM { public float X; public string S; }
            public struct DstM { public float X; public string S; }
            public class E { public SrcM[] V { get; set; } = System.Array.Empty<SrcM>(); }
            public class F { public DstM[] V { get; set; } = System.Array.Empty<DstM>(); }
            [DwarfMapper] public partial class M { public partial F Map(E e); }
            """;
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_layout_does_not_blit()
    {
        const string s = """
            using DwarfMapper;
            using System.Runtime.InteropServices;
            namespace Demo;
            [StructLayout(LayoutKind.Explicit)] public struct SrcE { [FieldOffset(0)] public int X; }
            public struct DstE { public int X; }
            public class G { public SrcE[] V { get; set; } = System.Array.Empty<SrcE>(); }
            public class H { public DstE[] V { get; set; } = System.Array.Empty<DstE>(); }
            [DwarfMapper] public partial class M { public partial H Map(G g); }
            """;
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_type_array_still_uses_clone_not_reinterpret()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public struct V { public float X; }
            public class A { public V[] Vs { get; set; } = System.Array.Empty<V>(); }
            public class B { public V[] Vs { get; set; } = System.Array.Empty<V>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("Clone()", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_struct_field_does_not_auto_blit_v1()
    {
        // v1 is primitives-only: a struct containing a nested struct field must NOT auto-blit.
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public struct Inner { public float A; }
            public struct SrcN { public Inner I; public float X; }
            public struct DstN { public Inner I; public float X; }
            public class C { public SrcN[] V { get; set; } = System.Array.Empty<SrcN>(); }
            public class D { public DstN[] V { get; set; } = System.Array.Empty<DstN>(); }
            [DwarfMapper] public partial class M { public partial D Map(C c); }
            """;
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Reinterpret_forces_blit_skipping_name_proof()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public struct SrcV { public float A; public float B; }   // names differ from target
            public struct DstV { public float X; public float Y; }
            public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
            public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
            [DwarfMapper] public partial class M
            {
                [Reinterpret("V")]
                public partial D Map(C c);
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal); // forced despite name mismatch
    }

    [Fact]
    public void Reinterpret_on_non_array_reports_DWARF022()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class C { public int V { get; set; } }
            public class D { public int V { get; set; } }
            [DwarfMapper] public partial class M
            {
                [Reinterpret("V")]
                public partial D Map(C c);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF022");
    }
}
