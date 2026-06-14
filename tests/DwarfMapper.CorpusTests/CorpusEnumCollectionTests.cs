// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper;

namespace DwarfMapper.CorpusTests;

// Real-world mapping parity corpus — Group 2: enums + collections. Mirrors Mapperly enum-strategy
// semantics and the List/null-collection behaviour AutoMapper/eShop rely on. `Ce_` prefixed types.

// #21 Enum ByName, different numeric values (Mapperly ByName).
// NOTE: DwarfMapper catches an unmapped source member (e.g. a "White" with no target) at COMPILE time
// (DWARF015) — stronger than Mapperly's runtime ArgumentOutOfRangeException. So every source member here
// has a target; the compile-time-error behaviour is covered by the generator EnumToEnumTests.
public enum Ce_ColorA { Black = 1, Blue = 2 }
public enum Ce_ColorB { Yellow = 1, Green = 2, Black = 3, Blue = 4 }
public class Ce_NameSrc { public Ce_ColorA C { get; set; } }
public class Ce_NameDst { public Ce_ColorB C { get; set; } }
[DwarfMapper(EnumStrategy = EnumStrategy.ByName)] public partial class Ce_ByNameMapper { public partial Ce_NameDst Map(Ce_NameSrc s); }

// #25 Enum → int (Jason Taylor TodoItemDto: (int)Priority)
public enum Ce_Priority { None = 0, Low = 1, Medium = 2, High = 3 }
public class Ce_TodoItem { public Ce_Priority Priority { get; set; } }
public class Ce_TodoItemDto { public int Priority { get; set; } }
[DwarfMapper] public partial class Ce_EnumToIntMapper { public partial Ce_TodoItemDto Map(Ce_TodoItem s); }

// #23 Enum → string (member name)
public class Ce_StatusSrc { public Ce_ColorA C { get; set; } }
public class Ce_StatusDst { public string C { get; set; } = ""; }
[DwarfMapper] public partial class Ce_EnumToStringMapper { public partial Ce_StatusDst Map(Ce_StatusSrc s); }

// #28 List<Child> → List<ChildDto>, new instance (Mapperly Tires)
public class Ce_Tire { public string Description { get; set; } = ""; }
public class Ce_TireDto { public string Description { get; set; } = ""; }
public class Ce_Car { public List<Ce_Tire> Tires { get; set; } = new(); }
public class Ce_CarDto { public List<Ce_TireDto> Tires { get; set; } = new(); }
[DwarfMapper] public partial class Ce_CollMapper { public partial Ce_CarDto Map(Ce_Car s); }

// #29 Element map drops extra source member
public class Ce_LineItem { public string Sku { get; set; } = ""; public int Qty { get; set; } public decimal Price { get; set; } }
public class Ce_LineItemDto { public string Sku { get; set; } = ""; public int Qty { get; set; } }
public class Ce_Cart { public List<Ce_LineItem> Items { get; set; } = new(); }
public class Ce_CartDto { public List<Ce_LineItemDto> Items { get; set; } = new(); }
[DwarfMapper] public partial class Ce_CartMapper { public partial Ce_CartDto Map(Ce_Cart s); }

// #30 Null source collection → empty (AutoMapper default AllowNullCollections=false)
public class Ce_NullSrc { public List<int>? Items { get; set; } }
public class Ce_NullDst { public List<int> Items { get; set; } = new(); }
[DwarfMapper] public partial class Ce_NullCollMapper { public partial Ce_NullDst Map(Ce_NullSrc s); }

public class CorpusEnumCollectionTests
{
    [Fact]
    public void S21_Enum_by_name_maps_across_different_numeric_values()
    {
        Assert.Equal(Ce_ColorB.Black, new Ce_ByNameMapper().Map(new Ce_NameSrc { C = Ce_ColorA.Black }).C); // 1 → 3 (by name)
        Assert.Equal(Ce_ColorB.Blue, new Ce_ByNameMapper().Map(new Ce_NameSrc { C = Ce_ColorA.Blue }).C);   // 2 → 4 (by name)
    }

    [Fact]
    public void S25_Enum_to_int()
    {
        Assert.Equal(3, new Ce_EnumToIntMapper().Map(new Ce_TodoItem { Priority = Ce_Priority.High }).Priority);
    }

    [Fact]
    public void S23_Enum_to_string_member_name()
    {
        Assert.Equal("Black", new Ce_EnumToStringMapper().Map(new Ce_StatusSrc { C = Ce_ColorA.Black }).C);
    }

    [Fact]
    public void S28_List_of_children_new_instance()
    {
        var src = new Ce_Car { Tires = new List<Ce_Tire> { new() { Description = "Winter" }, new() { Description = "Summer" } } };
        var d = new Ce_CollMapper().Map(src);
        Assert.Equal(2, d.Tires.Count);
        Assert.Equal("Winter", d.Tires[0].Description);
        Assert.Equal("Summer", d.Tires[1].Description);
        Assert.NotSame(src.Tires, (object)d.Tires); // a new collection, never aliasing the source
    }

    [Fact]
    public void S29_Element_map_drops_extra_member()
    {
        var src = new Ce_Cart { Items = new List<Ce_LineItem> { new() { Sku = "A1", Qty = 2, Price = 9.99m }, new() { Sku = "B2", Qty = 1, Price = 4.00m } } };
        var d = new Ce_CartMapper().Map(src);
        Assert.Equal(new[] { "A1", "B2" }, d.Items.Select(i => i.Sku).ToArray());
        Assert.Equal(new[] { 2, 1 }, d.Items.Select(i => i.Qty).ToArray());
    }

    [Fact]
    public void S30_Null_source_collection_becomes_empty()
    {
        var d = new Ce_NullCollMapper().Map(new Ce_NullSrc { Items = null });
        Assert.NotNull(d.Items);
        Assert.Empty(d.Items);
    }
}
