// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     I14's second half: a projection member that cannot be translated must kill its own <c>Project</c>
///     method and nothing else.
///     <para>
///         <c>DWARF028</c> is an Error, and an error suppressed the whole class, so a mapper carrying a
///         <c>Map</c> and a <c>Project</c> generated NOTHING the moment one projected member was
///         untranslatable — every method lost its implementing part and <c>DWARF078</c> announced it. The
///         <c>Map</c> methods were collateral: nothing about them is translated by a query provider, so
///         nothing about them can fail to translate.
///     </para>
///     <para>
///         The refusal is now scoped. The projection method is dropped (a projection missing the members that
///         did not resolve would return them silently unset — worse than none), the class is emitted with its
///         <c>Map</c>, and <c>DWARF096</c> signposts the one <c>CS8795</c> that follows. The CONTROL below is
///         a class-level error, which must still take everything down: "untranslatable" is the one error that
///         is a property of the endpoint rather than of the mapping.
///     </para>
/// </summary>
public class ProjectionScopedRefusalTests
{
    /// <summary>A HashSet target: a real, permanent projection refusal that <c>.Map</c> handles happily.</summary>
    private const string UntranslatableMember = """
        using System.Collections.Generic;
        using System.Linq;
        using DwarfMapper;
        namespace Demo;

        public sealed class Src { public int Id { get; set; } public List<int> Tags { get; set; } = new(); }
        public sealed class Dst { public int Id { get; set; } public HashSet<int> Tags { get; set; } = new(); }

        [DwarfMapper]
        public partial class BothEndpointsMapper
        {
            public partial Dst Map(Src s);
            public partial IQueryable<Dst> Project(IQueryable<Src> q);
        }
        """;

    [Fact]
    public void An_untranslatable_projection_member_does_not_suppress_the_mappers_Map()
    {
        var (diagnostics, generated) = GeneratorTestHarness.Run(UntranslatableMember);

        Assert.Contains(diagnostics, d => d.Id == "DWARF028");

        // The signpost moved from the class to the method — and DWARF078's ABSENCE is the assertion, because
        // it is the diagnostic that says "nothing was generated".
        Assert.Contains(diagnostics, d => d.Id == "DWARF096");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");

        // The consumer still gets their Map...
        Assert.Contains("public partial global::Demo.Dst Map(", generated, StringComparison.Ordinal);
        // ...and does NOT get a half-built projection, which would return Tags silently unset.
        Assert.DoesNotContain("IQueryable<global::Demo.Dst> Project(", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Emission is not the claim — the number of CS8795 is. Before the fix BOTH partial methods lost their
    ///     implementing part; now exactly one does, and it is the projection.
    /// </summary>
    [Fact]
    public void Exactly_one_CS8795_follows_and_it_is_the_projection_method()
    {
        var (_, errors) = GeneratorTestHarness.EmitAssembly(UntranslatableMember);
        var walls = errors.Where(e => e.Id == "CS8795").ToList();

        var only = Assert.Single(walls);
        Assert.Contains("Project", only.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    ///     CONTROL. A CLASS-level error still suppresses everything: DWARF010 describes the SOURCE MODEL and is
    ///     just as true of the Map method, so scoping it would emit a mapper built on an ambiguity. A fix that
    ///     scoped every error rather than only the translatability one would pass the two tests above and fail
    ///     here.
    /// </summary>
    [Fact]
    public void A_class_level_error_still_suppresses_the_whole_mapper()
    {
        const string code = """
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public sealed class Src { public int Foo { get; set; } public int foo { get; set; } }
            public sealed class Dst { public int Foo { get; set; } }

            [DwarfMapper(CaseInsensitive = true)]
            public partial class AmbiguousMapper
            {
                public partial Dst Map(Src s);
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(code);

        Assert.Contains(diagnostics, d => d.Id == "DWARF010");
        Assert.Contains(diagnostics, d => d.Id == "DWARF078");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF096");
        Assert.Equal(string.Empty, generated.Trim());
    }

    /// <summary>
    ///     The two signposts must never contradict each other. A projection that refuses CLEANLY (only
    ///     DWARF028) sits beside a Map method with an unmapped member: the projection's refusal is scoped, so
    ///     DWARF096 would be added — and then the Map method's DWARF001 suppresses the class anyway and
    ///     DWARF078 says nothing was generated. Both reported, one of them lying. DWARF096 exists solely to
    ///     describe the SCOPE of the damage, so it stands down when the scope is no longer what it claims and
    ///     lets the class-wide signpost speak. The errors underneath are unaffected.
    /// </summary>
    [Fact]
    public void The_scoped_signpost_stands_down_when_a_class_level_error_kills_everything_anyway()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public sealed class Src { public List<int> Tags { get; set; } = new(); }
            public sealed class Dst { public HashSet<int> Tags { get; set; } = new(); }
            public sealed class Other { public int X { get; set; } }
            public sealed class OtherDto { public int X { get; set; } public int Missing { get; set; } }

            [DwarfMapper]
            public partial class MixedFailureMapper
            {
                public partial OtherDto MapIncomplete(Other o);
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(code);

        // Both errors still reported — suppressing the signpost must not suppress the diagnosis.
        Assert.Contains(diagnostics, d => d.Id == "DWARF001");
        Assert.Contains(diagnostics, d => d.Id == "DWARF028");

        // The accurate signpost speaks; the one whose claim has become false does not.
        Assert.Contains(diagnostics, d => d.Id == "DWARF078");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF096");
        Assert.Equal(string.Empty, generated.Trim());
    }

    /// <summary>
    ///     The reference-handling early exit used to add the projection method with an EMPTY member list "so
    ///     no further cascades" — harmless only because the DWARF028 beside it suppressed the class, so the
    ///     empty model was never emitted. With the refusal scoped, emitting it would produce
    ///     <c>Select(q, __s =&gt; new Dst { })</c>: a projection that silently drops every member. It is
    ///     dropped like every other refused projection, and this pins that it is.
    /// </summary>
    [Fact]
    public void ReferenceHandling_drops_the_projection_rather_than_emitting_an_empty_one()
    {
        const string code = """
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public sealed class Src { public int Id { get; set; } }
            public sealed class Dst { public int Id { get; set; } }

            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            public partial class PreserveMapper
            {
                public partial Dst Map(Src s);
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(code);

        Assert.Contains(diagnostics, d => d.Id == "DWARF028");
        Assert.Contains(diagnostics, d => d.Id == "DWARF096");
        Assert.Contains("Map(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Project(", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A mapper whose ONLY method is a refused projection. Nothing survives to emit a body for, but the
    ///     class must still be produced and the signpost must still be reported — the alternative is the
    ///     unexplained CS8795 that DWARF078 was written to prevent, one method down.
    /// </summary>
    [Fact]
    public void A_projection_only_mapper_still_reports_the_signpost()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using DwarfMapper;
            namespace Demo;

            public sealed class Src { public List<int> Tags { get; set; } = new(); }
            public sealed class Dst { public HashSet<int> Tags { get; set; } = new(); }

            [DwarfMapper]
            public partial class ProjectionOnlyMapper
            {
                public partial IQueryable<Dst> Project(IQueryable<Src> q);
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(code);

        Assert.Contains(diagnostics, d => d.Id == "DWARF028");
        Assert.Contains(diagnostics, d => d.Id == "DWARF096");
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF078");

        var (_, errors) = GeneratorTestHarness.EmitAssembly(code);
        Assert.Single(errors.Where(e => e.Id == "CS8795"));
    }
}
