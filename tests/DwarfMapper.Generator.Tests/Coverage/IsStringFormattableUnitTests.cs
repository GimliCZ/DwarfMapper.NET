// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Unit tests for MapperExtractor.IsStringFormattable, extracted from ResolveProjectionExpr's "string parse/format"
// refusal (per-branch rule: extract and test directly). It names bool and char beside IFormattable. The modern BCL
// declares char IFormattable, so through any real compilation the interface test answers first and the explicit char
// test never decides. A compilation that IS its own core library, declaring Char with no interfaces, is the input that
// test exists for.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class IsStringFormattableUnitTests
    {
        private static readonly CSharpCompilation BareCorlib = CSharpCompilation.Create("IsStringFormattable",
            [CSharpSyntaxTree.ParseText("""
                                        namespace System
                                        {
                                            public class Object { }
                                            public abstract class ValueType { }
                                            public struct Void { }
                                            public struct Int32 { }
                                            public struct Boolean { }
                                            public struct Char { }
                                            public sealed class String { }
                                            public interface IFormattable { }
                                            public struct Formatted : IFormattable { }
                                            public struct Plain { }
                                        }
                                        """)],
            [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        [Fact]
        public void The_core_library_really_declares_Char_without_IFormattable()
        {
            var ch = BareCorlib.GetSpecialType(SpecialType.System_Char);

            Assert.NotEqual(TypeKind.Error, ch.TypeKind);
            Assert.Empty(ch.AllInterfaces);
        }

        [Fact]
        public void Char_and_bool_count_even_without_the_interface()
        {
            Assert.True(MapperExtractor.IsStringFormattable(BareCorlib.GetSpecialType(SpecialType.System_Char)));
            Assert.True(MapperExtractor.IsStringFormattable(BareCorlib.GetSpecialType(SpecialType.System_Boolean)));
        }

        [Fact]
        public void Other_types_count_only_through_IFormattable()
        {
            Assert.True(MapperExtractor.IsStringFormattable(BareCorlib.GetTypeByMetadataName("System.Formatted")!));
            Assert.False(MapperExtractor.IsStringFormattable(BareCorlib.GetTypeByMetadataName("System.Plain")!));
        }
    }
}
