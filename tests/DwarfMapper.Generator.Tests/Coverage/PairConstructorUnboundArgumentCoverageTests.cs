// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Pairs.cs's ReadPairConstructors skip of a [MapConstructor<S,T>] whose argument does
// not bind. An int where the factory NAME belongs is a compile error (CS1503) that already sits on the attribute, and
// Roslyn hands the generator the attribute with no constructor arguments at all. There is no factory name to check, so
// the generator adds no second report and maps the pair with its ordinary construction.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PairConstructorUnboundArgumentCoverageTests
    {
        [Fact]
        public void A_factory_argument_that_does_not_bind_is_left_to_the_compiler()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Inner { public int A { get; set; } public int B { get; set; } }
                                  public class InnerDto { public int A { get; set; } public int B { get; set; } }
                                  [DwarfMapper]
                                  [GenerateMap<Inner, InnerDto>]
                                  [MapConstructor<Inner, InnerDto>(1)]
                                  public partial class M { }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Id is "DWARF056" or "DWARF059");
            Assert.Contains("return new global::Demo.InnerDto", generated, StringComparison.Ordinal);
            Assert.Contains("A = src.A,", generated, StringComparison.Ordinal);
        }
    }
}
