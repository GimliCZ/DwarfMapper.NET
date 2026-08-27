// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[DwarfMapper(IgnoreObsoleteMembers = true)]</c> — the point of deprecating a member is to stop feeding
    ///     it, but the completeness gate (DWARF001) works against that: it forces you to map or explicitly ignore every
    ///     obsolete destination member, and mapping one revives the data flow you are retiring. This flag drops
    ///     <c>[Obsolete]</c> members from mapping: an obsolete destination is neither required nor auto-populated, an
    ///     obsolete source needn't be consumed under RequiredMapping=Both, and an explicit [MapProperty] can still opt
    ///     a specific one back in.
    /// </summary>
    public class IgnoreObsoleteMembersTests
    {
        private static string[] Ids(string source)
        {
            return GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();
        }

        [Fact]
        public void By_default_an_obsolete_unmapped_destination_still_reports_DWARF001()
        {
            // Baseline: without the flag, an obsolete destination with no source is just an unmapped member.
            const string source = """
                                  using System;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } }
                                  public class Dst { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  [DwarfMapper]
                                  public partial class M { public partial Dst Map(Src s); }
                                  """;

            Assert.Contains("DWARF001", Ids(source));
        }

        [Fact]
        public void Obsolete_destination_member_is_dropped_and_compiles()
        {
            const string source = """
                                  using System;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } public int Legacy { get; set; } }
                                  public class Dst { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  [DwarfMapper(IgnoreObsoleteMembers = true)]
                                  public partial class M { public partial Dst Map(Src s); }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            // No completeness error, and — crucially — the obsolete member is NOT populated even though a same-named
            // source exists: deprecating it means stop feeding it.
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("A = s.A", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Legacy = s.Legacy", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_explicit_MapProperty_opts_an_obsolete_member_back_in()
        {
            const string source = """
                                  using System;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } public int Legacy { get; set; } }
                                  public class Dst { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  [DwarfMapper(IgnoreObsoleteMembers = true)]
                                  public partial class M
                                  {
                                      [MapProperty(nameof(Src.Legacy), nameof(Dst.Legacy))]
                                      public partial Dst Map(Src s);
                                  }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            // Explicit intent wins over the blanket ignore — no DWARF012 conflict, and Legacy IS mapped.
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("Legacy = s.Legacy", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Obsolete_source_member_does_not_report_DWARF039_under_RequiredMapping_Both()
        {
            const string source = """
                                  using System;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  public class Dst { public int A { get; set; } }
                                  [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both, IgnoreObsoleteMembers = true)]
                                  public partial class M { public partial Dst Map(Src s); }
                                  """;

            // Without the flag, Legacy (unconsumed source) would surface DWARF039. With it, retiring the source is
            // not nagged.
            Assert.DoesNotContain("DWARF039", Ids(source));
        }

        [Fact]
        public void Obsolete_source_member_DOES_report_DWARF039_without_the_flag()
        {
            // Control for the test above: the suppression is the flag's doing, not an accident.
            const string source = """
                                  using System;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  public class Dst { public int A { get; set; } }
                                  [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]
                                  public partial class M { public partial Dst Map(Src s); }
                                  """;

            Assert.Contains("DWARF039", Ids(source));
        }

        /// <summary>
        ///     The PROJECTION endpoint, which nothing exercised until a mutation-battery survivor said so.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The option matrix declares <c>IgnoreObsoleteMembers</c> <c>Honoured</c> at projection, and the
        ///         way it checks that is to require the generated output to DIFFER from the same source without the
        ///         option. That proves the option does <em>something</em>; it cannot say in which direction.
        ///         Inverting the guard in <c>MapperExtractor.Projection.cs</c> also produces a different output, so
        ///         that mutant survived the entire suite.
        ///     </para>
        ///     <para>
        ///         These two pin the DIRECTION: with the flag the obsolete member must be absent, and without it —
        ///         the control — it must be present. An inverted guard fails both.
        ///     </para>
        /// </remarks>
        [Fact]
        public void At_projection_an_obsolete_destination_is_dropped()
        {
            const string source = """
                                  using System;
                                  using System.Linq;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } public int Legacy { get; set; } }
                                  public class Dst { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  [DwarfMapper(IgnoreObsoleteMembers = true)]
                                  public partial class M { public partial IQueryable<Dst> Project(IQueryable<Src> q); }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("A = ", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Legacy = ", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void At_projection_an_obsolete_destination_IS_populated_without_the_flag()
        {
            // The control. Without it the test above would also pass against a projection that maps nothing at all.
            const string source = """
                                  using System;
                                  using System.Linq;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Src { public int A { get; set; } public int Legacy { get; set; } }
                                  public class Dst { public int A { get; set; } [Obsolete] public int Legacy { get; set; } }
                                  [DwarfMapper]
                                  public partial class M { public partial IQueryable<Dst> Project(IQueryable<Src> q); }
                                  """;

            var (_, generated) = GeneratorTestHarness.Run(source);

            Assert.Contains("Legacy = ", generated, StringComparison.Ordinal);
        }
    }
}
