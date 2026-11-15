// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Roslyn512.Tests.Unions
{
    /// <summary>
    ///     Round 35 T23: the <c>[MapTo]</c> registry generator and unions, on the Roslyn 5.12 build. The registry maps no
    ///     union pair: the same union is copied, and a case type converts into its union under C# 15, the one place a union
    ///     conversion counts. Measured before the refusal existed: <c>Pet</c> into <c>object?</c> assigned the wrapper,
    ///     <c>Pet</c> into <c>PetDto</c> went through a synthesized struct map with no diagnostic, and <c>Cat</c> into
    ///     <c>Pet</c> under C# 14 wrote CS9327 into the generated file.
    /// </summary>
    public sealed class UnionMapToTests
    {
        private const string LibrarySource = """
            #nullable enable
            namespace Lib
            {
                public record class Cat(string Name);
                public record class Dog(string Name);
                public record class CatDto(string Name);
                public record class DogDto(string Name);
                public union Pet(Cat, Dog);
                public union PetDto(CatDto, DogDto);
            }
            """;

        private static readonly Lazy<CompiledLibrary> Library = new(() => Roslyn512Harness.Compile(LibrarySource));

        private static string Source(string sourceMember, string destinationMember)
        {
            return "#nullable enable\nusing DwarfMapper;\nnamespace Demo;\n" +
                   "[MapTo(typeof(D))] public class S { " + sourceMember + " }\n" +
                   "public class D { " + destinationMember + " }\n" +
                   "public static class Probe { public static string Run() => new S().ToD().P.Value?.ToString() ?? \"empty\"; }\n";
        }

        [Theory]
        [InlineData("public Lib.Pet P { get; set; }", "public object? P { get; set; }")]
        [InlineData("public Lib.Pet P { get; set; }", "public Lib.PetDto P { get; set; }")]
        public void A_union_member_of_a_MapTo_source_is_DWARF113(string sourceMember, string destinationMember)
        {
            var run = Roslyn512Harness.Run(Source(sourceMember, destinationMember).Replace("public static class Probe", "internal static class Unused", StringComparison.Ordinal),
                LanguageVersion.CSharp15, polyfill: false, libraries: [Library.Value]);

            var hit = Assert.Single(run.GeneratorDiagnostics, d => string.Equals(d.Id, "DWARF113", StringComparison.Ordinal));
            Assert.Contains("[MapTo] does not map union types", hit.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.DoesNotContain(run.GeneratorDiagnostics, d => string.Equals(d.Id, "DWARFR05", StringComparison.Ordinal));
        }

        [Fact]
        public void A_case_member_converts_into_a_union_member_in_CSharp15()
        {
            var run = Roslyn512Harness.CompilesClean(Source("public Lib.Cat P { get; set; } = new(\"tom\");", "public Lib.Pet P { get; set; }"),
                polyfill: false, libraries: [Library.Value]);

            Assert.Equal("Cat { Name = tom }", Roslyn512Harness.Invoke(run, "Demo.Probe", "Run", Library.Value));
        }

        [Fact]
        public void A_case_member_into_a_union_member_in_CSharp14_is_DWARF113_not_CS9327()
        {
            var run = Roslyn512Harness.Run(Source("public Lib.Cat P { get; set; } = new(\"tom\");", "public Lib.Pet P { get; set; }"),
                LanguageVersion.CSharp14, polyfill: false, libraries: [Library.Value]);

            Assert.Contains(run.GeneratorDiagnostics, d => string.Equals(d.Id, "DWARF113", StringComparison.Ordinal));
            Assert.DoesNotContain(run.CompileDiagnostics, d => string.Equals(d.Id, "CS9327", StringComparison.Ordinal));
        }
    }
}
