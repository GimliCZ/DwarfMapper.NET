// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.IsEnabledFlag and TryReadSingleBool, extracted from the [MapNullSkip] readers and
// ReadMethodAutoNest (per-branch rule: a branch no compilation reaches is extracted and tested directly). A constructor
// argument that does not bind — wrong type or wrong arity — reaches the generator as NO argument, so a non-bool constant
// in a bool parameter never arrives from source. Real TypedConstants stand in for it here: a string argument of
// [Obsolete], a bool argument of [DefaultValue].
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AttributeBoolArgumentUnitTests
    {
        private const string Source = """
                                      using System;
                                      using System.ComponentModel;
                                      namespace T
                                      {
                                          [Obsolete] public class None { }
                                          [Obsolete("x")] public class Text { }
                                          [Obsolete("x", true)] public class Two { }
                                          [DefaultValue(true)] public class On { }
                                          [DefaultValue(false)] public class Off { }
                                      }
                                      """;

        private static readonly Compilation Compilation = CSharpCompilation.Create("AttributeBoolArgumentUnitTests",
            [CSharpSyntaxTree.ParseText(Source)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)));

        private static ImmutableArray<TypedConstant> Args(string metadataName) =>
            Compilation.GetTypeByMetadataName(metadataName)!.GetAttributes().Single().ConstructorArguments;

        [Fact]
        public void The_enabled_flag_is_true_without_an_argument_or_with_a_non_bool_and_the_bool_otherwise()
        {
            Assert.True(MapperExtractor.IsEnabledFlag(Args("T.None")));
            Assert.True(MapperExtractor.IsEnabledFlag(Args("T.Text")));
            Assert.True(MapperExtractor.IsEnabledFlag(Args("T.On")));
            Assert.False(MapperExtractor.IsEnabledFlag(Args("T.Off")));
        }

        [Fact]
        public void A_single_bool_is_read_only_when_there_is_exactly_one_argument_and_it_is_a_bool()
        {
            Assert.True(MapperExtractor.TryReadSingleBool(Args("T.On"), out var on));
            Assert.True(on);
            Assert.True(MapperExtractor.TryReadSingleBool(Args("T.Off"), out var off));
            Assert.False(off);

            Assert.False(MapperExtractor.TryReadSingleBool(Args("T.None"), out _));
            Assert.False(MapperExtractor.TryReadSingleBool(Args("T.Text"), out _));
            Assert.False(MapperExtractor.TryReadSingleBool(Args("T.Two"), out _));
        }
    }
}
