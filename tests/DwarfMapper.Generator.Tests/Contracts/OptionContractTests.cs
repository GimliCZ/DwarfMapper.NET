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

        new("RegisterCollectionShapes", CellStatus.NotApplicable, null,
            "governs what goes into the AMBIENT REGISTRY, not how any endpoint maps. A projection emits an "
            + "expression tree for a query provider, not a runtime Func<object, object> the registry can "
            + "hold, so a projection contributes no registration rows for this option to add or withhold. "
            + "NotApplicable rather than Refused: nothing is rejected, there is simply nothing here to "
            + "configure"),

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

        new("EnumStringSource", CellStatus.Refused, "DWARF028",
            "measured, not assumed: enum<->string mapping is a generated switch, which projection refuses "
            + "outright with DWARF028 before any string-source policy is consulted. The member is rejected, "
            + "so the option cannot be silently dropped here — the failure mode this matrix exists for"),

        new("NullCollections", CellStatus.NotApplicable, null,
            "collection rebuilds are untranslatable outright, so the null policy for them is unreachable"),

        new("OnCycle", CellStatus.NotApplicable, null,
            "cycles require reference tracking, which is refused at this endpoint for the same reason"),

        new("MaxDepth", CellStatus.NotApplicable, null,
            "projection depth is bounded by what the provider can translate, not by this budget")
    ];

    /// <summary>
    ///     Driven by the SCANNED catalogue, not by the declaration list: an option added to the attribute
    ///     becomes a failing cell here immediately, instead of waiting for someone to notice the list.
    ///     <para>
    ///         Keyed on option AND value. The declared contract below is per option — projection either
    ///         translates that concept or it does not — but the contract is CHECKED once per value, so a
    ///         three-member enum whose third member projects differently from its second cannot hide behind
    ///         a row written for the first. Keying on the name alone would also make the
    ///         <c>Single</c> below throw the day any option domain grows past one value.
    ///     </para>
    /// </summary>
    public static TheoryData<string, string> OptionValues()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in OptionCatalog.Options) data.Add(c.Name, c.ValueLabel);
        return data;
    }

    [Theory]
    [MemberData(nameof(OptionValues))]
    public void Projection_honours_or_refuses_each_option_as_declared(string option, string value)
    {
        var scanned = OptionCatalog.Options.Single(
            c => string.Equals(c.Name, option, StringComparison.Ordinal)
                 && string.Equals(c.ValueLabel, value, StringComparison.Ordinal));
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

    [Fact]
    public void Every_non_default_member_of_every_option_enum_is_probed_by_the_matrix()
    {
        // The catalogue used to build an enum's probe with FirstOrDefault(v => !v.Equals(def)): complete for a
        // two-member enum, and silently dropping members two and three on anything larger. A partially-probed
        // enum reads in the matrix exactly like a fully-covered one, so the omission would be invisible at the
        // moment it was introduced — the first non-default member would be probed at every endpoint and the
        // rest would ship with no cell at all.
        //
        // The DEFAULT member is deliberately not demanded. A probe that assigns the value the option already
        // has is byte-identical to the baseline everywhere, so it can only read Silent whatever the generator
        // does; [DwarfMapper]'s own zero-argument case carries an Unmeasured declaration for that same reason.
        var missing = new List<string>();

        foreach (var p in typeof(DwarfMapperAttribute)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite && p.CanRead && p.GetIndexParameters().Length == 0))
        {
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!t.IsEnum) continue;

            var def = p.GetValue(new DwarfMapperAttribute());
            var probed = OptionCatalog.Options
                .Where(o => string.Equals(o.Name, p.Name, StringComparison.Ordinal))
                .Select(o => o.ValueLabel)
                .ToList();

            foreach (var member in Enum.GetNames(t))
            {
                if (string.Equals(member, def?.ToString(), StringComparison.Ordinal)) continue;
                if (!probed.Contains($"{t.Name}.{member}", StringComparer.Ordinal))
                    missing.Add($"{p.Name}.{member}");
            }
        }

        Assert.True(missing.Count == 0,
            "Enum option member(s) never probed by any matrix cell: " + string.Join(", ", missing)
            + ". Every member is a distinct behaviour; probing one alternative and calling the option covered "
            + "is how a member ships with no test at all.");
    }

    /// <summary>A three-member enum, which no shipped option has — see the test below for why that matters.</summary>
    private enum ThreeWay
    {
        First,
        Second,
        Third
    }

    private sealed class ThreeWayHolder
    {
        public ThreeWay Mode { get; set; } = ThreeWay.First;
    }

    [Fact]
    public void The_derived_value_domain_of_a_three_member_enum_holds_both_non_default_members()
    {
        // Non-vacuity proof for the test above, and the only place the full-domain rule is actually exercised.
        // Every option enum shipped today has exactly two members, so FirstOrDefault happened to be COMPLETE
        // and the completeness test passes against the defect it was written to catch. That makes the
        // completeness test a guard against a future edit, not a demonstration of one — which is worth having
        // and worth being honest about. This asserts the derivation itself on a domain large enough to tell
        // "picks one alternative" apart from "enumerates the domain", so the rule is proven on data the
        // library does not yet contain.
        // The default is read off a fresh instance, exactly as OptionCatalog.Build() reads the attribute's.
        var property = typeof(ThreeWayHolder).GetProperty(nameof(ThreeWayHolder.Mode))!;
        var domain = OptionCatalog.ValueDomain(property, property.GetValue(new ThreeWayHolder()));

        Assert.Equal<string>(["ThreeWay.Second", "ThreeWay.Third"], domain);
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics)
    {
        var list = diagnostics
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .ToList();
        return list.Count == 0 ? "(nothing)" : string.Join(" | ", list);
    }
}
