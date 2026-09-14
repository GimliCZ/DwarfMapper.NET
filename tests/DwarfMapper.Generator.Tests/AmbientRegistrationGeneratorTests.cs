// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Generator-side tests for the ambient-registry emission: the module-initializer self-registration +
    ///     <c>[assembly: DwarfProvidesMap]</c> manifest (asserted on the aggregate file), and DWARF062 when a
    ///     mapper has constructor dependencies and so cannot self-register.
    /// </summary>
    public sealed class AmbientRegistrationGeneratorTests
    {
        [Fact]
        public void Public_typed_map_emits_module_initializer_and_provides_manifest()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class A { public int V { get; set; } }
                             public class B { public int V { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<A, B>]
                             public partial class M { }
                             """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");

            Assert.Contains("ModuleInitializer", ambient, StringComparison.Ordinal);
            Assert.Contains("DwarfMapperRegistry.Register(typeof(global::Demo.A), typeof(global::Demo.B)",
                ambient,
                StringComparison.Ordinal);
            Assert.Contains(
                "[assembly: global::DwarfMapper.DwarfProvidesMap(typeof(global::Demo.A), typeof(global::Demo.B))]",
                ambient,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Internal_typed_map_is_not_ambient_registered()
        {
            // Internal source/dest types cannot be named by another assembly, so they are NOT registered
            // ambiently (no aggregate file produced for this mapper).
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             internal class A { public int V { get; set; } }
                             internal class B { public int V { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<A, B>]
                             public partial class M { }
                             """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");
            Assert.Equal(string.Empty, ambient);
        }

        [Fact]
        public void Mapper_with_constructor_dependency_reports_DWARF062()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class A { public int V { get; set; } }
                             public class B { public int V { get; set; } }
                             public class Dep { }
                             [DwarfMapper]
                             [GenerateMap<A, B>]
                             public partial class M
                             {
                                 public M(Dep dep) { _ = dep; }
                             }
                             """;

            var (diags, _) = GeneratorTestHarness.Run(s);

            Assert.Contains(diags, d => d.Id == "DWARF062" && d.Severity == DiagnosticSeverity.Info);
            // ...and no ambient registration file is produced (it cannot self-register without DI).
            Assert.Equal(string.Empty, GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs"));
        }

        [Fact]
        public void Internal_typed_update_into_is_not_ambient_registered()
        {
            // The mirror of Internal_typed_map_is_not_ambient_registered, for IsAmbientUpdateRegisterable's
            // own public-type gate — the create-map and update-into gates are two separate methods and the
            // create one's fixture does not exercise the update one.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             internal class A { public int V { get; set; } }
                             internal class B { public int V { get; set; } }
                             [DwarfMapper] public partial class M { public partial void Update(A a, B b); }
                             """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");
            Assert.Equal(string.Empty, ambient);
        }

        [Fact]
        public void Private_update_into_is_not_ambient_registered()
        {
            // A private partial method is a legal declaration (C# 9+), but IsAmbientUpdateRegisterable
            // requires public or internal — the same accessibility floor the create-map gate enforces, never
            // independently exercised for the update-into twin.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class A { public int V { get; set; } }
                             public class B { public int V { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial B Map(A a);
                                 private partial void Update(A a, B b);
                             }
                             """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");
            // The create-map still registers; RegisterUpdate for this pair must not appear.
            Assert.Contains("DwarfMapperRegistry.Register(typeof(global::Demo.A), typeof(global::Demo.B)",
                ambient,
                StringComparison.Ordinal);
            Assert.DoesNotContain("RegisterUpdate", ambient, StringComparison.Ordinal);
        }

        [Fact]
        public void Static_ProvidesMap_method_is_invoked_on_the_type_not_a_cached_field()
        {
            // A static [ProvidesMap] needs no cached instance — every other hand-written or generated
            // registration invokes through a `private static readonly` field, but a static provider is
            // invoked on the mapper TYPE itself (AggregateEmitter.EmitAmbientRegistration's IsStatic arm).
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Widget { public int V { get; set; } }
                             public class WidgetDto { public int V { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [ProvidesMap]
                                 public static WidgetDto Provide(Widget w) => new() { V = w.V };
                             }
                             """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");

            Assert.Contains("global::Demo.M.Provide((global::Demo.Widget)__s)", ambient, StringComparison.Ordinal);
            // No cached field for a mapper that hosts only a static provider — nothing needs an instance.
            Assert.DoesNotContain("private static readonly global::Demo.M", ambient, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pair_provided_by_two_mappers_is_registered_once_by_the_first()
        {
            // The same (source, target) [ProvidesMap] on two mappers of one assembly: the first mapper in hint-name order
            // provides it, exactly as a same-assembly duplicate generated map does, so the manifest names the pair once.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper] public partial class P1 { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                             [DwarfMapper] public partial class P2 { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                             """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");

            const string manifest = "[assembly: global::DwarfMapper.DwarfProvidesMap(typeof(global::Demo.Src), typeof(global::Demo.Dst))]";
            Assert.Single(ambient.Split('\n'), line => line.Contains(manifest, StringComparison.Ordinal));
            Assert.Contains("global::Demo.P1.Provide((global::Demo.Src)__s)", ambient, StringComparison.Ordinal);
            Assert.DoesNotContain("global::Demo.P2.Provide", ambient, StringComparison.Ordinal);
        }

        private const string PairTypes = """
                                          using System.Collections.Generic;
                                          using DwarfMapper;
                                          namespace Demo;
                                          public class Src { public int Id { get; set; } }
                                          public class Dst { public int Id { get; set; } }
                                          """;

        private const string DirectRegistration =
            "DwarfMapperRegistry.Register(typeof(global::Demo.Src), typeof(global::Demo.Dst),";

        private const string DirectManifest =
            "[assembly: global::DwarfMapper.DwarfProvidesMap(typeof(global::Demo.Src), typeof(global::Demo.Dst))]";

        [Theory]
        [InlineData("P1", "P2")] // the generated map's mapper comes first in hint-name order
        [InlineData("Z9", "A0")] // the [ProvidesMap] mapper comes first — the generated map must still be the one kept
        public void A_pair_both_generated_and_ProvidesMap_is_registered_once_by_the_generated_map(string generatedHost, string providerHost)
        {
            // The create table used to deduplicate generated maps and [ProvidesMap] methods in two separate sets, so a
            // pair declared both ways was registered TWICE into one key: a second Register call the registry records as
            // a competing provider (IsAmbiguous turns true on this assembly's own pair), and a repeated manifest line.
            // [ProvidesMap] exists for shapes the generator cannot express, so the generated map is the one kept.
            var s = PairTypes + $$"""

                                  [DwarfMapper][GenerateMap<Src, Dst>] public partial class {{generatedHost}} { }
                                  [DwarfMapper] public partial class {{providerHost}} { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                                  """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");
            var lines = ambient.Split('\n');

            Assert.Single(lines, line => line.Contains(DirectRegistration, StringComparison.Ordinal));
            Assert.Single(lines, line => line.Contains(DirectManifest, StringComparison.Ordinal));
            Assert.Contains($".Map((global::Demo.Src)__s)", ambient, StringComparison.Ordinal);
            Assert.DoesNotContain($"global::Demo.{providerHost}.Provide", ambient, StringComparison.Ordinal);
        }

        [Fact]
        public void A_ProvidesMap_colliding_with_a_generated_collection_shape_is_registered_once_by_the_shape()
        {
            // The same split, one level out: a generated map also registers its collection shapes, and a hand-written
            // provider of one of those exact shapes was registered on top of it.
            var s = PairTypes + """

                                [DwarfMapper][GenerateMap<Src, Dst>] public partial class P1 { }
                                [DwarfMapper] public partial class P2
                                {
                                    [ProvidesMap] public static List<Dst> ProvideAll(IEnumerable<Src> s) => new();
                                }
                                """;

            var ambient = GeneratorTestHarness.RunAndGetSource(s, "DwarfMapper.AmbientRegistration.g.cs");
            var lines = ambient.Split('\n');

            const string shape = "DwarfMapperRegistry.Register(typeof(global::System.Collections.Generic.IEnumerable<global::Demo.Src>), typeof(global::System.Collections.Generic.List<global::Demo.Dst>),";
            Assert.Single(lines, line => line.Contains(shape, StringComparison.Ordinal));
            Assert.DoesNotContain("global::Demo.P2.ProvideAll", ambient, StringComparison.Ordinal);
        }
    }
}
