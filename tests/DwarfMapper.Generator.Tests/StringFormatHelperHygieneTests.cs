// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapProperty(StringFormat="…")]</c> REPLACES the converter the member's conversion resolved to, and
    ///     the replaced one used to stay in the synthesized-helper table and be emitted beside the formatted one as
    ///     a <c>private static</c> nothing calls (TASKS.md <c>B17</c>, filed at A4). Harmless, but it is generated
    ///     code the consumer did not ask for, in a file they cannot edit.
    ///     <para>
    ///         The interesting half of this file is not the orphan's absence — it is the CONTROL. The remedy drops
    ///         the keys the member's own resolution added, so the way to get it wrong is to drop a helper a
    ///         DIFFERENT member still needs. Both orders are pinned (the sibling declared before the formatted
    ///         member, and after it) because they take different routes: a helper an earlier member added is in the
    ///         snapshot and survives, while a later member re-adds it through the same add-if-absent synthesizer.
    ///     </para>
    /// </summary>
    public class StringFormatHelperHygieneTests
    {
        /// <summary>
        ///     Every <c>private static</c> helper the generator emitted, paired with how many times its name
        ///     appears in the whole file. A helper that is CALLED appears at least twice: once at its declaration
        ///     and once at the call. A name that appears exactly once is emitted and referenced by nothing.
        /// </summary>
        private static List<(string Name, int Occurrences)> HelperOccurrences(string generated)
        {
            return Regex.Matches(generated, @"private static [^\s]+ (__DwarfMap_\w+)\(")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Select(name => (name, Regex.Matches(generated, Regex.Escape(name)).Count))
                .ToList();
        }

        [Fact]
        public void The_converter_a_StringFormat_replaced_is_not_emitted_beside_it()
        {
            var generated = GeneratorAssert.EmitsCompilableCode("""
                                                                using DwarfMapper;
                                                                namespace Demo;
                                                                public class Src { public int N { get; set; } }
                                                                public class Dst { public string N { get; set; } }
                                                                [DwarfMapper]
                                                                public partial class M
                                                                {
                                                                    [MapProperty(nameof(Src.N), nameof(Dst.N), StringFormat = "N0")]
                                                                    public partial Dst Map(Src s);
                                                                }
                                                                """);

            // The formatted helper IS emitted and IS called — without this the test would pass on an empty file.
            Assert.Contains(".ToString(\"N0\", global::System.Globalization.CultureInfo.InvariantCulture)",
                generated,
                StringComparison.Ordinal);

            var orphans = HelperOccurrences(generated).Where(h => h.Occurrences == 1).ToList();
            Assert.True(orphans.Count == 0,
                "The generator emitted private static helper(s) that nothing calls — B17. " + "A StringFormat replaces the converter its member resolved to, and the replaced one must not " + "stay in the synthesized table:\n  " + string.Join("\n  ", orphans.Select(o => o.Name)) + "\n\n--- generated ---\n" + generated);
        }

        [Theory]
        // The sibling that needs the PLAIN int→string converter, declared BEFORE the formatted member (its helper
        // is in the snapshot the remedy takes, so it must survive removal) and AFTER it (its helper is re-added by
        // the same add-if-absent synthesizer). Different routes, same required outcome.
        [InlineData("before")]
        [InlineData("after")]
        public void A_sibling_needing_the_plain_converter_keeps_it_whichever_side_of_the_formatted_member_it_sits(
            string side)
        {
            const string formatted =
                "[MapProperty(nameof(Src.N), nameof(Dst.N), StringFormat = \"N0\")]";
            const string plain = "[MapProperty(nameof(Src.P), nameof(Dst.P))]";
            var attributes = side == "before" ? plain + "\n    " + formatted : formatted + "\n    " + plain;

            var generated = GeneratorAssert.EmitsCompilableCode($$"""
                                                                  using DwarfMapper;
                                                                  namespace Demo;
                                                                  public class Src { public int N { get; set; } public int P { get; set; } }
                                                                  public class Dst { public string N { get; set; } public string P { get; set; } }
                                                                  [DwarfMapper]
                                                                  public partial class M
                                                                  {
                                                                      {{attributes}}
                                                                      public partial Dst Map(Src s);
                                                                  }
                                                                  """);

            // The plain converter is the one the formatted member's resolution ALSO produced. Dropping it because
            // the format replaced it there would leave this member calling a method that is not emitted — which is
            // a compile error, so EmitsCompilableCode above is already the sharp end of this assertion. The two
            // explicit checks say WHICH shape is required, so a failure names the defect rather than a CS id.
            Assert.Contains("v.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)",
                generated,
                StringComparison.Ordinal);
            Assert.Contains(".ToString(\"N0\", global::System.Globalization.CultureInfo.InvariantCulture)",
                generated,
                StringComparison.Ordinal);

            var orphans = HelperOccurrences(generated).Where(h => h.Occurrences == 1).ToList();
            Assert.True(orphans.Count == 0,
                "Unused private static helper(s) emitted (B17), sibling declared " + side + ":\n  " + string.Join("\n  ", orphans.Select(o => o.Name)) + "\n\n--- generated ---\n" + generated);
        }
    }
}
