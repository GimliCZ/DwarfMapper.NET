// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Blit: layout-identical unmanaged structs → MemoryMarshal.Cast ─────────
    [Fact]
    public Task Snap_Blit_LayoutIdenticalStructArray()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public struct SrcV { public float X; public float Y; public float Z; }
            public struct DstV { public float X; public float Y; public float Z; }
            public class A { public SrcV[] Verts { get; set; } = System.Array.Empty<SrcV>(); }
            public class B { public DstV[] Verts { get; set; } = System.Array.Empty<DstV>(); }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
