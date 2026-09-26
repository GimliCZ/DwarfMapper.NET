// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A <c>[DwarfMapper]</c> class nested inside another type with an accessibility that code OUTSIDE that type
    ///     cannot name.
    /// </summary>
    /// <remarks>
    ///     The mapper's own generated half is emitted inside the containing type and compiles. The assembly-wide
    ///     aggregates do not live there: the convenience extensions, <c>AddDwarfMappers()</c> and the ambient
    ///     registration are top-level classes that hold a <c>new()</c> of every mapper. For a <c>private</c>,
    ///     <c>protected</c> or <c>private protected</c> nested mapper that reference is CS0122, raised in a generated
    ///     file the consumer did not write, and the whole assembly stops building. Those aggregates now leave out a
    ///     mapper they cannot name, exactly as the ambient registry already leaves out a map whose types another
    ///     assembly cannot name; the mapper itself is still generated and usable where its author can reach it.
    /// </remarks>
    public class NestedMapperAccessibilityTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public int Id { get; set; } }
                                     public class Dst { public int Id { get; set; } }

                                     """;

        private static string Nested(string accessibility)
        {
            return Types + "public partial class Outer { [DwarfMapper] " + accessibility
                         + " partial class M { public partial Dst Map(Src s); } }\n";
        }

        [Theory]
        [InlineData("private")]
        [InlineData("protected")]
        [InlineData("private protected")]
        public void A_mapper_nested_where_the_assembly_cannot_name_it_still_builds(string accessibility)
        {
            var source = Nested(accessibility);

            var mapper = GeneratorAssert.EmitsCompilableCode(source);
            var (_, everything) = GeneratorTestHarness.RunAll(source);

            Assert.Contains("public partial global::Demo.Dst Map(global::Demo.Src s)", mapper, StringComparison.Ordinal);
            Assert.DoesNotContain("global::Demo.Outer.M", everything, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Leaving the mapper out of the aggregates is right, and doing it SILENTLY is not: the author may well
        ///     expect <c>AddDwarfMappers()</c> to register it. DWARF110 says it was left out, names the type that
        ///     hides it, and says what to change.
        /// </summary>
        [Theory]
        [InlineData("private")]
        [InlineData("protected")]
        [InlineData("private protected")]
        public void A_mapper_left_out_of_the_aggregates_is_told_so(string accessibility)
        {
            var (diagnostics, _) = GeneratorTestHarness.RunAll(Nested(accessibility));

            var info = Assert.Single(diagnostics, d => d.Id == "DWARF110");
            Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, info.Severity);
            var message = info.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("Mapper 'Outer.M'", message, StringComparison.Ordinal);
            Assert.Contains($"'Outer.M' is {accessibility}", message, StringComparison.Ordinal);
            Assert.Contains("AddDwarfMappers()", message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The hiding link need not be the mapper itself: an internal mapper inside a PRIVATE type is just as
        ///     unreachable from namespace scope, and the message must name the type that actually hides it.
        /// </summary>
        [Fact]
        public void An_accessible_mapper_hidden_by_a_private_containing_type_names_that_type()
        {
            var source = Types + "public partial class Outer { private partial class Inner { [DwarfMapper] internal partial class M "
                               + "{ public partial Dst Map(Src s); } } }\n";

            var (diagnostics, generated) = GeneratorTestHarness.RunAll(source);

            var info = Assert.Single(diagnostics, d => d.Id == "DWARF110");
            var message = info.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("Mapper 'Outer.Inner.M'", message, StringComparison.Ordinal);
            Assert.Contains("'Outer.Inner' is private", message, StringComparison.Ordinal);
            Assert.DoesNotContain("global::Demo.Outer.Inner.M", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The control: a nested mapper the assembly CAN name keeps every aggregate, so the rule above is about
        ///     reachability and not about nesting.
        /// </summary>
        [Theory]
        [InlineData("internal")]
        [InlineData("protected internal")]
        [InlineData("public")]
        public void A_mapper_nested_where_the_assembly_can_name_it_keeps_its_aggregates(string accessibility)
        {
            var source = Nested(accessibility);

            GeneratorAssert.EmitsCompilableCode(source);
            var (diagnostics, generated) = GeneratorTestHarness.RunAll(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF110");
            Assert.Contains("AddSingleton<global::Demo.Outer.M>", generated, StringComparison.Ordinal);
            Assert.Contains("global::DwarfMapper.DwarfMapperRegistry.Register(typeof(global::Demo.Src), typeof(global::Demo.Dst)", generated, StringComparison.Ordinal);
        }
    }
}
