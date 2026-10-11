// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[MapClone]</c> - the per-method statement that a same-type map is a deliberate clone, so <c>DWARF076</c>
    ///     is not reported for it. Owner ruling 2026-09-27: no DWARF warning may need a project-wide <c>NoWarn</c>, and
    ///     DWARF076's own message used to tell the reader to add exactly that. The statement changes nothing that is
    ///     generated; a statement on a method that is not a same-type create map accepts nothing and is
    ///     <c>DWARF116</c>.
    /// </summary>
    public class MapCloneIntentTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class A { public int Id { get; set; } public string Name { get; set; } = ""; }
                                     public class B { public int Id { get; set; } public string Name { get; set; } = ""; }
                                     """;

        private static string Mapper(string body, string classAttributes = "")
        {
            return Types + "\n" + classAttributes + "\n[DwarfMapper] public partial class M\n{\n" + body + "\n}\n";
        }

        private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string source)
        {
            return GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
        }

        private static string Message(Diagnostic d)
        {
            return d.GetMessage(CultureInfo.InvariantCulture);
        }

        [Fact]
        public void Without_MapClone_a_self_map_reports_DWARF076_naming_the_statement_and_never_NoWarn()
        {
            var d = Assert.Single(Run(Mapper("public partial A Copy(A a);")).Diagnostics, x => x.Id == "DWARF076");

            Assert.Contains("say so on the method with [MapClone]", Message(d), StringComparison.Ordinal);
            Assert.Contains("[SuppressMessage(\"DwarfMapper\", \"DWARF076\")]", Message(d), StringComparison.Ordinal);
            Assert.DoesNotContain("NoWarn", Message(d), StringComparison.Ordinal);
        }

        [Fact]
        public void MapClone_drops_DWARF076_and_changes_no_generated_byte()
        {
            var (withDiagnostics, withGenerated) = Run(Mapper("[MapClone] public partial A Copy(A a);"));
            var (_, withoutGenerated) = Run(Mapper("public partial A Copy(A a);"));

            Assert.DoesNotContain(withDiagnostics, d => d.Id is "DWARF076" or "DWARF116");
            Assert.False(string.IsNullOrWhiteSpace(withGenerated));
            Assert.Equal(withoutGenerated, withGenerated);
        }

        [Fact]
        public void MapClone_accepts_only_the_method_it_is_written_on()
        {
            var d = Assert.Single(
                Run(Mapper("[MapClone] public partial A Copy(A a);\n public partial B CopyB(B b);")).Diagnostics,
                x => x.Id == "DWARF076");

            Assert.Contains("'CopyB'", Message(d), StringComparison.Ordinal);
        }

        [Fact]
        public void MapClone_beside_the_class_level_SuppressMessage_is_not_read_as_stale()
        {
            // The precise statement must never be reported dead merely because the broad one is also there - that
            // would push the reader from the one method towards the whole class.
            var source = Mapper("[MapClone] public partial A Copy(A a);",
                "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"DwarfMapper\", \"DWARF076\")]");

            Assert.DoesNotContain(Run(source).Diagnostics, d => d.Id is "DWARF076" or "DWARF116");
        }

        [Fact]
        public void The_class_level_SuppressMessage_still_accepts_every_method_of_the_class()
        {
            var source = Mapper("public partial A Copy(A a);\n public partial B CopyB(B b);",
                "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"DwarfMapper\", \"DWARF076\")]");

            Assert.DoesNotContain(Run(source).Diagnostics, d => d.Id == "DWARF076");
        }

        [Fact]
        public void MapClone_on_a_map_between_different_types_is_DWARF116()
        {
            var d = Assert.Single(Run(Mapper("[MapClone] public partial B Map(A a);")).Diagnostics, x => x.Id == "DWARF116");

            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
            Assert.Equal(
                "[MapClone] on 'Map' accepts a same-type map that is not there: its source 'global::Demo.A' and " +
                "destination 'global::Demo.B' are different types. Remove it - left in place, it would silently accept " +
                "a same-type map introduced here later, one nobody has reviewed.",
                Message(d));
        }

        [Fact]
        public void MapClone_on_an_update_into_is_DWARF116_because_an_update_into_is_never_a_self_map()
        {
            var diagnostics = Run(Mapper("[MapClone] public partial void Refresh(A source, A target);")).Diagnostics;

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF116");
            Assert.Contains("[MapClone] on 'Refresh'", Message(d), StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics, x => x.Id == "DWARF076");
        }

        [Theory]
        [InlineData("public partial System.Linq.IQueryable<A> Project(System.Linq.IQueryable<A> q);")]
        [InlineData("public partial System.Collections.Generic.IAsyncEnumerable<A> Stream(System.Collections.Generic.IAsyncEnumerable<A> s);")]
        [InlineData("public partial void Fill(System.ReadOnlySpan<A> source, System.Span<A> destination);")]
        public void MapClone_on_a_same_type_span_stream_or_projection_map_is_DWARF116_not_accepted_in_silence(string method)
        {
            // These models record no location to join a statement by, so they are judged from the declaration -
            // without that, a same-type one of them was the one place [MapClone] was read and ignored.
            var diagnostics = Run(Mapper("[MapClone] " + method)).Diagnostics;

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF116");
            Assert.Contains("it is a span, stream or projection map, which DWARF076 never judges", Message(d), StringComparison.Ordinal);
        }

        [Fact]
        public void MapClone_does_not_reach_DWARF030_the_cycle_a_constructor_parameter_cannot_rebuild()
        {
            // [MapClone] accepts the SAME-TYPE question and nothing else. A self-map under Preserve whose constructor
            // closes a reference cycle is still DWARF030 - an error about what can be rebuilt, not about intent.
            // The shape CyclicConstructorParameterNamingTests pins DWARF030 on, with the statement added.
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public record ImmutableNode(int V, ImmutableNode? Next);
                                  [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                                  public partial class M { [MapClone] public partial ImmutableNode Copy(ImmutableNode n); }
                                  """;

            var diagnostics = Run(source).Diagnostics;

            Assert.Contains(diagnostics, d => d.Id == "DWARF030");
            Assert.DoesNotContain(diagnostics, d => d.Id is "DWARF076" or "DWARF116");
        }
    }
}
