// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests.Round31
{
    public class StaticMapBase
    {
        public string Name { get; set; } = "";
    }

    public sealed class StaticMapDerived : StaticMapBase
    {
        public int Extra { get; set; }
    }

    public sealed class StaticMapBaseDto
    {
        public string Name { get; set; } = "";
    }

    public sealed class StaticMapDerivedDto
    {
        public string Name { get; set; } = "";

        public int Extra { get; set; }
    }

    /// <summary>Both a (Base, Dto) and a (Derived, DerivedDto) pair, so "which pair was used" is observable.</summary>
    [DwarfMapper]
    public partial class StaticMapMappers
    {
        public partial StaticMapBaseDto Map(StaticMapBase src);

        public partial StaticMapDerivedDto Map(StaticMapDerived src);
    }

    /// <summary>
    ///     Round 31 T26 at run time. This project registers <see cref="FacadeMergeMappers" /> and
    ///     <see cref="StaticMapMappers" />, so a direct <c>Dwarf.Map</c> call here is BOUND at compile time to the
    ///     generated mapper, while the same call made through a generic helper - whose type arguments are type
    ///     parameters, so nothing can be bound - runs the registry path. Every assertion compares the two: the
    ///     differential form of "a bound call and a looked-up one cannot differ".
    /// </summary>
    public sealed class DwarfStaticMapTests
    {
        private static void Register()
        {
            RuntimeHelpers.RunModuleConstructor(typeof(StaticMapMappers).Module.ModuleHandle);
        }

        private static TDestination LookedUp<TSource, TDestination>(TSource source)
        {
            return Dwarf.Map<TSource, TDestination>(source);
        }

        private static void LookedUpMerge<TSource, TDestination>(TSource source, TDestination destination)
        {
            Dwarf.Map(source, destination);
        }

        [Fact]
        public void A_bound_create_call_returns_what_the_registry_path_returns()
        {
            Register();
            var src = new FacadeMergeSrc
            {
                Name = "ore",
                Count = 7
            };

            var bound = Dwarf.Map<FacadeMergeSrc, FacadeMergeDst>(src);
            var looked = LookedUp<FacadeMergeSrc, FacadeMergeDst>(src);
            var facade = DwarfMapperFacade.Instance.Map<FacadeMergeSrc, FacadeMergeDst>(src);

            Assert.Equal(("ore", 7), (bound.Name, bound.Count));
            Assert.Equal((looked.Name, looked.Count), (bound.Name, bound.Count));
            Assert.Equal((facade.Name, facade.Count), (bound.Name, bound.Count));
        }

        [Fact]
        public void A_bound_update_call_merges_in_place_like_the_registry_path()
        {
            Register();
            var boundTarget = new FacadeMergeDst
            {
                Name = "old",
                Count = 1
            };
            var lookedTarget = new FacadeMergeDst
            {
                Name = "old",
                Count = 1
            };
            var alias = boundTarget;

            Dwarf.Map(new FacadeMergeSrc
            {
                Name = "new",
                Count = 2
            }, boundTarget);
            LookedUpMerge(new FacadeMergeSrc
            {
                Name = "new",
                Count = 2
            }, lookedTarget);

            Assert.Same(alias, boundTarget);
            Assert.Equal(("new", 2), (boundTarget.Name, boundTarget.Count));
            Assert.Equal((lookedTarget.Name, lookedTarget.Count), (boundTarget.Name, boundTarget.Count));
        }

        [Fact]
        public void A_bound_update_call_refuses_null_exactly_as_the_registry_path_does()
        {
            Register();

            var boundSource = Assert.Throws<ArgumentNullException>(() => Dwarf.Map<FacadeMergeSrc, FacadeMergeDst>(null!, new FacadeMergeDst()));
            var lookedSource = Assert.Throws<ArgumentNullException>(() => LookedUpMerge<FacadeMergeSrc, FacadeMergeDst>(null!, new FacadeMergeDst()));
            var boundDestination = Assert.Throws<ArgumentNullException>(() => Dwarf.Map(new FacadeMergeSrc(), (FacadeMergeDst)null!));
            var lookedDestination = Assert.Throws<ArgumentNullException>(() => LookedUpMerge(new FacadeMergeSrc(), (FacadeMergeDst)null!));

            Assert.Equal("source", boundSource.ParamName);
            Assert.Equal(lookedSource.ParamName, boundSource.ParamName);
            Assert.Equal("destination", boundDestination.ParamName);
            Assert.Equal(lookedDestination.ParamName, boundDestination.ParamName);
        }

        [Fact]
        public void The_static_source_type_decides_the_pair_on_both_paths()
        {
            Register();
            StaticMapBase asBase = new StaticMapDerived
            {
                Name = "deep",
                Extra = 3
            };

            // Map<Base, BaseDto>(derived) must use the (Base, BaseDto) pair the caller named, bound or not.
            var bound = Dwarf.Map<StaticMapBase, StaticMapBaseDto>(asBase);
            var looked = LookedUp<StaticMapBase, StaticMapBaseDto>(asBase);

            Assert.Equal("deep", bound.Name);
            Assert.Equal(looked.Name, bound.Name);
        }

        [Fact]
        public void With_no_exact_pair_the_registry_path_dispatches_on_the_runtime_type()
        {
            Register();
            object derived = new StaticMapDerived
            {
                Name = "vein",
                Extra = 9
            };

            // (object, StaticMapDerivedDto) is registered by nobody, so the exact lookup misses and the runtime type
            // of the instance - StaticMapDerived - picks the map. Nothing here can be bound: object is not a pair key.
            var dto = Dwarf.Map<object, StaticMapDerivedDto>(derived);

            Assert.Equal(("vein", 9), (dto.Name, dto.Extra));
        }
    }
}
