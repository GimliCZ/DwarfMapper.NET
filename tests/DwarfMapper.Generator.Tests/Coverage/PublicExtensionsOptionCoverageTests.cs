// SPDX-License-Identifier: GPL-2.0-only

// [assembly: DwarfMapperOptions(PublicExtensions = …)] decides the accessibility of the generated extension class. A
// value of the wrong type is a compile error in the consumer's own code, and the generator still runs on that
// compilation: it must read no opt-in from it and keep the documented internal default.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PublicExtensionsOptionCoverageTests
    {
        private static string Source(string value)
        {
            return "using DwarfMapper;\n"
                   + "[assembly: DwarfMapperOptions(PublicExtensions = " + value + ")]\n"
                   + """
                     namespace Demo
                     {
                         public class Src { public int Id { get; set; } }
                         public class Dst { public int Id { get; set; } }
                         [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                     }
                     """;
        }

        [Theory]
        [InlineData("true", "public static class DwarfMapperGeneratedExtensions")]
        [InlineData("false", "internal static class DwarfMapperGeneratedExtensions")]
        [InlineData("\"yes\"", "internal static class DwarfMapperGeneratedExtensions")]
        public void The_extension_class_accessibility_follows_only_a_boolean_opt_in(string value, string expected)
        {
            var (_, generated) = GeneratorTestHarness.RunAll(Source(value));

            Assert.Contains(expected, generated, StringComparison.Ordinal);
        }
    }
}
