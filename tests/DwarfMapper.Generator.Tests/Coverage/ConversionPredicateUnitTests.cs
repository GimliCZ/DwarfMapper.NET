// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.Conversions predicates whose remaining arms no generator fixture reaches, widened from
// private to internal for the purpose (owner ruling 2026-09-13: extract, expose, test — no deletion):
//   - ConverterReturnIsNullableRef's null converter: its sole caller, ForgiveConverterNullableReturn, returns on null
//     first;
//   - IsAbstractOrInterfaceAutoNestSource's Nullable<T> and enumerable TARGET refusals: every surface probe for those
//     shapes was answered earlier (DWARF033 / DWARF027) before this predicate was asked;
//   - ImplementsIEnumerable's interface walk and IsUnsupportedCollectionTarget's arms, which the same earlier refusals
//     shadow;
//   - IsMappableObjectPair's enumerable TARGET refusal for a non-enumerable source: every nested member of that shape
//     is refused as DWARF027 before auto-nest is asked;
//   - HasDerivedTypesInCompilation's non-class and special-type refusals: its only caller asks after
//     IsMappableObjectPair accepted the pair, which already refused both shapes.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ConversionPredicateUnitTests
    {
        private const string Source = """
                                      using System.Collections;
                                      using System.Collections.Generic;
                                      namespace T
                                      {
                                          public abstract class AbstractSrc { public int A { get; set; } }
                                          public class ConcreteSrc { public int A { get; set; } }
                                          public class Dst { public int A { get; set; } }
                                          public struct ValueDst { public int A { get; set; } }
                                          public class LegacyBag : IEnumerable { public IEnumerator GetEnumerator() => null!; }
                                          public class TypedBag : IEnumerable<int>
                                          {
                                              public IEnumerator<int> GetEnumerator() => null!;
                                              IEnumerator IEnumerable.GetEnumerator() => null!;
                                          }
                                      }
                                      """;

        private static readonly Compilation Compilation = CSharpCompilation.Create("ConversionPredicateUnitTests",
            [CSharpSyntaxTree.ParseText(Source)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        private static INamedTypeSymbol Named(string metadataName) =>
            Compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName + " not found");

        private static INamedTypeSymbol NullableOf(string metadataName) =>
            Compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(Named(metadataName));

        [Fact]
        public void ConverterReturnIsNullableRef_answers_false_for_no_converter()
        {
            Assert.False(MapperExtractor.ConverterReturnIsNullableRef(null, [], []));
        }

        [Fact]
        public void ConverterReturnIsNullableRef_finds_an_annotated_reference_return_in_either_list()
        {
            var src = Named("T.ConcreteSrc");
            var annotated = Named("T.Dst").WithNullableAnnotation(NullableAnnotation.Annotated);
            var notAnnotated = Named("T.Dst").WithNullableAnnotation(NullableAnnotation.NotAnnotated);

            Assert.True(MapperExtractor.ConverterReturnIsNullableRef("ToDto", [("ToDto", src, annotated)], []));
            Assert.True(MapperExtractor.ConverterReturnIsNullableRef("ToDto", [], [("ToDto", src, annotated)]));
            Assert.False(MapperExtractor.ConverterReturnIsNullableRef("ToDto", [("ToDto", src, notAnnotated)], [("Other", src, annotated)]));
        }

        [Fact]
        public void IsAbstractOrInterfaceAutoNestSource_is_true_for_an_abstract_source_into_a_constructible_class()
        {
            Assert.True(MapperExtractor.IsAbstractOrInterfaceAutoNestSource(Compilation, Named("T.AbstractSrc"), Named("T.Dst")));
            Assert.False(MapperExtractor.IsAbstractOrInterfaceAutoNestSource(Compilation, Named("T.ConcreteSrc"), Named("T.Dst")));
        }

        [Fact]
        public void IsAbstractOrInterfaceAutoNestSource_refuses_a_nullable_value_target()
        {
            Assert.False(MapperExtractor.IsAbstractOrInterfaceAutoNestSource(Compilation, Named("T.AbstractSrc"), NullableOf("T.ValueDst")));
        }

        [Fact]
        public void IsAbstractOrInterfaceAutoNestSource_refuses_an_enumerable_target()
        {
            Assert.False(MapperExtractor.IsAbstractOrInterfaceAutoNestSource(Compilation, Named("T.AbstractSrc"), Named("T.TypedBag")));
        }

        [Fact]
        public void IsMappableObjectPair_refuses_an_enumerable_target_for_a_plain_class_source()
        {
            Assert.True(MapperExtractor.IsMappableObjectPair(Compilation, Named("T.ConcreteSrc"), Named("T.Dst")));
            Assert.False(MapperExtractor.IsMappableObjectPair(Compilation, Named("T.ConcreteSrc"), Named("T.TypedBag")));
        }

        [Fact]
        public void HasDerivedTypesInCompilation_refuses_a_non_class_source_and_object()
        {
            Assert.False(MapperExtractor.HasDerivedTypesInCompilation(Compilation, Compilation.CreateArrayTypeSymbol(Named("T.ConcreteSrc"))));
            Assert.False(MapperExtractor.HasDerivedTypesInCompilation(Compilation, Named("T.ValueDst")));
            Assert.False(MapperExtractor.HasDerivedTypesInCompilation(Compilation, Compilation.GetSpecialType(SpecialType.System_Object)));
        }

        [Fact]
        public void ImplementsIEnumerable_recognizes_generic_and_non_generic_implementers_outside_the_known_names()
        {
            Assert.True(MapperExtractor.ImplementsIEnumerable(Named("T.LegacyBag")));
            Assert.True(MapperExtractor.ImplementsIEnumerable(Named("T.TypedBag")));
            Assert.True(MapperExtractor.ImplementsIEnumerable(Named("System.Collections.Generic.List`1")));
            Assert.False(MapperExtractor.ImplementsIEnumerable(Named("T.Dst")));
        }

        [Fact]
        public void IsUnsupportedCollectionTarget_flags_collection_shaped_types_only()
        {
            var int32 = Compilation.GetSpecialType(SpecialType.System_Int32);

            Assert.False(MapperExtractor.IsUnsupportedCollectionTarget(Compilation.GetSpecialType(SpecialType.System_String)));
            Assert.True(MapperExtractor.IsUnsupportedCollectionTarget(Compilation.CreateArrayTypeSymbol(int32, 2)));
            Assert.False(MapperExtractor.IsUnsupportedCollectionTarget(Compilation.CreateArrayTypeSymbol(int32)));
            Assert.True(MapperExtractor.IsUnsupportedCollectionTarget(Named("T.LegacyBag")));
            Assert.True(MapperExtractor.IsUnsupportedCollectionTarget(Named("T.TypedBag")));
            Assert.True(MapperExtractor.IsUnsupportedCollectionTarget(
                Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T).Construct(int32)));
            Assert.False(MapperExtractor.IsUnsupportedCollectionTarget(Named("T.Dst")));
        }

        [Fact]
        public void IsUnsupportedCollectionTarget_flags_IEnumerable_T_itself_when_the_core_library_gives_it_no_non_generic_base()
        {
            // A real IEnumerable<T> always lists the non-generic IEnumerable among AllInterfaces, so the interface
            // loop answers first and the own-type check never runs against the BCL. A compilation that IS its own
            // core library (no references, declares System.Object) can declare IEnumerable<T> without that base —
            // the input the own-type check exists for.
            const string corlib = """
                                  namespace System
                                  {
                                      public class Object { }
                                      public abstract class ValueType { }
                                      public struct Void { }
                                      public struct Int32 { }
                                      public sealed class String { }
                                  }
                                  namespace System.Collections.Generic
                                  {
                                      public interface IEnumerable<T> { }
                                  }
                                  """;
            var bare = CSharpCompilation.Create("BareCorlib", [CSharpSyntaxTree.ParseText(corlib)], [],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var enumerableT = bare.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
            Assert.NotEqual(TypeKind.Error, enumerableT.TypeKind);
            var closed = enumerableT.Construct(bare.GetSpecialType(SpecialType.System_Int32));
            Assert.Empty(closed.AllInterfaces);

            Assert.True(MapperExtractor.IsUnsupportedCollectionTarget(closed));
        }
    }
}
