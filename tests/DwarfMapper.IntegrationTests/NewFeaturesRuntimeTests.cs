// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Runtime (behavioural) verification of the Phase 1–8 features, which previously had generator/compile
// tests only. Domain types are file-scoped with an `Nf_` prefix to stay unique across the project.

// ── Phase 2: [MapValue] constant + computed ──────────────────────────────────
public class Nf_MvSrc { public int Id { get; set; } }
public class Nf_MvDst { public int Id { get; set; } public string Source { get; set; } = ""; public int Seq { get; set; } }
[DwarfMapper] public partial class Nf_MvMapper
{
    [MapValue(nameof(Nf_MvDst.Source), "api-v2")]
    [MapValue(nameof(Nf_MvDst.Seq), Use = nameof(Next))]
    public partial Nf_MvDst Map(Nf_MvSrc s);
    private static int Next() => 99;
}

// ── Phase 3: deep source path ────────────────────────────────────────────────
public class Nf_Inner { public string Name { get; set; } = ""; }
public class Nf_DpSrc { public Nf_Inner Customer { get; set; } = new(); }
public class Nf_DpDst { public string CustomerName { get; set; } = ""; }
[DwarfMapper] public partial class Nf_DpMapper
{
    [MapProperty("Customer.Name", nameof(Nf_DpDst.CustomerName))]
    public partial Nf_DpDst Map(Nf_DpSrc s);
}

// ── Phase 4: unflatten ───────────────────────────────────────────────────────
public class Nf_Addr { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
public class Nf_UfSrc { public string City { get; set; } = ""; public string Zip { get; set; } = ""; }
public class Nf_UfDst { public Nf_Addr Address { get; set; } = new(); }
[DwarfMapper] public partial class Nf_UfMapper
{
    [MapProperty(nameof(Nf_UfSrc.City), "Address.City")]
    [MapProperty(nameof(Nf_UfSrc.Zip), "Address.Zip")]
    public partial Nf_UfDst Map(Nf_UfSrc s);
}

// ── Phase 5: additional parameters ───────────────────────────────────────────
public class Nf_ApSrc { public int Id { get; set; } }
public class Nf_ApDst { public int Id { get; set; } public string Tenant { get; set; } = ""; public int Version { get; set; } }
[DwarfMapper] public partial class Nf_ApMapper { public partial Nf_ApDst Map(Nf_ApSrc s, string tenant, int version); }

// ── Phase 6: NameConvention.Flexible ─────────────────────────────────────────
public class Nf_NcSrc { public string user_name { get; set; } = ""; public int USER_ID { get; set; } }
public class Nf_NcDst { public string UserName { get; set; } = ""; public int UserId { get; set; } }
[DwarfMapper(NameConvention = NameConvention.Flexible)] public partial class Nf_NcMapper { public partial Nf_NcDst Map(Nf_NcSrc s); }

// ── Phase 8: NullSubstitute + When ───────────────────────────────────────────
public class Nf_NwSrc { public string? Name { get; set; } public int Tier { get; set; } public int Bonus { get; set; } }
public class Nf_NwDst { public string Name { get; set; } = ""; public int Tier { get; set; } public int Bonus { get; set; } }
[DwarfMapper] public partial class Nf_NwMapper
{
    [MapProperty(nameof(Nf_NwSrc.Name), nameof(Nf_NwDst.Name), NullSubstitute = "(none)")]
    [MapProperty(nameof(Nf_NwSrc.Bonus), nameof(Nf_NwDst.Bonus), When = nameof(Eligible))]
    public partial Nf_NwDst Map(Nf_NwSrc s);
    private static bool Eligible(Nf_NwSrc s) => s.Tier > 0;
}

// ── Phase 7: [ReverseMap] round-trip ─────────────────────────────────────────
public class Nf_Entity { public int Id { get; set; } public string FullName { get; set; } = ""; }
public class Nf_Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
[DwarfMapper] public partial class Nf_RmMapper
{
    [ReverseMap]
    [MapProperty(nameof(Nf_Entity.FullName), nameof(Nf_Dto.Name))]
    public partial Nf_Dto ToDto(Nf_Entity e);
    public partial Nf_Entity FromDto(Nf_Dto d);
}

public class NewFeaturesRuntimeTests
{
    [Fact]
    public void MapValue_constant_and_computed_are_assigned()
    {
        var d = new Nf_MvMapper().Map(new Nf_MvSrc { Id = 1 });
        Assert.Equal(1, d.Id);
        Assert.Equal("api-v2", d.Source);
        Assert.Equal(99, d.Seq);
    }

    [Fact]
    public void Deep_source_path_reads_through_graph()
    {
        var d = new Nf_DpMapper().Map(new Nf_DpSrc { Customer = new Nf_Inner { Name = "Ann" } });
        Assert.Equal("Ann", d.CustomerName);
    }

    [Fact]
    public void Unflatten_builds_intermediate_and_assigns_leaves()
    {
        var d = new Nf_UfMapper().Map(new Nf_UfSrc { City = "Brno", Zip = "60200" });
        Assert.NotNull(d.Address);
        Assert.Equal("Brno", d.Address.City);
        Assert.Equal("60200", d.Address.Zip);
    }

    [Fact]
    public void Additional_parameters_fill_destinations()
    {
        var d = new Nf_ApMapper().Map(new Nf_ApSrc { Id = 7 }, "acme", 3);
        Assert.Equal(7, d.Id);
        Assert.Equal("acme", d.Tenant);
        Assert.Equal(3, d.Version);
    }

    [Fact]
    public void Flexible_naming_maps_snake_and_upper_to_pascal()
    {
        var d = new Nf_NcMapper().Map(new Nf_NcSrc { user_name = "ann", USER_ID = 42 });
        Assert.Equal("ann", d.UserName);
        Assert.Equal(42, d.UserId);
    }

    [Fact]
    public void NullSubstitute_replaces_null_source()
    {
        var d = new Nf_NwMapper().Map(new Nf_NwSrc { Name = null, Tier = 1, Bonus = 5 });
        Assert.Equal("(none)", d.Name);
    }

    [Fact]
    public void When_predicate_gates_assignment()
    {
        var eligible = new Nf_NwMapper().Map(new Nf_NwSrc { Name = "x", Tier = 5, Bonus = 10 });
        Assert.Equal(10, eligible.Bonus); // Tier > 0 → assigned

        var ineligible = new Nf_NwMapper().Map(new Nf_NwSrc { Name = "x", Tier = 0, Bonus = 10 });
        Assert.Equal(0, ineligible.Bonus); // Tier == 0 → keeps default
    }

    [Fact]
    public void ReverseMap_round_trips_identity()
    {
        var mapper = new Nf_RmMapper();
        var entity = new Nf_Entity { Id = 1, FullName = "Ann Smith" };
        var dto = mapper.ToDto(entity);
        Assert.Equal(1, dto.Id);
        Assert.Equal("Ann Smith", dto.Name);

        var back = mapper.FromDto(dto);
        Assert.Equal(entity.Id, back.Id);
        Assert.Equal(entity.FullName, back.FullName); // A → B → A' identity
    }
}
