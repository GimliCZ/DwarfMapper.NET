// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for MapperExtractor.Projection.cs refusals that only a failing INNER resolution reaches. The
// projection resolver recurses — through a Nullable<T> lift, a reference-to-Nullable<struct> lift, a nested
// member-init, a nested constructor projection and its leftover members — and each recursion has its own
// "the inner answer was null, so give up" arm. The suite exercised every one of those paths only with an inner
// resolution that succeeded. Also the two [MapProperty]/[MapValue] validation refusals at the projection endpoint.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionRefusalCoverageTests
    {
        private const string NotGenerated = "DWARF096";

        private static string Proj(string types, string attrs = "") =>
            "using DwarfMapper;\nusing System.Linq;\nnamespace Demo;\n" + types + "\n[DwarfMapper]\npublic partial class M\n{\n" + attrs +
            "\n    public partial IQueryable<D> Project(IQueryable<S> src);\n}\n";

        private static string SingleMessage(string src, string id) =>
            Assert.Single(GeneratorAssert.Reports(src, id)).GetMessage(CultureInfo.InvariantCulture);

        [Fact]
        public void Nullable_value_lift_whose_inner_conversion_fails_is_refused()
        {
            var src = Proj("""
                           public class S { public int? A { get; set; } }
                           public class D { public System.Guid? A { get; set; } }
                           """);

            var message = SingleMessage(src, "DWARF028");
            Assert.Contains("'A'", message, StringComparison.Ordinal);
            Assert.Contains("no translatable conversion found", message, StringComparison.Ordinal);
            Assert.NotEmpty(GeneratorAssert.Reports(src, NotGenerated));
        }

        [Fact]
        public void Reference_to_nullable_struct_lift_whose_inner_member_fails_is_refused()
        {
            var src = Proj("""
                           public class S1 { public string X { get; set; } = ""; }
                           public struct D1 { public int X { get; set; } }
                           public class S { public S1? M { get; set; } }
                           public class D { public D1? M { get; set; } }
                           """);

            var message = SingleMessage(src, "DWARF028");
            Assert.Contains("'M.X'", message, StringComparison.Ordinal);
            Assert.Contains("string parse/format", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_member_init_target_member_without_a_source_is_refused()
        {
            var src = Proj("""
                           public class Inner { public int A { get; set; } }
                           public class InnerDto { public int A { get; set; } public int Extra { get; set; } }
                           public class S { public Inner I { get; set; } = new(); }
                           public class D { public InnerDto I { get; set; } = new(); }
                           """);

            Assert.Contains("'I.Extra'", SingleMessage(src, "DWARF001"), StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_constructor_projection_whose_parameter_is_untranslatable_is_refused()
        {
            var src = Proj("""
                           public class Inner { public string Start { get; set; } = ""; }
                           public class InnerDto { public InnerDto(int start) { Start = start; } public int Start { get; } }
                           public class S { public Inner I { get; set; } = new(); }
                           public class D { public InnerDto? I { get; set; } }
                           """);

            var message = SingleMessage(src, "DWARF028");
            Assert.Contains("'start'", message, StringComparison.Ordinal);
            Assert.Contains("string parse/format", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_constructor_projection_leftover_member_without_a_source_is_refused()
        {
            // The constructor takes Start; Extra is left for an object initializer on the constructor call, and has
            // no source — refused rather than dropped (R18-32's nested half).
            var src = Proj("""
                           public class Inner { public int Start { get; set; } }
                           public class InnerDto { public InnerDto(int start) { Start = start; } public int Start { get; } public int Extra { get; set; } }
                           public class S { public Inner I { get; set; } = new(); }
                           public class D { public InnerDto? I { get; set; } }
                           """);

            Assert.Contains("'I.Extra'", SingleMessage(src, "DWARF001"), StringComparison.Ordinal);
        }

        [Fact]
        public void Projection_map_property_on_an_ignored_target_is_a_conflict()
        {
            var src = Proj("""
                           public class S { public int A { get; set; } }
                           public class D { public int B { get; set; } }
                           """,
                """
                    [MapProperty("A", "B")]
                    [MapIgnore("B")]
                """);

            Assert.Contains("'B'", SingleMessage(src, "DWARF012"), StringComparison.Ordinal);
        }

        [Fact]
        public void Projection_map_value_on_an_unknown_target_is_refused()
        {
            var src = Proj("""
                           public class S { public int A { get; set; } }
                           public class D { public int A { get; set; } }
                           """,
                """
                    [MapValue("Nope", 1)]
                """);

            Assert.Contains("'Nope'", SingleMessage(src, "DWARF042"), StringComparison.Ordinal);
        }
    }
}
