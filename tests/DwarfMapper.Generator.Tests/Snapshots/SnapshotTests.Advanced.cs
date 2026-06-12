// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Flatten dotted path ───────────────────────────────────────────────────
    [Fact]
    public Task Snap_Flatten_DottedPath()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Address  { public string City { get; set; } = ""; }
            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public string City { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [Flatten("Address")]
                public partial CustomerDto ToDto(Customer c);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── BeforeMap / AfterMap hooks ────────────────────────────────────────────
    [Fact]
    public Task Snap_Hooks_BeforeAndAfter()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Age { get; set; } }
            public class Dst { public int Age { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
                [BeforeMap] private static void OnBefore(Src s) { }
                [AfterMap]  private static void OnAfter(Src s, Dst d) { }
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── [RoundTrip] emission ──────────────────────────────────────────────────
    [Fact]
    public Task Snap_RoundTrip_Emission()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Order    { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class OrderDto { public int Id { get; set; } public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [RoundTrip] public partial OrderDto ToDto(Order o);
                public partial Order FromDto(OrderDto d);
            }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
