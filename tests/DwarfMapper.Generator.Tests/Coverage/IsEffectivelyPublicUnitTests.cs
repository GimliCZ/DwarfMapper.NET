// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.IsEffectivelyPublic, now the one statement of "can another assembly name this type?".
// Two copies existed: this one, which gates public facade extensions and ambient registration, and a verbatim copy in
// AmbientRequiresCollector, which gates the [assembly: DwarfRequiresMap] manifest. The copy's array and generic type
// argument arms had never executed, and it still carried the ContainingSymbol walk whose null exit no symbol can take.
// The copy is gone and the rule is pinned here, arm by arm.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class IsEffectivelyPublicUnitTests
    {
        private const string Source = """
                                      using System.Collections.Generic;
                                      namespace T
                                      {
                                          public class Open { public class Nested { } }
                                          internal class Hidden { public class Nested { } }
                                          public class Generic<TArg> { public TArg? Value; }
                                      }
                                      """;

        private static readonly Compilation Compilation = CSharpCompilation.Create("IsEffectivelyPublicUnitTests",
            [CSharpSyntaxTree.ParseText(Source)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        private static INamedTypeSymbol Named(string metadataName) =>
            Compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName + " not found");

        private static INamedTypeSymbol ListOf(ITypeSymbol element) =>
            Named("System.Collections.Generic.List`1").Construct(element);

        [Fact]
        public void A_type_is_effectively_public_only_if_it_and_every_containing_type_are_public()
        {
            Assert.True(MapperExtractor.IsEffectivelyPublic(Named("T.Open")));
            Assert.True(MapperExtractor.IsEffectivelyPublic(Named("T.Open+Nested")));
            Assert.False(MapperExtractor.IsEffectivelyPublic(Named("T.Hidden")));
            Assert.False(MapperExtractor.IsEffectivelyPublic(Named("T.Hidden+Nested")));
        }

        [Fact]
        public void An_array_is_judged_by_its_element()
        {
            Assert.True(MapperExtractor.IsEffectivelyPublic(Compilation.CreateArrayTypeSymbol(Named("T.Open"))));
            Assert.False(MapperExtractor.IsEffectivelyPublic(Compilation.CreateArrayTypeSymbol(Named("T.Hidden"))));
        }

        [Fact]
        public void A_constructed_generic_is_judged_by_every_type_argument_too()
        {
            Assert.True(MapperExtractor.IsEffectivelyPublic(ListOf(Named("T.Open"))));
            Assert.False(MapperExtractor.IsEffectivelyPublic(ListOf(Named("T.Hidden"))));
            Assert.False(MapperExtractor.IsEffectivelyPublic(ListOf(Named("T.Hidden+Nested"))));
        }

        [Fact]
        public void A_type_parameter_is_not_effectively_public()
        {
            Assert.False(MapperExtractor.IsEffectivelyPublic(Named("T.Generic`1").TypeParameters[0]));
        }
    }
}
