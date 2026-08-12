// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>DWARF083</c> — an enum whose string form is not its identifier.
/// </summary>
/// <remarks>
///     <para>
///         For enum↔string, a member's <c>[EnumMember(Value=…)]</c> wins, then <c>[Description(…)]</c>, then
///         the identifier. That precedence is deliberate and good: it lets <c>InProgress</c> serialize as
///         <c>"in_progress"</c> with no custom converter.
///     </para>
///     <para>
///         The hazard is that <c>[Description]</c> is overwhelmingly a <b>display</b> annotation — people put
///         it on enums for combo-box labels — and here it silently becomes the <b>persistence</b> format.
///         Round 18 came within one code review of shipping exactly that: <c>DonationSource.Kofi</c> carried
///         <c>[Description("Ko-Fi")]</c>, and the migration would have begun writing <c>"Ko-Fi"</c> into a
///         MongoDB collection full of <c>"Kofi"</c>, breaking reads of every existing document. The previous
///         mapper used <c>.ToString()</c>, i.e. always the identifier.
///     </para>
/// </remarks>
public class EnumStringNameDivergesTests
{
    private const string Id = "DWARF083";

    private const string KofiSource = """
        using System.ComponentModel;
        using DwarfMapper;
        namespace Demo;

        public enum DonationSource
        {
            [Description("Ko-Fi")] Kofi,
            Patreon
        }

        public class Src { public DonationSource Source { get; set; } }
        public class Dst { public string Source { get; set; } = ""; }

        [DwarfMapper]
        [GenerateMap<Src, Dst>]
        public partial class M { }
        """;

    [Fact]
    public void Reports_when_a_Description_redirects_the_persisted_string()
    {
        Assert.NotEmpty(GeneratorAssert.Reports(KofiSource, Id));
    }

    [Fact]
    public void The_message_shows_the_actual_value_that_will_be_written()
    {
        var message = GeneratorAssert.Reports(KofiSource, Id)[0].GetMessage(CultureInfo.InvariantCulture);

        // Naming the enum is not enough — the reader has to SEE that "Kofi" becomes "Ko-Fi", because that is
        // the fact that makes it a data problem rather than a style note.
        Assert.Contains("DonationSource", message, StringComparison.Ordinal);
        Assert.Contains("Kofi", message, StringComparison.Ordinal);
        Assert.Contains("Ko-Fi", message, StringComparison.Ordinal);
        Assert.Contains("display", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_for_the_string_to_enum_direction_too()
    {
        // Reading is as affected as writing: the generated parse switch matches on the serialized name, so a
        // store full of identifiers stops parsing.
        const string src = """
            using System.ComponentModel;
            using DwarfMapper;
            namespace Demo;

            public enum DonationSource
            {
                [Description("Ko-Fi")] Kofi,
                Patreon
            }

            public class Src { public string Source { get; set; } = ""; }
            public class Dst { public DonationSource Source { get; set; } }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        Assert.NotEmpty(GeneratorAssert.Reports(src, Id));
    }

    [Fact]
    public void Is_silent_when_every_member_serializes_as_its_identifier()
    {
        // The overwhelmingly common case. Firing here would make the id noise.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Level { Low, High }
            public class Src { public Level Level { get; set; } }
            public class Dst { public string Level { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        GeneratorAssert.CompilesClean(src);
        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Is_silent_for_a_Flags_enum()
    {
        // [Flags] keeps identifier semantics in both directions — its string form is a comma-joined list
        // Enum.ToString builds from identifiers — so there is nothing to diverge.
        const string src = """
            using System;
            using System.ComponentModel;
            using DwarfMapper;
            namespace Demo;

            [Flags]
            public enum Perm { [Description("R")] Read = 1, [Description("W")] Write = 2 }

            public class Src { public Perm Perm { get; set; } }
            public class Dst { public string Perm { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Reports_once_per_enum_not_once_per_member()
    {
        // An enum annotated for display usually annotates most of its members. One report per member is how a
        // useful diagnostic gets ignored.
        const string src = """
            using System.ComponentModel;
            using DwarfMapper;
            namespace Demo;

            public enum Status
            {
                [Description("a")] A,
                [Description("b")] B,
                [Description("c")] C,
                [Description("d")] D,
                [Description("e")] E
            }

            public class Src { public Status Status { get; set; } }
            public class Dst { public string Status { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        var reported = GeneratorAssert.Reports(src, Id);

        Assert.Single(reported);

        // …and it must still be useful: the first few are named, with a count so the reader knows the scale.
        var message = reported[0].GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("5 in total", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_Info_because_the_precedence_is_a_deliberate_feature()
    {
        // Serializing InProgress as "in_progress" is exactly what the precedence is FOR. This surfaces the
        // consequence; it does not forbid it, and it must not break a warnings-as-errors build.
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info,
            GeneratorAssert.Reports(KofiSource, Id)[0].Severity);
    }
}
