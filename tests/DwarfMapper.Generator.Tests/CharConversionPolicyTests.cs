// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     ISSUE-017 asked whether `char` is handled inconsistently: it is excluded from
    ///     <c>TypeInterfaces.IsIntegral</c> (so <c>int -> char</c> gets no CreateChecked converter and is refused
    ///     with DWARF005) yet counted as integer-kind by <c>NumericConverter.IsCrossCategoryLossy</c> (so
    ///     <c>char -> double</c> is allowed with a DWARF038 note). Two definitions of "char is an integer" in one
    ///     pipeline certainly LOOKS like a bug.
    ///     <para>
    ///         It is not. The split falls exactly on C#'s own implicit/explicit cast boundary: <c>char -> int</c> and
    ///         <c>char -> double</c> are IMPLICIT conversions, <c>int -> char</c> is an EXPLICIT one. Refusing the
    ///         explicit direction while allowing the implicit ones is also what the closest comparable tool does —
    ///         Mapperly made exactly this its default in v5.0, moving ExplicitCast OUT of the default conversion set
    ///         "to prevent potential data loss", so an int -> char mapping there is now opt-in too.
    ///     </para>
    ///     Deliberately kept as-is, and pinned here so a future reader who spots the asymmetry does not "fix" one
    ///     half and silently start narrowing ints into chars.
    /// </summary>
    public class CharConversionPolicyTests
    {
        private static string Pair(string srcType, string dstType)
        {
            return $$"""
                     using DwarfMapper;
                     namespace Demo;
                     public class A { public {{srcType}} V { get; set; } }
                     public class B { public {{dstType}} V { get; set; } }
                     [DwarfMapper] public partial class M { public partial B Map(A a); }
                     """;
        }

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

        // ── ISSUE-045: the FULL matrix, measured ────────────────────────────────────────────────────────
        // The open question was whether `char` is treated as an integral type or as a text boundary. Measured
        // answer: BOTH, and the two rules do not overlap or contradict.
        //
        //   • As a number, char participates exactly where C# ITSELF widens implicitly — char to ushort/int/
        //     uint/long/ulong/float/double/decimal. Every narrowing direction (anything to char, and char to
        //     sbyte/byte/short) is an explicit cast in C# and is refused with DWARF005, because a silent
        //     truncation is precisely what an auto-mapper must not do. Mapperly reached the same default in
        //     v5.0 by moving ExplicitCast out of the default conversion set.
        //   • As text, char crosses the string boundary in both directions like any other primitive, through
        //     the Parsable/ToString path rather than a cast. `string -> char` is allowed even though
        //     `int -> char` is refused, and that is coherent rather than inconsistent: a bad parse throws
        //     loudly at runtime, whereas a narrowing cast would silently keep the low 16 bits.
        //   • `bool` has no conversion with char in either direction, and gets none here.
        //
        // Pinned as ONE table so the next reader sees the whole shape at once — the previous five InlineData
        // rows above (kept, they carry the ISSUE-017 argument) covered the corners but not the boundary cases
        // that make the policy legible: ushort (the widest implicit target) and short (the nearest refused one).
        [Theory]
        // char AS A NUMBER — allowed exactly where C# widens implicitly.
        [InlineData("char", "ushort", true)]
        [InlineData("char", "int", true)]
        [InlineData("char", "uint", true)]
        [InlineData("char", "long", true)]
        [InlineData("char", "ulong", true)]
        [InlineData("char", "float", true)]
        [InlineData("char", "double", true)]
        [InlineData("char", "decimal", true)]
        // …and refused in every narrowing direction, both ways.
        [InlineData("char", "sbyte", false)]
        [InlineData("char", "byte", false)]
        [InlineData("char", "short", false)]
        [InlineData("sbyte", "char", false)]
        [InlineData("byte", "char", false)]
        [InlineData("short", "char", false)]
        [InlineData("ushort", "char", false)]
        [InlineData("int", "char", false)]
        [InlineData("uint", "char", false)]
        [InlineData("long", "char", false)]
        [InlineData("ulong", "char", false)]
        [InlineData("float", "char", false)]
        [InlineData("double", "char", false)]
        [InlineData("decimal", "char", false)]
        // char AS TEXT — the string boundary is open both ways, via parse/ToString, not a cast.
        [InlineData("char", "string", true)]
        [InlineData("string", "char", true)]
        // char is not a boolean in either direction.
        [InlineData("char", "bool", false)]
        [InlineData("bool", "char", false)]
        public void Char_conversion_matrix(string src, string dst, bool allowed)
        {
            var (diags, _) = GeneratorTestHarness.Run(Pair(src, dst));
            var error = diags.FirstOrDefault(x => x.Severity == DiagnosticSeverity.Error);

            if (allowed)
            {
                Assert.True(error is null,
                    $"{src} -> {dst} should be allowed but reported {error?.Id}: " + error?.GetMessage(CultureInfo.InvariantCulture));
            }
            else
            {
                Assert.True(error is not null, $"{src} -> {dst} should be refused but was allowed");
            }
        }

        // Control: the string boundary is not a char special case. If this ever diverges from the two `string`
        // rows above, the text rule stopped being a general rule and became an exception for char.
        [Theory]
        [InlineData("string", "int")]
        [InlineData("int", "string")]
        public void The_string_boundary_is_general_not_a_char_special_case(string s, string d)
        {
            var (diags, _) = GeneratorTestHarness.Run(Pair(s, d));
            Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
        }
    }
}
