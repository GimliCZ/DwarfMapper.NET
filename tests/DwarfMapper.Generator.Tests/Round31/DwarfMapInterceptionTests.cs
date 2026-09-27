// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Round 31 T26: a <c>Dwarf.Map</c> call whose pair the calling assembly registers is bound at compile time to
    ///     the registration's own expression; every other call is left to the registry. The compile succeeding is
    ///     itself the proof that a bound call's interceptor was accepted: an <c>[InterceptsLocation]</c> that does not
    ///     name a real call site, or whose signature does not match it, is a compile error.
    /// </summary>
    public sealed class DwarfMapInterceptionTests
    {
        private const string Types = """
            #nullable enable
            using DwarfMapper;
            namespace Demo;
            public class Order { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class OrderDto { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class Unmapped { public int Id { get; set; } }
            public class UnmappedDto { public int Id { get; set; } }
            [DwarfMapper]
            public partial class OrderMapper
            {
                public partial OrderDto ToDto(Order source);
                public partial void Merge(Order source, OrderDto destination);
            }
            """;

        private static readonly CSharpParseOptions Interceptable = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures(new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "DwarfMapper.Generated") });

        [Fact]
        public void A_create_call_for_a_pair_this_assembly_registers_is_bound_to_the_registration()
        {
            var (interceptors, errors) = Generate(Types + """
                public static class Calls { public static OrderDto Create(Order o) => Dwarf.Map<Order, OrderDto>(o); }
                """, out var field);

            Assert.Empty(errors);
            Assert.Contains("InterceptsLocation(", interceptors, StringComparison.Ordinal);
            // The SAME instance the registration registers - not a fresh `new OrderMapper()`, which would diverge
            // from a looked-up call on any mapper that carries state.
            Assert.Contains("internal static global::Demo.OrderDto __DwarfMap_0(global::Demo.Order source) => " + field +
                            ".ToDto((global::Demo.Order)source);", interceptors, StringComparison.Ordinal);
        }

        [Fact]
        public void An_update_call_is_bound_and_refuses_nulls_in_the_registry_order()
        {
            var (interceptors, errors) = Generate(Types + """
                public static class Calls { public static void Merge(Order o, OrderDto d) => Dwarf.Map(o, d); }
                """, out var field);

            Assert.Empty(errors);
            var source = interceptors.IndexOf("ThrowIfNull(source);", StringComparison.Ordinal);
            var destination = interceptors.IndexOf("ThrowIfNull(destination);", StringComparison.Ordinal);
            Assert.True(source >= 0 && destination > source, interceptors);
            Assert.Contains(field + ".Merge((global::Demo.Order)source, (global::Demo.OrderDto)destination);", interceptors,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Every_call_of_one_pair_shares_one_bound_method()
        {
            var (interceptors, errors) = Generate(Types + """
                public static class Calls
                {
                    public static OrderDto A(Order o) => Dwarf.Map<Order, OrderDto>(o);
                    public static OrderDto B(Order o) => Dwarf.Map<Order, OrderDto>(o);
                }
                """, out _);

            Assert.Empty(errors);
            Assert.Equal(2, CountOf(interceptors, "InterceptsLocation("));
            Assert.Equal(1, CountOf(interceptors, " __DwarfMap_"));
        }

        [Theory]
        [InlineData("public static UnmappedDto C(Unmapped u) => Dwarf.Map<Unmapped, UnmappedDto>(u);")]
        [InlineData("public static OrderDto C(Order? o) => Dwarf.Map<Order?, OrderDto>(o);")]
        [InlineData("public static TD C<TS, TD>(TS s) => Dwarf.Map<TS, TD>(s);")]
        [InlineData("public static OrderDto C(Order o) => DwarfMapperFacade.Instance.Map<Order, OrderDto>(o);")]
        [InlineData("public static OrderDto C(IDwarfMapper m, Order o) => m.Map<Order, OrderDto>(o);")]
        public void A_call_that_is_not_this_assemblys_exact_registered_pair_is_left_to_the_registry(string call)
        {
            var (interceptors, errors) = Generate(Types + "public static class Calls { " + call + " }\n", out _);

            Assert.Empty(errors);
            Assert.Equal(string.Empty, interceptors);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_pair_a_referenced_assembly_also_provides_is_left_to_the_registry(bool referencedProvidesIt)
        {
            // Which of two registrations of one pair the registry answers with depends on load order, so binding
            // the local one here could pick the other. The referenced manifest is what makes the duplicate visible.
            // The pair's types live in the referenced assembly either way; only the manifest differs, so the `false`
            // row is the control that proves the local registration does bind when nothing else provides the pair.
            var manifest = referencedProvidesIt
                ? "[assembly: DwarfMapper.DwarfProvidesMap(typeof(Demo.Order), typeof(Demo.OrderDto))]\n"
                : string.Empty;
            var other = GeneratorTestHarness.BuildCompilation("OtherProvider_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(manifest + """
                    namespace Demo
                    {
                        public class Order { public int Id { get; set; } public string Name { get; set; } = ""; }
                        public class OrderDto { public int Id { get; set; } public string Name { get; set; } = ""; }
                    }
                    """, Interceptable)]);
            using var ms = new MemoryStream();
            Assert.True(other.Emit(ms).Success);

            var (interceptors, errors) = Generate("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                [DwarfMapper]
                public partial class OrderMapper { public partial OrderDto ToDto(Order source); }
                public static class Calls { public static OrderDto Create(Order o) => Dwarf.Map<Order, OrderDto>(o); }
                """,
                out _,
                MetadataReference.CreateFromImage(ms.ToArray()));

            Assert.Empty(errors);
            Assert.Equal(!referencedProvidesIt, interceptors.Contains("InterceptsLocation(", StringComparison.Ordinal));
        }

        private static int CountOf(string text, string needle)
        {
            var count = 0;
            for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
                count++;
            return count;
        }

        /// <summary>
        ///     Runs the generator the way the build does - with the project's parse options, which is where
        ///     <c>InterceptorsNamespaces</c> lives - and returns the interceptor file (empty when none was emitted) and
        ///     the output compilation's errors, and the name of the field the ambient registration holds the
        ///     <c>OrderMapper</c> instance in.
        /// </summary>
        private static (string Interceptors, IReadOnlyList<Diagnostic> Errors) Generate(string source,
            out string registeredField, MetadataReference? extra = null)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapInterception_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source, Interceptable)], NullableContextOptions.Enable);
            if (extra is not null)
            {
                compilation = compilation.AddReferences(extra);
            }

            var driver = CSharpGeneratorDriver.Create([new DwarfGenerator().AsSourceGenerator()], parseOptions: Interceptable);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            var interceptors = output.SyntaxTrees
                .FirstOrDefault(t => t.FilePath.EndsWith("DwarfMapper.Interceptors.g.cs", StringComparison.Ordinal))
                ?.ToString() ?? string.Empty;
            var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            var registration = output.SyntaxTrees
                .FirstOrDefault(t => t.FilePath.EndsWith("DwarfMapper.AmbientRegistration.g.cs", StringComparison.Ordinal))
                ?.ToString() ?? string.Empty;
            const string declaration = "private static readonly global::Demo.OrderMapper ";
            var at = registration.IndexOf(declaration, StringComparison.Ordinal);
            registeredField = at < 0
                ? "<no OrderMapper field>"
                : registration.Substring(at + declaration.Length,
                    registration.IndexOf(' ', at + declaration.Length) - at - declaration.Length);
            return (interceptors, errors);
        }
    }
}
