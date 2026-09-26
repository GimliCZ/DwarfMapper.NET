// SPDX-License-Identifier: GPL-2.0-only

// A user-defined conversion operator is called through a synthesized __DwarfMap_UserConv_ shim whose name is built
// from the two types' simple names. Two name shapes had never been sanitized: an ARRAY, whose simple name is empty and
// is written as a single underscore, and a name holding a character that is not a letter or digit.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class UserConversionHelperNameCoverageTests
    {
        [Fact]
        public void An_operator_from_an_array_names_its_helper_with_a_placeholder_for_the_empty_type_name()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Wrap { public static implicit operator Wrap(int[] a) => new(); }
                                  public class S { public int[] W { get; set; } = System.Array.Empty<int>(); }
                                  public class D { public Wrap W { get; set; } = new(); }
                                  [DwarfMapper] public partial class M { public partial D Map(S s); }
                                  """;

            var generated = GeneratorAssert.EmitsCompilableCode(source);

            Assert.Matches(@"__DwarfMap_UserConv___To_Wrap_[0-9a-f]{8}\(", generated);
        }

        [Fact]
        public void An_operator_on_a_type_with_an_underscore_keeps_it_in_the_helper_name()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Raw_Value { public int V { get; set; } }
                                  public class Wrap { public static implicit operator Wrap(Raw_Value r) => new(); }
                                  public class S { public Raw_Value W { get; set; } = new(); }
                                  public class D { public Wrap W { get; set; } = new(); }
                                  [DwarfMapper(AutoNest = false)] public partial class M { public partial D Map(S s); }
                                  """;

            var generated = GeneratorAssert.EmitsCompilableCode(source);

            Assert.Contains("__DwarfMap_UserConv_Raw_Value_To_Wrap_", generated, StringComparison.Ordinal);
        }
    }
}
