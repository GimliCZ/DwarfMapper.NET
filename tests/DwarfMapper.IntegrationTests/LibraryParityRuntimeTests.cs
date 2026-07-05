// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.IntegrationTests;

// Library-parity suite: one runtime test per competitor feature/mechanic, proving DwarfMapper reproduces
// the equivalent behaviour described in docs/MIGRATION.md. Each test names the AutoMapper 14 / Mapster /
// Mapperly feature it validates. Where DwarfMapper deliberately DIVERGES (e.g. enum default flips to
// by-name, update replaces nested), the test pins DwarfMapper's documented behaviour and the comment says
// so. Compile-time-only parity (completeness gate, source coverage) lives in the generator test project;
// this file covers runtime behaviour. File-scoped domain types use a `Par_` prefix to stay unique.

#region domain types

public class Par_Src
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
}

public class Par_Dst
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class Par_RenameMapper
{
    [MapProperty(nameof(Par_Src.FullName), nameof(Par_Dst.Name))]
    public partial Par_Dst Map(Par_Src s);
}

public class Par_IgnSrc
{
    public int Id { get; set; }
}

public class Par_IgnDst
{
    public int Id { get; set; }
    public string Computed { get; set; } = "";
}

[DwarfMapper]
public partial class Par_IgnoreMapper
{
    [MapIgnore(nameof(Par_IgnDst.Computed))]
    public partial Par_IgnDst Map(Par_IgnSrc s);
}

public class Par_ConstSrc
{
    public int Id { get; set; }
}

public class Par_ConstDst
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public int Stamp { get; set; }
}

[DwarfMapper]
public partial class Par_ConstMapper
{
    [MapValue(nameof(Par_ConstDst.Source), "api-v2")] // AutoMapper MapFrom(_=>const) / Mapperly [MapValue]
    [MapValue(nameof(Par_ConstDst.Stamp), Use = nameof(FixedStamp))] // AutoMapper MapFrom(_=>fn())
    public partial Par_ConstDst Map(Par_ConstSrc s);

    private static int FixedStamp()
    {
        return 1234;
    }
}

public class Par_ConvSrc
{
    public int Cents { get; set; }
}

public class Par_ConvDst
{
    public string Price { get; set; } = "";
}

[DwarfMapper]
public partial class Par_ConverterMapper
{
    [MapProperty(nameof(Par_ConvSrc.Cents), nameof(Par_ConvDst.Price), Use = nameof(ToMoney))]
    public partial Par_ConvDst Map(Par_ConvSrc s);

    private static string ToMoney(int cents)
    {
        return "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }
}

public class Par_NullSubSrc
{
    public string? Name { get; set; }
}

public class Par_NullSubDst
{
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class Par_NullSubMapper
{
    [MapProperty(nameof(Par_NullSubSrc.Name), nameof(Par_NullSubDst.Name), NullSubstitute = "(none)")]
    public partial Par_NullSubDst Map(Par_NullSubSrc s);
}

public class Par_WhenSrc
{
    public int Age { get; set; }
}

public class Par_WhenDst
{
    public int Age { get; set; }
}

[DwarfMapper]
public partial class Par_WhenMapper
{
    [MapProperty(nameof(Par_WhenSrc.Age), nameof(Par_WhenDst.Age), When = nameof(IsAdult))]
    public partial Par_WhenDst Map(Par_WhenSrc s);

    private static bool IsAdult(Par_WhenSrc s)
    {
        return s.Age >= 18;
    }
}

public class Par_Addr
{
    public string City { get; set; } = "";
}

public class Par_FlatSrc
{
    public int Id { get; set; }
    public Par_Addr Address { get; set; } = new();
}

public class Par_FlatDst
{
    public int Id { get; set; }
    public string City { get; set; } = "";
}

[DwarfMapper]
public partial class Par_FlattenMapper
{
    [Flatten(nameof(Par_FlatSrc.Address))]
    public partial Par_FlatDst Map(Par_FlatSrc s);
}

public class Par_Cust
{
    public string Name { get; set; } = "";
}

public class Par_PathSrc
{
    public int Id { get; set; }
    public Par_Cust Customer { get; set; } = new();
}

public class Par_PathDst
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
}

[DwarfMapper]
public partial class Par_DeepPathMapper
{
    [MapProperty("Customer.Name", nameof(Par_PathDst.CustomerName))]
    public partial Par_PathDst Map(Par_PathSrc s);
}

public class Par_UnflatSrc
{
    public int Id { get; set; }
    public string City { get; set; } = "";
}

public class Par_UnflatDst
{
    public int Id { get; set; }
    public Par_Addr Address { get; set; } = new();
}

[DwarfMapper]
public partial class Par_UnflattenMapper
{
    [MapProperty(nameof(Par_UnflatSrc.City), "Address.City")]
    public partial Par_UnflatDst Map(Par_UnflatSrc s);
}

public class Par_CtorSrc
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
}

