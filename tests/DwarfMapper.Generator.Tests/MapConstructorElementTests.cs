// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     A <c>[MapConstructor]</c> factory must not be adopted as the ELEMENT converter for a collection
    ///     pair over the same types.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Regression: found migrating a ~300-map codebase off AutoMapper. For a pair configured as
    ///     </para>
    ///     <code>
    ///         [GenerateMap&lt;Src, Dst&gt;]
    ///         [MapConstructor&lt;Src, Dst&gt;(nameof(Create))]     // Create ignores its argument
    ///     </code>
    ///     <para>
    ///         plus a collection pair <c>IEnumerable&lt;Src&gt; -&gt; ICollection&lt;Dst&gt;</c>, the emitted element
    ///         loop was <c>__r.Add(Create(__item))</c> — the bare factory, WITHOUT the member assignments the
    ///         real element map performs. The scalar <c>Map(Src)</c> in the same file correctly calls the
    ///         factory and then fills members; only the collection path skipped that.
    ///     </para>
    ///     <para>
    ///         The factory's whole purpose is to construct for its pair, after which the pair assigns members.
    ///         Treating it as a complete <c>Src -&gt; Dst</c> converter drops every member — silently, with a
    ///         green build and no diagnostic. In the codebase that surfaced it, the factory returned an
    ///         <c>Empty</c> singleton, so an entire collection mapped to blank objects.
    ///     </para>
    ///     <para>
    ///         Same root cause as the <c>Use=</c> over-reach: element resolution scans class methods for a
    ///         matching <c>(source, target)</c> signature, and a method dedicated to one pair matched.
    ///     </para>
    /// </remarks>
    public class MapConstructorElementTests
    {
        private const string Source = """
                                      using System.Collections.Generic;
                                      using DwarfMapper;
                                      namespace Demo;

                                      public class Src { public string Name { get; set; } = ""; public int Age { get; set; } }

                                      public class Dst
                                      {
                                          public static Dst Empty => new();
                                          public string Name { get; set; } = "";
                                          public int Age { get; set; }
                                      }

                                      [DwarfMapper]
                                      [GenerateMap<Src, Dst>]
                                      [MapConstructor<Src, Dst>(nameof(Create))]
                                      [GenerateMap<List<Src>, List<Dst>>]
                                      public partial class M
                                      {
                                          // Deliberately ignores its argument, exactly like the Empty-returning
                                          // factory that surfaced this.
                                          private static Dst Create(Src s) => Dst.Empty;
                                      }
                                      """;

        [Fact]
        public void Collection_element_mapping_does_not_call_the_constructor_factory_alone()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source);

            // The bug's signature: the element loop adding the bare factory result.
            Assert.DoesNotContain("Add(Create(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Collection_element_mapping_goes_through_the_full_pair_map()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Source);

            // Whatever it is named, the element conversion must be the pair's own map — the one that calls
            // the factory AND assigns members — not the factory by itself.
            Assert.Contains("Name = ", generated, StringComparison.Ordinal);
            Assert.Contains("Age = ", generated, StringComparison.Ordinal);
        }
    }
}
