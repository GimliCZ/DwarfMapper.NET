// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>[MapNullSkip]</c> — pair- and method-scoped <c>SkipNullSourceMembers</c>.
/// </summary>
/// <remarks>
///     <para>
///         AutoMapper's <c>ForAllMembers(o =&gt; o.Condition((_,_,src) =&gt; src != null))</c> was configured
///         <b>per map</b>, while <c>[DwarfMapper(SkipNullSourceMembers = true)]</c> is a whole-class policy.
///         A profile mixing patch-merge maps with ordinary ones therefore could not be translated one-for-one
///         — the Round-18 migration had to invent two extra mapper classes purely to carry one boolean.
///     </para>
///     <para>
///         That split was not merely inconvenient. A nested pair reached from both classes was synthesized
///         twice, once guarded and once not, silently — recorded at the time as "a real behavioural
///         difference, not a cosmetic one". Narrowing the scope removes the split, and with it that fork.
///     </para>
/// </remarks>
public class MapNullSkipScopeTests
{
    private const string Types = """
        using DwarfMapper;
        namespace Demo;
        public class Dto { public string? Name { get; set; } public string? Note { get; set; } }
        public class Entity { public string Name { get; set; } = ""; public string Note { get; set; } = ""; }
        """;

    /// <summary>The guard <c>SkipNullSourceMembers</c> emits, as it appears in generated code.</summary>
    private const string Guard = "is not null";

    [Fact]
    public void Method_level_MapNullSkip_guards_only_the_method_that_carries_it()
    {
        // The shape the migration could not express: one mapper, two maps, opposite null semantics.
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            public partial class M
            {
                public partial void Replace(Dto src, Entity dst);

                [MapNullSkip]
                public partial void Patch(Dto src, Entity dst);
            }
            """);

        var replace = Body(generated, "Replace");
        var patch = Body(generated, "Patch");

        Assert.DoesNotContain(Guard, replace, StringComparison.Ordinal);
        Assert.Contains(Guard, patch, StringComparison.Ordinal);
    }

    [Fact]
    public void Method_level_MapNullSkip_false_carves_a_method_out_of_an_enabled_class()
    {
        // The inverse direction. Without it, a class that mostly patches would need the split all over again
        // for the one map that must replace.
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper(SkipNullSourceMembers = true)]
            public partial class M
            {
                public partial void Patch(Dto src, Entity dst);

                [MapNullSkip(false)]
                public partial void Replace(Dto src, Entity dst);
            }
            """);

        Assert.Contains(Guard, Body(generated, "Patch"), StringComparison.Ordinal);
        Assert.DoesNotContain(Guard, Body(generated, "Replace"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pair_scoped_MapNullSkip_applies_to_the_named_GenerateMap_pair()
    {
        // The attribute-only mapper shape, which is what an AutoMapper Profile actually becomes.
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper]
            [GenerateMap<Dto, Entity>]
            [MapNullSkip<Dto, Entity>]
            public partial class M { }
            """);

        Assert.Contains(Guard, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Pair_scoped_MapNullSkip_does_not_leak_to_a_pair_it_does_not_name()
    {
        // Same rule the Use= fix established: a pair-scoped attribute configures the pair it names and no
        // other. A leak here would silently make an ordinary map stop clearing values.
        //
        // Two SOURCES sharing one target, rather than one source with two targets — the latter is DWARF060
        // (C# cannot overload by return type) and would emit nothing at all, so it cannot express this
        // question.
        var generated = GeneratorAssert.EmitsCompilableCode("""
            using DwarfMapper;
            namespace Demo;
            public class DtoA { public string? Name { get; set; } }
            public class DtoB { public string? Name { get; set; } }
            public class Entity { public string Name { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<DtoA, Entity>]
            [GenerateMap<DtoB, Entity>]
            [MapNullSkip<DtoA, Entity>]
            public partial class M { }
            """);

        // Exactly one guarded assignment: the EntityA pair. EntityB must assign unconditionally.
        var guards = generated.Split(Guard, StringSplitOptions.None).Length - 1;
        Assert.Equal(1, guards);
    }

    [Fact]
    public void Pair_scoped_false_overrides_an_enabled_class()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper(SkipNullSourceMembers = true)]
            [GenerateMap<Dto, Entity>]
            [MapNullSkip<Dto, Entity>(false)]
            public partial class M { }
            """);

        Assert.DoesNotContain(Guard, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pair_with_no_attribute_still_inherits_the_class_policy()
    {
        // The narrowing must not change what already worked: the class-level option remains the way to say
        // "every map on this mapper patches", which is easier to read than repeating an attribute per pair.
        var generated = GeneratorAssert.EmitsCompilableCode(Types + """

            [DwarfMapper(SkipNullSourceMembers = true)]
            [GenerateMap<Dto, Entity>]
            public partial class M { }
            """);

        Assert.Contains(Guard, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_attribute_forms_are_public_and_carry_an_Enabled_flag()
    {
        // Names the types outright rather than only their [Attribute] shorthand, so the public-surface scan
        // can see them — and pins that the three-state design (present-true / present-false / absent) is
        // expressed by a constructor flag, which is why this is a separate attribute rather than a named
        // property on [GenerateMap] (an attribute argument cannot be bool?).
        Assert.True(typeof(MapNullSkipAttribute).IsPublic);
        Assert.True(typeof(MapNullSkipAttribute<,>).IsPublic);

        Assert.True(new MapNullSkipAttribute().Enabled);
        Assert.False(new MapNullSkipAttribute(false).Enabled);
        Assert.True(new MapNullSkipAttribute<string, string>().Enabled);
        Assert.False(new MapNullSkipAttribute<string, string>(false).Enabled);
    }

    /// <summary>The generated body of one mapping method, so per-method assertions cannot read a sibling.</summary>
    private static string Body(string generated, string methodName)
    {
        var start = generated.IndexOf(" " + methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"method '{methodName}' not found in:\n{generated}");

        // Up to the next method declaration, or the end of the file.
        var next = generated.IndexOf("    public ", start + 1, StringComparison.Ordinal);
        return next < 0 ? generated[start..] : generated[start..next];
    }
}
