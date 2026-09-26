// SPDX-License-Identifier: GPL-2.0-only

// ObsoleteMemberNames walks the destination type and its base types, stopping at `object`. An INTERFACE has no base type
// at all, so the walk ends on null instead — an arm no fixture had reached, because a create map refuses an interface
// destination before member resolution. An update-into writes into an existing instance and accepts one, and with
// IgnoreObsoleteMembers the obsolete interface member must still be dropped rather than the walk misbehaving.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ObsoleteInterfaceDestinationCoverageTests
    {
        [Fact]
        public void An_update_into_an_interface_destination_drops_its_obsolete_member()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } public int B { get; set; } }
                               public interface IDst { int A { get; set; } [System.Obsolete] int B { get; set; } }
                               [DwarfMapper(IgnoreObsoleteMembers = true)]
                               public partial class M { public partial void Map(Src s, IDst d); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Contains("d.A = s.A;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("d.B =", generated, StringComparison.Ordinal);
        }
    }
}
