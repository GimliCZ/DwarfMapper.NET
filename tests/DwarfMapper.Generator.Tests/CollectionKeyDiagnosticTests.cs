// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     DWARF074 — <c>[MapCollectionKey]</c> validity. The v1 key-based upsert refuses (loudly) anything outside its
    ///     scope rather than silently falling back to whole-collection replacement: it must be a <c>List&lt;T&gt;</c>
    ///     with the same element type on both sides and a real key member.
    /// </summary>
    public class CollectionKeyDiagnosticTests
    {
        private static string[] Ids(string source)
        {
            return GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();
        }

        [Fact]
        public void Valid_upsert_compiles_with_no_error()
        {
            const string source = """
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Item { public int Id { get; set; } public string Name { get; set; } = ""; }
                                  public class Src { public List<Item> Items { get; set; } = new(); }
                                  public class Dst { public List<Item> Items { get; set; } = new(); }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      [MapCollectionKey(nameof(Dst.Items), nameof(Item.Id))]
                                      public partial void Merge(Src src, Dst dst);
                                  }
                                  """;

            Assert.DoesNotContain(GeneratorTestHarness.Run(source).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
        }

        [Fact]
        public void Key_member_not_on_element_type_reports_DWARF074()
        {
            const string source = """
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Item { public int Id { get; set; } }
                                  public class Src { public List<Item> Items { get; set; } = new(); }
                                  public class Dst { public List<Item> Items { get; set; } = new(); }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      [MapCollectionKey(nameof(Dst.Items), "NoSuchKey")]
                                      public partial void Merge(Src src, Dst dst);
                                  }
                                  """;

            Assert.Contains("DWARF074", Ids(source));
        }

        [Fact]
        public void Different_element_types_report_DWARF074()
        {
            // v1 requires the same element type on both sides.
            const string source = """
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class ItemA { public int Id { get; set; } }
                                  public class ItemB { public int Id { get; set; } }
                                  public class Src { public List<ItemA> Items { get; set; } = new(); }
                                  public class Dst { public List<ItemB> Items { get; set; } = new(); }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      [MapCollectionKey(nameof(Dst.Items), "Id")]
                                      public partial void Merge(Src src, Dst dst);
                                  }
                                  """;

            Assert.Contains("DWARF074", Ids(source));
        }

        [Fact]
        public void Non_list_member_reports_DWARF074()
        {
            const string source = """
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Item { public int Id { get; set; } }
                                  public class Src { public HashSet<Item> Items { get; set; } = new(); }
                                  public class Dst { public HashSet<Item> Items { get; set; } = new(); }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      [MapCollectionKey(nameof(Dst.Items), "Id")]
                                      public partial void Merge(Src src, Dst dst);
                                  }
                                  """;

            Assert.Contains("DWARF074", Ids(source));
        }

        /// <summary>
        ///     The collection member may be a FIELD on both sides. Its type is looked up by name across properties AND
        ///     fields; every other fixture declared properties, so the field arm had never answered.
        /// </summary>
        [Fact]
        public void A_list_field_is_accepted_as_the_keyed_collection()
        {
            const string source = """
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Item { public int Id { get; set; } }
                                  public class Src { public List<Item> Items = new(); }
                                  public class Dst { public List<Item> Items = new(); }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      [MapCollectionKey(nameof(Dst.Items), nameof(Item.Id))]
                                      public partial void Merge(Src src, Dst dst);
                                  }
                                  """;

            Assert.DoesNotContain("DWARF074", Ids(source));
            GeneratorAssert.EmitsCompilableCode(source);
        }

        /// <summary>
        ///     A keyed collection fed from a DOTTED source path has no single source member of that name to check the
        ///     List&lt;T&gt; shape on, so it is refused — from a class source, whose walk ends at <c>object</c>, and from an
        ///     interface source, whose walk ends when the base type runs out.
        /// </summary>
        [Theory]
        [InlineData("public class Src { public Holder H { get; set; } = new(); }", "Src")]
        [InlineData("public interface ISrc { Holder H { get; } }", "ISrc")]
        public void A_keyed_collection_fed_from_a_dotted_source_path_reports_DWARF074(string sourceDeclaration, string sourceType)
        {
            var source = """
                         using System.Collections.Generic;
                         using DwarfMapper;
                         namespace Demo;
                         public class Item { public int Id { get; set; } }
                         public class Holder { public List<Item> Inner { get; set; } = new(); }
                         public class Dst { public List<Item> Items { get; set; } = new(); }

                         """
                         + sourceDeclaration + "\n"
                         + "[DwarfMapper]\npublic partial class M\n{\n"
                         + "    [MapProperty(\"H.Inner\", nameof(Dst.Items))]\n"
                         + "    [MapCollectionKey(nameof(Dst.Items), nameof(Item.Id))]\n"
                         + "    public partial void Merge(" + sourceType + " src, Dst dst);\n}\n";

            var dwarf074 = Assert.Single(GeneratorTestHarness.Run(source).Diagnostics, d => d.Id == "DWARF074");
            Assert.Contains("must be a List<T> on both source and destination",
                dwarf074.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }
    }
}
