// SPDX-License-Identifier: GPL-2.0-only

// Runtime proof for init-only and required members under ReferenceHandling = Preserve. The register-before-populate
// emitter now writes them in the object initializer, BEFORE the instance is registered. That is only sound because
// DWARF030 refuses any such member whose mapping leads back to the pair — so what this has to show is that identity
// is still preserved everywhere it can be: two init-only members reaching one source object share one target, and a
// settable cycle beside an init-only scalar still closes onto the same instance. (Not demonstrated RED here: the
// pre-fix generator emitted CS8852 into this very project, which would not have built. The generator-level RED is
// PreserveInitializerMemberEmissionTests.)
namespace DwarfMapper.IntegrationTests
{
    public class PimAddr
    {
        public string City { get; set; } = "";
    }

    public class PimAddrDto
    {
        public string City { get; init; } = "";
    }

    public class PimNode
    {
        public int V { get; set; }

        public PimAddr Home { get; set; } = new();

        public PimAddr Work { get; set; } = new();

        public PimNode? Next { get; set; }
    }

    public class PimNodeDto
    {
        public required int V { get; init; }

        public PimAddrDto Home { get; init; } = new();

        public PimAddrDto Work { get; init; } = new();

        public PimNodeDto? Next { get; set; }
    }

    [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
    public partial class PimMapper
    {
        public partial PimNodeDto Map(PimNode n);
    }

    public class PreserveInitializerMemberRuntimeTests
    {
        [Fact]
        public void Two_init_only_members_reaching_one_source_object_share_one_target()
        {
            var shared = new PimAddr { City = "Brno" };

            var dto = new PimMapper().Map(new PimNode { V = 7, Home = shared, Work = shared });

            Assert.Equal(7, dto.V);
            Assert.Equal("Brno", dto.Home.City);
            Assert.Same(dto.Home, dto.Work);
        }

        [Fact]
        public void A_settable_cycle_beside_init_only_members_closes_onto_the_same_instance()
        {
            var node = new PimNode { V = 3, Home = new PimAddr { City = "Praha" }, Work = new PimAddr { City = "Ostrava" } };
            node.Next = node;

            var dto = new PimMapper().Map(node);

            Assert.Same(dto, dto.Next);
            Assert.Equal(3, dto.V);
            Assert.Equal("Ostrava", dto.Work.City);
            Assert.NotSame(dto.Home, dto.Work);
        }
    }
}
