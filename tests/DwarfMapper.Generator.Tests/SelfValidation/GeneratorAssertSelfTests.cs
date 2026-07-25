// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Tests the shared assertion fixture itself.
///     <para>
///     243 generator tests now route their "it compiled" / "it reported X" checks through
///     <see cref="GeneratorAssert" />. That is a large deduplication win and a much better failure message, but
///     it also concentrates the risk: a helper that silently passed everything would neuter 243 tests at once
///     and the suite would stay green while checking nothing. Exactly the vacuity this repo keeps finding.
///     </para>
///     So every branch is proven to FIRE here — each fact asserts the helper throws on input it must reject.
/// </summary>
public class GeneratorAssertSelfTests
{
    // Maps cleanly: no diagnostics, emission compiles.
    private const string Valid = """
                                 using DwarfMapper;
                                 namespace Demo;
                                 public class A { public int X { get; set; } }
                                 public class B { public int X { get; set; } }
                                 [DwarfMapper] public partial class M { public partial B Map(A a); }
                                 """;

    // B.Orphan has no source member → DWARF001 (unmapped destination), an ERROR.
    private const string ReportsDwarf001 = """
                                           using DwarfMapper;
                                           namespace Demo;
                                           public class A { public int X { get; set; } }
                                           public class B { public int X { get; set; } public int Orphan { get; set; } }
                                           [DwarfMapper] public partial class M { public partial B Map(A a); }
                                           """;

    // Valid mapper, but the USER's own source does not compile — proves the compilation branch fires.
    private const string UserCodeDoesNotCompile = """
                                                  using DwarfMapper;
                                                  namespace Demo;
                                                  public class A { public int X { get; set; } }
                                                  public class B { public int X { get; set; } }
                                                  [DwarfMapper] public partial class M { public partial B Map(A a); }
                                                  public class Broken { public void Go() { NoSuchType t = null; } }
                                                  """;

    [Fact]
    public void CompilesClean_passes_and_returns_the_generated_source()
    {
        var generated = GeneratorAssert.CompilesClean(Valid);

        Assert.NotEmpty(generated);
        Assert.Contains("partial class M", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilesClean_throws_when_the_generator_reports_an_error()
    {
        var ex = Assert.ThrowsAny<Exception>(() => GeneratorAssert.CompilesClean(ReportsDwarf001));
        Assert.Contains("DWARF001", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilesClean_throws_when_the_emitted_code_does_not_compile()
    {
        var ex = Assert.ThrowsAny<Exception>(() => GeneratorAssert.CompilesClean(UserCodeDoesNotCompile));
        Assert.Contains("does not compile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsCompilableCode_throws_when_the_compilation_fails()
    {
        var ex = Assert.ThrowsAny<Exception>(() => GeneratorAssert.EmitsCompilableCode(UserCodeDoesNotCompile));
        Assert.Contains("does not compile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsCompilableCode_tolerates_a_non_error_diagnostic()
    {
        // The whole reason this is separate from CompilesClean: a warning/Info diagnostic must not fail it.
        Assert.NotEmpty(GeneratorAssert.EmitsCompilableCode(Valid));
    }

    [Fact]
    public void Reports_returns_the_matching_diagnostic()
    {
        var matches = GeneratorAssert.Reports(ReportsDwarf001, "DWARF001");
        Assert.NotEmpty(matches);
        Assert.All(matches, d => Assert.Equal("DWARF001", d.Id));
    }

    [Fact]
    public void Reports_throws_when_the_diagnostic_is_absent()
    {
        var ex = Assert.ThrowsAny<Exception>(() => GeneratorAssert.Reports(Valid, "DWARF001"));
        Assert.Contains("Expected diagnostic DWARF001", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotReport_throws_when_the_diagnostic_is_present()
    {
        var ex = Assert.ThrowsAny<Exception>(() => GeneratorAssert.DoesNotReport(ReportsDwarf001, "DWARF001"));
        Assert.Contains("Expected NO DWARF001", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotReport_passes_when_the_diagnostic_is_absent()
    {
        GeneratorAssert.DoesNotReport(Valid, "DWARF001");
    }
}
