// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-017 asked whether `char` is handled inconsistently: it is excluded from
///     <c>TypeInterfaces.IsIntegral</c> (so <c>int -> char</c> gets no CreateChecked converter and is refused
///     with DWARF005) yet counted as integer-kind by <c>NumericConverter.IsCrossCategoryLossy</c> (so
///     <c>char -> double</c> is allowed with a DWARF038 note). Two definitions of "char is an integer" in one
///     pipeline certainly LOOKS like a bug.
///     <para>
///     It is not. The split falls exactly on C#'s own implicit/explicit cast boundary: <c>char -> int</c> and
///     <c>char -> double</c> are IMPLICIT conversions, <c>int -> char</c> is an EXPLICIT one. Refusing the
///     explicit direction while allowing the implicit ones is also what the closest comparable tool does —
///     Mapperly made exactly this its default in v5.0, moving ExplicitCast OUT of the default conversion set
///     "to prevent potential data loss", so an int -> char mapping there is now opt-in too.
///     </para>
///     Deliberately kept as-is, and pinned here so a future reader who spots the asymmetry does not "fix" one
///     half and silently start narrowing ints into chars.
/// </summary>
public class CharConversionPolicyTests
{
    private static string Pair(string srcType, string dstType) => $$"""
        using DwarfMapper;
        namespace Demo;
        public class A { public {{srcType}} V { get; set; } }
        public class B { public {{dstType}} V { get; set; } }
        [DwarfMapper] public partial class M { public partial B Map(A a); }
        """;

    [Theory]
    [InlineData("int", "char")]
    [InlineData("long", "char")]
    public void Explicit_cast_to_char_is_refused(string s, string d)
    {
        var (diags, _) = GeneratorTestHarness.Run(Pair(s, d));
        Assert.Contains(diags, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("char", "int")]
    [InlineData("char", "long")]
    [InlineData("char", "double")]
    public void Implicit_cast_from_char_is_allowed(string s, string d)
    {
        var (diags, _) = GeneratorTestHarness.Run(Pair(s, d));
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
    }
}
