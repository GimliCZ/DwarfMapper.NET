// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <c>DWARF078</c> — the signpost for the <c>CS8795</c> cascade.
/// </summary>
/// <remarks>
///     <para>
///         When any diagnostic on a mapper class is an error the generator emits nothing for that class, so
///         every partial mapping method loses its implementing part at once and the build fills with
///         <c>CS8795</c>. That wall looks IDENTICAL to the other common cause — a project that never wired the
///         analyzer — and the two have opposite fixes.
///     </para>
///     <para>
///         Found the expensive way while migrating a ~300-map codebase: a debugging session was spent on
///         "the analyzer isn't wired" when the real signal was a <c>DWARF007</c>/<c>DWARF026</c> further up,
///         buried under its own cascade. See <c>Issues/Rount18/</c>.
///     </para>
/// </remarks>
public class NoCodeGeneratedTests
{
    private const string Id = "DWARF078";

    /// <summary>A read-only destination member, which is DWARF007 — a blocking error.</summary>
    private const string BlockingSource = """
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Value { get; set; } }
        public class Dst { public int Value { get; } }

        [DwarfMapper]
        public partial class M
        {
            public partial Dst Map(Src s);
        }
        """;

    [Fact]
    public void Reports_DWARF078_when_a_blocking_error_suppresses_emission()
    {
        Assert.NotEmpty(GeneratorAssert.Reports(BlockingSource, Id));
    }

    [Fact]
    public void The_signpost_names_the_mapper_and_the_real_diagnostic_ids()
    {
        var reported = GeneratorAssert.Reports(BlockingSource, Id);
        var message = reported[0].GetMessage(CultureInfo.InvariantCulture);

        // The whole value of this diagnostic is that it points AT the real cause. A message that merely says
        // "generation failed" would leave the reader exactly where they started.
        Assert.Contains("'M'", message, StringComparison.Ordinal);
        Assert.Contains("DWARF007", message, StringComparison.Ordinal);

        // And it must actively rule out the look-alike cause, because that is the wrong turn people take.
        Assert.Contains("CS8795", message, StringComparison.Ordinal);
        Assert.Contains("analyzer reference", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_once_per_class_not_once_per_affected_member()
    {
        // Three unmappable members on one class. A per-member signpost would itself become the wall it is
        // meant to explain.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; set; } public int B { get; set; } public int C { get; set; } }
            public class Dst { public int A { get; } public int B { get; } public int C { get; } }

            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """;

        Assert.Single(GeneratorAssert.Reports(src, Id));
    }

    [Fact]
    public void Is_silent_when_the_mapper_generates_successfully()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Value { get; set; } }
            public class Dst { public int Value { get; set; } }

            [DwarfMapper]
            public partial class M
            {
                public partial Dst Map(Src s);
            }
            """;

        GeneratorAssert.CompilesClean(src);
        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Is_silent_when_the_mapper_only_has_warnings()
    {
        // DWARF076 (same-type map) is a Warning, so emission proceeds and there is no cascade to explain.
        // Reporting here would train consumers to ignore the signpost.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Dto { public int Id { get; set; } }

            [DwarfMapper]
            [GenerateMap<Dto, Dto>]
            public partial class M { }
            """;

        Assert.NotEmpty(GeneratorAssert.Reports(src, "DWARF076"));
        GeneratorAssert.DoesNotReport(src, Id);
    }

    [Fact]
    public void Is_a_Warning_so_it_cannot_turn_a_passing_build_red_on_its_own()
    {
        // It only ever accompanies an existing error, so severity cannot change pass/fail — but Warning keeps
        // it visible in build output next to the errors, where Info would be filtered out of most views.
        var reported = GeneratorAssert.Reports(BlockingSource, Id);

        Assert.Equal(DiagnosticSeverity.Warning, reported[0].Severity);
    }
}
