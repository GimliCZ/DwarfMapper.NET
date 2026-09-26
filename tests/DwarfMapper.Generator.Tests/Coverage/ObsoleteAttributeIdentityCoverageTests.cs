// SPDX-License-Identifier: GPL-2.0-only

// [DwarfMapper(IgnoreObsoleteMembers = true)] drops destination members marked [System.Obsolete]. IsObsolete decides that
// by the attribute's NAME and its NAMESPACE, and only `System.ObsoleteAttribute` itself had ever been seen: no fixture
// put a different attribute on a destination member, or an attribute that merely shares the name. A consumer's own
// `ObsoleteAttribute` (or a library's) is common enough, and silently dropping those members would lose data with no
// diagnostic — the ignored member simply keeps its default.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ObsoleteAttributeIdentityCoverageTests
    {
        [Fact]
        public void Only_System_Obsolete_drops_a_destination_member_not_a_same_named_or_unrelated_attribute()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Other { public sealed class ObsoleteAttribute : System.Attribute { } }
                               namespace Demo
                               {
                                   public class Src { public int A { get; set; } public int B { get; set; } public int C { get; set; } }
                                   public class Dst
                                   {
                                       [Other.Obsolete] public int A { get; set; }
                                       [System.ComponentModel.Description("d")] public int B { get; set; }
                                       [System.Obsolete] public int C { get; set; }
                                   }
                                   [DwarfMapper(IgnoreObsoleteMembers = true)]
                                   public partial class M { public partial Dst Map(Src s); }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Contains("A = s.A,", generated, StringComparison.Ordinal);
            Assert.Contains("B = s.B,", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("C = s.C", generated, StringComparison.Ordinal);
        }
    }
}
