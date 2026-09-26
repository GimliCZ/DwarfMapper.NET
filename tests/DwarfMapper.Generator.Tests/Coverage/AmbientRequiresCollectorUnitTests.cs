// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;

// Unit tests for AmbientRequiresCollector answers no compilation reaches (per-branch rule: extract and test directly):
//   - IsFacadeMap for another name or no containing type: IsFacadeMapCall only admits invocations named Map, and a method
//     bound from an invocation always has a containing type;
//   - ReadUsesMapAttribute without an attribute class: every attribute a compilation hands the generator carries one.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AmbientRequiresCollectorUnitTests
    {
        private static readonly Compilation Compilation = GeneratorTestHarness.BuildCompilation("AmbientRequiresCollectorUnit",
            """
            [assembly: DwarfMapper.UsesMap(typeof(Demo.Doc), typeof(Demo.Model))]
            namespace Demo { public class Doc { } public class Model { } }
            """);

        private static INamedTypeSymbol Facade => Compilation.GetTypeByMetadataName("DwarfMapper.IDwarfMapper")!;

        [Fact]
        public void The_facade_map_method_is_recognised()
        {
            Assert.True(AmbientRequiresCollector.IsFacadeMap("Map", Facade));
        }

        [Fact]
        public void Another_name_on_the_facade_is_not_the_facade_map()
        {
            Assert.False(AmbientRequiresCollector.IsFacadeMap("Update", Facade));
        }

        [Fact]
        public void A_map_method_on_another_type_is_not_the_facade_map()
        {
            Assert.False(AmbientRequiresCollector.IsFacadeMap("Map", Compilation.GetSpecialType(SpecialType.System_Object)));
        }

        [Fact]
        public void A_map_method_with_no_containing_type_is_not_the_facade_map()
        {
            Assert.False(AmbientRequiresCollector.IsFacadeMap("Map", null));
        }

        [Fact]
        public void An_attribute_with_no_class_declares_no_pair()
        {
            Assert.Null(AmbientRequiresCollector.ReadUsesMapAttribute(null, ImmutableArray<TypedConstant>.Empty));
        }

        [Fact]
        public void A_non_generic_UsesMap_declares_its_pair()
        {
            var attribute = Assert.Single(Compilation.Assembly.GetAttributes(), a => a.AttributeClass?.Name == "UsesMapAttribute");

            Assert.Equal<(string, string)?>(("global::Demo.Doc", "global::Demo.Model"),
                AmbientRequiresCollector.ReadUsesMapAttribute(attribute.AttributeClass, attribute.ConstructorArguments));
        }
    }
}
