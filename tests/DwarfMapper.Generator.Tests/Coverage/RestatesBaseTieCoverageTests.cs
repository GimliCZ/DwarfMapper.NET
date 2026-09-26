// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.RestatesBase.cs's TryFindBasePair tie. The base pair is the declared pair
// nearest to the derived one, counting base-class steps on the source and the target together. Two candidates at the
// SAME total distance, one a step closer on the source and the other a step closer on the target, leave no single
// nearest pair. The check refuses to guess and names both (DWARF084), instead of comparing the derived pair against
// whichever happened to be declared first.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class RestatesBaseTieCoverageTests
    {
        [Fact]
        public void Two_equally_close_base_pairs_are_reported_as_ambiguous()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class BaseSrc { public int A { get; set; } }
                                  public class MidSrc : BaseSrc { }
                                  public class DerivedSrc : MidSrc { }
                                  public class BaseTgt { public int A { get; set; } }
                                  public class MidTgt : BaseTgt { }
                                  public class DerivedTgt : MidTgt { }
                                  [DwarfMapper]
                                  [RestatesBase<DerivedSrc, DerivedTgt>]
                                  public partial class M
                                  {
                                      public partial BaseTgt MapMidToBase(MidSrc s);
                                      public partial MidTgt MapBaseToMid(BaseSrc s);
                                      public partial DerivedTgt MapDerived(DerivedSrc s);
                                  }
                                  """;

            var message = Assert.Single(GeneratorAssert.Reports(source, "DWARF084")).GetMessage(CultureInfo.InvariantCulture);

            Assert.Equal(
                "[RestatesBase<Demo.DerivedSrc, Demo.DerivedTgt>] finds more than one equally-close base pair " +
                "(Demo.MidSrc -> Demo.BaseTgt, Demo.BaseSrc -> Demo.MidTgt). Only base CLASSES are walked, so this " +
                "means two candidates sit at the same depth; declare the pair you mean and remove the other, or drop " +
                "the attribute.",
                message);
        }
    }
}
