// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     The class-level <c>[DwarfMapper(...)]</c> OPTIONS crossed with the projection endpoint.
///     <para>
///         The attribute matrix covers attributes and the <c>[MapProperty]</c> modifiers. It never covered the
///         sixteen class-level options — and that is where every silent projection divergence this project has
///         shipped actually lived: <c>SkipNullSourceMembers</c>, <c>AllowNonPublic</c>, <c>NameConvention</c>,
///         and the nullable-to-non-nullable decision were each honoured by the runtime resolver and quietly
///         ignored by the projection one. The option was tested, the endpoint was tested, the CELL was not.
///     </para>
///     <para>
///         The load-bearing rule is that <see cref="CellStatus.Honoured" /> must produce OBSERVABLY DIFFERENT
///         output from the same source compiled without the option. A silent drop cannot satisfy that, which
///         is the whole reason this file exists — asserting "compiles clean" would have passed against all
///         four defects.
///     </para>
/// </summary>
/// <summary>
///     One option's declared contract at the projection endpoint. <paramref name="Types" /> substitutes the
///     DTO pair when an option only becomes observable against a shape that triggers it — an enum for
///     <c>EnumStrategy</c>, a nested class for <c>AutoNest</c>. Declaring an option without a triggering
///     shape would make "no difference" indistinguishable from "silently dropped".
/// </summary>
public sealed record OptionCell(
    string Option,
    CellStatus Status,
    string? DiagnosticId,
    string Reason);

public class OptionContractTests
{

    /// <summary>
    ///     Every class-level option, with what projection is expected to do about it. A cell declared
    ///     <c>Refused</c> that reports nothing, or <c>Honoured</c> that changes nothing, is a silent divergence
    ///     — the test does not care which, because both are the same bug from the caller's side.
    /// </summary>
    public static readonly OptionCell[] ProjectionCells =
    [
        new("SkipNullSourceMembers", CellStatus.Refused, "DWARF028",
            "merge-shaped semantics: a projection CREATES a row, so 'leave the target alone' has no meaning"),

        new("AllowNonPublic", CellStatus.Refused, "DWARF028",
            "an expression tree the provider translates cannot read a non-public member"),

        new("NullStrategy", CellStatus.NotApplicable, null,
            "int?->int is refused with DWARF028 structurally, with or without this option, so the option is "
            + "never consulted. Recorded as NotApplicable rather than Refused because an earlier Refused "
            + "declaration passed for the WRONG reason: it asserted DWARF028 was present, and the baseline "
            + "emits it too. The endpoint-parity classifier caught that by only counting diagnostics the "
            + "option itself introduces"),

        new("AutoNest", CellStatus.Refused, "DWARF005",
            "explicit-nesting mode still refuses an unmapped nested member, as it does everywhere else"),

        new("AutoMatchMembers", CellStatus.Refused, "DWARF072",
            "the mass-assignment guard is a trust boundary and must not weaken at the projection endpoint"),

        new("NameConvention", CellStatus.Honoured, null,
            "name resolution happens before translatability, so it applies identically here"),

        new("CaseInsensitive", CellStatus.Honoured, null,
            "name resolution happens before translatability, so it applies identically here"),

        new("IgnoreObsoleteMembers", CellStatus.Honoured, null,
            "member filtering is a resolution concern and precedes translatability"),

        new("ReferenceHandling", CellStatus.Refused, "DWARF028",
            "identity preservation needs a runtime dictionary, which an expression tree cannot carry"),

        new("RequiredMapping", CellStatus.Refused, "DWARF039",
            "source-side completeness: an unconsumed source member is reported here exactly as it is at the "
            + "create and update endpoints"),

        // ── Declared NotApplicable ────────────────────────────────────────────────────────────────────────
        // The UNVERIFIED class. Each states why no fixture distinguishes honoured from dropped, because
        // "I could not think of a fixture" and "there is nothing to observe" must not look alike. These are
        // the rows the generated matrix renders as "not probed" — it does not claim they are fine.
        new("ImplicitConversions", CellStatus.NotApplicable, null,
            "verified against both endpoints rather than assumed: for a WIDENING pair (int->long) neither "
            + "endpoint reacts to the option at all, and for a NARROWING pair projection refuses the member "
            + "with DWARF028 before any conversion policy is consulted (CreateMap reports DWARF038). There is "
            + "no fixture in which projection observes this option, so there is nothing to diverge"),

        new("GenerateExtensions", CellStatus.NotApplicable, null,
            "projection emits no convenience extension in the first place (verified: no static class appears "
            + "in the output with or without the option), so there is nothing for it to suppress"),

        new("EnumStrategy", CellStatus.NotApplicable, null,
            "a cross-enum conversion is itself untranslatable, so the STRATEGY never gets to matter — the "
            + "member is refused before the strategy is consulted"),

        new("NullCollections", CellStatus.NotApplicable, null,
            "collection rebuilds are untranslatable outright, so the null policy for them is unreachable"),

        new("OnCycle", CellStatus.NotApplicable, null,
            "cycles require reference tracking, which is refused at this endpoint for the same reason"),

        new("MaxDepth", CellStatus.NotApplicable, null,
            "projection depth is bounded by what the provider can translate, not by this budget")
    ];

