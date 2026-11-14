// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Roslyn512.Tests.Unions
{
    /// <summary>
    ///     Round 35 T23: a union reached as a member, a collection element, a <c>Nullable&lt;T&gt;</c> value or an
    ///     update-into member, on the Roslyn 5.12 build - through the declared union map when the class has one, through a
    ///     synthesized case dispatch otherwise - and under Preserve and SetNull. Each executed test compiles clean, then runs.
    /// </summary>
    public sealed class UnionNestedTests
    {
        private const string Pets = """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace Demo;
            public record class Cat(string Name);
            public record class Dog(string Name);
            public record class CatDto(string Name);
            public record class DogDto(string Name);
            public union Pet(Cat, Dog);
            public union PetDto(CatDto, DogDto);
            public static class Show
            {
                public static string Of(object? value) => value?.ToString() ?? "empty";
            }

            """;

        private const string CaseMaps = """
                public partial CatDto Map(Cat c);
                public partial DogDto Map(Dog d);
            """;

        private static string Run(GeneratorResult run)
        {
            return (string)Roslyn512Harness.Invoke(run, "Demo.Probe", "Run")!;
        }

        [Fact]
        public void A_union_member_maps_through_a_synthesized_case_dispatch()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public class S { public Pet P { get; set; } }
                public class D { public PetDto P { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial D Map(S s);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    public static string Run() => Show.Of(new M().Map(new S { P = new Pet(new Dog("rex")) }).P.Value) + "|" + Show.Of(new M().Map(new S()).P.Value);
                }
                """);

            Assert.Equal("DogDto { Name = rex }|empty", Run(run));
        }

        [Fact]
        public void A_union_member_calls_the_declared_union_map_when_the_class_has_one()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public class S { public Pet P { get; set; } }
                public class D { public PetDto P { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial D Map(S s);
                    public partial PetDto Map(Pet p);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    public static string Run() => Show.Of(new M().Map(new S { P = new Pet(new Cat("tom")) }).P.Value);
                }
                """);

            Assert.Equal("CatDto { Name = tom }", Run(run));
            Assert.Contains("P = Map(s.P)", run.Generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Lists_arrays_and_dictionary_values_of_unions_map_element_by_element()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public class S
                {
                    public List<Pet> L { get; set; } = new();
                    public Pet[] A { get; set; } = System.Array.Empty<Pet>();
                    public Dictionary<string, Pet> D { get; set; } = new();
                }
                public class T
                {
                    public List<PetDto> L { get; set; } = new();
                    public PetDto[] A { get; set; } = System.Array.Empty<PetDto>();
                    public Dictionary<string, PetDto> D { get; set; } = new();
                }
                [DwarfMapper] public partial class M
                {
                    public partial T Map(S s);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var s = new S
                        {
                            L = new() { new Cat("a"), new Dog("b") },
                            A = new Pet[] { new Dog("c") },
                            D = new() { ["k"] = new Cat("d") },
                        };
                        var t = new M().Map(s);
                        return string.Join(",", t.L.Select(p => Show.Of(p.Value))) + "|" + Show.Of(t.A[0].Value) + "|" + Show.Of(t.D["k"].Value);
                    }
                }
                """);

            Assert.Equal("CatDto { Name = a },DogDto { Name = b }|DogDto { Name = c }|CatDto { Name = d }", Run(run));
        }

        [Fact]
        public void A_nullable_union_member_keeps_null_apart_from_an_empty_union()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public class S { public Pet? P { get; set; } }
                public class D { public PetDto? P { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial D Map(S s);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var none = new M().Map(new S { P = null }).P;
                        var empty = new M().Map(new S { P = default(Pet) }).P;
                        var cat = new M().Map(new S { P = new Pet(new Cat("tom")) }).P;
                        return (none.HasValue ? "value" : "null") + "|" + (empty.HasValue ? "present:" + Show.Of(empty.Value.Value) : "null") + "|" + Show.Of(cat!.Value.Value);
                    }
                }
                """);

            Assert.Equal("null|present:empty|CatDto { Name = tom }", Run(run));
        }

        /// <summary>
        ///     A <c>null</c> beside a union-typed value in a conditional converts to the union itself when every case is a
        ///     reference type, so the lift written as <c>x.HasValue ? Map(x.Value) : null</c> was CS0037 (measured on the
        ///     member route first): the null arm is typed for a union destination, on every route that lifts.
        /// </summary>
        [Fact]
        public void Nullable_union_elements_keep_null_apart_from_an_empty_union_in_lists_arrays_and_dictionaries()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public class S
                {
                    public List<Pet?> L { get; set; } = new();
                    public Pet?[] A { get; set; } = System.Array.Empty<Pet?>();
                    public Dictionary<string, Pet?> D { get; set; } = new();
                }
                public class T
                {
                    public List<PetDto?> L { get; set; } = new();
                    public PetDto?[] A { get; set; } = System.Array.Empty<PetDto?>();
                    public Dictionary<string, PetDto?> D { get; set; } = new();
                }
                [DwarfMapper] public partial class M
                {
                    public partial T Map(S s);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    private static string Of(PetDto? p) => p.HasValue ? "present:" + Show.Of(p.Value.Value) : "null";

                    public static string Run()
                    {
                        var s = new S
                        {
                            L = new() { null, default(Pet), new Cat("a") },
                            A = new Pet?[] { null, new Dog("b") },
                            D = new() { ["n"] = null, ["e"] = default(Pet) },
                        };
                        var t = new M().Map(s);
                        return string.Join(",", t.L.Select(Of)) + "|" + string.Join(",", t.A.Select(Of)) + "|" + Of(t.D["n"]) + "," + Of(t.D["e"]);
                    }
                }
                """);

            Assert.Equal("null,present:empty,present:CatDto { Name = a }|null,present:DogDto { Name = b }|null,present:empty", Run(run));
        }

        [Fact]
        public void An_update_into_map_assigns_a_mapped_union_member()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public class S { public Pet P { get; set; } }
                public class D { public PetDto P { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial void Update(S s, D d);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var d = new D();
                        new M().Update(new S { P = new Pet(new Dog("rex")) }, d);
                        return Show.Of(d.P.Value);
                    }
                }
                """);

            Assert.Equal("DogDto { Name = rex }", Run(run));
        }

        private const string Graph = """
            #nullable enable
            using DwarfMapper;
            namespace Demo;
            public class Owner { public string Name { get; set; } = ""; public Pet Pet { get; set; } }
            public class Cat { public string Name { get; set; } = ""; public Owner? Owner { get; set; } }
            public class Dog { public string Name { get; set; } = ""; }
            public union Pet(Cat, Dog);
            public class OwnerDto { public string Name { get; set; } = ""; public PetDto Pet { get; set; } }
            public class CatDto { public string Name { get; set; } = ""; public OwnerDto? Owner { get; set; } }
            public class DogDto { public string Name { get; set; } = ""; }
            public union PetDto(CatDto, DogDto);

            """;

        private const string GraphProbe = """
            public static class Probe
            {
                public static string Run()
                {
                    var owner = new Owner { Name = "ann" };
                    owner.Pet = new Cat { Name = "tom", Owner = owner };
                    var dto = new M().Map(owner);
                    var cat = (CatDto)dto.Pet.Value!;
                    return cat.Owner is null ? "null" : ReferenceEquals(cat.Owner, dto) ? "same" : "copy";
                }
            }
            """;

        [Fact]
        public void Under_Preserve_a_cycle_through_a_case_value_keeps_its_identity()
        {
            var run = Roslyn512Harness.CompilesClean(Graph + """
                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                public partial class M
                {
                    public partial OwnerDto Map(Owner o);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                """ + GraphProbe);

            Assert.Equal("same", Run(run));
        }

        [Fact]
        public void Under_SetNull_a_cycle_through_a_case_value_is_cut_with_a_null()
        {
            var run = Roslyn512Harness.CompilesClean(Graph + """
                [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                public partial class M
                {
                    public partial OwnerDto Map(Owner o);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                """ + GraphProbe);

            Assert.Equal("null", Run(run));
        }

        [Fact]
        public void A_span_map_of_unions_maps_each_element_through_the_case_dispatch()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                [DwarfMapper] public partial class M
                {
                    public partial void Map(System.ReadOnlySpan<Pet> src, System.Span<PetDto> dst);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var dst = new PetDto[2];
                        new M().Map(new Pet[] { new Cat("a"), new Dog("b") }, dst);
                        return Show.Of(dst[0].Value) + "|" + Show.Of(dst[1].Value);
                    }
                }
                """);

            Assert.Equal("CatDto { Name = a }|DogDto { Name = b }", Run(run));
        }

        [Fact]
        public void An_async_stream_of_unions_maps_each_element_through_the_case_dispatch()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                [DwarfMapper] public partial class M
                {
                    public partial System.Collections.Generic.IAsyncEnumerable<PetDto> Map(System.Collections.Generic.IAsyncEnumerable<Pet> src);
                """ + CaseMaps + """
                }
                public static class Probe
                {
                    private static async System.Collections.Generic.IAsyncEnumerable<Pet> Source()
                    {
                        yield return new Cat("a");
                        await System.Threading.Tasks.Task.Yield();
                        yield return new Dog("b");
                    }

                    public static string Run()
                    {
                        var seen = new System.Collections.Generic.List<string>();
                        var e = new M().Map(Source()).GetAsyncEnumerator();
                        try
                        {
                            while (e.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                                seen.Add(Show.Of(e.Current.Value));
                        }
                        finally
                        {
                            e.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        }

                        return string.Join("|", seen);
                    }
                }
                """);

            Assert.Equal("CatDto { Name = a }|DogDto { Name = b }", Run(run));
        }

        [Fact]
        public void A_union_member_in_a_projection_stays_DWARF113()
        {
            var run = Roslyn512Harness.Run(Pets + """
                public class S { public Pet P { get; set; } }
                public class D { public PetDto P { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial IQueryable<D> Project(IQueryable<S> q);
                """ + CaseMaps + """
                }
                """);

            Assert.Contains(run.GeneratorDiagnostics, d => string.Equals(d.Id, "DWARF113", StringComparison.Ordinal) &&
                                                           d.GetMessage(CultureInfo.InvariantCulture).Contains("A union is not projected", StringComparison.Ordinal));
        }

        [Fact]
        public void A_nested_union_pair_with_an_unpaired_case_is_DWARF114_naming_the_synthesized_map()
        {
            var run = Roslyn512Harness.Run(Pets + """
                public class S { public Pet P { get; set; } }
                public class D { public PetDto P { get; set; } }
                [DwarfMapper] public partial class M
                {
                    public partial D Map(S s);
                    public partial CatDto Map(Cat c);
                }
                """);

            var hit = Assert.Single(run.GeneratorDiagnostics, d => string.Equals(d.Id, "DWARF114", StringComparison.Ordinal));
            var message = hit.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("synthesized for a member or element", message, StringComparison.Ordinal);
            Assert.Contains("'Demo.Dog' have no arm", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_helper_beside_a_declared_union_map_does_not_repeat_the_maps_own_diagnostics()
        {
            // Preserve routes the member through a synthesized helper beside the declared map; the declared map's
            // DWARF114 is reported once, at the map.
            var run = Roslyn512Harness.Run(Pets + """
                public class S { public Pet P { get; set; } }
                public class D { public PetDto P { get; set; } }
                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)] public partial class M
                {
                    public partial D Map(S s);
                    public partial PetDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                }
                """);

            Assert.Single(run.GeneratorDiagnostics, d => string.Equals(d.Id, "DWARF114", StringComparison.Ordinal));
        }
    }
}
