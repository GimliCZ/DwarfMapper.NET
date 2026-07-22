// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// DWARF074 — <c>[MapCollectionKey]</c> validity. The v1 key-based upsert refuses (loudly) anything outside its
/// scope rather than silently falling back to whole-collection replacement: it must be a <c>List&lt;T&gt;</c>
/// with the same element type on both sides and a real key member.
/// </summary>
public class CollectionKeyDiagnosticTests
{
    private static string[] Ids(string source) =>
        GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();

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
}
