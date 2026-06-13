// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// ── Type hierarchy ─────────────────────────────────────────────────────────────

public abstract class DrvAnimal { public string Name { get; set; } = ""; }
public class DrvDog : DrvAnimal { public string Breed { get; set; } = ""; }
public class DrvCat : DrvAnimal { public int Lives { get; set; } }
public class DrvUnknown : DrvAnimal { }

// 3-level: Puppy : DrvDog : DrvAnimal
public class DrvPuppy : DrvDog { public bool IsVaccinated { get; set; } }

// Interface-based hierarchy
public interface IDrvCreature { string Name { get; set; } }
public class DrvFish : IDrvCreature { public string Name { get; set; } = ""; public int Fins { get; set; } }
public class DrvBird : IDrvCreature { public string Name { get; set; } = ""; public bool CanFly { get; set; } }

// DTOs — must form an inheritance hierarchy matching the source hierarchy
public class DrvAnimalDto { public string Name { get; set; } = ""; }
public class DrvDogDto : DrvAnimalDto { public string Breed { get; set; } = ""; }
public class DrvCatDto : DrvAnimalDto { public int Lives { get; set; } }
public class DrvPuppyDto : DrvDogDto { public bool IsVaccinated { get; set; } }
public interface IDrvCreatureDto { string Name { get; } }
public record DrvFishDto(string Name, int Fins) : IDrvCreatureDto;
public record DrvBirdDto(string Name, bool CanFly) : IDrvCreatureDto;

// ── Mappers ────────────────────────────────────────────────────────────────────

// Generic attribute syntax — declared overloads for Dog/Cat
[DwarfMapper]
public partial class DrvAnimalMapper
{
    [MapDerivedType<DrvDog, DrvDogDto>]
    [MapDerivedType<DrvCat, DrvCatDto>]
    public partial DrvAnimalDto Map(DrvAnimal a);

    public partial DrvDogDto Map(DrvDog d);
    public partial DrvCatDto Map(DrvCat c);
}

// Non-generic (typeof) syntax — declared overloads for Dog/Cat
[DwarfMapper]
public partial class DrvAnimalTypeofMapper
{
    [MapDerivedType(typeof(DrvDog), typeof(DrvDogDto))]
    [MapDerivedType(typeof(DrvCat), typeof(DrvCatDto))]
    public partial DrvAnimalDto Map(DrvAnimal a);

    public partial DrvDogDto Map(DrvDog d);
    public partial DrvCatDto Map(DrvCat c);
}

// 3-level hierarchy: Puppy must be first (most-derived), then Dog
[DwarfMapper]
public partial class DrvAnimalThreeLevelMapper
{
    [MapDerivedType<DrvPuppy, DrvPuppyDto>]
    [MapDerivedType<DrvDog, DrvDogDto>]
    public partial DrvAnimalDto Map(DrvAnimal a);

    public partial DrvPuppyDto Map(DrvPuppy p);
    public partial DrvDogDto Map(DrvDog d);
}

// Auto-nest (no declared overloads) — generator synthesizes __DwarfMap_Obj_...
[DwarfMapper(AutoNest = true)]
public partial class DrvAutoNestMapper
{
    [MapDerivedType<DrvDog, DrvDogDto>]
    [MapDerivedType<DrvCat, DrvCatDto>]
    public partial DrvAnimalDto Map(DrvAnimal a);
}

// Interface source
[DwarfMapper]
public partial class DrvCreatureMapper
{
    [MapDerivedType<DrvFish, DrvFishDto>]
    [MapDerivedType<DrvBird, DrvBirdDto>]
    public partial IDrvCreatureDto Map(IDrvCreature c);

    public partial DrvFishDto Map(DrvFish f);
    public partial DrvBirdDto Map(DrvBird b);
}

// Collection scenario: use dispatch method in a loop
[DwarfMapper]
public partial class DrvAnimalCollectionMapper
{
    [MapDerivedType<DrvDog, DrvDogDto>]
    [MapDerivedType<DrvCat, DrvCatDto>]
    public partial DrvAnimalDto Map(DrvAnimal a);

