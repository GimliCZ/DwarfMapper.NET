// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests
{
    public sealed class FacadeMergeSrc
    {
        public string Name { get; set; } = "";

        public int Count { get; set; }
    }

    public sealed class FacadeMergeDst
    {
        public string Name { get; set; } = "";

        public int Count { get; set; }
    }

    public sealed class FacadeOnlyCreateSrc
    {
        public int Id { get; set; }
    }

    public sealed class FacadeOnlyCreateDst
    {
        public int Id { get; set; }
    }

    /// <summary>
    ///     Declares both shapes over the same pair, which is the case that forced update-into into its own key
    ///     space: <c>Map</c> and <c>Merge</c> are different operations over identical types.
    /// </summary>
    [DwarfMapper]
    public partial class FacadeMergeMappers
    {
        public partial FacadeMergeDst Map(FacadeMergeSrc src);

        public partial void Merge(FacadeMergeSrc src, FacadeMergeDst dest);
    }

    /// <summary>A create-map with no merge counterpart — the "not registered" case must stay loud.</summary>
    [DwarfMapper]
    [GenerateMap<FacadeOnlyCreateSrc, FacadeOnlyCreateDst>]
    public partial class FacadeOnlyCreateMappers
    {
    }

    /// <summary>
    ///     <c>IDwarfMapper.Map(source, destination)</c> — update-into through the ambient facade.
    /// </summary>
    /// <remarks>
    ///     Before this, both facade overloads CONSTRUCTED a new destination, so the only way to reach update-into
    ///     was to inject the concrete generated mapper alongside the facade. That was the single place a ~300-map
    ///     migration was not a near-verbatim swap, at 16 call sites — and the count was initially reported as 11
    ///     because .razor files had not been scanned. See <c>Issues/Rount18/</c>.
    /// </remarks>
    public sealed class FacadeUpdateIntoTests
    {
        private static IDwarfMapper Facade()
        {
            // Module initializers run lazily, and nothing in a test process necessarily touches the generated
            // types first, so registration is forced explicitly.
            RuntimeHelpers.RunModuleConstructor(typeof(FacadeMergeMappers).Module.ModuleHandle);
            return DwarfMapperFacade.Instance;
        }

        [Fact]
        public void Maps_onto_an_existing_instance_and_preserves_its_identity()
        {
            var dest = new FacadeMergeDst
            {
                Name = "old",
                Count = 1
            };
            var alias = dest; // stands in for the reference a DbContext is holding

            Facade().Map(new FacadeMergeSrc
                {
                    Name = "new",
                    Count = 42
                },
                dest);

            Assert.Equal("new", dest.Name);
            Assert.Equal(42, dest.Count);

            // The whole point: the object passed in is the object that was mutated. A create-map would have left
            // alias untouched.
            Assert.Same(dest, alias);
            Assert.Equal(42, alias.Count);
        }

        [Fact]
        public void The_create_map_over_the_same_pair_still_constructs()
        {
            // Both shapes coexist over identical types, which is why they cannot share a key space.
            var created = Facade().Map<FacadeMergeDst>(new FacadeMergeSrc
            {
                Name = "n",
                Count = 3
            });

            Assert.Equal("n", created.Name);
            Assert.Equal(3, created.Count);
        }

        [Fact]
        public void A_pair_with_only_a_create_map_throws_and_says_which_kind_is_missing()
        {
            // The failure a reader is most likely to misdiagnose: a create-map for the pair EXISTS, so "no map
            // registered" alone would send them hunting for a registration that is already there.
            RuntimeHelpers.RunModuleConstructor(typeof(FacadeOnlyCreateMappers).Module.ModuleHandle);

            var ex = Assert.Throws<DwarfMapMissingException>(() => DwarfMapperFacade.Instance.Map(new FacadeOnlyCreateSrc
                {
                    Id = 1
                },
                new FacadeOnlyCreateDst()));

            Assert.Contains("UPDATE-INTO", ex.Message, StringComparison.Ordinal);
            Assert.Contains("keyed separately", ex.Message, StringComparison.Ordinal);
            Assert.Contains("partial void", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void IsUpdateProvided_reports_the_update_key_space_separately()
        {
            Facade();
            RuntimeHelpers.RunModuleConstructor(typeof(FacadeOnlyCreateMappers).Module.ModuleHandle);

            Assert.True(DwarfMapperRegistry.IsUpdateProvided(typeof(FacadeMergeSrc), typeof(FacadeMergeDst)));

            // A create-map does not imply an update-into map...
            Assert.True(DwarfMapperRegistry.IsProvided(
                typeof(FacadeOnlyCreateSrc),
                typeof(FacadeOnlyCreateDst)));
            Assert.False(DwarfMapperRegistry.IsUpdateProvided(
                typeof(FacadeOnlyCreateSrc),
                typeof(FacadeOnlyCreateDst)));
        }

        [Fact]
        public void A_null_destination_is_refused_rather_than_silently_ignored()
        {
            Assert.Throws<ArgumentNullException>(() => Facade().Map<FacadeMergeSrc, FacadeMergeDst>(new FacadeMergeSrc(), null!));
        }
    }
}
