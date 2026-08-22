// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <b>I20.</b> <c>ImplicitConversions</c> is a documented option of the MAPPER, with no endpoint
///     qualifier, and it is the strict TRUST setting: a consumer writes
///     <c>[DwarfMapper(ImplicitConversions = false)]</c> precisely to be told about lossy conversions. The
///     projection pipeline never read it. <c>long → double</c> was an <b>Error DWARF038</b> through
///     <c>.Map</c> and produced <b>no diagnostic at all</b> through <c>.Project</c> — the gate silently off
///     at one endpoint, which is worse than a divergence because nothing says so.
///     <para>
///         <b>Why DWARF038 and not DWARF028.</b> <c>DWARF028</c> means "a query provider cannot translate
///         this", and that is false here: <c>long → double</c> is a widening cast, the most translatable
///         thing there is. Refusing it would also make the PERMISSIVE default refuse a member <c>.Map</c>
///         happily maps — a capability regression at one endpoint, the branch I19 rejected for
///         <c>NullCollections</c>. Reusing the runtime endpoint's own emitter is what makes the two agree on
///         the only thing the option is about: <b>whether the build breaks</b>.
///     </para>
///     <para>
///         <b>Why the kill is not scoped.</b> I14 gave the rule: a <c>DWARF028</c> describes THIS endpoint's
///         translatability and is confined to its method (<c>DWARF096</c>); every other error describes the
///         SOURCE MODEL and is equally true of the <c>.Map</c> methods over the same pair, so it keeps the
///         whole-class kill. A lossy type pair is the second kind. The absence of <c>DWARF096</c> is
///         asserted, not assumed — reaching for the scoped mechanism here would have been the easy mistake.
///     </para>
///     <para>
///         <b>Four routes, because the option is threaded through four resolvers.</b> A plain member, a
///         nested object's member, a collection ELEMENT, and a constructor PARAMETER each reach
///         <c>ResolveProjectionExpr</c> down a different path, and a fix that threads three of them looks
///         exactly like a fix that threads four.
///     </para>
///     <para>
///         <b>Stated limit (B19's rule).</b> The permissive half EXECUTES over LINQ-to-Objects
///         (<c>AsQueryable()</c>), which proves the tree evaluates to the same value <c>.Map</c> produces.
///         No provider runs here. What bounds the claim is that <b>the emitted expression is unchanged</b>:
///         this change adds a diagnostic and not a single character of generated code, so a provider that
///         translated the projection yesterday translates the identical tree today.
///     </para>
/// </summary>
public class ProjectionImplicitConversionsTests
{
    /// <summary>A mapper carrying BOTH endpoints over one pair, so each shape is asked at both.</summary>
    private static string BothEndpoints(string types, bool strict) => $$"""
        using System.Linq;
        using DwarfMapper;
        namespace Demo;
        {{types}}
        {{(strict ? "[DwarfMapper(ImplicitConversions = false)]" : "[DwarfMapper]")}}
        public partial class M
        {
            public partial Dst Map(Src s);
            public partial IQueryable<Dst> Project(IQueryable<Src> q);
        }
        """;

    private static string ProjectOnly(string types, bool strict) => $$"""
        using System.Linq;
        using DwarfMapper;
        namespace Demo;
        {{types}}
        {{(strict ? "[DwarfMapper(ImplicitConversions = false)]" : "[DwarfMapper]")}}
        public partial class M { public partial IQueryable<Dst> Project(IQueryable<Src> q); }
        """;

    public static TheoryData<string, string> LossyShapes() => new()
    {
        {
            "plain member",
            "public class Src { public long M { get; set; } } public class Dst { public double M { get; set; } }"
        },
        {
            "nested member",
            "public class I1 { public long M { get; set; } } public class I2 { public double M { get; set; } } "
            + "public class Src { public I1 C { get; set; } } public class Dst { public I2 C { get; set; } }"
        },
        {
            "collection element",
            "public class Src { public System.Collections.Generic.List<long> A { get; set; } } "
            + "public class Dst { public System.Collections.Generic.List<double> A { get; set; } }"
        },
        {
            "constructor parameter",
            "public class Src { public long M { get; set; } } "
            + "public class Dst { public Dst(double m) { M = m; } public double M { get; set; } }"
        },
    };

    /// <summary>
    ///     The option's whole content, at the endpoint that ignored it: under
    ///     <c>ImplicitConversions = false</c> the build STOPS. Asked at a projection-only mapper, so nothing
    ///     a <c>.Map</c> method reports can be mistaken for the projection reporting it.
    /// </summary>
    [Theory]
    [MemberData(nameof(LossyShapes))]
    public void Project_refuses_a_lossy_conversion_under_strict_exactly_as_Map_does(string shape, string types)
    {
        var (diags, generated) = GeneratorTestHarness.Run(ProjectOnly(types, true));
        var d038 = diags.Where(d => d.Id == "DWARF038").ToList();

        Assert.True(d038.Count == 1,
            $"[{shape}] expected exactly one DWARF038 from the projection under "
            + $"ImplicitConversions = false, got {d038.Count}. I20: this endpoint reported none at all — "
            + "the strictness gate was silently off here.");
        Assert.True(d038[0].Severity == DiagnosticSeverity.Error,
            $"[{shape}] DWARF038 is {d038[0].Severity}, expected Error. The endpoints must agree on "
            + "WHETHER THE BUILD BREAKS — that is the only thing this option promises.");

        // The whole-class kill, not a scoped refusal. DWARF078 is the cascade signpost that accompanies a
        // class-killing error; DWARF096 is I14's SCOPED signpost and must NOT appear, because a lossy type
        // pair describes the SOURCE MODEL and is equally true of any .Map method over the same pair.
        Assert.Contains(diags, d => d.Id == "DWARF078");
        Assert.DoesNotContain(diags, d => d.Id == "DWARF096");
        Assert.True(generated.Length == 0,
            $"[{shape}] the mapper was still generated under a blocking error:\n{generated}");

        // And it is NOT a translatability refusal wearing a policy hat.
        Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
    }

