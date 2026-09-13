// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

        // IsIEnumerableT's name fallback answers only for a type that is named System.Collections.Generic.IEnumerable<T>
        // without being the core library's: here, a source declaration that shadows it. The destination is classified
        // as a lazy IEnumerable<T> target (no DWARF027 refusal). Its helper then returns an array into the look-alike,
        // which does not compile (CS0029): shadowing the BCL type breaks every emitted use of the name, not this arm.
        [Fact]
        public void A_shadowing_IEnumerable_look_alike_destination_is_classified_as_a_lazy_IEnumerable_target()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo
                             {
                                 public class Src { public System.Collections.Generic.List<int> Items { get; set; } = new(); }
                                 public class Dst { public System.Collections.Generic.IEnumerable<int> Items { get; set; } }
                                 [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                             }
                             namespace System.Collections.Generic
                             {
                                 public interface IEnumerable<T> { }
                             }
                             """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF027");
            Assert.Contains("private global::System.Collections.Generic.IEnumerable<int> __DwarfMapColl_", generated, StringComparison.Ordinal);
        }

        // HasPublicInstanceInt32 refuses a Count property with no getter at all. Every Count the corpus meets is
        // readable; a set-only one is a class shape no collection fixture declares.
        [Fact]
        public void CountOf_a_class_whose_Count_has_no_getter_is_None()
        {
            var compilation = CSharpCompilation.Create("CollectionConverterCoverage",
                [CSharpSyntaxTree.ParseText("namespace Demo { public class SetOnlyCount { public int Count { set { } } } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

            Assert.Equal(CollectionConverter.CountKind.None,
                CollectionConverter.CountOf(compilation.GetTypeByMetadataName("Demo.SetOnlyCount")!));
        }

        // A type parameter is read through its constraints: member lookup on a value of type T sees the members its
        // constraint types declare, so T : ICollection<int> is an enumerable with a cheap Count. No generator input
        // hands these a type parameter — DWARF053/DWARF054 refuse generic mappers before collection conversion runs.
        private static ITypeParameterSymbol TypeParameter(string constraint)
        {
            var compilation = CSharpCompilation.Create("CollectionConverterTypeParameters",
                [CSharpSyntaxTree.ParseText("using System.Collections.Generic; namespace Demo { public class Holder<T> " + constraint + " { } }")],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            return compilation.GetTypeByMetadataName("Demo.Holder`1")!.TypeParameters[0];
        }

        [Fact]
        public void A_type_parameter_constrained_to_ICollection_is_an_enumerable_with_a_Count()
        {
            var t = TypeParameter("where T : ICollection<int>");

            Assert.True(CollectionConverter.TryGetEnumerableElement(t, out var element, out var count));
            Assert.Equal(SpecialType.System_Int32, element.SpecialType);
            Assert.Equal(CollectionConverter.CountKind.Count, count);
        }

        [Fact]
        public void A_type_parameter_constrained_to_IReadOnlyCollection_has_a_Count()
        {
            Assert.Equal(CollectionConverter.CountKind.Count,
                CollectionConverter.CountOf(TypeParameter("where T : IReadOnlyCollection<int>")));
        }

        [Fact]
        public void A_type_parameter_constrained_to_a_non_collection_interface_is_not_an_enumerable_and_has_no_count()
        {
            // The constraint is walked to the end without a match, where the two tests above stop at the first one.
            var t = TypeParameter("where T : System.IDisposable");

            Assert.False(CollectionConverter.TryGetEnumerableElement(t, out _, out _));
            Assert.Equal(CollectionConverter.CountKind.None, CollectionConverter.CountOf(t));
        }

        [Fact]
        public void An_unconstrained_type_parameter_is_not_an_enumerable_and_has_no_count()
        {
            var t = TypeParameter("");

            Assert.False(CollectionConverter.TryGetEnumerableElement(t, out _, out _));
            Assert.Equal(CollectionConverter.CountKind.None, CollectionConverter.CountOf(t));
        }
    }
}
