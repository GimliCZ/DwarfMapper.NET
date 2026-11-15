// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A declared mapping method whose destination is an enum or a built-in scalar converts the value</b>, as
    ///     the <c>[GenerateMap]</c> form of the same pair does (round 35, T16).
    ///     <para>
    ///         Measured before: <c>partial StatusDto Map(Status s)</c> emitted
    ///         <c>return new global::Demo.StatusDto { };</c> - an object with no members - and so did <c>string</c>
    ///         to an enum, an enum to <c>int</c> and back, <c>long</c> to <c>int</c>, <c>string</c> to <c>int</c>,
    ///         <c>int?</c> to <c>int</c>: a clean build returning the default for every input. A <c>string</c>
    ///         destination was DWARF024 four times (the parameters of <c>string</c>'s constructors), a nullable enum
    ///         DWARF007 (on <c>HasValue</c>). <c>[GenerateMap]</c> had been given the conversion route for this in an
    ///         earlier round; the declared method, the same map at the other door, had not.
    ///     </para>
    ///     <para>The runtime half is <c>ScalarMapMethodRuntimeTests</c> in IntegrationTests.</para>
    /// </summary>
    public class ScalarTargetMapMethodTests
    {
        private static string Source(string declaration)
        {
            return $$"""
                     using DwarfMapper;
                     namespace Demo;
                     public enum Status { Open, Closed }
                     public enum StatusDto { Open, Closed }
                     public struct P { public int X { get; set; } }
                     public struct PDto { public int X { get; set; } }
                     public class Order { public Status State { get; set; } public int Count { get; set; } }
                     public class OrderDto { public StatusDto State { get; set; } public long Count { get; set; } }
                     {{declaration}}
                     """;
        }

        private static string Method(string source, string target)
        {
            return Source($"[DwarfMapper] public partial class M {{ public partial {target} Map({source} s); }}");
        }

        private static string Attribute(string source, string target)
        {
            return Source($"[DwarfMapper] [GenerateMap<{source}, {target}>] public partial class M {{ }}");
        }

        public static TheoryData<string, string, string> Conversions()
        {
            return new TheoryData<string, string, string>
            {
                // source, destination, the family of helper the body returns through
                { "Status", "StatusDto", "__DwarfMap_EnumName_" },
                { "string", "Status", "__DwarfMap_StrEnum_" },
                { "Status", "string", "__DwarfMap_EnumStr_" },
                { "Status", "int", "__DwarfMap_EnumNum_" },
                { "int", "Status", "__DwarfMap_NumEnum_" },
                { "long", "int", "__DwarfMap_Num_" },
                { "string", "int", "__DwarfMap_StrParse_" },
                { "int", "string", "__DwarfMap_FmtToStr_" },
                { "System.DateTime", "string", "__DwarfMap_FmtToStr_" }
            };
        }

        [Theory]
        [MemberData(nameof(Conversions))]
        public void A_declared_method_with_a_scalar_destination_returns_the_conversion(string source, string target, string helper)
        {
            var text = Method(source, target);
            var (diagnostics, generated) = GeneratorTestHarness.Run(text, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(text);
            Assert.Contains("return " + helper, generated, StringComparison.Ordinal);
            Assert.DoesNotContain("return new ", generated, StringComparison.Ordinal);
        }

        // The two doors of one map: for every such pair the declared method and the attribute return through the same
        // helper, under the same diagnostics.
        [Theory]
        [MemberData(nameof(Conversions))]
        public void The_declared_method_and_the_attribute_agree(string source, string target, string helper)
        {
            var declared = GeneratorTestHarness.Run(Method(source, target), NullableContextOptions.Enable);
            var attribute = GeneratorTestHarness.Run(Attribute(source, target), NullableContextOptions.Enable);

            Assert.Equal(attribute.Diagnostics.Select(d => d.Id).Order(StringComparer.Ordinal), declared.Diagnostics.Select(d => d.Id).Order(StringComparer.Ordinal));
            Assert.Contains("return " + helper, attribute.GeneratedSource, StringComparison.Ordinal);
            Assert.Contains("return " + helper, declared.GeneratedSource, StringComparison.Ordinal);
        }

        // A nullable on either side is a null to handle before the value can be converted, which `return helper(value);`
        // does not do: refused by name at both doors. (The declared method used to report DWARF007 on HasValue for a
        // nullable destination, and to return the default in silence for a nullable source.)
        [Theory]
        [InlineData("Status?", "StatusDto?")]
        [InlineData("int?", "Status?")]
        [InlineData("Status?", "StatusDto")]
        [InlineData("int?", "int")]
        public void A_scalar_pair_with_a_null_to_handle_is_refused_at_both_doors(string source, string target)
        {
            foreach (var text in new[] { Method(source, target), Attribute(source, target) })
            {
                var refusal = Assert.Single(GeneratorTestHarness.Run(text, NullableContextOptions.Enable).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

                Assert.Equal("DWARF117", refusal.Id);
                Assert.Contains("a null has to be handled before the value can be converted", refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }
        }

        // DWARF117: the value is assigned as it is - the same type, or an implicit conversion - so there is no
        // conversion and no body. The declared method was left with a bare CS8795 and the attribute generated nothing
        // and said nothing.
        [Theory]
        [InlineData("int", "long")]
        [InlineData("int", "int")]
        [InlineData("string", "string")]
        [InlineData("Status", "Status")]
        public void A_scalar_pair_that_needs_no_conversion_is_refused_at_both_doors(string source, string target)
        {
            var declared = Assert.Single(GeneratorTestHarness.Run(Method(source, target), NullableContextOptions.Enable).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal("DWARF117", declared.Id);
            Assert.StartsWith("Mapping method 'Map' maps '", declared.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Contains("which needs no conversion", declared.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Equal(8, declared.Location.GetLineSpan().StartLinePosition.Line);

            var attribute = Assert.Single(GeneratorTestHarness.Run(Attribute(source, target), NullableContextOptions.Enable).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal("DWARF117", attribute.Id);
            Assert.StartsWith("[GenerateMap<", attribute.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        // A struct or a class destination is an object with members, whatever its source, and is mapped as before.
        [Theory]
        [InlineData("Order", "OrderDto")]
        [InlineData("P", "PDto")]
        public void An_object_destination_is_mapped_member_by_member_as_before(string source, string target)
        {
            var text = Method(source, target);
            var (diagnostics, generated) = GeneratorTestHarness.Run(text, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(text);
            Assert.Contains("return new global::Demo." + target, generated, StringComparison.Ordinal);
        }
    }
}
