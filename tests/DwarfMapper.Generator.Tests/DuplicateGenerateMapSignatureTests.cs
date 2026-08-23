// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>DWARF094</c>: a <c>[GenerateMap&lt;S, T&gt;]</c> that would synthesize a <c>Map</c> method whose
    ///     exact signature AND return type the class already produces — the same pair declared twice, or the
    ///     pair declared beside a partial mapping method with the same name over the same pair.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both shapes used to reach the compiler as a raw <c>CS0111</c> plus a <c>CS0121</c> cascade in the
    ///         GENERATED file (B27 — eight surface-matrix cells in the <c>EmittedInvalidCode</c> population).
    ///         The refusal lives in the same signature pass as <c>DWARF060</c>, which is the point: identical
    ///         signature with a DIFFERENT return type was already refused there, and identical-with-identical
    ///         fell through a <c>continue</c> labelled "a duplicate-pair concern" that nothing downstream owned.
    ///     </para>
    ///     <para>
    ///         The legal neighbours are pinned here too (the B4 lesson — a new build-breaking Error needs its
    ///         false-positive guard in the same commit): a differently-NAMED partial over the same pair, two
    ///         pairs sharing only a source or only a target, and the wrapper family's expansion.
    ///     </para>
    /// </remarks>
    public class DuplicateGenerateMapSignatureTests
    {
        private const string Types = """
                                     using System;
                                     using System.Linq;
                                     using System.Collections.Generic;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public int Id { get; set; } }
                                     public class Dst { public int Id { get; set; } }
                                     public class Other { public int Id { get; set; } }
                                     """;

        [Fact]
        public void The_same_pair_declared_twice_is_refused_and_no_CS0111_reaches_the_consumer()
        {
            const string source = Types +
                                  """

                                  [DwarfMapper]
                                  [GenerateMap<Src, Dst>]
                                  [GenerateMap<Src, Dst>]
                                  public partial class M
                                  {
                                  }
                                  """;

            var report = GeneratorAssert.Reports(source, "DWARF094")[0];
            Assert.Equal(DiagnosticSeverity.Error, report.Severity);

            var message = report.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("Duplicate [GenerateMap]", message, StringComparison.Ordinal);
            Assert.Contains("declared more than once on this class", message, StringComparison.Ordinal);
            Assert.Contains("remove the duplicate [GenerateMap] attribute", message, StringComparison.Ordinal);

            // The whole point: the collision must never reach the consumer as a compiler error in a generated
            // file. This is the DWARF060 test's assertion shape, on the identical-return twin it never covered.
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(source);
            Assert.DoesNotContain(errors, d => string.Equals(d.Id, "CS0111", StringComparison.Ordinal));
        }

        [Fact]
        public void A_pair_already_mapped_by_a_declared_partial_of_the_same_name_is_refused()
        {
            var report = GeneratorAssert.Reports(Types +
                                                 """

                                                 [DwarfMapper]
                                                 [GenerateMap<Src, Dst>]
                                                 public partial class M
                                                 {
                                                     public partial Dst Map(Src s);
                                                 }
                                                 """,
                "DWARF094")[0];

            Assert.Equal(DiagnosticSeverity.Error, report.Severity);

            var message = report.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("already declares a partial method with that exact signature over the same pair",
                message,
                StringComparison.Ordinal);
            Assert.Contains("remove the [GenerateMap] and keep the partial method, or delete the partial method",
                message,
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_mode2_colocated_host_with_a_duplicate_pair_is_refused_too()
        {
            // Mode 2: [GenerateMap] on a PLAIN class (no [DwarfMapper]) — the co-located host, where the matrix
            // measured two of B27's cells. The blocking Error must suppress the synthesized <Host>Mapper the
            // same way it suppresses a mapper class's body.
            const string source = Types +
                                  """

                                  [GenerateMap<Src, Dst>]
                                  [GenerateMap<Src, Dst>]
                                  public sealed class Host
                                  {
                                  }
                                  """;

            GeneratorAssert.Reports(source, "DWARF094");

            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(source);
            Assert.DoesNotContain(errors, d => string.Equals(d.Id, "CS0111", StringComparison.Ordinal));
        }

        // ── The legal neighbours, pinned in the same commit as the Error that could misfire on them ─────

        [Fact]
        public void A_differently_named_partial_over_the_same_pair_stays_accepted()
        {
            // The remedy DWARF060 prescribes for its own shape — a distinctly-named partial — must not trip
            // this refusal: the signatures differ by NAME, so nothing collides.
            var source = Types +
                         """

                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         public partial class M
                         {
                             public partial Dst MapToOther(Src s);
                         }
                         """;

            GeneratorAssert.DoesNotReport(source, "DWARF094");
            GeneratorAssert.CompilesClean(source);
        }

        [Fact]
        public void Two_pairs_sharing_only_a_target_stay_accepted()
        {
            // Map(Src) and Map(Other) are legal overloads — the parameter types differ.
            var source = Types +
                         """

                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         [GenerateMap<Other, Dst>]
                         public partial class M
                         {
                         }
                         """;

            GeneratorAssert.DoesNotReport(source, "DWARF094");
            GeneratorAssert.CompilesClean(source);
        }

        [Fact]
        public void Two_pairs_sharing_a_source_are_DWARF060_not_DWARF094()
        {
            // The neighbouring rule keeps its territory: same signature with a DIFFERENT return type is the
            // return-type-overload clash, and it must not start double-reporting under the new id.
            const string source = Types +
                                  """

                                  [DwarfMapper]
                                  [GenerateMap<Src, Dst>]
                                  [GenerateMap<Src, Other>]
                                  public partial class M
                                  {
                                  }
                                  """;

            GeneratorAssert.Reports(source, "DWARF060");
            GeneratorAssert.DoesNotReport(source, "DWARF094");
        }

        [Fact]
        public void A_wrapper_expansion_meeting_an_explicit_closed_pair_defers_rather_than_duplicating()
        {
            // The 1-of-N check the A13 lesson demands, measured rather than assumed: [GenerateWrapperMap]
            // appends W<A> -> W<B> to the same pair list the detection reads — but its expansion loop already
            // guards this exact shape, skipping any closed pair the user declared explicitly (and, for the
            // same reason, anything a second wrapper attribute already appended). So the wrapper path cannot
            // manufacture a duplicate, and this shape is LEGAL: no DWARF094, one emission per pair. This test
            // pins the sibling guard so removing it fails here rather than resurfacing as broken emission.
            const string source = Types +
                                  """

                                  public class Envelope<T> { public T Payload { get; set; } = default!; }

                                  [DwarfMapper]
                                  [GenerateMap<Src, Dst>]
                                  [GenerateMap<Envelope<Src>, Envelope<Dst>>]
                                  [GenerateWrapperMap(typeof(Envelope<>))]
                                  public partial class M
                                  {
                                  }
                                  """;

            GeneratorAssert.DoesNotReport(source, "DWARF094");
            GeneratorAssert.CompilesClean(source);
        }
    }
}
