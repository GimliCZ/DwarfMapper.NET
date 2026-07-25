// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.CodeFixes;

/// <summary>
///     ISSUE-028 — the contract between a diagnostic and the code fix that acts on it.
///     <para>
///     Both member-oriented fixes used to recover the member name by parsing the text between the first pair of
///     quotes in the human-readable message. That couples a fix to the exact wording of a display string:
///     rewording a message — or localising it, which <c>GetMessage(CultureInfo)</c> invites — silently breaks
///     the fix. No compile error, no failing test; the lightbulb just stops appearing. The member now travels in
///     the diagnostic's PROPERTY bag, which is what that bag is for.
///     </para>
///     These pin the property, so the messages stay free to change.
/// </summary>
public class DiagnosticPropertyContractTests
{
    private const string UnmappedDestination = """
                                               using DwarfMapper;
                                               namespace Demo;
                                               public class A { public int X { get; set; } }
                                               public class B { public int X { get; set; } public int Extra { get; set; } }
                                               [DwarfMapper] public partial class M { public partial B Map(A a); }
                                               """;

    private const string AutoMatchDisabled = """
                                             using DwarfMapper;
                                             namespace Demo;
                                             public class A { public int X { get; set; } }
                                             public class B { public int X { get; set; } }
                                             [DwarfMapper(AutoMatchMembers = false)]
                                             public partial class M { public partial B Map(A a); }
                                             """;

    [Fact]
    public void DWARF001_carries_the_member_name_as_a_property()
    {
        var d = Assert.Single(GeneratorAssert.Reports(UnmappedDestination, "DWARF001"));

        Assert.True(d.Properties.TryGetValue("Member", out var member),
            "DWARF001 carries no 'Member' property — AddMapIgnoreCodeFixProvider would fall back to nothing "
            + "and silently stop offering the fix.");
        Assert.Equal("Extra", member);
    }

    [Fact]
    public void DWARF072_carries_the_member_name_as_a_property()
    {
        var reported = GeneratorAssert.Reports(AutoMatchDisabled, "DWARF072");
        var d = reported[0];

        Assert.True(d.Properties.TryGetValue("Member", out var member),
            "DWARF072 carries no 'Member' property — ResolveExplicitOnlyMemberCodeFixProvider would stop "
            + "offering its fix.");
        Assert.Equal("X", member);
    }

    [Fact]
    public void The_member_property_does_not_depend_on_the_message_wording()
    {
        // The point of the change: the property must be recoverable WITHOUT reading the message at all.
        var d = Assert.Single(GeneratorAssert.Reports(UnmappedDestination, "DWARF001"));

        var member = d.Properties["Member"];
        Assert.False(string.IsNullOrEmpty(member));

        // Sanity: the message happens to mention it today, but nothing may depend on that continuing.
        Assert.NotNull(d.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }
}