    /// <summary>
    ///     Under the PERMISSIVE default the member still maps — at both endpoints, to the same value — and
    ///     each endpoint warns once. A fix that refused under the default would be a capability regression.
    /// </summary>
    [Theory]
    [MemberData(nameof(LossyShapes))]
    public void Under_the_default_both_endpoints_warn_once_and_neither_refuses(string shape, string types)
    {
        var (diags, generated) = GeneratorTestHarness.Run(BothEndpoints(types, false));
        var d038 = diags.Where(d => d.Id == "DWARF038").ToList();

        Assert.True(d038.Count == 2,
            $"[{shape}] expected one DWARF038 per method (2), got {d038.Count}. Both endpoints apply the "
            + "conversion, so both must say so — and neither may say it twice.");
        Assert.All(d038, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(generated);
    }

    /// <summary>
    ///     The DIAGONAL. Same-category widening loses nothing, so the option has no opinion about it at
    ///     either endpoint or either severity — a fix that started refusing every numeric conversion would
    ///     pass every test above and fail here.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_widening_conversion_stays_silent_at_the_projection_endpoint(bool strict)
    {
        const string types =
            "public class Src { public int M { get; set; } } public class Dst { public long M { get; set; } }";
        var (diags, generated) = GeneratorTestHarness.Run(ProjectOnly(types, strict));

        Assert.DoesNotContain(diags, d => d.Id == "DWARF038");
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(generated);
    }

    /// <summary>
    ///     The other CONTROL: a NARROWING pair has no implicit C# conversion, so the projection refuses it
    ///     with <c>DWARF028</c> before any policy is consulted — and must go on doing exactly that. This is
    ///     the half of the old <c>NotApplicable</c> excuse that was TRUE, pinned so the fix cannot have
    ///     quietly reclassified a translatability refusal as a policy one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_narrowing_pair_is_still_the_endpoints_own_DWARF028_refusal(bool strict)
    {
        const string types =
            "public class Src { public long M { get; set; } } public class Dst { public int M { get; set; } }";
        var (diags, _) = GeneratorTestHarness.Run(ProjectOnly(types, strict));

        Assert.Contains(diags, d => d.Id == "DWARF028");
        Assert.DoesNotContain(diags, d => d.Id == "DWARF038");
    }

    /// <summary>
    ///     Runtime evidence (B19). Reporting a conversion is not the same as applying it: the permissive
    ///     projection is EXECUTED against the identical <c>.Map</c> call, and the two must produce the same
    ///     double from the same long — with a widening member alongside as the untouched control, and two
    ///     rows in one query so cardinality is asserted with the values.
    /// </summary>
    [Fact]
    public void The_conversion_the_projection_now_reports_is_the_one_it_performs_and_Map_agrees()
    {
        const string types =
            "public class Src { public long Lossy { get; set; } public int Widening { get; set; } } "
            + "public class Dst { public double Lossy { get; set; } public long Widening { get; set; } }";
        var (assembly, errors) = GeneratorTestHarness.EmitAssembly(BothEndpoints(types, false));
        Assert.True(assembly is not null,
            "did not emit: " + string.Join(", ",
                errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

        var srcType = assembly!.GetType("Demo.Src")!;
        var mapperType = assembly.GetType("Demo.M")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        // Two longs ONE APART that share a double: the precision loss DWARF038 warns about, made concrete.
        var rows = new[] { long.MaxValue, long.MaxValue - 1 };
        var listType = typeof(List<>).MakeGenericType(srcType);
        var list = (IList)Activator.CreateInstance(listType)!;
        var mapped = new List<(double Lossy, long Widening)>();
        foreach (var v in rows)
        {
            var s = Activator.CreateInstance(srcType)!;
            srcType.GetProperty("Lossy")!.SetValue(s, v);
            srcType.GetProperty("Widening")!.SetValue(s, 7);
            list.Add(s);
            var d = mapperType.GetMethod("Map")!.Invoke(mapper, [s])!;
            mapped.Add(((double)d.GetType().GetProperty("Lossy")!.GetValue(d)!,
                (long)d.GetType().GetProperty("Widening")!.GetValue(d)!));
        }

        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(srcType)
            .Invoke(null, [list])!;
        // Materialised FIRST, then read: the Select below must run in memory, because a tuple literal is not
        // legal inside an expression tree — and pushing it into the query would also stop testing the
        // generator's tree and start testing ours.
        var rowsOut = ((IQueryable)mapperType.GetMethod("Project")!.Invoke(mapper, [queryable])!)
            .Cast<object>()
            .ToList();
        var projected = rowsOut
            .Select(d => (Lossy: (double)d.GetType().GetProperty("Lossy")!.GetValue(d)!,
                Widening: (long)d.GetType().GetProperty("Widening")!.GetValue(d)!))
            .ToList();

        Assert.Equal(2, projected.Count);
        Assert.Equal(mapped[0], projected[0]);
        Assert.Equal(mapped[1], projected[1]);
        Assert.Equal(7L, projected[0].Widening); // the widening control, untouched
        Assert.Equal((double)long.MaxValue, projected[0].Lossy);

        // The loss itself: two distinct sources, one destination value, at BOTH endpoints. This is what the
        // consumer is being warned about, and why ImplicitConversions = false exists to stop it.
        Assert.Equal(mapped[0].Lossy, mapped[1].Lossy);
        Assert.Equal(projected[0].Lossy, projected[1].Lossy);
    }
}
