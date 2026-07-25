// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-007 — the facade's cached-instance field name was the mapper's fully-qualified name with '.'
///     replaced by '_'. That mapping is not injective: <c>Demo.B_C.M</c> and <c>Demo.B.C.M</c> both collapse to
///     <c>__Demo_B_C_M</c>, so the aggregate declared the same field twice → CS0102 out of generated code. Rare
///     (it needs a namespace segment containing an underscore) but perfectly legal C#.
/// </summary>
public class FieldNameCollisionTests
{
    [Fact]
    public void Mapper_names_that_collapse_to_one_field_name_still_compile()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo.B_C
                           {
                               public class S1 { public int X { get; set; } }
                               public class D1 { public int X { get; set; } }
                               [DwarfMapper] public partial class M { public partial D1 Map(S1 s); }
                           }
                           namespace Demo.B.C
                           {
                               public class S2 { public int X { get; set; } }
                               public class D2 { public int X { get; set; } }
                               [DwarfMapper] public partial class M { public partial D2 Map(S2 s); }
                           }
                           """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.DoesNotContain(errors, e => e.Id == "CS0102");
        Assert.Empty(errors);
    }
}
