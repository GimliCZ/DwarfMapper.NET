// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.TryGetNamedArgument, extracted from three readers of single-property attributes
// ([MapDenseEnumKeys] Offset, [MapValue<T>] Use, [RestatesBase] Overrides). Each tested every named argument's key
// against its attribute's only settable property, so "a key that is not that property" was an outcome no real
// application could produce (per-branch rule: extract and test directly). A probe attribute with two properties
// produces both answers.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class TryGetNamedArgumentUnitTests
    {
        private static AttributeData Applied(string application)
        {
            var source = """
                         [System.AttributeUsage(System.AttributeTargets.Class)]
                         public sealed class ProbeAttribute : System.Attribute { public int First { get; set; } public string? Second { get; set; } }
                         """ + "\n" + application + "\npublic class C { }\n";
            var compilation = CSharpCompilation.Create("TryGetNamedArgument", [CSharpSyntaxTree.ParseText(source)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
            return Assert.Single(compilation.GetTypeByMetadataName("C")!.GetAttributes());
        }

        [Fact]
        public void A_set_named_argument_is_found_past_one_with_another_key()
        {
            var attr = Applied("[Probe(First = 1, Second = \"two\")]");

            Assert.True(MapperExtractor.TryGetNamedArgument(attr.NamedArguments, "Second", out var value));
            Assert.Equal("two", value.Value);
        }

        [Fact]
        public void A_named_argument_that_is_not_set_is_not_found()
        {
            var attr = Applied("[Probe(First = 1)]");

            Assert.False(MapperExtractor.TryGetNamedArgument(attr.NamedArguments, "Second", out var value));
            Assert.True(value.IsNull);
        }
    }
}
