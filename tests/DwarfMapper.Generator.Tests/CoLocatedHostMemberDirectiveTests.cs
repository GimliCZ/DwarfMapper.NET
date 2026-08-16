// SPDX-License-Identifier: GPL-2.0-only

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
    /// </summary>
    [Fact]
    public void A_DwarfMapper_class_declaring_a_pair_does_not_read_its_own_members()
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
        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF089");
        Assert.Contains("Full = src.Full", generated, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A Warning, for the reason <c>DWARF088</c> is one: a blocking error suppresses the host's emission
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
