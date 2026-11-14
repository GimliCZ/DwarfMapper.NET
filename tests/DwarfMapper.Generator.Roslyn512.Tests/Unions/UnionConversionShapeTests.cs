// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Roslyn512.Tests.Unions
{
    /// <summary>
    ///     <see cref="MapperExtractor.IsUnionConversion" /> reads a union conversion from the Roslyn 5.0 API (an implicit
    ///     conversion that is neither user-defined nor a method group and still carries a method), so that both builds
    ///     share it. This holds the rule to Roslyn 5.12's own classification of each kind of implicit conversion.
    /// </summary>
    public sealed class UnionConversionShapeTests
    {
        private const string Source = """
            #nullable enable
            namespace Demo
            {
                public record class Cat(string Name);
                public record class Dog(string Name);
                public union Pet(Cat, Dog);
                public struct Celsius { public static implicit operator Celsius(int degrees) => default; }
            }
            """;

        public static TheoryData<string, string, bool> Pairs()
        {
            return new TheoryData<string, string, bool>
            {
                { "Demo.Cat", "Demo.Pet", true },
                { "Demo.Cat", "Demo.Pet?", true },
                { "Demo.Cat", "Demo.Cat", false },
                { "int", "long", false },
                { "string", "object", false },
                { "int", "object", false },
                { "int", "int?", false },
                { "Demo.Pet", "object", false },
                { "int", "Demo.Celsius", false },
            };
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Only_a_union_conversion_is_read_as_one(string from, string to, bool isUnion)
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);
            var run = Roslyn512Harness.Run(Source);
            var compilation = run.Output;

            var conversion = compilation.ClassifyConversion(Resolve(compilation, from), Resolve(compilation, to));

            Assert.True(conversion.IsImplicit, $"{from} -> {to} should be an implicit conversion for this row to mean anything");
            Assert.Equal(isUnion, MapperExtractor.IsUnionConversion(conversion));
        }

        private static ITypeSymbol Resolve(CSharpCompilation compilation, string name)
        {
            // The span form: the net11 job builds this project as net11.0, where string.EndsWith(char) draws CA1307
            // (net11.0 adds an overload with a StringComparison; net10.0 has none). The span form has none on either.
            var nullable = name.AsSpan().EndsWith('?');
            var bare = nullable ? name[..^1] : name;
            ITypeSymbol type = bare switch
            {
                "int" => compilation.GetSpecialType(SpecialType.System_Int32),
                "long" => compilation.GetSpecialType(SpecialType.System_Int64),
                "string" => compilation.GetSpecialType(SpecialType.System_String),
                "object" => compilation.GetSpecialType(SpecialType.System_Object),
                _ => compilation.GetTypeByMetadataName(bare) ?? throw new InvalidOperationException("no type " + bare),
            };
            return nullable ? compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(type) : type;
        }
    }
}