public record Par_CtorDst(int Id, string Title);

[DwarfMapper]
public partial class Par_CtorParamMapper
{
    [MapProperty(nameof(Par_CtorSrc.Label), "Title")] // AutoMapper ForCtorParam
    public partial Par_CtorDst Map(Par_CtorSrc s);
}

public class Par_RevEntity
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
}

public class Par_RevDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class Par_ReverseMapper
{
    [ReverseMap]
    [MapProperty(nameof(Par_RevEntity.FullName), nameof(Par_RevDto.Name))]
    public partial Par_RevDto ToDto(Par_RevEntity e);

    public partial Par_RevEntity FromDto(Par_RevDto d);
}

public class Par_Animal
{
    public string Name { get; set; } = "";
}

public class Par_Cat : Par_Animal
{
    public int Lives { get; set; }
}

public class Par_AnimalDto
{
    public string Name { get; set; } = "";
}

public class Par_CatDto : Par_AnimalDto
{
    public int Lives { get; set; }
}

[DwarfMapper]
public partial class Par_DerivedMapper
{
    [MapDerivedType<Par_Cat, Par_CatDto>]
    public partial Par_AnimalDto Map(Par_Animal a);
}

public enum Par_E1
{
    Red,
    Green,
    Blue
}

public enum Par_E2
{
    Blue,
    Green,
    Red
} // different ordinals on purpose

public class Par_EnumSrc
{
    public Par_E1 Color { get; set; }
}

public class Par_EnumDst
{
    public Par_E2 Color { get; set; }
}

[DwarfMapper]
public partial class Par_EnumByNameMapper // DwarfMapper / Mapperly default: by name
{
    public partial Par_EnumDst Map(Par_EnumSrc s);
}

