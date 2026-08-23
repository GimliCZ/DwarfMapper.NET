// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Members whose names are C# keywords — <c>@class</c>, <c>@event</c>, <c>@operator</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ordinary in code generated from a JSON or OpenAPI schema, where <c>class</c> and <c>event</c> are
    ///         perfectly good field names. <see cref="Microsoft.CodeAnalysis.ISymbol.Name" /> hands the name over
    ///         WITHOUT the <c>@</c> — the escape is syntax, not part of the name — so writing it straight into an
    ///         object initializer produced <c>class = src.class,</c>, which the compiler parses as a malformed
    ///         event declaration: <c>CS0065</c>/<c>CS0101</c>/<c>CS0102</c>, with an EMPTY member name, against
    ///         generated code the consumer never wrote, and with no DwarfMapper diagnostic to connect it to
    ///         anything.
    ///     </para>
    ///     <para>
    ///         Found by the R18-29 shape harvest on its first run — a shape neither this corpus nor Mapperly's had
    ///         ever asked about (Mapperly emitted the same unescaped form). Nobody writing fixtures by hand chooses
    ///         to type <c>@class</c>, which is precisely why an outside inventory was worth building.
    ///     </para>
    /// </remarks>
    public class ReservedKeywordMemberTests
    {
        private const string Types = """
                                     using System.Collections.Generic;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class S
                                     {
                                         public string @class { get; set; } = "";
                                         public int @event { get; set; }
                                         public bool @operator { get; set; }
                                     }
                                     public class D
                                     {
                                         public string @class { get; set; } = "";
                                         public int @event { get; set; }
                                         public bool @operator { get; set; }
                                     }
                                     """;

        [Fact]
        public void A_keyword_named_member_maps_and_the_emission_compiles()
        {
            var generated = GeneratorAssert.CompilesClean(Types +
                                                          """

                                                          [DwarfMapper]
                                                          [GenerateMap<S, D>]
                                                          public partial class M;
                                                          """);

            Assert.Contains("@class = src.@class", generated, StringComparison.Ordinal);
            Assert.Contains("@event = src.@event", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Update_into_escapes_them_too()
        {
            // A different emitter, and the one where the names appear on both sides of an assignment statement
            // rather than inside an object initializer.
            GeneratorAssert.CompilesClean(Types +
                                          """

                                          [DwarfMapper]
                                          public partial class M
                                          {
                                              public partial void Update(S src, D dst);
                                          }
                                          """);
        }

        [Fact]
        public void A_constructor_parameter_named_for_a_keyword_is_escaped()
        {
            // Constructor arguments are emitted as NAMED arguments (`@class: src.@class`), a third distinct site.
            GeneratorAssert.CompilesClean("""
                                          using DwarfMapper;
                                          namespace Demo;
                                          public class S { public string @class { get; set; } = ""; }
                                          public record D(string @class);

                                          [DwarfMapper]
                                          [GenerateMap<S, D>]
                                          public partial class M;
                                          """);
        }

        [Fact]
        public void A_collection_of_keyword_named_elements_is_escaped()
        {
            // The element mapper is synthesized by a different path again, so it gets its own case.
            GeneratorAssert.CompilesClean("""
                                          using System.Collections.Generic;
                                          using DwarfMapper;
                                          namespace Demo;
                                          public class Item { public string @class { get; set; } = ""; }
                                          public class ItemDto { public string @class { get; set; } = ""; }
                                          public class Box { public List<Item> @event { get; set; } = new(); }
                                          public class BoxDto { public List<ItemDto> @event { get; set; } = new(); }

                                          [DwarfMapper]
                                          [GenerateMap<Box, BoxDto>]
                                          public partial class M;
                                          """);
        }

        [Fact]
        public void A_DOTTED_source_path_escapes_each_segment_independently()
        {
            // The case a naive fix gets wrong. Flattening and deep source paths carry `a.b.c` in ONE name, so
            // "@" + name would emit `@base.class` — escaping the wrong thing and still not compiling.
            var generated = GeneratorAssert.CompilesClean("""
                                                          using DwarfMapper;
                                                          namespace Demo;
                                                          public class Inner { public string @class { get; set; } = ""; }
                                                          public class S { public Inner @base { get; set; } = new(); }
                                                          public class D { public string Name { get; set; } = ""; }

                                                          [DwarfMapper]
                                                          [GenerateMap<S, D>]
                                                          [MapProperty<S, D>("base.class", nameof(D.Name))]
                                                          public partial class M;
                                                          """);

            Assert.Contains("@base.@class", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_contextual_keyword_is_left_alone()
        {
            // `value`, `record` and `from` are perfectly good identifiers already. Escaping them would be legal
            // but would churn output for no gain, so only RESERVED keywords are escaped.
            var generated = GeneratorAssert.CompilesClean("""
                                                          using DwarfMapper;
                                                          namespace Demo;
                                                          public class S { public string value { get; set; } = ""; public int record { get; set; } }
                                                          public class D { public string value { get; set; } = ""; public int record { get; set; } }

                                                          [DwarfMapper]
                                                          [GenerateMap<S, D>]
                                                          public partial class M;
                                                          """);

            Assert.Contains("value = src.value", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("@value", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Projection_escapes_them_as_well()
        {
            // The expression-tree endpoint builds its own inline expressions and so could have been missed.
            GeneratorAssert.CompilesClean("""
                                          using System.Linq;
                                          using DwarfMapper;
                                          namespace Demo;
                                          public class S { public string @class { get; set; } = ""; }
                                          public class D { public string @class { get; set; } = ""; }

                                          [DwarfMapper]
                                          public partial class M
                                          {
                                              public partial IQueryable<D> Project(IQueryable<S> source);
                                          }
                                          """);
        }
    }
}
