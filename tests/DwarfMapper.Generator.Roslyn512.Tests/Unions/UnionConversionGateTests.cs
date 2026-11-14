// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Roslyn512.Tests.Unions
{
    /// <summary>
    ///     Round 35 T23: where a C# 15 union conversion counts, and what is refused, on the Roslyn 5.12 build.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Roslyn 5.12 classifies a union conversion (case type to union) as IMPLICIT in a C# 14 compilation too, while
    ///         C# 14 code that uses one fails with CS9327. Every check that asked "is there an implicit conversion?" - the
    ///         <c>Use=</c> converter's parameter, hook matching, a direct assignment - therefore accepted it and wrote
    ///         CS9327 into the consumer's <c>.g.cs</c>, where nothing the consumer writes can reach it. The C# 14 case is a
    ///         C# 14 project referencing a C# 15 library, so the unions here come from a compiled library.
    ///     </para>
    /// </remarks>
    public sealed class UnionConversionGateTests
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

        private static GeneratorResult Run(string source, LanguageVersion lang)
        {
            return Roslyn512Harness.Run(source, lang, polyfill: false, libraries: [Library.Value]);
        }

        private static IEnumerable<string> Messages(GeneratorResult run, string id)
        {
            return run.GeneratorDiagnostics
                .Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture));
        }

        private static void AssertNoCs9327(GeneratorResult run)
        {
            Assert.DoesNotContain(run.CompileDiagnostics, d => string.Equals(d.Id, "CS9327", StringComparison.Ordinal));
        }

        private const string UseConverterSource = """
            #nullable enable
            using DwarfMapper;
            namespace Demo;
            public class S { public Lib.Cat P { get; set; } = new("tom"); }
            public class D { public string P { get; set; } = ""; }
            [DwarfMapper] public partial class M
            {
                [MapProperty(nameof(S.P), nameof(D.P), Use = nameof(Describe))] public partial D Map(S s);
                private static string Describe(Lib.Pet p) => p.Value?.ToString() ?? "";
            }
            public static class Probe { public static string Run() => new M().Map(new S()).P; }
            """;

        [Fact]
        public void A_Use_converter_taking_the_union_does_not_apply_to_a_case_member_in_CSharp14()
        {
            var run = Run(UseConverterSource, LanguageVersion.CSharp14);

            Assert.NotEmpty(Messages(run, "DWARF014"));
            AssertNoCs9327(run);
        }

        [Fact]
        public void The_same_Use_converter_applies_in_CSharp15_and_receives_the_wrapped_case()
        {
            var run = Roslyn512Harness.CompilesClean(UseConverterSource, polyfill: false, libraries: [Library.Value]);

            Assert.Equal("Cat { Name = tom }", Roslyn512Harness.Invoke(run, "Demo.Probe", "Run", Library.Value));
        }

        private const string HookSource = """
            #nullable enable
            using DwarfMapper;
            namespace Demo;
            [DwarfMapper] public partial class M
            {
                public static int Seen;
                public partial Lib.CatDto Map(Lib.Cat c);
                [BeforeMap] private void Before(Lib.Pet p) => Seen++;
            }
            public static class Probe
            {
                public static string Run()
                {
                    new M().Map(new Lib.Cat("tom"));
                    return M.Seen.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            """;

        [Fact]
        public void A_hook_taking_the_union_is_not_matched_by_a_case_source_in_CSharp14()
        {
            var run = Run(HookSource, LanguageVersion.CSharp14);

            Assert.DoesNotContain("Before(", run.Generated, StringComparison.Ordinal);
            AssertNoCs9327(run);
        }

        [Fact]
        public void In_CSharp15_the_hook_matches_a_case_source_as_the_hook_rule_says_assignable_sources_do()
        {
            var run = Roslyn512Harness.CompilesClean(HookSource, polyfill: false, libraries: [Library.Value]);

            Assert.Equal("1", Roslyn512Harness.Invoke(run, "Demo.Probe", "Run", Library.Value));
        }

        [Fact]
        public void A_union_pair_in_a_CSharp14_project_is_DWARF113_naming_CSharp15()
        {
            var run = Run("""
                using DwarfMapper;
                namespace Demo;
                [DwarfMapper] public partial class M { public partial Lib.PetDto Map(Lib.Pet p); }
                """, LanguageVersion.CSharp14);

            var message = Assert.Single(Messages(run, "DWARF113"));
            Assert.Contains("Mapping a union needs C# 15, and this project compiles C# 14.0", message, StringComparison.Ordinal);
            AssertNoCs9327(run);
        }

        private const string CaseIntoUnionSource = """
            #nullable enable
            using DwarfMapper;
            namespace Demo;
            public class S { public Lib.Cat P { get; set; } = new("tom"); }
            public class D { public Lib.Pet P { get; set; } }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            public static class Probe { public static string Run() => new M().Map(new S()).P.Value?.ToString() ?? "empty"; }
            """;

        [Fact]
        public void A_case_member_converts_into_a_union_member_in_CSharp15()
        {
            var run = Roslyn512Harness.CompilesClean(CaseIntoUnionSource, polyfill: false, libraries: [Library.Value]);

            Assert.Equal("Cat { Name = tom }", Roslyn512Harness.Invoke(run, "Demo.Probe", "Run", Library.Value));
        }

        [Fact]
        public void A_case_member_into_a_union_member_in_CSharp14_is_DWARF113_not_CS9327()
        {
            var run = Run(CaseIntoUnionSource, LanguageVersion.CSharp14);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("needs C# 15", StringComparison.Ordinal));
            AssertNoCs9327(run);
        }

        [Fact]
        public void A_union_into_a_type_that_is_not_a_union_is_DWARF113()
        {
            var run = Run("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                public class S { public Lib.Pet P { get; set; } }
                public class D { public object? P { get; set; } }
                [DwarfMapper] public partial class M { public partial D Map(S s); }
                """, LanguageVersion.CSharp15);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("A union maps only into another union", StringComparison.Ordinal));
        }

        [Fact]
        public void A_type_that_is_no_case_of_the_union_into_the_union_is_DWARF113()
        {
            var run = Run("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                public class S { public string P { get; set; } = ""; }
                public class D { public Lib.Pet P { get; set; } }
                [DwarfMapper] public partial class M { public partial D Map(S s); }
                """, LanguageVersion.CSharp15);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("is not one of its case types and does not convert to one", StringComparison.Ordinal));
        }

        [Fact]
        public void A_union_in_a_projection_is_DWARF113()
        {
            var run = Run("""
                #nullable enable
                using DwarfMapper;
                using System.Linq;
                namespace Demo;
                public class S { public Lib.Cat P { get; set; } = new("tom"); }
                public class D { public Lib.Pet P { get; set; } }
                [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                """, LanguageVersion.CSharp15);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("A union is not projected", StringComparison.Ordinal));
        }

        [Fact]
        public void A_union_with_no_case_types_is_DWARF113()
        {
            var run = Run("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                [System.Runtime.CompilerServices.Union]
                public struct Nothing : System.Runtime.CompilerServices.IUnion { public object? Value => null; }
                public class S { public Nothing P { get; set; } }
                public class D { public Lib.Pet P { get; set; } }
                [DwarfMapper] public partial class M { public partial D Map(S s); }
                """, LanguageVersion.CSharp15);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("'Demo.Nothing' is a union type with no case types", StringComparison.Ordinal));
        }
    }
}
