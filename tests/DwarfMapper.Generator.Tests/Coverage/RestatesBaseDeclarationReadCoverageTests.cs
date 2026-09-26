// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for how MapperExtractor.RestatesBase.cs reads its inputs, on two shapes no fixture reached:
//   - ResolveByFqn over PARTIAL-METHOD pairs: with no [GenerateMap] attribute to find a type on, it walks the mapper's
//     one-parameter methods, passing over any whose parameter and return type both differ. So a hierarchy declared
//     with named partial methods is checked exactly as one declared with [GenerateMap];
//   - ReadRestatesBase's Overrides list: a null entry names nothing and is skipped, and the entries beside it still
//     exempt their members.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class RestatesBaseDeclarationReadCoverageTests
    {
        [Fact]
        public void A_hierarchy_declared_with_partial_methods_is_checked()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } }
                                  public class DerivedSrc : Src { }
                                  public class Dto { public int A { get; set; } }
                                  public class DerivedDto : Dto { }
                                  [DwarfMapper]
                                  [RestatesBase<DerivedSrc, DerivedDto>]
                                  public partial class M
                                  {
                                      [MapIgnore("A")]
                                      public partial DerivedDto MapDerived(DerivedSrc s);
                                      public partial Dto MapBase(Src s);
                                  }
                                  """;

            var message = Assert.Single(GeneratorAssert.Reports(source, "DWARF085")).GetMessage(CultureInfo.InvariantCulture);

            Assert.StartsWith("'global::Demo.DerivedSrc' -> 'global::Demo.DerivedDto' declares [RestatesBase] but does not map A", message, StringComparison.Ordinal);
            Assert.Contains("The base pair is 'global::Demo.Src' -> 'global::Demo.Dto'.", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_Overrides_entry_is_skipped_and_the_rest_still_exempt()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } }
                                  public class DerivedSrc : Src { }
                                  public class Dto { public int A { get; set; } }
                                  public class DerivedDto : Dto { }
                                  [DwarfMapper]
                                  [GenerateMap<Src, Dto>]
                                  [GenerateMap<DerivedSrc, DerivedDto>]
                                  [RestatesBase<DerivedSrc, DerivedDto>(Overrides = new[] { null!, "A" })]
                                  [MapIgnore<DerivedDto>("A")]
                                  public partial class M { }
                                  """;

            GeneratorAssert.DoesNotReport(source, "DWARF085");
        }
    }
}
