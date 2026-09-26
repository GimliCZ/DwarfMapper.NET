// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.ImplementsIEnumerable's known-name fast path. The name list answers "collection" before
// the interface walk runs, for every supported and well-known unsupported collection or dictionary name, and it
// matches the NAME, not the interface. So a type named after any of them counts, even one that implements nothing.
// Most names were only ever met through types the interface walk would also have caught, so each is pinned on a plain
// class that implements no interface at all.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ImplementsIEnumerableKnownNamesUnitTests
    {
        private static readonly string[] KnownNames =
        [
            "List", "Array", "HashSet", "Dictionary",
            "IEnumerable", "ICollection", "IList",
            "IReadOnlyList", "IReadOnlyCollection",
            "ISet", "IReadOnlySet",
            "ImmutableArray", "ImmutableList", "IImmutableList",
            "ImmutableHashSet", "IImmutableSet",
            "ImmutableDictionary", "IImmutableDictionary",
            "IDictionary", "IReadOnlyDictionary",
            "SortedSet", "SortedDictionary", "SortedList",
            "Queue", "Stack", "LinkedList",
            "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentStack", "ConcurrentBag"
        ];

        private static readonly Compilation Compilation = CSharpCompilation.Create("ImplementsIEnumerableKnownNames",
            [CSharpSyntaxTree.ParseText("namespace T { " + string.Concat(KnownNames.Select(n => "public class " + n + " { } ")) + "public class Lookalike { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        public static TheoryData<string> Names => new(KnownNames);

        [Theory]
        [MemberData(nameof(Names))]
        public void A_type_bearing_a_known_collection_name_is_treated_as_enumerable(string name)
        {
            var type = Compilation.GetTypeByMetadataName("T." + name);

            Assert.NotNull(type);
            Assert.Empty(type.AllInterfaces);
            Assert.True(MapperExtractor.ImplementsIEnumerable(type));
        }

        [Fact]
        public void A_plain_type_with_another_name_is_not()
        {
            Assert.False(MapperExtractor.ImplementsIEnumerable(Compilation.GetTypeByMetadataName("T.Lookalike")!));
        }
    }
}