    public static TheoryData<string> OptionNames()
    {
        // Driven by the SCANNED catalogue, not by the declaration list: an option added to the attribute
        // becomes a failing cell here immediately, instead of waiting for someone to notice the list.
        var data = new TheoryData<string>();
        foreach (var c in OptionCatalog.Options) data.Add(c.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Projection_honours_or_refuses_each_option_as_declared(string option)
    {
        var scanned = OptionCatalog.Options.Single(c => string.Equals(c.Name, option, StringComparison.Ordinal));
        var cell = ProjectionCells.SingleOrDefault(
            c => string.Equals(c.Option, option, StringComparison.Ordinal));

        Assert.True(cell is not null,
            $"[DwarfMapper] option '{option}' exists on the attribute but has no declared projection "
            + "contract. Add a ProjectionCells row saying whether projection honours it, refuses it, or "
            + "cannot observe it.");

        // Probe value comes from the catalogue so the doc and the contract test cannot disagree about what
        // was actually set.
        var withOption = EndpointSources.Build(
            Endpoint.Projection, options: scanned.NonDefault, types: scanned.Types);
        var withoutOption = EndpointSources.Build(Endpoint.Projection, types: scanned.Types);

        var (diagnostics, generated) = GeneratorTestHarness.Run(withOption);
        var (_, baseline) = GeneratorTestHarness.Run(withoutOption);

        switch (cell.Status)
        {
            case CellStatus.Refused:
                Assert.True(
                    diagnostics.Any(d => string.Equals(d.Id, cell.DiagnosticId, StringComparison.Ordinal)),
                    $"[DwarfMapper({scanned.NonDefault})] on a Project method must report {cell.DiagnosticId} "
                    + $"({cell.Reason}), but reported: {Describe(diagnostics)}. A projection that accepts the "
                    + "option and quietly ignores it produces silently wrong data — this is the exact shape of "
                    + "the four divergences this matrix was built for.");
                break;

            case CellStatus.Honoured:
                Assert.True(
                    !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
                    $"[DwarfMapper({scanned.NonDefault})] is declared Honoured at projection but errored: "
                    + Describe(diagnostics));

                // The assertion that a silent drop cannot survive. "Compiles clean" would pass against every
                // defect this file exists to catch, because a dropped option compiles perfectly well.
                Assert.True(
                    !string.Equals(generated, baseline, StringComparison.Ordinal),
                    $"[DwarfMapper({scanned.NonDefault})] is declared Honoured at projection, but the generated "
                    + "output is byte-identical to the same source without it. Either the option is silently "
                    + $"dropped ({cell.Reason} says it should not be), or the fixture does not trigger it — "
                    + "and those must not be indistinguishable.");
                break;

            case CellStatus.NotApplicable:
                Assert.False(string.IsNullOrWhiteSpace(cell.Reason),
                    $"{cell.Option} is declared NotApplicable without saying why it cannot be observed.");
                break;

            default:
                throw new InvalidOperationException($"Unhandled status {cell.Status}");
        }
    }

    [Fact]
    public void Every_class_level_option_has_a_declared_projection_contract()
    {
        // The growth ratchet. Option 17 must not reach a release having been considered only at the runtime
        // endpoint — that omission is precisely how the four divergences shipped.
        var declared = ProjectionCells.Select(c => c.Option).ToHashSet(StringComparer.Ordinal);

        var actual = typeof(DwarfMapperAttribute)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .ToList();

        var missing = actual.Where(n => !declared.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "Class-level [DwarfMapper] option(s) with no declared projection contract:\n"
            + string.Join("\n", missing)
            + "\n\nAdd a row to ProjectionCells saying whether projection honours it, refuses it (with the "
            + "diagnostic id), or genuinely cannot observe it (with the reason).");

        var stale = declared.Where(n => !actual.Contains(n, StringComparer.Ordinal)).ToList();
        Assert.True(stale.Count == 0,
            "ProjectionCells declares option(s) that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void The_option_slot_actually_reaches_the_generated_source()
    {
        // Guards the harness rather than the generator: if Build() dropped the options argument, every Refused
        // cell would fail loudly but every Honoured and NotApplicable cell would pass for the wrong reason.
        var built = EndpointSources.Build(Endpoint.Projection, options: "CaseInsensitive = true");
        Assert.Contains("[DwarfMapper(CaseInsensitive = true)]", built, StringComparison.Ordinal);
        Assert.DoesNotContain("[DwarfMapper]\n[DwarfMapper", built, StringComparison.Ordinal);
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics)
    {
        var list = diagnostics
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();
        return list.Count == 0 ? "(nothing)" : string.Join(" | ", list);
    }
}
