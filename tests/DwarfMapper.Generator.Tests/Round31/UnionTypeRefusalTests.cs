// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.Linq;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Research C3 / round 31 T24: C# 15 unions ship with .NET 11 on 2026-11-10, and DwarfMapper has no mapping
    ///     policy for them. Measured before DWARF113: <c>Pet</c> into <c>object</c> boxed the union WRAPPER silently, and
    ///     <c>Pet</c> into <c>PetDto</c> was refused as DWARF025 "ambiguous constructor" — the wrong problem. The union
    ///     types here are declared by hand, exactly as the .NET 11 compiler lowers <c>union Pet(Cat, Dog)</c>: a struct
    ///     with <c>[Union]</c>, <c>IUnion</c>, one constructor per case type and <c>object? Value</c>.
    /// </summary>
    public sealed class UnionTypeRefusalTests
    {
        private const string Unions = """
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute { }
                public interface IUnion { object? Value { get; } }
            }
            namespace P
            {
                public record class Cat(string Name);
                public record class Dog(string Name);
                public record class CatDto(string Name);
                public record class DogDto(string Name);
                [System.Runtime.CompilerServices.Union]
                public struct Pet : System.Runtime.CompilerServices.IUnion
                {
                    public Pet(Cat c) { Value = c; }
                    public Pet(Dog d) { Value = d; }
                    public object? Value { get; }
                }
                [System.Runtime.CompilerServices.Union]
                public struct PetDto : System.Runtime.CompilerServices.IUnion
                {
                    public PetDto(CatDto c) { Value = c; }
                    public PetDto(DogDto d) { Value = d; }
                    public object? Value { get; }
                }

            """;

        private static string Src(string body)
        {
            return Unions + body + "\n}\n";
        }

        public static TheoryData<string, string> Refused()
        {
            return new TheoryData<string, string>
            {
                {
                    "union member into another union",
                    "public class S { public Pet P { get; set; } } public class D { public PetDto P { get; set; } }" +
                    "[DwarfMapper.DwarfMapper] public partial class M { public partial D Map(S s); }"
                },
                {
                    "union member into object (used to box the wrapper silently)",
                    "public class S { public Pet P { get; set; } } public class D { public object? P { get; set; } }" +
                    "[DwarfMapper.DwarfMapper] public partial class M { public partial D Map(S s); }"
                },
                {
                    "case type into a union member",
                    "public class S { public Cat P { get; set; } = new(\"x\"); } public class D { public Pet P { get; set; } }" +
                    "[DwarfMapper.DwarfMapper] public partial class M { public partial D Map(S s); }"
                },
                {
                    "union endpoint",
                    "[DwarfMapper.DwarfMapper] public partial class M { public partial PetDto Map(Pet p); }"
                },
                {
                    "nullable union member into another union",
                    "public class S { public Pet? P { get; set; } } public class D { public PetDto? P { get; set; } }" +
                    "[DwarfMapper.DwarfMapper] public partial class M { public partial D Map(S s); }"
                },
            };
        }

        [Theory]
        [MemberData(nameof(Refused))]
        public void A_mapping_between_different_types_where_one_is_a_union_is_refused_as_DWARF113(string shape, string body)
        {
            var diagnostics = GeneratorTestHarness.Run(Src(body)).Diagnostics;
            var hits = diagnostics.Where(d => string.Equals(d.Id, "DWARF113", StringComparison.Ordinal)).ToList();
            Assert.True(hits.Count > 0, shape + ": expected DWARF113, got [" + string.Join(", ", diagnostics.Select(d => d.Id)) + "]");
            Assert.All(hits, d => Assert.Contains("is a C# 15 union type", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "DWARF025", StringComparison.Ordinal));
        }

        [Fact]
        public void The_same_union_on_both_sides_is_copied_as_is()
        {
            var generated = GeneratorAssert.CompilesClean(Src(
                "public class S { public Pet P { get; set; } } public class D { public Pet P { get; set; } }" +
                "[DwarfMapper.DwarfMapper] public partial class M { public partial D Map(S s); }"));
            Assert.Contains("P = s.P", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_explicit_converter_is_the_remedy_and_is_honoured()
        {
            GeneratorAssert.CompilesClean(Src(
                "public class S { public Pet P { get; set; } } public class D { public PetDto P { get; set; } }" +
                "[DwarfMapper.DwarfMapper] public partial class M {" +
                "  [DwarfMapper.MapProperty(nameof(S.P), nameof(D.P), Use = nameof(Convert))] public partial D Map(S s);" +
                "  private static PetDto Convert(Pet p) => p.Value is Cat c ? new PetDto(new CatDto(c.Name)) : new PetDto(new DogDto(((Dog)p.Value!).Name)); }"));
        }

        [Fact]
        public void A_union_member_in_a_projection_is_refused_as_DWARF113()
        {
            var diagnostics = GeneratorTestHarness.Run(Src(
                "public class S { public Pet P { get; set; } } public class D { public PetDto P { get; set; } }" +
                "[DwarfMapper.DwarfMapper] public partial class M { public partial System.Linq.IQueryable<D> Project(System.Linq.IQueryable<S> q); }")).Diagnostics;
            Assert.Contains(diagnostics, d => string.Equals(d.Id, "DWARF113", StringComparison.Ordinal));
            Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "DWARF025", StringComparison.Ordinal));
        }
    }
}
