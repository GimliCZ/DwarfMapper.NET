// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // Key types used by this file alone: the registry is process-wide with no reset hook, so isolation comes from
    // types nothing else registers. Namespace scope because this project treats CA1034 as an error.
    public sealed class AmbThreeSrc
    {
        public int X { get; set; }
    }

    public sealed class AmbThreeDst
    {
        public int X { get; set; }
    }

    /// <summary>
    ///     An ambiguous interface lookup must name EVERY interface that accepted the source, not just the first two.
    ///     77dc345 moved the candidate list's allocation from the first match to the second (the single-match case
    ///     no longer pays for a list it only counted), writing <c>candidates ??= [firstMatch]</c>. Nothing registered
    ///     three accepting interfaces, so a <c>??=</c> weakened to <c>=</c> — which rebuilds the list on every match
    ///     after the first and silently drops the middle candidates — survived the runtime mutation leg
    ///     (StrykerOutput/2026-09-14.19-15-03, DwarfMapperRegistry.cs:222).
    /// </summary>
    [Collection("registry-torture")]
    public sealed class AmbiguousInterfaceCandidatesTests
    {
        [Fact]
        public void Three_accepting_interfaces_are_all_named_in_the_ambiguity()
        {
            DwarfMapperRegistry.Register(typeof(IEnumerable<AmbThreeSrc>), typeof(AmbThreeDst), _ => new AmbThreeDst { X = 1 });
            DwarfMapperRegistry.Register(typeof(IReadOnlyCollection<AmbThreeSrc>), typeof(AmbThreeDst), _ => new AmbThreeDst { X = 2 });
            DwarfMapperRegistry.Register(typeof(ICollection<AmbThreeSrc>), typeof(AmbThreeDst), _ => new AmbThreeDst { X = 3 });

            var ex = Assert.Throws<DwarfMapMissingException>(() => DwarfMapperRegistry.Map(new List<AmbThreeSrc>(), typeof(AmbThreeDst)));

            Assert.Equal(3, ex.AmbiguousInterfaces.Count);
            Assert.Contains(typeof(IEnumerable<AmbThreeSrc>), ex.AmbiguousInterfaces);
            Assert.Contains(typeof(IReadOnlyCollection<AmbThreeSrc>), ex.AmbiguousInterfaces);
            Assert.Contains(typeof(ICollection<AmbThreeSrc>), ex.AmbiguousInterfaces);
            Assert.Contains("IEnumerable`1", ex.Message, StringComparison.Ordinal);
            Assert.Contains("IReadOnlyCollection`1", ex.Message, StringComparison.Ordinal);
            Assert.Contains("ICollection`1", ex.Message, StringComparison.Ordinal);
        }
    }
}
