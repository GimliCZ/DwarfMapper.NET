// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The null-forgiving <c>!</c> at synthesized-converter call sites is emitted ONLY when the source
///     member is a possibly-null reference (nullable/oblivious) AND the converter takes a non-nullable
///     parameter (scalar/nested <c>__DwarfMap_*</c> helpers). Value-type and non-nullable-reference
///     sources never get it, and collection/dictionary helpers (nullable parameter) never need it.
/// </summary>
public class NullForgivingCodegenTests
{
    private static string Gen(string src)
    {
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        return generated;
    }

    [Fact]
    public void Value_type_source_through_numeric_converter_gets_no_bang()
    {
        // long → int (CreateChecked). Source is a value type → '!' would be spurious.
        var gen = Gen("""
                      using DwarfMapper;
                      namespace Demo;
                      public class A { public long Score { get; set; } }
                      public class B { public int Score { get; set; } }
                      [DwarfMapper] public partial class M { public partial B Map(A a); }
                      """);
        Assert.Contains("(a.Score)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("(a.Score!)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_source_through_converter_gets_no_bang()
    {
        var gen = Gen("""
                      using DwarfMapper;
                      namespace Demo;
                      public enum CA { X, Y } public enum CB { X, Y }
                      public class A { public CA C { get; set; } }
                      public class B { public CB C { get; set; } }
                      [DwarfMapper] public partial class M { public partial B Map(A a); }
                      """);
        Assert.Contains("(a.C)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("(a.C!)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_nullable_reference_nested_source_gets_no_bang()
    {
        // Non-nullable Inner under #nullable enable (NotAnnotated) → '!' not needed. (Without
        // #nullable enable the member is oblivious, which is conservatively treated as may-be-null.)
        var gen = Gen("""
                      using DwarfMapper;
                      #nullable enable
                      namespace Demo;
                      public class Inner { public int X { get; set; } }
                      public class InnerD { public int X { get; set; } }
                      public class A { public Inner Inner { get; set; } = new(); }
                      public class B { public InnerD Inner { get; set; } = new(); }
                      [DwarfMapper] public partial class M { public partial B Map(A a); }
                      """);
        Assert.Contains("(a.Inner)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("(a.Inner!)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_reference_nested_source_keeps_bang()
    {
        // Nullable Inner? → non-null helper parameter → '!' IS required (suppresses CS8604).
        var gen = Gen("""
                      using DwarfMapper;
                      #nullable enable
                      namespace Demo;
                      public class Inner { public int X { get; set; } }
                      public class InnerD { public int X { get; set; } }
                      public class A { public Inner? Inner { get; set; } }
                      public class B { public InnerD Inner { get; set; } = new(); }
                      [DwarfMapper] public partial class M { public partial B Map(A a); }
                      """);
        Assert.Contains("(a.Inner!)", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Collection_source_never_gets_bang()
    {
        // Collection helpers take a nullable parameter and null-guard internally → '!' never needed,
        // even for a nullable source collection.
        var gen = Gen("""
                      using DwarfMapper;
                      using System.Collections.Generic;
                      #nullable enable
                      namespace Demo;
                      public class Inner { public int X { get; set; } }
                      public class InnerD { public int X { get; set; } }
                      public class A { public List<Inner>? Items { get; set; } }
                      public class B { public List<InnerD> Items { get; set; } = new(); }
                      [DwarfMapper] public partial class M { public partial B Map(A a); }
                      """);
        Assert.Contains("(a.Items)", gen, StringComparison.Ordinal);
        Assert.DoesNotContain("(a.Items!)", gen, StringComparison.Ordinal);
    }
}
