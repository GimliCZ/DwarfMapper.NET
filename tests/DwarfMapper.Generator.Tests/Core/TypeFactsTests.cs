// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Core
{
    /// <summary>
    ///     Pins <see cref="TypeFacts.CanBeNull" /> in BOTH directions, against real Roslyn symbols.
    ///     <para>
    ///         Both directions is the whole point. The defect this predicate fixes was a null guard emitted
    ///         unconditionally, so "a struct gets no guard" is only half the contract — a <c>Nullable&lt;T&gt;</c>
    ///         source is a VALUE type that can still be null, and the obvious repair (<c>!IsValueType</c>) would
    ///         have silently dropped its guard while the CS0037 went away. That direction is unreachable through
    ///         <c>[MapTo]</c>, whose <c>AttributeUsage</c> admits only class and struct DECLARATIONS, so it can be
    ///         pinned nowhere except here — which is why the predicate is a named internal rather than a private
    ///         test inside the emitter.
    ///     </para>
    /// </summary>
    public class TypeFactsTests
    {
        private const string Source = """
                                      namespace Demo;
                                      public class RefType { public int Id { get; set; } }
                                      public struct ValType { public int Id { get; set; } }
                                      public record struct RecStruct(int Id);
                                      public ref struct RefStruct { public int Id { get; set; } }
                                      public enum Colour { A }
                                      public interface IThing { }
                                      public class Generic<TAny, TStruct, TClass>
                                          where TStruct : struct
                                          where TClass : class { }
                                      """;

        private static readonly Compilation Compiled =
            GeneratorTestHarness.BuildCompilation("TypeFactsTestAsm", Source);

        private static INamedTypeSymbol Type(string metadataName)
        {
            return Compiled.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException($"Test fixture does not declare '{metadataName}'.");
        }

        private static ITypeSymbol TypeParameter(string name)
        {
            return Type("Demo.Generic`3").TypeParameters.Single(p => p.Name == name);
        }

        private static ITypeSymbol NullableOf(ITypeSymbol underlying)
        {
            return Type("System.Nullable`1").Construct(underlying);
        }

        [Theory]
        // Reference types keep the guard: they can be null, whatever their nullable annotation says.
        [InlineData("Demo.RefType", true)]
        [InlineData("Demo.IThing", true)]
        // Value types lose it — `x is null` against one is CS0037, which is the shipped defect.
        [InlineData("Demo.ValType", false)]
        [InlineData("Demo.RecStruct", false)]
        [InlineData("Demo.RefStruct", false)]
        [InlineData("Demo.Colour", false)]
        public void A_declared_type_answers_by_whether_it_is_a_value_type(string metadataName, bool expected)
        {
            Assert.Equal(expected, TypeFacts.CanBeNull(Type(metadataName)));
        }

        [Fact]
        public void A_nullable_value_type_can_be_null_even_though_it_is_a_value_type()
        {
            // The direction a naive `!IsValueType` gets wrong. Both a primitive T? and a user struct's T?, so the
            // predicate is shown keying on Nullable<T> itself rather than on anything special about int.
            Assert.True(TypeFacts.CanBeNull(NullableOf(Compiled.GetSpecialType(SpecialType.System_Int32))));
            Assert.True(TypeFacts.CanBeNull(NullableOf(Type("Demo.ValType"))));
        }

        [Fact]
        public void A_type_parameter_answers_by_its_constraint()
        {
            // An unconstrained T is neither IsValueType nor IsReferenceType. It keeps its guard, because a
            // reference-type substitution makes it nullable and `is null` is legal C# against it either way.
            Assert.True(TypeFacts.CanBeNull(TypeParameter("TAny")));
            Assert.True(TypeFacts.CanBeNull(TypeParameter("TClass")));
            // `where T : struct` is a non-nullable value type at every substitution — no guard, and CS0037 if one
            // were emitted.
            Assert.False(TypeFacts.CanBeNull(TypeParameter("TStruct")));
        }
    }
}