    public partial DrvDogDto Map(DrvDog d);
    public partial DrvCatDto Map(DrvCat c);
}

// Zoo/ZooDto: collection MEMBER with [MapDerivedType] dispatch (Fix 2 regression)
public class Zoo
{
    public List<DrvAnimal> Animals { get; set; } = new();
}

public class ZooDto
{
    public List<DrvAnimalDto> Animals { get; set; } = new();
}

[DwarfMapper]
public partial class ZooMapper
{
    public partial ZooDto Map(Zoo zoo);

    [MapDerivedType<DrvDog, DrvDogDto>]
    [MapDerivedType<DrvCat, DrvCatDto>]
    public partial DrvAnimalDto Map(DrvAnimal a);

    public partial DrvDogDto Map(DrvDog d);
    public partial DrvCatDto Map(DrvCat c);
}

// ── Interface hierarchy with WRONG declaration order — audit #1 adversarial ──
// IFoo : IBar; DrvC : IFoo — declared [IBar] BEFORE [IFoo] (wrong order)
// Generator MUST sort most-derived-first; DrvC must dispatch to IFoo arm (IDrvFooDto), not IBar arm.
public interface IDrvBar { string Tag { get; set; } }
public interface IDrvFoo : IDrvBar { int Extra { get; set; } }
public class DrvC : IDrvFoo { public string Tag { get; set; } = ""; public int Extra { get; set; } }
public class IDrvBarDto { public string Tag { get; set; } = ""; }
public class IDrvFooDto : IDrvBarDto { public int Extra { get; set; } }

[DwarfMapper]
public partial class DrvIfaceOrderMapper
{
    // Declaration order is deliberately WRONG: concrete DrvC arm listed LAST (most specific should win)
    [MapDerivedType(typeof(IDrvBar), typeof(IDrvBarDto))]  // IBar arm — less derived; declared first
    [MapDerivedType<DrvC, IDrvFooDto>]                     // DrvC : IDrvFoo : IDrvBar — most derived; declared last
    public partial IDrvBarDto Map(IDrvBar x);

    // Explicit overload for concrete DrvC → IDrvFooDto
    public partial IDrvFooDto Map(DrvC c);
}

// ── 4-arm class hierarchy with wrong declaration order ────────────────────────
public abstract class DrvBase { public string Id { get; set; } = ""; }
public class DrvLevel1 : DrvBase { public int L1 { get; set; } }
public class DrvLevel2 : DrvLevel1 { public int L2 { get; set; } }
public class DrvLevel3 : DrvLevel2 { public int L3 { get; set; } }

public class DrvBaseDto { public string Id { get; set; } = ""; }
public class DrvLevel1Dto : DrvBaseDto { public int L1 { get; set; } }
public class DrvLevel2Dto : DrvLevel1Dto { public int L2 { get; set; } }
public class DrvLevel3Dto : DrvLevel2Dto { public int L3 { get; set; } }

[DwarfMapper]
public partial class DrvFourArmMapper
{
    // Deliberately wrong order: level1 first, leaf last
    [MapDerivedType<DrvLevel1, DrvLevel1Dto>]
    [MapDerivedType<DrvLevel2, DrvLevel2Dto>]
    [MapDerivedType<DrvLevel3, DrvLevel3Dto>]
    public partial DrvBaseDto Map(DrvBase b);