[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
public partial class Par_EnumByValueMapper // AutoMapper/Mapster default
{
    public partial Par_EnumDst Map(Par_EnumSrc s);
}

public class Par_NullCollSrc
{
    public List<int>? Items { get; set; }
}

public class Par_NullCollEmptyDst
{
    public List<int> Items { get; set; } = new();
} // non-nullable dest → AsEmpty

public class Par_NullCollNullDst
{
    public List<int>? Items { get; set; }
} // nullable dest → AsNull can propagate

[DwarfMapper]
public partial class Par_NullCollEmptyMapper // AutoMapper default AllowNullCollections=false
{
    public partial Par_NullCollEmptyDst Map(Par_NullCollSrc s);
}

[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
public partial class Par_NullCollNullMapper // AllowNullCollections=true
{
    public partial Par_NullCollNullDst Map(Par_NullCollSrc s);
}

public class Par_Node
{
    public int V { get; set; }
    public Par_Node? Next { get; set; }
}

public class Par_NodeDto
{
    public int V { get; set; }
    public Par_NodeDto? Next { get; set; }
}

[DwarfMapper(MaxDepth = 32)]
public partial class Par_DepthMapper // AutoMapper/Mapster MaxDepth
{
    public partial Par_NodeDto Map(Par_Node n);
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class Par_PreserveMapper // PreserveReferences
{
    public partial Par_NodeDto Map(Par_Node n);
}

[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
public partial class Par_SetNullMapper // System.Text.Json IgnoreCycles
{
    public partial Par_NodeDto Map(Par_Node n);
}

public class Par_Parent
{
    public Par_Node Left { get; set; } = new();
    public Par_Node Right { get; set; } = new();
}

public class Par_ParentDto
{
    public Par_NodeDto Left { get; set; } = new();
    public Par_NodeDto Right { get; set; } = new();
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class Par_SharedMapper
{
    public partial Par_ParentDto Map(Par_Parent p);
}

public class Par_UpdSrc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Par_UpdDst
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper]
public partial class Par_UpdateMapper // AutoMapper Map(s,d) / Mapster Adapt(s,d) / Mapperly existing target
{
    public partial Par_UpdDst Update(Par_UpdSrc s, Par_UpdDst d);
}

public class Par_ApSrc
{
    public int Id { get; set; }
}

public class Par_ApDst
{
    public int Id { get; set; }
    public string Tenant { get; set; } = "";
}

[DwarfMapper]
public partial class Par_ExtraParamMapper // AutoMapper context.Items, by typed param
{
    public partial Par_ApDst Map(Par_ApSrc s, string tenant);
}

public class Par_SnakeSrc
{
    public int user_id { get; set; }
    public string user_name { get; set; } = "";
}

public class Par_PascalDst
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
}

[DwarfMapper(NameConvention = NameConvention.Flexible)]
public partial class Par_FlexibleMapper // Mapster Flexible
{
    public partial Par_PascalDst Map(Par_SnakeSrc s);
}

public class Par_CiSrc
{
    public int id { get; set; }
    public string name { get; set; } = "";
}

public class Par_CiDst
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[DwarfMapper(CaseInsensitive = true)]
public partial class Par_CaseInsensitiveMapper // Mapster IgnoreCase / Mapperly CaseInsensitive
{
    public partial Par_CiDst Map(Par_CiSrc s);
}

public class Par_HookSrc
{
    public int Id { get; set; }
}

public class Par_HookDst
{
    public int Id { get; set; }
    public string Audit { get; set; } = "";
}

[DwarfMapper]
public partial class Par_HookMapper // AutoMapper Before/AfterMap, Mapster Before/AfterMapping
{
    [MapIgnore(nameof(Par_HookDst.Audit))]
    public partial Par_HookDst Map(Par_HookSrc s);

    [AfterMap]
    private static void Stamp(Par_HookSrc s, Par_HookDst d)
    {
        d.Audit = $"src#{s.Id}";
    }
}

public class Par_ScalarSrc
{
    public int Count { get; set; }
    public string Amount { get; set; } = "";
}

public class Par_ScalarDst
{
    public long Count { get; set; }
    public int Amount { get; set; }
}

[DwarfMapper]
public partial class Par_ScalarMapper // built-in widening + IParsable, all libraries
{
    public partial Par_ScalarDst Map(Par_ScalarSrc s);
}

public class Par_Money
{
    public int V { get; set; }
}

public class Par_MoneyDto
{
    public int V { get; set; }
    public string Via { get; set; } = "";
}

public class Par_UmSrc
{
    public Par_Money Amount { get; set; } = new();
}

public class Par_UmDst
{
    public Par_MoneyDto Amount { get; set; } = new();
}

[DwarfMapper]
public partial class Par_UserMethodMapper // Mapperly [UserMapping] / AutoMapper ConvertUsing precedence
{
    public partial Par_UmDst Map(Par_UmSrc s);

    // A user-defined conversion for the (Par_Money, Par_MoneyDto) pair takes precedence over auto-nesting.
    // (Auto-nesting could not satisfy Par_MoneyDto.Via anyway — proving the user method is the one used.)
    private static Par_MoneyDto Convert(Par_Money m)
    {
        return new Par_MoneyDto { V = m.V, Via = "user" };
    }
}

#endregion

public class LibraryParityRuntimeTests
{
    // AutoMapper ForMember.MapFrom / Mapster .Map(d,s) / Mapperly [MapProperty] — rename.
    [Fact]
    public void Rename_member_parity()
    {
        Assert.Equal("Ada", new Par_RenameMapper().Map(new Par_Src { Id = 1, FullName = "Ada" }).Name);
    }

    // AutoMapper .Ignore() / Mapster .Ignore() / Mapperly [MapperIgnoreTarget] — ignored stays default.
    [Fact]
    public void Ignore_member_parity()
    {
        Assert.Equal("", new Par_IgnoreMapper().Map(new Par_IgnSrc { Id = 1 }).Computed);
    }

    // AutoMapper MapFrom(_=>const) / Mapperly [MapValue] — constant + computed.
    [Fact]
    public void Constant_and_computed_value_parity()
    {
        var d = new Par_ConstMapper().Map(new Par_ConstSrc { Id = 1 });
        Assert.Equal("api-v2", d.Source);
        Assert.Equal(1234, d.Stamp);
    }

    // AutoMapper IValueConverter / Mapster .Map expr / Mapperly Use — custom per-member converter.
    [Fact]
    public void Custom_converter_parity()
    {
        Assert.Equal("$1.50", new Par_ConverterMapper().Map(new Par_ConvSrc { Cents = 150 }).Price);
    }

    // AutoMapper .NullSubstitute(v).
    [Fact]
    public void Null_substitution_parity()
    {
        Assert.Equal("(none)", new Par_NullSubMapper().Map(new Par_NullSubSrc { Name = null }).Name);
        Assert.Equal("Ada", new Par_NullSubMapper().Map(new Par_NullSubSrc { Name = "Ada" }).Name);
    }

    // AutoMapper .Condition / Mapster .IgnoreIf — gated assignment keeps default when false.
    [Fact]
    public void Conditional_member_parity()
    {
        Assert.Equal(21, new Par_WhenMapper().Map(new Par_WhenSrc { Age = 21 }).Age);
        Assert.Equal(0, new Par_WhenMapper().Map(new Par_WhenSrc { Age = 10 }).Age); // condition false → default
    }

    // AutoMapper convention flattening / Mapster convention — DwarfMapper explicit [Flatten], same result.
    [Fact]
    public void Flattening_parity()
    {
        Assert.Equal("Brno",
            new Par_FlattenMapper().Map(new Par_FlatSrc { Address = new Par_Addr { City = "Brno" } }).City);
    }

    // Mapperly / AutoMapper deep source path.
    [Fact]
    public void Deep_source_path_parity()
    {
        Assert.Equal("Ada",
            new Par_DeepPathMapper().Map(new Par_PathSrc { Customer = new Par_Cust { Name = "Ada" } }).CustomerName);
    }

    // AutoMapper ForPath / ReverseMap unflattening — single-level dotted target.
    [Fact]
    public void Unflattening_parity()
    {
        Assert.Equal("Brno", new Par_UnflattenMapper().Map(new Par_UnflatSrc { City = "Brno" }).Address.City);
    }

    // AutoMapper ForCtorParam — redirect a source member onto a differently-named ctor param.
    [Fact]
    public void Constructor_parameter_redirect_parity()
    {
        var d = new Par_CtorParamMapper().Map(new Par_CtorSrc { Id = 7, Label = "Boss" });
        Assert.Equal(7, d.Id);
        Assert.Equal("Boss", d.Title);
    }

    // AutoMapper .ReverseMap() / Mapster .TwoWays() — both directions, rename inverted.
    [Fact]
    public void Reverse_map_parity()
    {
        var m = new Par_ReverseMapper();
        var dto = m.ToDto(new Par_RevEntity { Id = 1, FullName = "Ada" });
        Assert.Equal("Ada", dto.Name);
        var back = m.FromDto(dto);
        Assert.Equal("Ada", back.FullName); // Name → FullName inverted
    }

    // AutoMapper .Include<DS,DD>() / Mapperly [MapDerivedType] — runtime polymorphic dispatch.
    [Fact]
    public void Derived_type_dispatch_parity()
    {
        Par_Animal a = new Par_Cat { Name = "Tom", Lives = 9 };
        var dto = new Par_DerivedMapper().Map(a);
        var cat = Assert.IsType<Par_CatDto>(dto);
        Assert.Equal("Tom", cat.Name);
        Assert.Equal(9, cat.Lives);
    }

    // DwarfMapper/Mapperly DEFAULT = by name: ordinals differ, names line up → matched by name.
    [Fact]
    public void Enum_by_name_default_parity()
    {
        Assert.Equal(Par_E2.Green, new Par_EnumByNameMapper().Map(new Par_EnumSrc { Color = Par_E1.Green }).Color);
    }

    // AutoMapper/Mapster DEFAULT = by value (DIVERGENT default): casts the underlying ordinal.
    [Fact]
    public void Enum_by_value_opt_in_parity()
    // Par_E1.Green has ordinal 1; cast to Par_E2 ordinal 1 = Green here too, but Red(0)->Blue(0) proves value-cast.
    {
    Assert.Equal(Par_E2.Blue, new Par_EnumByValueMapper().Map(new Par_EnumSrc { Color = Par_E1.Red }).Color);
    }

    // AutoMapper default AllowNullCollections=false — null source collection → empty (DwarfMapper default).
    [Fact]
    public void Null_collection_becomes_empty_parity()
    {
        var d = new Par_NullCollEmptyMapper().Map(new Par_NullCollSrc { Items = null });
        Assert.NotNull(d.Items);
        Assert.Empty(d.Items);
    }

    // AutoMapper AllowNullCollections=true — null source collection → null (opt-in).
    [Fact]
    public void Null_collection_stays_null_opt_in_parity()
    {
        Assert.Null(new Par_NullCollNullMapper().Map(new Par_NullCollSrc { Items = null }).Items);
    }

    // AutoMapper/Mapster MaxDepth — a cycle throws a catchable depth exception, never a silent StackOverflow.
    [Fact]
    public void MaxDepth_throws_on_cycle_parity()
    {
        var head = new Par_Node { V = 1 };
        head.Next = head; // self-cycle
        Assert.Throws<DwarfMappingDepthException>(() => new Par_DepthMapper().Map(head));
    }

    // AutoMapper/Mapster PreserveReferences — shared node mapped to a single shared instance (identity kept).
    [Fact]
    public void PreserveReferences_keeps_shared_identity_parity()
    {
        var shared = new Par_Node { V = 42 };
        var p = new Par_Parent { Left = shared, Right = shared };
        var dto = new Par_SharedMapper().Map(p);
        Assert.Same(dto.Left, dto.Right); // one instance, relinked isomorphically
        Assert.Equal(42, dto.Left.V);
    }

    // System.Text.Json ReferenceHandler.IgnoreCycles — cycle broken by nulling the back-edge (DwarfMapper-only).
    [Fact]
    public void OnCycle_SetNull_breaks_cycle_parity()
    {
        var head = new Par_Node { V = 1 };
        head.Next = head;
        var dto = new Par_SetNullMapper().Map(head);
        Assert.Equal(1, dto.V);
        Assert.Null(dto.Next); // re-entrant back-edge nulled → finite acyclic projection
    }

    // AutoMapper Map(s,d) / Mapster Adapt(s,d) / Mapperly existing-target — identity preserved, fields updated.
    [Fact]
    public void Update_into_existing_preserves_identity_parity()
    {
        var dest = new Par_UpdDst { Id = 0, Name = "old" };
        var result = new Par_UpdateMapper().Update(new Par_UpdSrc { Id = 5, Name = "new" }, dest);
        Assert.Same(dest, result);
        Assert.Equal(5, dest.Id);
        Assert.Equal("new", dest.Name);
    }

    // AutoMapper context.Items — DwarfMapper uses a typed extra parameter matched to a dest member by name.
    [Fact]
    public void Additional_parameter_parity()
    {
        Assert.Equal("acme", new Par_ExtraParamMapper().Map(new Par_ApSrc { Id = 1 }, "acme").Tenant);
    }

    // Mapster NameMatchingStrategy.Flexible — snake_case source matches PascalCase destination.
    [Fact]
    public void Flexible_naming_parity()
    {
        var d = new Par_FlexibleMapper().Map(new Par_SnakeSrc { user_id = 7, user_name = "ada" });
        Assert.Equal(7, d.UserId);
        Assert.Equal("ada", d.UserName);
    }

    // Mapster IgnoreCase / Mapperly CaseInsensitive — case-insensitive member matching.
    [Fact]
    public void Case_insensitive_naming_parity()
    {
        var d = new Par_CaseInsensitiveMapper().Map(new Par_CiSrc { id = 7, name = "ada" });
        Assert.Equal(7, d.Id);
        Assert.Equal("ada", d.Name);
    }

    // AutoMapper Before/AfterMap / Mapster Before/AfterMapping — AfterMap fills an ignored member.
    [Fact]
    public void After_map_hook_parity()
    {
        Assert.Equal("src#3", new Par_HookMapper().Map(new Par_HookSrc { Id = 3 }).Audit);
    }

    // All libraries — built-in scalar conversions (int→long widening, string→int via IParsable).
    [Fact]
    public void Builtin_scalar_conversion_parity()
    {
        var d = new Par_ScalarMapper().Map(new Par_ScalarSrc { Count = 100, Amount = "42" });
        Assert.Equal(100L, d.Count);
        Assert.Equal(42, d.Amount);
    }

    // Mapperly [UserMapping] / AutoMapper ConvertUsing — a user-defined type-pair converter takes precedence.
    [Fact]
    public void User_method_converter_precedence_parity()
    {
        var d = new Par_UserMethodMapper().Map(new Par_UmSrc { Amount = new Par_Money { V = 5 } });
        Assert.Equal(5, d.Amount.V);
        Assert.Equal("user", d.Amount.Via); // proves the user Convert method ran, not auto-nesting
    }
}
