// SPDX-License-Identifier: GPL-2.0-only

// NestedMappingRegistry.Sanitize truncates each sanitized type segment of a synthesized helper name to 48 characters.
// No fixture declared a type long enough to reach the cap, so the truncation never ran.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class NestedHelperNameLengthTests
    {
        [Fact]
        public void An_auto_nested_helper_caps_each_long_type_segment_at_48_characters()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Averyveryverylongnamespacesegment.Anotherverylongsegment.Deeper;
                               public class InnerSourceTypeWithAVeryLongName { public int X { get; set; } }
                               public class InnerTargetTypeWithAVeryLongName { public int X { get; set; } }
                               public class S { public InnerSourceTypeWithAVeryLongName A { get; set; } = new(); }
                               public class D { public InnerTargetTypeWithAVeryLongName A { get; set; } = new(); }
                               [DwarfMapper] public partial class M { public partial D Map(S s); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            // Both fully qualified names sanitize to the same first 48 characters; the hash suffix is what keeps
            // them apart from any other pair that truncates alike. The segments contain underscores, so the name
            // is matched whole rather than split on them.
            const string segment = "global__Averyveryverylongnamespacesegment_Anothe";
            Assert.Equal(48, segment.Length);
            Assert.Matches("__DwarfMap_Obj_" + segment + "_" + segment + "_[0-9A-F]{8}\\b", generated);
        }
    }
}
