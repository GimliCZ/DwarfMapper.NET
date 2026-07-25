// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// A <c>MapConfig&lt;S,T&gt;</c> convention method is read by the generator but never CALLED, so a consumer
/// building with <c>IDE0051</c>-as-error would see its own compile-time config flagged as an unused private
/// member. The generated half nameof-references each convention method (in a static constructor) so it counts
/// as used — the mapper's config must never break the mapper's build. These lock that emission.
/// </summary>
public class MapConfigConventionRefTests
{
    private const string ConfigMapper = """
        using DwarfMapper;
        namespace Demo;
        public class S { public int A { get; set; } public int Unused { get; set; } }
        public class D { public int A { get; set; } }
        [DwarfMapper]
        public partial class M
        {
            private static void Cfg(MapConfig<S, D> c) => c.IgnoreSource(s => s.Unused);
            public partial D Map(S s);
        }
        """;

    [Fact]
    public void The_convention_method_is_nameof_referenced_in_a_static_constructor()
    {
        var (_, generated) = GeneratorTestHarness.Run(ConfigMapper);

        // A static constructor referencing the convention method by nameof — the thing that keeps IDE0051
        // (unused private member) quiet in a consumer's build.
        Assert.Contains("static M()", generated, StringComparison.Ordinal);
        Assert.Contains("_ = nameof(Cfg);", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mapper_with_no_convention_method_gets_no_static_constructor()
    {
        const string plain = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;

        var (_, generated) = GeneratorTestHarness.Run(plain);

        // No convention methods → nothing to reference → no generated static ctor (zero noise).
        Assert.DoesNotContain("static M()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_declared_static_constructor_suppresses_the_generated_one()
    {
        // The generated static ctor is only emitted when the slot is free — a user static ctor would otherwise
        // collide (CS0111). The whole thing must still COMPILE (that is the real guarantee).
        const string withStaticCtor = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } public int Unused { get; set; } }
            public class D { public int A { get; set; } }
            [DwarfMapper]
            public partial class M
            {
                static M() { }
                private static void Cfg(MapConfig<S, D> c) => c.IgnoreSource(s => s.Unused);
                public partial D Map(S s);
            }
            """;

        GeneratorAssert.EmitsCompilableCode(withStaticCtor);
    }
}
