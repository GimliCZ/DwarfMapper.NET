// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Distinct key types for this file only: the registry is process-wide with no reset hook, so isolation comes from
    // types nothing else registers. Namespace scope because this project treats CA1034 as an error.
    public class FbBase
    {
        public int X { get; set; }
    }

    public sealed class FbDerived : FbBase
    {
    }

    public sealed class FbDto
    {
        public int X { get; set; }
    }

    /// <summary>
    ///     <see cref="DwarfMapperFacade" />'s <c>Map&lt;TSource, TDestination&gt;</c> looks up the EXACT static pair first
    ///     and, when that pair is not registered, falls back to the registry's runtime-type walk. Every existing test
    ///     called it with a pair that was registered exactly, so the fallback had never run: a caller holding a
    ///     derived value under its own static type, with only the base pair registered, reaches it.
    /// </summary>
    [Collection("registry-torture")]
    public sealed class FacadeStaticPairFallbackTests
    {
        [Fact]
        public void An_unregistered_static_pair_falls_back_to_the_base_type_registration()
        {
            DwarfMapperRegistry.Register(typeof(FbBase), typeof(FbDto), static s => new FbDto { X = ((FbBase)s).X });

            Assert.False(DwarfMapperRegistry.IsProvided(typeof(FbDerived), typeof(FbDto)));

            var dto = DwarfMapperFacade.Instance.Map<FbDerived, FbDto>(new FbDerived { X = 42 });

            Assert.Equal(42, dto.X);
        }
    }
}
