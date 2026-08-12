// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>[MapDerivedType]</c> arms have to compose with everything else on the mapper — including with the
///     method they dispatch from.
/// </summary>
/// <remarks>
///     <para>
///         The symptom that started this was user-visible and silent: an <c>AliasCommand</c> inside a
///         <c>List&lt;Command&gt;</c> lost its <c>Alias</c> in API responses, because a collection loop binds
///         at COMPILE time while the previous mapper dispatched on the RUNTIME type. <c>[MapDerivedType]</c> is
///         the feature built for exactly that, and the migration could not make it work — it ended up
///         re-implementing polymorphic dispatch by hand in an <c>[AfterMap]</c> downcast.
///     </para>
///     <para>
///         The live defect underneath was worse than the diagnostics it was reported as. An arm whose target is
///         the dispatching method's own return type resolved to the dispatching method ITSELF, because a
///         derived source converts to the declared source type and the signature therefore matches. The
///         emission was <c>AliasCommand __s =&gt; ToDto(__s)</c> — a switch arm calling its own switch. It
///         compiles, reports nothing, and overflows the stack on the first <c>AliasCommand</c>.
///     </para>
/// </remarks>
public class DerivedTypeArmCompositionTests
{
    private const string Types = """
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Command { public string Name { get; set; } = ""; }
        public class AliasCommand : Command { public string Alias { get; set; } = ""; }
        public class CommandDto { public string Name { get; set; } = ""; }
        public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }
        """;

    [Fact]
    public void An_arm_never_dispatches_back_into_the_method_it_dispatches_from()
    {
        // Two sources, one target — the shape the migration actually wrote, and the one that recursed.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Command { public string Name { get; set; } = ""; }
            public class AliasCommand : Command { public string Alias { get; set; } = ""; }
            public class CommandOverviewDto
            {
                public string Name { get; set; } = "";
                public string Alias { get; set; } = "";
            }

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType<AliasCommand, CommandOverviewDto>]
                public partial CommandOverviewDto ToDto(Command c);
            }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        Assert.DoesNotContain("__s => ToDto(__s)", generated, StringComparison.Ordinal);

        // The arm gets a real AliasCommand -> CommandOverviewDto mapper, which is what asking to map a
        // derived type differently meant — and which carries the member the base does not have.
        Assert.Contains("Alias = s.Alias", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arm_may_resolve_to_a_differently_named_declared_method()
    {
        // The record reported DWARF035 demanding a declared partial OVERLOAD of the dispatching method. It
        // does not: any declared method with the arm's signature serves, whatever it is called.
        var generated = GeneratorAssert.CompilesClean(Types + """

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType<AliasCommand, AliasCommandDto>]
                public partial CommandDto ToDto(Command c);

                public partial AliasCommandDto ToAliasDto(AliasCommand c);
            }
            """);

        Assert.Contains("__s => ToAliasDto(__s)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arm_does_not_make_the_reverse_direction_ambiguous()
    {
        // The second half of the reported catch-22: adding the overload the arm wanted was said to make
        // AliasCommandDto -> AliasCommand ambiguous, because that pair already owns a method.
        var generated = GeneratorAssert.CompilesClean(Types + """

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType<AliasCommand, AliasCommandDto>]
                public partial CommandDto ToDto(Command c);

                public partial AliasCommandDto ToDto(AliasCommand c);

                public partial AliasCommand FromDto(AliasCommandDto d);
            }
            """);

        Assert.Contains("__s => ToDto(__s)", generated, StringComparison.Ordinal);
        Assert.Contains("public partial global::Demo.AliasCommand FromDto", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dispatch_only_method_does_not_demand_completeness_of_the_base_pair()
    {
        // A dispatching method never maps its declared pair — every runtime type either matches an arm or
        // throws — so requiring the base source to fill every target member would be asking for a mapping
        // that is never emitted. Pinned because the obvious implementation gets this wrong and the workaround
        // ([MapIgnore] on members the DERIVED type supplies) reads as nonsense.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Command { public string Name { get; set; } = ""; }
            public class AliasCommand : Command { public string Alias { get; set; } = ""; }
            public class CommandOverviewDto
            {
                public string Name { get; set; } = "";
                public string Alias { get; set; } = "";
            }

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType<AliasCommand, CommandOverviewDto>]
                public partial CommandOverviewDto ToDto(Command c);
            }
            """;

        GeneratorAssert.DoesNotReport(src, "DWARF001");
    }

    [Fact]
    public void The_collection_element_route_dispatches_on_the_runtime_type()
    {
        // The whole point. A List<Command> holding an AliasCommand must go through the dispatching method,
        // not through a compile-time-bound Command -> CommandDto mapping that drops Alias on the floor.
        var generated = GeneratorAssert.CompilesClean(Types + """

            public class Page { public List<Command> Items { get; set; } = new(); }
            public class PageDto { public List<CommandDto> Items { get; set; } = new(); }

            [DwarfMapper]
            [GenerateMap<Page, PageDto>]
            public partial class M
            {
                [MapDerivedType<AliasCommand, AliasCommandDto>]
                public partial CommandDto ToDto(Command c);

                public partial AliasCommandDto ToAliasDto(AliasCommand c);
            }
            """);

        Assert.Contains("__r.Add(ToDto(__item))", generated, StringComparison.Ordinal);
    }
}
