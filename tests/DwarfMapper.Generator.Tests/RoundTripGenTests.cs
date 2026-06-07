// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class RoundTripGenTests
{
    private const string Pair = """
        using DwarfMapper;
        namespace Demo;
        public class Order { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class OrderDto { public int Id { get; set; } public string Name { get; set; } = ""; }
        """;

    [Fact]
    public void Emits_verifier_for_roundtrip_pair()
    {
        const string s = Pair + """
            [DwarfMapper]
            public partial class M
            {
                [RoundTrip] public partial OrderDto ToDto(Order o);
                public partial Order FromDto(OrderDto d);
            }
            """;
        var (diagnostics, gen) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
        Assert.Contains("public void VerifyRoundTrip_ToDto", gen, StringComparison.Ordinal);
        Assert.Contains("global::DwarfMapper.Testing.RoundTrip.Verify<", gen, StringComparison.Ordinal);
        Assert.Contains("(ToDto, FromDto, seed, iterations)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void No_inverse_reports_DWARF020()
    {
        const string s = Pair + """
            [DwarfMapper]
            public partial class M
            {
                [RoundTrip] public partial OrderDto ToDto(Order o);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF020");
    }

    [Fact]
    public void Ambiguous_inverse_reports_DWARF021()
    {
        const string s = Pair + """
            [DwarfMapper]
            public partial class M
            {
                [RoundTrip] public partial OrderDto ToDto(Order o);
                public partial Order FromDto(OrderDto d);
                public partial Order FromDto2(OrderDto d);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diagnostics, d => d.Id == "DWARF021");
    }
}
