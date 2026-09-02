// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A diagnostic raised while resolving a SYNTHESIZED nested pair is anchored at the declared method that
    ///     reached the pair — never at <c>Location.None</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A synthesized pair has no declaration of its own, and until this was pinned its resolution reported
    ///         everything — DWARF001, DWARF005, DWARF025, DWARF038, DWARF070 … — with no location at all: the
    ///         compiler printed <c>CSC : error DWARF001: Destination member 'X' has no matching source member</c>
    ///         against the project, and Rider titled it "Generator 'DwarfGenerator' failed to generate sources".
    ///         The message names the member but not the pair, and its remedy — "annotate the method" — names a
    ///         method that does not exist for a synthesized pair. With two nested pairs that each have an <c>X</c>,
    ///         the reader could not tell which one fired.
    ///     </para>
    ///     <para>
    ///         The anchor is the site that first requested the pair. Every requester threads the location it is
    ///         resolving at into <c>NestedMappingRegistry.GetOrReserve</c>; the registry hands it back with the
    ///         pair, and the drain loop resolves the pair's members under it — so a pair two levels down inherits
    ///         the declared method's location through the pair in between.
    ///     </para>
    /// </remarks>
    public class NestedPairDiagnosticLocationTests
    {
        private static Diagnostic SingleDwarf001(string source)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source);
            return Assert.Single(diagnostics, d => string.Equals(d.Id, "DWARF001", StringComparison.Ordinal));
        }

        private static int LineOf(string source, string marker)
        {
            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(marker, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"marker '{marker}' not in source");
        }

        [Fact]
        public void A_nested_pair_completeness_error_is_anchored_at_the_declared_method_that_reached_it()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class ChildS { public int A { get; set; } }
                                  public class Child { public int A { get; set; } public int TargetOnly { get; set; } }
                                  public class S { public ChildS? Child { get; set; } }
                                  public class T { public Child? Child { get; set; } }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      public partial T Map(S s); // anchor
                                  }
                                  """;

            var d = SingleDwarf001(source);

            Assert.Contains("'TargetOnly'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.NotEqual(Location.None, d.Location);
            Assert.Equal(LineOf(source, "// anchor"), d.Location.GetLineSpan().StartLinePosition.Line);
        }

        [Fact]
        public void A_pair_two_levels_down_inherits_the_anchor_through_the_pair_between()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class GrandS { public int A { get; set; } }
                                  public class Grand { public int A { get; set; } public int TargetOnly { get; set; } }
                                  public class ChildS { public GrandS? Grand { get; set; } }
                                  public class Child { public Grand? Grand { get; set; } }
                                  public class S { public ChildS? Child { get; set; } }
                                  public class T { public Child? Child { get; set; } }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      public partial T Map(S s); // anchor
                                  }
                                  """;

            var d = SingleDwarf001(source);

            Assert.Contains("'TargetOnly'", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.NotEqual(Location.None, d.Location);
            Assert.Equal(LineOf(source, "// anchor"), d.Location.GetLineSpan().StartLinePosition.Line);
        }

        [Fact]
        public void The_anchor_is_the_method_that_reached_the_pair_not_the_first_method_on_the_class()
        {
            // Two declared methods; only the SECOND one's target reaches the incomplete nested pair. C3's original
            // wording ("the first declared method's location") would have put this on line `// first`.
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class ChildS { public int A { get; set; } }
                                  public class Child { public int A { get; set; } public int TargetOnly { get; set; } }
                                  public class PlainS { public int Id { get; set; } }
                                  public class PlainT { public int Id { get; set; } }
                                  public class S { public ChildS? Child { get; set; } }
                                  public class T { public Child? Child { get; set; } }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      public partial PlainT MapPlain(PlainS s); // first
                                      public partial T Map(S s); // anchor
                                  }
                                  """;

            var d = SingleDwarf001(source);

            Assert.Equal(LineOf(source, "// anchor"), d.Location.GetLineSpan().StartLinePosition.Line);
        }

        [Fact]
        public void A_nested_pair_conversion_error_is_anchored_the_same_way()
        {
            // Not only completeness: DWARF005 from a nested pair's member resolution carries the same anchor.
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Opaque { }
                                  public class ChildS { public Opaque V { get; set; } = new(); }
                                  public class Child { public int V { get; set; } }
                                  public class S { public ChildS? Child { get; set; } }
                                  public class T { public Child? Child { get; set; } }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      public partial T Map(S s); // anchor
                                  }
                                  """;

            var (diagnostics, _) = GeneratorTestHarness.Run(source);
            var d = Assert.Single(diagnostics, x => string.Equals(x.Id, "DWARF005", StringComparison.Ordinal));

            Assert.NotEqual(Location.None, d.Location);
            Assert.Equal(LineOf(source, "// anchor"), d.Location.GetLineSpan().StartLinePosition.Line);
        }
    }
}
