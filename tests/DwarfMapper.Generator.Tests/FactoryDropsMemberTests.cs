// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>DWARF080</c> — a <c>[MapConstructor]</c> factory silently discarding a mapped source value.
/// </summary>
/// <remarks>
///     <para>
///         Under a factory the generator does not assign <c>init</c>-only or <c>required</c> members: the
///         factory owns construction, so those members keep whatever it chose (<c>CS8852</c> forbids assigning
///         them afterwards). If the source has a matching member, its value is dropped — with a green build.
///     </para>
///     <para>
///         Round 18 hit this twice in one codebase: an entity lost its <c>Identifier</c> through an
///         <c>.Empty</c> factory that minted a fresh <c>Guid</c>, and a second map "compiled green but silently
///         dropped Identifier, TotalArguments and IsCoreCommand" and was backed out on the principle that
///         lossy-but-green is worse than undone. See <c>Issues/Rount18/</c>.
///     </para>
/// </remarks>
public class FactoryDropsMemberTests
{
    private const string Id = "DWARF080";

    /// <summary>Identifier is init-only, so the factory owns it — and Src has an Identifier to lose.</summary>
    private const string LossySource = """
        using System;
        using DwarfMapper;
        namespace Demo;
        public class Src { public Guid Identifier { get; set; } public string Name { get; set; } = ""; }
        public class Dst
        {
            private Dst() { }
            public Guid Identifier { get; init; }
            public string Name { get; set; } = "";
            public static Dst Empty => new() { Identifier = Guid.NewGuid() };
        }

        [DwarfMapper]
        [GenerateMap<Src, Dst>]
        [MapConstructor<Src, Dst>(nameof(Create))]
        public partial class M
        {
            private static Dst Create(Src s) => Dst.Empty;
        }
        """;

    [Fact]
    public void Reports_when_a_factory_drops_a_mapped_source_value()
    {
        Assert.NotEmpty(GeneratorAssert.Reports(LossySource, Id));
    }

    [Fact]
    public void The_message_names_the_member_and_the_parameter_binding_alternative()
    {
        var message = GeneratorAssert.Reports(LossySource, Id)[0].GetMessage(CultureInfo.InvariantCulture);

        Assert.Contains("'Identifier'", message, StringComparison.Ordinal);

        // The generalisable lesson from Round 18: prefer constructor-parameter binding to a factory, because
        // direct construction fills an object initializer where init members ARE assignable. A message that
        // only said "this is lossy" would leave the reader without the fix.
        Assert.Contains("[MapProperty]", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_silent_when_nothing_in_the_source_would_have_supplied_a_value()
    {
        // A factory-owned member that no source member maps to loses nothing. Warning here would fire on
        // essentially every factory-constructed record and train people to ignore the id.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public string Name { get; set; } = ""; }
            public class Dst
            {
                private Dst() { }
                public Guid Identifier { get; init; }
                public string Name { get; set; } = "";
                public static Dst Empty => new() { Identifier = Guid.NewGuid() };
            }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [MapConstructor<Src, Dst>(nameof(Create))]
            public partial class M
            {
                private static Dst Create(Src s) => Dst.Empty;
            }
            """;

        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Is_silenced_by_an_explicit_MapIgnore()
    {
        // Stating "the factory's value is intended" is a legitimate answer, and the message offers it. The
        // point of the diagnostic is that the choice be explicit, not that factories be forbidden.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public Guid Identifier { get; set; } public string Name { get; set; } = ""; }
            public class Dst
            {
                private Dst() { }
                public Guid Identifier { get; init; }
                public string Name { get; set; } = "";
                public static Dst Empty => new() { Identifier = Guid.NewGuid() };
            }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [MapConstructor<Src, Dst>(nameof(Create))]
            [MapIgnore<Dst>(nameof(Dst.Identifier))]
            public partial class M
            {
                private static Dst Create(Src s) => Dst.Empty;
            }
            """;

        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Is_silent_without_a_factory_because_direct_construction_assigns_init_members()
    {
        // The contrast that makes the diagnostic actionable, verified rather than asserted in prose:
        // no factory, so the generator constructs directly and fills an object initializer — where `init`
        // members are assignable and Identifier survives.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public Guid Identifier { get; set; } public string Name { get; set; } = ""; }
            public class Dst { public Guid Identifier { get; init; } public string Name { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        GeneratorAssert.DoesNotReport(src, Id);
        Assert.Contains("Identifier = ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_Info_because_the_generator_cannot_see_inside_the_factory()
    {
        // Severity is the whole judgement call here. A factory that DELIBERATELY supplies its own value is a
        // legitimate design — MapConstructorRuntimeTests does exactly that and asserts the factory's value
        // wins. The generator cannot read the factory body, so it cannot separate that from a factory that
        // simply forgot, and a Warning would break every warnings-as-errors consumer using the first shape.
        //
        // Info states the fact and leaves the judgement to the reader; teams wanting it strict can escalate
        // with dotnet_diagnostic.DWARF080.severity = warning.
        var reported = GeneratorAssert.Reports(LossySource, Id);

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, reported[0].Severity);
    }
}
