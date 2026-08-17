// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>DWARF090</c> — the directives a span or async-stream map cannot apply, and the ones it must not
///     complain about.
/// </summary>
/// <remarks>
///     The interesting half is the negative. A class-level <c>[MapIgnore]</c> is class-WIDE and legitimately
///     matches nothing on some of the pairs the class declares, so reporting it against every element pair
///     would name a pair the caller never wrote about — the objection
///     <c>ReportElementWiseDirectiveGaps</c> already makes in its own comment about class-site
///     <c>[MapProperty]</c>, and which was not applied to <c>[MapIgnore]</c> until these tests existed.
/// </remarks>
public class ElementWiseDirectiveTests
{
    /// <summary>
    ///     The shape the Important in A7's review named: one class, a create map over one pair and a span map
    ///     over an UNRELATED pair. The class-level ignore is about the create map's target and names nothing
    ///     on the span element's — so there is nothing here to tell the caller, and the remedy the message
    ///     would print (<c>[MapIgnore&lt;Bar&gt;("Id")]</c>) would be about a type with no such member.
    /// </summary>
    [Fact]
    public void Class_level_MapIgnore_naming_no_member_of_the_element_pair_is_not_reported()
    {
        const string s = """
                         using System;
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Id { get; set; } public string Name { get; set; } }
                         public class Dst { public int Id { get; set; } public string Name { get; set; } }
                         public struct Foo { public double Weight { get; set; } }
                         public struct Bar { public double Weight { get; set; } }
                         [DwarfMapper]
                         [MapIgnore("Id")]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             public partial void MapSpan(ReadOnlySpan<Foo> src, Span<Bar> dst);
                         }
                         """;
        GeneratorAssert.DoesNotReport(s, "DWARF090");
    }

    /// <summary>
    ///     The other side of the same filter: where the class-level ignore DOES name a member of the element
    ///     pair's target, the caller has excluded it on three overloads and not on this one, and that is the
    ///     divergence <c>DWARF090</c> exists to state. Without this test the fix above could be "never report
    ///     the class site", which would silently re-open finding D1's class-site half.
    /// </summary>
    [Fact]
    public void Class_level_MapIgnore_naming_a_member_of_the_element_pair_is_reported()
    {
        const string s = """
                         using System;
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Id { get; set; } public string Name { get; set; } }
                         public class Dst { public int Id { get; set; } public string Name { get; set; } }
                         [DwarfMapper]
                         [MapIgnore("Id")]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             public partial void MapSpan(ReadOnlySpan<Src> src, Span<Dst> dst);
                         }
                         """;
        var reported = GeneratorAssert.Reports(s, "DWARF090");
        Assert.Contains("[MapIgnore<Dst>(\"Id\")]",
            reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     The METHOD site is not filtered the same way, deliberately. A directive written on the span method
    ///     itself is about that method and nothing else, so a name that matches no member of the element pair
    ///     is a mistake worth reporting rather than an unrelated pair's business — and the caller gets the
    ///     same message either way, which is the point.
    /// </summary>
    /// <summary>
    ///     A CASE-MISMATCHED class-level ignore must not be reported either — because there is no gap to
    ///     report. The ignore set real resolution matches against is <c>StringComparer.Ordinal</c>, so
    ///     <c>[MapIgnore("id")]</c> against a property <c>Id</c> is inert at the create map as well;
    ///     <c>DWARF090</c> would be telling the caller to switch to a pair-scoped form that does not work
    ///     either.
    /// </summary>
    /// <remarks>
    ///     The companion assertion is the load-bearing one: it establishes the premise ON THE CREATE MAP,
    ///     where the directive is supposedly honoured. Without it this test pins a comparer choice against
    ///     nothing, and the first person to make the ignore set case-insensitive would make it wrong while it
    ///     went on passing.
    /// </remarks>
    [Fact]
    public void A_case_mismatched_class_level_MapIgnore_is_inert_everywhere_so_it_is_not_reported()
    {
        const string s = """
                         using System;
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int Id { get; set; } public string Name { get; set; } }
                         public class Dst { public int Id { get; set; } public string Name { get; set; } }
                         [DwarfMapper]
                         [MapIgnore("id")]
                         public partial class M
                         {
                             public partial Dst Map(Src s);
                             public partial void MapSpan(ReadOnlySpan<Src> src, Span<Dst> dst);
                         }
                         """;

        // The premise: `id` excludes nothing from the CREATE map either. Id is still assigned there, so the
        // element-wise endpoints are not diverging from anything.
        var gen = GeneratorAssert.CompilesClean(s);
        Assert.Contains("Id = s.Id", gen, StringComparison.Ordinal);

        GeneratorAssert.DoesNotReport(s, "DWARF090");
    }

    [Fact]
    public void Method_level_MapIgnore_on_the_span_method_is_reported_even_when_it_names_nothing()
    {
        const string s = """
                         using System;
                         using DwarfMapper;
                         namespace Demo;
                         public struct Foo { public double Weight { get; set; } }
                         public struct Bar { public double Weight { get; set; } }
                         [DwarfMapper]
                         public partial class M
                         {
                             [MapIgnore("Nonexistent")]
                             public partial void MapSpan(ReadOnlySpan<Foo> src, Span<Bar> dst);
                         }
                         """;
        GeneratorAssert.Reports(s, "DWARF090");
    }
}
