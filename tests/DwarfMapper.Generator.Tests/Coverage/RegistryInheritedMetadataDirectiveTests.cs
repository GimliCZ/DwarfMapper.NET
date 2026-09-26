// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Coverage suite for a [MapTo] source that INHERITS a member from a referenced assembly. MemberFacts.Readable walks the
// whole base-type chain, so the base member's member-form directives are read. They come from metadata, where an
// attribute has no application syntax: MemberDirectives.Read then has no file, span or location to record
// (MemberDirectives.cs's `reference?.` arms), and MapToGenerator's DWARFR12 report falls back to the [MapTo] type's own
// location (`d.Loc ?? location`) instead of reporting nowhere.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class RegistryInheritedMetadataDirectiveTests
    {
        private static PortableExecutableReference Library(string source)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("RegistryInheritedMetadataDirective.Library", source);
            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            return MetadataReference.CreateFromImage(ms.ToArray());
        }

        [Fact]
        public void An_inherited_metadata_MapIgnore_argument_is_reported_at_the_MapTo_source_type()
        {
            var library = Library("""
                                  namespace Lib;
                                  public class Base { [global::DwarfMapper.MapIgnore("X")] public int A { get; set; } }
                                  """);
            const string consumer = """
                                    namespace App;
                                    public class Dto { public int A { get; set; } public int B { get; set; } }
                                    [global::DwarfMapper.MapTo(typeof(Dto))]
                                    public class Src : global::Lib.Base { public int B { get; set; } }
                                    """;
            var compilation = GeneratorTestHarness.BuildCompilation("RegistryInheritedMetadataDirective.Consumer", consumer).AddReferences(library);

            CSharpGeneratorDriver.Create(new MapToGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARFR12");
            Assert.StartsWith("[MapIgnore(\"X\")] on 'A' carries an argument the [MapTo] registry does not read", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            // Pipeline diagnostics travel as LocationInfo and are rebuilt as file locations, so the fallback is
            // checked by its span rather than by IsInSource.
            Assert.NotEqual(Location.None, d.Location);
            var span = d.Location.GetLineSpan().Span;
            Assert.Equal(3, span.Start.Line);
            Assert.Equal("Src", consumer.Split('\n')[3].Substring(span.Start.Character, span.End.Character - span.Start.Character));
        }
    }
}
