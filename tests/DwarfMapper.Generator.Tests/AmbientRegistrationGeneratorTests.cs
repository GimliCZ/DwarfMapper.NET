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
    }
}
