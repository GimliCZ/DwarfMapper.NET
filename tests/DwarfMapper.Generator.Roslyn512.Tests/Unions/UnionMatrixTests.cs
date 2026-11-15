// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Roslyn512.Tests.Unions
{
    /// <summary>
    ///     Round 35 T23: every union kind through every route that carries a value, in every reference mode, on the
    ///     Roslyn 5.12 build. Each cell compiles clean (no warning in a generated file) and RUNS, mapping one case value;
    ///     the expected value is literal per kind. The refused routes (projection) and the shapes a kind cannot take are
    ///     pinned in <see cref="UnionNestedTests" /> and <see cref="UnionEndpointTests" />, by name.
    /// </summary>
    public sealed class UnionMatrixTests
    {
        private const string Header = """
            #nullable enable
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public static class Show { public static string Of(object? value) => value?.ToString() ?? "empty"; }
            public record class Cat(string Name);
            public record class Dog(string Name);
            public record class CatDto(string Name);
            public record class DogDto(string Name);

            """;

        private const string CaseMaps = "public partial CatDto Map(Cat c); public partial DogDto Map(Dog d);";

        /// <summary>A union kind: its declarations, its two type names, one value, what that value maps to, and the mapper members it needs.</summary>
        private sealed record UnionKind(string Types, string Source, string Target, string Value, string Expected, string MapperMembers);

        private static readonly Dictionary<string, UnionKind> Kinds = new(StringComparer.Ordinal)
        {
            ["declaration"] = new("public union Pet(Cat, Dog); public union PetDto(CatDto, DogDto);",
                "Pet", "PetDto", "new Pet(new Cat(\"a\"))", "CatDto { Name = a }", CaseMaps),
            ["hand-written"] = new("""
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
                """, "Pet", "PetDto", "new Pet(new Cat(\"a\"))", "CatDto { Name = a }", CaseMaps),
            ["value-cases"] = new("public union Num(int, string); public union NumDto(long, string);",
                "Num", "NumDto", "new Num(5)", "5", "[MapDerivedType(typeof(int), typeof(long))] public partial NumDto Map(Num n);"),
            ["nullable-case"] = new("public union MaybePet(Cat?, Dog); public union MaybePetDto(CatDto?, DogDto);",
                "MaybePet", "MaybePetDto", "new MaybePet(new Cat(\"a\"))", "CatDto { Name = a }", CaseMaps),
            ["generic"] = new("public record class None; public union Opt<T>(T, None);",
                "Opt<Cat>", "Opt<CatDto>", "new Opt<Cat>(new Cat(\"a\"))", "CatDto { Name = a }", "public partial CatDto Map(Cat c);"),
        };

        /// <summary>A route: the types it adds ({P} and {D} are the two union types), its mapping method, and a probe body ({V} is the value).</summary>
        private sealed record Route(string Types, string MapperMethod, string Probe);

        private const string MemberPair = "public class S { public {P} P { get; set; } } public class D { public {D} P { get; set; } }";

        private static readonly Dictionary<string, Route> Routes = new(StringComparer.Ordinal)
        {
            ["member"] = new(MemberPair, "public partial D Map(S s);",
                "return Show.Of(new M().Map(new S { P = {V} }).P.Value);"),
            ["list-element"] = new("public class S { public List<{P}> L { get; set; } = new(); } public class D { public List<{D}> L { get; set; } = new(); }",
                "public partial D Map(S s);",
                "return Show.Of(new M().Map(new S { L = new() { {V} } }).L[0].Value);"),
            ["array-element"] = new("public class S { public {P}[] A { get; set; } = System.Array.Empty<{P}>(); } public class D { public {D}[] A { get; set; } = System.Array.Empty<{D}>(); }",
                "public partial D Map(S s);",
                "return Show.Of(new M().Map(new S { A = new {P}[] { {V} } }).A[0].Value);"),
            ["dictionary-value"] = new("public class S { public Dictionary<string, {P}> V { get; set; } = new(); } public class D { public Dictionary<string, {D}> V { get; set; } = new(); }",
                "public partial D Map(S s);",
                "return Show.Of(new M().Map(new S { V = new() { [\"k\"] = {V} } }).V[\"k\"].Value);"),
            ["nullable-member"] = new("public class S { public {P}? P { get; set; } } public class D { public {D}? P { get; set; } }",
                "public partial D Map(S s);",
                "return Show.Of(new M().Map(new S { P = {V} }).P!.Value.Value);"),
            ["update-into"] = new(MemberPair, "public partial void Update(S s, D d);",
                "var d = new D(); new M().Update(new S { P = {V} }, d); return Show.Of(d.P.Value);"),
            ["span"] = new("", "public partial void Map(System.ReadOnlySpan<{P}> src, System.Span<{D}> dst);",
                "var dst = new {D}[1]; new M().Map(new {P}[] { {V} }, dst); return Show.Of(dst[0].Value);"),
        };

        private static readonly Dictionary<string, string> Modes = new(StringComparer.Ordinal)
        {
            ["None"] = "[DwarfMapper]",
            ["Preserve"] = "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]",
            ["SetNull"] = "[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]",
        };

        public static TheoryData<string, string, string> Cells()
        {
            var data = new TheoryData<string, string, string>();
            foreach (var kind in Kinds.Keys)
            foreach (var route in Routes.Keys)
            foreach (var mode in Modes.Keys)
                data.Add(kind, route, mode);

            return data;
        }

        [Theory]
        [MemberData(nameof(Cells))]
        public void Every_union_kind_maps_its_case_value_on_every_route_in_every_mode(string kind, string route, string mode)
        {
            var k = Kinds[kind];
            var r = Routes[route];
            string Fill(string text)
            {
                return text.Replace("{P}", k.Source, StringComparison.Ordinal)
                    .Replace("{D}", k.Target, StringComparison.Ordinal)
                    .Replace("{V}", k.Value, StringComparison.Ordinal);
            }

            var source = Header + k.Types + "\n" + Fill(r.Types) + "\n" +
                         Modes[mode] + " public partial class M { " + Fill(r.MapperMethod) + " " + k.MapperMembers + " }\n" +
                         "public static class Probe { public static string Run() { " + Fill(r.Probe) + " } }\n";

            var run = Roslyn512Harness.CompilesClean(source);

            Assert.Equal(k.Expected, Roslyn512Harness.Invoke(run, "Demo.Probe", "Run"));
        }

        [Fact]
        public void The_matrix_is_whole()
        {
            // The vacuity guard: a dictionary that lost an entry would quietly shrink the matrix.
            Assert.Equal(5 * 7 * 3, Cells().Count);
        }
    }
}
