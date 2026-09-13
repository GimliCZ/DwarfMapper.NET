// SPDX-License-Identifier: GPL-2.0-only

// Runtime proof for the recursion-cycle phase's overload-exact edges and [MapDerivedType] arm edges.
//
// (a) An overloaded self-map used to get no depth guard at all: the call graph fanned the bare name `Map` out to every
//     overload EXCEPT the caller, which excluded exactly Map(Node) -> Map(Node). A cyclic graph recursed until the
//     stack ran out. It must throw DwarfMappingDepthException, as the uniquely named mapper always did. (Not
//     demonstrated RED here: a stack overflow terminates the test host. The generator-level RED is
//     OverloadedSelfMapDepthGuardTests.)
// (b) A dispatch method on a cycle through its own arm now calls its depth companion instead of the Preserve dispatch
//     wrapper. The identity map must still close the cycle onto the same target instance.
namespace DwarfMapper.IntegrationTests
{
    public class OvrNode
    {
        public int V { get; set; }

        public OvrNode? Next { get; set; }
    }

    public class OvrNodeDto
    {
        public int V { get; set; }

        public OvrNodeDto? Next { get; set; }
    }

    public class OvrOther
    {
        public int B { get; set; }
    }

    public class OvrOtherDto
    {
        public int B { get; set; }
    }

    [DwarfMapper]
    public partial class OvrSelfMapMapper
    {
        public partial OvrNodeDto Map(OvrNode n);

        public partial OvrOtherDto Map(OvrOther o);
    }

    public class OvrLeafSrc
    {
        public int V { get; set; }

        public OvrLeafSrc? Child { get; set; }
    }

    public class OvrLeafWrap
    {
        public OvrLeafDst? Inner { get; set; }
    }

    public class OvrLeafDst
    {
        public int V { get; set; }

        public OvrLeafWrap Wrap { get; set; } = new();
    }

    [DwarfMapper]
    public partial class OvrUnflattenLeafMapper
    {
        [MapProperty("Child", "Wrap.Inner", Use = "Map")]
        public partial OvrLeafDst Map(OvrLeafSrc s);

        public partial OvrOtherDto Map(OvrOther o);
    }

    public abstract class OvrAnimal
    {
        public string Name { get; set; } = "";

        public OvrAnimal? Friend { get; set; }
    }

    public class OvrDog : OvrAnimal
    {
        public string Breed { get; set; } = "";
    }

    public class OvrAnimalDto
    {
        public string Name { get; set; } = "";

        public OvrAnimalDto? Friend { get; set; }
    }

    public class OvrDogDto : OvrAnimalDto
    {
        public string Breed { get; set; } = "";
    }

    public class OvrZoo
    {
        public string Title { get; set; } = "";

        public OvrAnimal Star { get; set; } = new OvrDog();
    }

    public class OvrZooDto
    {
        public OvrZooDto(string title, OvrAnimalDto star)
        {
            Title = title;
            Star = star;
        }

        public string Title { get; }

        public OvrAnimalDto Star { get; }
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class OvrDispatchMapper
    {
        [MapDerivedType<OvrDog, OvrDogDto>]
        public partial OvrAnimalDto ToDto(OvrAnimal a);

        public partial OvrDogDto ToDog(OvrDog d);

        public partial OvrZooDto ToZoo(OvrZoo z);
    }

    public class OverloadedRecursionRuntimeTests
    {
        [Fact]
        public void An_overloaded_self_map_on_a_cyclic_graph_throws_the_depth_exception()
        {
            var node = new OvrNode { V = 1 };
            node.Next = node;

            Assert.Throws<DwarfMappingDepthException>(() => new OvrSelfMapMapper().Map(node));
        }

        [Fact]
        public void An_overloaded_self_map_on_an_acyclic_chain_maps_every_node()
        {
            var dto = new OvrSelfMapMapper().Map(new OvrNode { V = 1, Next = new OvrNode { V = 2 } });

            Assert.Equal(1, dto.V);
            Assert.Equal(2, dto.Next!.V);
            Assert.Null(dto.Next.Next);
        }

        [Fact]
        public void An_overloaded_self_map_cycling_through_an_unflatten_leaf_throws_the_depth_exception()
        {
            var node = new OvrLeafSrc { V = 1 };
            node.Child = node;

            Assert.Throws<DwarfMappingDepthException>(() => new OvrUnflattenLeafMapper().Map(node));
        }

        [Fact]
        public void An_overloaded_self_map_through_an_unflatten_leaf_maps_an_acyclic_chain()
        {
            var dto = new OvrUnflattenLeafMapper().Map(new OvrLeafSrc { V = 1, Child = new OvrLeafSrc { V = 2 } });

            Assert.Equal(1, dto.V);
            Assert.Equal(2, dto.Wrap.Inner!.V);
            Assert.Null(dto.Wrap.Inner.Wrap.Inner);
        }

        [Fact]
        public void A_dispatch_cycle_through_its_own_arm_closes_onto_the_same_target_instance()
        {
            var dog = new OvrDog { Name = "Rex", Breed = "Husky" };
            dog.Friend = dog;

            var zoo = new OvrDispatchMapper().ToZoo(new OvrZoo { Title = "City", Star = dog });

            var star = Assert.IsType<OvrDogDto>(zoo.Star);
            Assert.Equal("City", zoo.Title);
            Assert.Equal("Husky", star.Breed);
            Assert.Same(star, star.Friend);
        }
    }
}
