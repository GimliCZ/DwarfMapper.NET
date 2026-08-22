// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The MEMBER placement of <c>[MapProperty]</c> / <c>[MapIgnore]</c> at the co-located
///     <c>[GenerateMap&lt;S,T&gt;]</c> host — the mirror of
///     <see cref="MemberFormDirectiveTests" />, where the same forms are refused because a mapper class has no
///     annotated member to speak about.
///     <para>
///         Here it does: the mapping is declared BY the annotated type, so a member of the host is part of
///         the declaration. The host is the DESTINATION, so the one argument names the SOURCE member the
///         annotated member is filled from — the mirror of the <c>[MapTo]</c> registry, where the annotated
///         type is the source and the argument names the destination. Both placements go through one parser
///         (<c>MemberDirectives</c>), because two readers of one attribute form drift and the drift is
///         invisible until something measures both.
///     </para>
///     <para>
///         Every case here was measured SILENT by the surface matrix before this existed — twenty cells,
///         finding <c>D20</c>: the directive compiled, changed nothing, and said nothing.
///     </para>
/// </summary>
public sealed class CoLocatedHostMemberDirectiveTests
{
    private const string Src = """
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Person { public string Full { get; set; } = ""; public int Age { get; set; } }
                               """;

    /// <summary>The member form binds the annotated DESTINATION member to the source member it names.</summary>
    [Fact]
    public void Member_form_MapProperty_on_a_host_member_renames()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     [MapProperty("Full")] public string Name { get; set; } = "";
                                     public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Name = src.Full", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A FIELD, not a property. The two sites shared one arm of the matrix's fixture builder until gap G6
    ///     was closed, so every Field cell was a Property measurement wearing a Field label — pinned here so
    ///     the two placements cannot quietly become one again.
    /// </summary>
    [Fact]
    public void Member_form_MapProperty_on_a_host_FIELD_renames()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     [MapProperty("Full")] public string Name = "";
                                     public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Name = src.Full", generated, StringComparison.Ordinal);
    }

    /// <summary>The bare form excludes the annotated member, so the completeness gate stops demanding it.</summary>
    [Fact]
    public void Member_form_MapIgnore_on_a_host_member_excludes_it()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     public string Full { get; set; } = "";
                                     [MapIgnore] public string Extra { get; set; } = "";
                                     public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
        Assert.DoesNotContain("Extra =", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The named arguments ride on that same one-argument constructor, so a reader that takes the binding
    ///     and drops them re-creates <c>D20</c> one field at a time. <c>StringFormat</c> is the one that had
    ///     no plumbing at all on this path.
    /// </summary>
    [Fact]
    public void Member_form_MapProperty_carries_its_named_arguments()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     public string Full { get; set; } = "";
                                     [MapProperty("Age", StringFormat = "D4")] public string Age { get; set; } = "";
                                 }
                                 """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("\"D4\"", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The two-name METHOD form on a host member. Refused rather than read: at the host the destination
    ///     IS the annotated member, so a second name is either a contradiction or the caller reaching for the
    ///     mapping-method overload — and the registry refuses the identical mistake as <c>DWARFR04</c>.
    /// </summary>
    [Fact]
    public void Method_form_MapProperty_on_a_host_member_reports_DWARF089()
    {
        // `Name` exists on BOTH sides so auto-matching can still complete the pair once the directive is
        // dropped — otherwise the drop would show up only as a DWARF001 cascade and the assertion below
        // could not tell "dropped" from "applied and then failed".
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public sealed class Person { public string Full { get; set; } = ""; public string Name { get; set; } = ""; }

                           [GenerateMap<Person, PersonDto>]
                           public sealed class PersonDto
                           {
                               [MapProperty("Full", "Name")] public string Name { get; set; } = "";
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF089");

        // Dropped, not half-applied: the mapping is the one the host would have had with no directive.
        Assert.Contains("Name = src.Name", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The one-name METHOD/CLASS form of <c>[MapIgnore]</c> on a host member. It names a destination to
    ///     exclude, which on a member is a second answer to a question the placement already answered.
    /// </summary>
    [Fact]
    public void Method_form_MapIgnore_on_a_host_member_reports_DWARF089()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     public string Full { get; set; } = "";
                                     [MapIgnore("Age")] public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF089");
    }

    /// <summary>
    ///     Stacked directives bind POSITIONALLY, one per host-targeted pair in source order — the same rule
    ///     the registry applies to its <c>[MapTo]</c> targets, which is why the one parser can serve both.
    /// </summary>
    [Fact]
    public void Stacked_directives_bind_one_per_declared_pair()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public sealed class A { public string One { get; set; } = ""; }
                           public sealed class B { public string Two { get; set; } = ""; }

                           [GenerateMap<A, Dto>]
                           [GenerateMap<B, Dto>]
                           public sealed class Dto
                           {
                               [MapProperty("One")]
                               [MapProperty("Two")]
                               public string Value { get; set; } = "";
                           }
                           """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF089");
        Assert.Contains("Value = src.One", generated, StringComparison.Ordinal);
        Assert.Contains("Value = src.Two", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Two directives and one pair. Neither reading is safe — the second could be a copy-paste or the one
    ///     the caller meant — so it is refused rather than collapsed, exactly as <c>DWARF011</c> and
    ///     <c>DWARF087</c> refuse their duplicates and as <c>DWARFR04</c> refuses this at the registry.
    /// </summary>
    [Fact]
    public void A_directive_count_that_does_not_match_the_pairs_reports_DWARF089()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     [MapIgnore]
                                     [MapIgnore]
                                     public string Full { get; set; } = "";
                                     public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF089");
    }

    /// <summary>
    ///     A host that declares a pair between two OTHER types. Its members are part of no mapping, so a
    ///     member directive there configures nothing — the case the "annotated type declares the mapping"
    ///     reasoning does not cover, and therefore the one it must not silently accept.
    /// </summary>
    [Fact]
    public void A_host_that_is_not_the_destination_reports_DWARF089()
    {
        const string src = Src + """

                                 public sealed class PersonDto { public string Full { get; set; } = ""; public int Age { get; set; } }

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class Registrations
                                 {
                                     [MapIgnore] public string Unrelated { get; set; } = "";
                                 }
                                 """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF089");
    }

    /// <summary>
    ///     THE BOUNDARY. <c>[GenerateMap]</c> on a <c>[DwarfMapper]</c> class is the other mode: the DTO pair
    ///     is two ordinary types the consumer may not own, and the mapper class's own members are converters
    ///     and hooks, not destination members. Nothing here may be read as a directive — which is what makes
    ///     it safe for the co-located path to read them at all.
    ///     <para>
    ///         <b>INVERTED 2026-08-23 (B18), and the old remarks asked for exactly this.</b> They said the
    ///         absence of any other diagnostic was NOT a claim: a member-form directive on a member of a
    ///         <c>[DwarfMapper]</c> class was reported by nothing at all, because <c>DWARF088</c> was raised
    ///         off the class and method symbols and never off a member, and the shape was recorded as
    ///         <b>B18</b> rather than fixed then. It is fixed now, so the silence this test declined to bless
    ///         is gone and the assertion is the other way round: <c>DWARF088</c> is REPORTED, and because it
    ///         is an Error the class is refused rather than generated.
    ///     </para>
    ///     <para>
    ///         The boundary itself is unchanged and is still what this test is for: the co-located reader
    ///         must not reach into mode 1, so the id is <c>DWARF088</c> (a directive the mapper does not read)
    ///         and NOT <c>DWARF089</c> (a host directive that cannot be placed). Both are asserted.
    ///     </para>
    /// </summary>
    [Fact]
    public void A_DwarfMapper_class_declaring_a_pair_refuses_a_directive_on_its_own_member()
    {
        const string src = Src + """

                                 public sealed class PersonDto { public string Full { get; set; } = ""; public int Age { get; set; } }

                                 [DwarfMapper]
                                 [GenerateMap<Person, PersonDto>]
                                 public partial class Mappers
                                 {
                                     [MapIgnore] public string Scratch { get; set; } = "";
                                 }
                                 """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);

        // The co-located reader did not reach into mode 1 — that is still the boundary this test pins.
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF089");

        // B18: the placement is refused instead of swallowed, and the message says WHY it is inert here.
        var d088 = diagnostics.Where(d => d.Id == "DWARF088").ToList();
        Assert.True(d088.Count == 1,
            $"expected one DWARF088 for [MapIgnore] on the mapper's own member; got {d088.Count}. "
            + "B18: this placement used to be reported by nothing at all.");
        Assert.Contains("Mappers.Scratch", d088[0].GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Error, d088[0].Severity);

        // DWARF088 is an Error, so the class is refused — the [GenerateMap] pair is not emitted either.
        Assert.DoesNotContain("Full = src.Full", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The other side of B18's boundary, and the reason the check is skipped on the separate-emit path:
    ///     on a real <c>[GenerateMap]</c> host the annotated type IS a mapped type, so its members ARE read
    ///     (A4) and reporting them would name a working feature as a mistake. Asserted here rather than
    ///     assumed, because "skipped on that path" and "never reached on that path" look identical until one
    ///     of them is wrong.
    /// </summary>
    [Fact]
    public void A_directive_on_a_real_GenerateMap_host_member_is_read_and_not_reported()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class HostSrc { public int A { get; set; } public int B { get; set; } }
            [GenerateMap<HostSrc, HostDst>]
            public partial class HostDst
            {
                [MapIgnore] public int B { get; set; }
                public int A { get; set; }
            }
            """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF088");
        Assert.NotEmpty(generated);
    }

    /// <summary>
    ///     B18's second shape, which the row named only in passing and which is the same silence: the
    ///     CLASS/METHOD-placement overload written on a mapper member. It is the form the mapper really does
    ///     read — from the class or from a mapping method — and on a member it was inert too, with the
    ///     completeness gate then demanding the member the caller believed they had excluded. The remedy in
    ///     the message differs accordingly: move it, do not rewrite it.
    /// </summary>
    [Fact]
    public void The_class_form_written_on_a_mapper_member_is_refused_with_a_move_it_remedy()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class MSrc { public int A { get; set; } }
            public class MDst { public int A { get; set; } public int B { get; set; } }
            [DwarfMapper]
            public partial class MoveMe
            {
                [MapIgnore("B")] public string Scratch { get; set; } = "";
                public partial MDst Map(MSrc s);
            }
            """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);

        var d088 = diagnostics.Single(d => d.Id == "DWARF088");
        var message = d088.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("MoveMe.Scratch", message, StringComparison.Ordinal);
        Assert.Contains("Move it to either", message, StringComparison.Ordinal);

        // And the proof that it really was inert: B is still unmapped, so completeness still complains.
        Assert.Contains(diagnostics, d => d.Id == "DWARF001");
    }

    /// <summary>
    ///     <c>[MapProperty(null)]</c> is a legal application — the argument is a string parameter and
    ///     <c>null</c> is a constant, so the compiler emits at most <c>CS8625</c>. The arity is one, so an
    ///     arity-only check accepts it, and the name it hands downstream is <c>null</c>. Refused as
    ///     unplaceable instead, because the alternative is a NAME that does not exist reaching member
    ///     resolution: nothing downstream takes a null source name, and before A4 this shape was ignored
    ///     entirely — a generator that fails on input it used to ignore is worse than the gap A4 closed.
    /// </summary>
    [Fact]
    public void A_null_name_on_a_host_member_reports_DWARF089_rather_than_reaching_resolution()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     [MapProperty(null)] public string Full { get; set; } = "";
                                     public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF089");
        Assert.DoesNotContain(diagnostics,
            d => d.Id.StartsWith("CS", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    ///     The same guard reached the other way: an argument the compiler could not bind to <c>string</c>
    ///     leaves an ERROR constant in the attribute application, which is what a half-typed directive looks
    ///     like to a generator running in the IDE on every keystroke. The build is already failing; the
    ///     generator must not add a crash to it.
    /// </summary>
    [Fact]
    public void A_non_string_name_on_a_host_member_does_not_crash_the_generator()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     [MapProperty(42)] public string Full { get; set; } = "";
                                     public int Age { get; set; }
                                 }
                                 """;
        var (diagnostics, _) = GeneratorTestHarness.Run(src);

        // CS8785 is "the generator failed to generate source": the crash this guards against, reported
        // against a file the consumer never wrote.
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
    }

    /// <summary>
    ///     A Warning — unlike <c>DWARF088</c>, which is an Error — for a reason of this id's own: a blocking
    ///     error suppresses the host's emission
    ///     entirely, so the generated <c>&lt;Host&gt;Mapper</c>, its convenience extension and its DI
    ///     registration all vanish and every call site meets <c>CS1061</c> instead of the refusal.
    /// </summary>
    [Fact]
    public void The_refusal_is_a_warning_so_the_host_mapper_still_emits()
    {
        const string src = Src + """

                                 [GenerateMap<Person, PersonDto>]
                                 public sealed class PersonDto
                                 {
                                     [MapIgnore("Age")] public int Age { get; set; }
                                     public string Full { get; set; } = "";
                                 }
                                 """;
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.Contains(diagnostics, d => d.Id == "DWARF089" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains("class PersonDtoMapper", generated, StringComparison.Ordinal);
    }
}