    public partial DrvLevel1Dto Map(DrvLevel1 x);
    public partial DrvLevel2Dto Map(DrvLevel2 x);
    public partial DrvLevel3Dto Map(DrvLevel3 x);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class MapDerivedTypeRuntimeTests
{
    [Fact]
    public void Generic_attr_Dog_dispatches_to_DogDto()
    {
        var m = new DrvAnimalMapper();
        var result = m.Map(new DrvDog { Name = "Rex", Breed = "Husky" });
        var dog = Assert.IsType<DrvDogDto>(result);
        Assert.Equal("Rex", dog.Name);
        Assert.Equal("Husky", dog.Breed);
    }

    [Fact]
    public void Generic_attr_Cat_dispatches_to_CatDto()
    {
        var m = new DrvAnimalMapper();
        var result = m.Map(new DrvCat { Name = "Whiskers", Lives = 9 });
        var cat = Assert.IsType<DrvCatDto>(result);
        Assert.Equal("Whiskers", cat.Name);
        Assert.Equal(9, cat.Lives);
    }

    [Fact]
    public void Typeof_attr_Dog_dispatches_to_DogDto()
    {
        var m = new DrvAnimalTypeofMapper();
        var result = m.Map(new DrvDog { Name = "Buddy", Breed = "Lab" });
        // Must dispatch to DogDto — not just base AnimalDto
        var dog = Assert.IsType<DrvDogDto>(result);
        Assert.Equal("Buddy", dog.Name);
        Assert.Equal("Lab", dog.Breed);  // derived-only member — proves correct dispatch
    }

    [Fact]
    public void Typeof_attr_Cat_dispatches_to_CatDto()
    {
        var m = new DrvAnimalTypeofMapper();
        var result = m.Map(new DrvCat { Name = "Luna", Lives = 7 });
        var cat = Assert.IsType<DrvCatDto>(result);
        Assert.Equal(7, cat.Lives);
    }

    [Fact]
    public void ThreeLevel_Puppy_dispatches_via_Puppy_arm_not_Dog_arm()
    {
        var m = new DrvAnimalThreeLevelMapper();
        var puppy = new DrvPuppy { Name = "Spot", Breed = "Dalmatian", IsVaccinated = true };
        var result = m.Map(puppy);
        var dto = Assert.IsType<DrvPuppyDto>(result);
        Assert.True(dto.IsVaccinated);
        Assert.Equal("Spot", dto.Name);
        Assert.Equal("Dalmatian", dto.Breed);
    }

    [Fact]
    public void ThreeLevel_Dog_dispatches_via_Dog_arm()
    {
        var m = new DrvAnimalThreeLevelMapper();
        var dog = new DrvDog { Name = "Max", Breed = "Shepherd" };
        var result = m.Map(dog);
        var dto = Assert.IsType<DrvDogDto>(result);
        Assert.Equal("Max", dto.Name);
        Assert.Equal("Shepherd", dto.Breed);
    }

    [Fact]
    public void Unregistered_type_throws_ArgumentException()
    {
        var m = new DrvAnimalMapper();
        var ex = Assert.Throws<ArgumentException>(() => m.Map(new DrvUnknown { Name = "????" }));
        Assert.Contains("DwarfMapper", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MapDerivedType", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_source_throws_ArgumentNullException()
    {
        var m = new DrvAnimalMapper();
        Assert.Throws<ArgumentNullException>(() => m.Map((DrvAnimal)null!));
    }

    [Fact]
    public void Interface_source_Fish_dispatches_correctly()
    {
        var m = new DrvCreatureMapper();
        var result = m.Map(new DrvFish { Name = "Nemo", Fins = 8 });
        var dto = Assert.IsType<DrvFishDto>(result);
        Assert.Equal("Nemo", dto.Name);
        Assert.Equal(8, dto.Fins);
    }

    [Fact]
    public void Interface_source_Bird_dispatches_correctly()
    {
        var m = new DrvCreatureMapper();
        var result = m.Map(new DrvBird { Name = "Tweety", CanFly = true });
        var dto = Assert.IsType<DrvBirdDto>(result);
        Assert.True(dto.CanFly);
    }

    [Fact]
    public void Record_ctor_target_maps_correctly()
    {
        var m = new DrvCreatureMapper();
        var fish = new DrvFish { Name = "Dory", Fins = 4 };
        var dto = Assert.IsType<DrvFishDto>(m.Map(fish));
        Assert.Equal("Dory", dto.Name);
        Assert.Equal(4, dto.Fins);
    }

    [Fact]
    public void AutoNest_Dog_dispatches_without_declared_overload()
    {
        var m = new DrvAutoNestMapper();
        var result = m.Map(new DrvDog { Name = "Rexo", Breed = "Poodle" });
        // Must dispatch to DogDto — synthesized __DwarfMap_Obj_ helper, not base
        var dog = Assert.IsType<DrvDogDto>(result);
        Assert.Equal("Rexo", dog.Name);
        Assert.Equal("Poodle", dog.Breed);  // derived-only member — proves auto-nest dispatch worked
    }

    [Fact]
    public void Collection_mixed_Dog_Cat_dispatches_each_element()
    {
        var m = new DrvAnimalCollectionMapper();
        var animals = new List<DrvAnimal>
        {
            new DrvDog { Name = "Rex", Breed = "Husky" },
            new DrvCat { Name = "Luna", Lives = 9 },
            new DrvDog { Name = "Buddy", Breed = "Lab" },
        };
        // Map each element via the dispatch method
        var dtos = animals.ConvertAll(a => m.Map(a));
        Assert.Equal(3, dtos.Count);
        // Collection uses the dispatch method so DrvDogDto/DrvCatDto are returned as DrvAnimalDto
        Assert.Equal("Rex", dtos[0].Name);
        Assert.Equal("Luna", dtos[1].Name);
        Assert.Equal("Buddy", dtos[2].Name);
        // Verify actual runtime types
        Assert.IsType<DrvDogDto>(dtos[0]);
        Assert.IsType<DrvCatDto>(dtos[1]);
        Assert.IsType<DrvDogDto>(dtos[2]);
    }

    [Fact]
    public void Generated_collection_member_dispatches_each_element_via_MapDerivedType()
    {
        var m = new ZooMapper();
        var zoo = new Zoo
        {
            Animals = new List<DrvAnimal>
            {
                new DrvDog { Name = "Rex", Breed = "Husky" },
                new DrvCat { Name = "Luna", Lives = 9 },
                new DrvDog { Name = "Buddy", Breed = "Lab" },
            }
        };

        var dto = m.Map(zoo);

        Assert.Equal(3, dto.Animals.Count);

        var dog1 = Assert.IsType<DrvDogDto>(dto.Animals[0]);
        Assert.Equal("Rex", dog1.Name);
        Assert.Equal("Husky", dog1.Breed);

        var cat = Assert.IsType<DrvCatDto>(dto.Animals[1]);
        Assert.Equal("Luna", cat.Name);
        Assert.Equal(9, cat.Lives);

        var dog2 = Assert.IsType<DrvDogDto>(dto.Animals[2]);
        Assert.Equal("Buddy", dog2.Name);
        Assert.Equal("Lab", dog2.Breed);
    }

    [Fact]
    public void Interface_hierarchy_wrong_decl_order_C_dispatches_to_IFoo_not_IBar()
    {
        // DrvC : IDrvFoo : IDrvBar — declared in wrong order ([IBar] before [DrvC])
        // Generator sorts most-derived-first; DrvC arm must win over IBar arm
        var m = new DrvIfaceOrderMapper();
        var c = new DrvC { Tag = "t1", Extra = 42 };
        var result = m.Map(c);
        var dto = Assert.IsType<IDrvFooDto>(result);   // NOT IDrvBarDto
        Assert.Equal(42, dto.Extra);                    // derived-only member
        Assert.Equal("t1", dto.Tag);
    }

    [Fact]
    public void Four_arm_class_hierarchy_wrong_decl_order_level3_dispatches_correctly()
    {
        var m = new DrvFourArmMapper();
        var l3 = new DrvLevel3 { Id = "x", L1 = 1, L2 = 2, L3 = 3 };
        var result = m.Map(l3);
        var dto = Assert.IsType<DrvLevel3Dto>(result);
        Assert.Equal(3, dto.L3);                         // leaf-only member
        Assert.Equal(2, dto.L2);
        Assert.Equal(1, dto.L1);
        Assert.Equal("x", dto.Id);
    }

    [Fact]
    public void Four_arm_class_hierarchy_wrong_decl_order_level2_dispatches_correctly()
    {
        var m = new DrvFourArmMapper();
        var l2 = new DrvLevel2 { Id = "y", L1 = 10, L2 = 20 };
        var result = m.Map(l2);
        var dto = Assert.IsType<DrvLevel2Dto>(result);
        Assert.Equal(20, dto.L2);
        Assert.Equal(10, dto.L1);
        Assert.Equal("y", dto.Id);
        // Result is DrvLevel2Dto — not upgraded to DrvLevel3Dto (correct dispatch)
        Assert.False(result is DrvLevel3Dto);
    }
}

// ── Item 1 regression: MapDerivedType + Preserve → shared instance dedup ──────
// A container with two Animal-typed members both pointing to the SAME Dog instance.
// Under ReferenceHandling=Preserve, the two dispatch calls must yield the SAME
// target DogDto instance (identity preservation via the ctx identity map).
// PsvAnimal is abstract so C2b (concrete-src auto-nest bypass) does NOT fire for
// PsvAnimal→PsvAnimalDto; the dispatch method Map(PsvAnimal) is used instead.
public abstract class PsvAnimal { public string Name { get; set; } = ""; }
public class PsvDog : PsvAnimal { public string Breed { get; set; } = ""; }
public class PsvAnimalDto { public string Name { get; set; } = ""; }
public class PsvDogDto : PsvAnimalDto { public string Breed { get; set; } = ""; }

public class PsvTwoSlot
{
    public PsvAnimal? First  { get; set; }
    public PsvAnimal? Second { get; set; }
}

public class PsvTwoSlotDto
{
    public PsvAnimalDto? First  { get; set; }
    public PsvAnimalDto? Second { get; set; }
}

// PsvAnimal is abstract, so C2b does NOT bypass the dispatch method for the container's
// PsvAnimal→PsvAnimalDto member mapping. The MF-B fix synthesizes a private ctx-accepting
// dispatch wrapper __DwarfMap_Disp_* so the container shares ctx across both members,
// enabling identity dedup when First and Second point to the same Dog instance.
[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class PsvDerivedMapper
{
    // Dispatch method named "Map" (overloaded), so container mapper's member resolution
    // for PsvAnimal→PsvAnimalDto picks it up as the converter method.
    [MapDerivedType<PsvDog, PsvDogDto>]
    public partial PsvAnimalDto Map(PsvAnimal a);

    // Explicit Dog→DogDto overload (no AutoNest needed).
    public partial PsvDogDto Map(PsvDog d);

    public partial PsvTwoSlotDto Map(PsvTwoSlot c);
}

public class MapDerivedTypePreserveRegressionTests
{
    // Item 1: same Dog instance referenced from two slots → Assert.Same on target DTOs.
    // This locks the dedup behaviour that was verified already by the MF-A fix: the
    // dispatch method forwards ctx to arm converters, which in turn call the
    // __DwarfMap_Obj_* helpers that do TryGetReference/SetReference under Preserve.
    [Fact]
    public void Preserve_shared_Dog_via_dispatch_yields_same_target_instance()
    {
        var dog = new PsvDog { Name = "Rex", Breed = "Husky" };
        var container = new PsvTwoSlot { First = dog, Second = dog }; // SAME instance

        var m = new PsvDerivedMapper();
        var dto = m.Map(container);

        Assert.NotNull(dto.First);
        Assert.NotNull(dto.Second);

        // Values must be correct
        var first  = Assert.IsType<PsvDogDto>(dto.First);
        var second = Assert.IsType<PsvDogDto>(dto.Second);
        Assert.Equal("Rex",   first.Name);
        Assert.Equal("Husky", first.Breed);

        // Topology: same source → same target (reference identity preserved via identity map)
        Assert.Same(dto.First, dto.Second);
    }

    [Fact]
    public void Preserve_distinct_Dog_instances_yield_distinct_target_instances()
    {
        // Two DISTINCT Dog sources → two DISTINCT DogDto targets (not collapsed)
        var dog1 = new PsvDog { Name = "Rex",   Breed = "Husky" };
        var dog2 = new PsvDog { Name = "Buddy", Breed = "Lab" };
        var container = new PsvTwoSlot { First = dog1, Second = dog2 };

        var m = new PsvDerivedMapper();
        var dto = m.Map(container);

        Assert.NotNull(dto.First);
        Assert.NotNull(dto.Second);
        Assert.NotSame(dto.First, dto.Second); // distinct sources → distinct targets
        Assert.Equal("Rex",   dto.First!.Name);
        Assert.Equal("Buddy", dto.Second!.Name);
    }
}
