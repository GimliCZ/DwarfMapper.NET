// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;

// Coverage suite for CollectionConverter's small named classification predicates — pure functions over
// TargetKind, tested directly the same way BlittableProofCoverageTests tests CanReinterpret's siblings.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class CollectionConverterCoverageTests
    {
        // HashSet/ISet/IReadOnlySet were the three IsMutableReferenceCollection arms no Preserve-mode
        // fixture had independently driven — List/ICollection/IList/IReadOnlyList/IReadOnlyCollection/
        // Array/immutables/default are all exercised through the existing Preserve-mode generator tests.
        // TargetKind itself is internal, so these stay [Fact]s rather than a [Theory] over it — a public
        // Theory method cannot declare a less-accessible parameter type (CS0051), IVT notwithstanding.

        [Fact]
        public void IsMutableReferenceCollection_true_for_HashSet()
        {
            Assert.True(CollectionConverter.IsMutableReferenceCollection(CollectionConverter.TargetKind.HashSet));
        }

        [Fact]
        public void IsMutableReferenceCollection_true_for_ISet()
        {
            Assert.True(CollectionConverter.IsMutableReferenceCollection(CollectionConverter.TargetKind.ISet));
        }

        [Fact]
        public void IsMutableReferenceCollection_true_for_IReadOnlySet()
        {
            Assert.True(CollectionConverter.IsMutableReferenceCollection(CollectionConverter.TargetKind.IReadOnlySet));
        }
    }
}
