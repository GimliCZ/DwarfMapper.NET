// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     Executes the <see cref="EndpointContractMatrix" /> and ratchets it against the real attribute surface.
///     <para>
///         The matrix is only worth its maintenance if it cannot fall behind the code, so the growth ratchet
///         is the load-bearing test here: a new public attribute, or a new endpoint, fails the build until it
///         has a declared expectation. Without that, this degrades into a snapshot of what was true the day it
///         was written — which is how the divergences it exists to catch arose in the first place.
///     </para>
/// </summary>
public class EndpointContractTests
{
    /// <summary>
    ///     Public attribute usage names, deduplicated exactly as <c>TestTheTestsScanTests</c> does for
    ///     <c>MatrixExemptAttributes</c>: strip the <c>Attribute</c> suffix and the generic arity, so
    ///     <c>MapDerivedTypeAttribute</c> and <c>MapDerivedTypeAttribute`2</c> collapse to one name.
    /// </summary>
    public static IReadOnlyList<string> UsageNames { get; } = typeof(DwarfMapperAttribute).Assembly
        .GetExportedTypes()
        .Where(t => typeof(Attribute).IsAssignableFrom(t) && !t.IsAbstract)
        .Select(t =>
        {
            var name = t.Name;
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0) name = name[..tick];
            return name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name[..^"Attribute".Length]
                : name;
        })
        .Distinct(StringComparer.Ordinal)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();

    public static IEnumerable<object[]> AllCells() =>
        from usage in UsageNames
        from endpoint in EndpointSources.All
        select new object[] { usage, endpoint };

    [Theory]
    [MemberData(nameof(AllCells))]
    public void Every_cell_has_a_declared_expectation(string usage, Endpoint endpoint)
    {
        var cell = EndpointContractMatrix.For(usage, endpoint);

        Assert.True(cell is not null,
            $"'{usage}' has no declared expectation at {endpoint}. A new public attribute must be classified "
            + "in EndpointContractMatrix (an explicit cell, or an entry in Uniform) before it can ship — "
            + "otherwise it could reach one endpoint and not another with nothing to notice.");

        Assert.Equal(usage, cell!.Usage);
        Assert.Equal(endpoint, cell.Endpoint);

        // The whole design: there is no "silent" status, and every non-Honoured cell must justify itself —
        // Refused with the id it reports, NotApplicable with a reason a reviewer can disagree with.
        switch (cell.Status)
        {
            case CellStatus.Refused:
                Assert.False(string.IsNullOrWhiteSpace(cell.DiagnosticId),
                    $"{usage} x {endpoint} is Refused but names no diagnostic — 'refused' without an error "
                    + "shape is indistinguishable from silently ignored, which is the defect class this "
                    + "matrix exists to make unrepresentable.");
                break;
            case CellStatus.NotApplicable:
                Assert.False(string.IsNullOrWhiteSpace(cell.Reason),
                    $"{usage} x {endpoint} is NotApplicable with no reason. An unexplained exemption is how a "
                    + "real gap hides.");
                break;
        }
    }

    [Fact]
    public void The_attribute_surface_has_not_grown_past_the_matrix()
    {
        // Growth ratchet. Reflection is the source of truth; the matrix must answer for every name it finds.
        var unanswered = UsageNames
            .Where(u => EndpointSources.All.Any(e => EndpointContractMatrix.For(u, e) is null))
            .ToList();

        Assert.True(unanswered.Count == 0,
            "Attribute(s) with no matrix answer: " + string.Join(", ", unanswered)
            + "\nAdd a row to EndpointContractMatrix (explicit cell, or an entry in Uniform).");

        Assert.True(UsageNames.Count >= 20,
            $"Only {UsageNames.Count} attribute usage names reflected — the matrix would be near-vacuous.");
    }

    [Fact]
    public void The_matrix_covers_every_endpoint()
    {
        // The other half of the ratchet: adding an Endpoint member without teaching EndpointSources how to
        // build it would silently drop a column.
        foreach (var endpoint in EndpointSources.All)
        {
            var built = EndpointSources.Build(endpoint);
            Assert.False(string.IsNullOrWhiteSpace(built),
                $"EndpointSources.Build has no template for {endpoint}.");
        }

        Assert.Equal(7, EndpointSources.All.Count);
    }

    [Theory]
    [MemberData(nameof(EndpointBaselines))]
    public void Each_endpoint_compiles_clean_with_no_attributes(Endpoint endpoint)
    {
        // Baseline control. If an endpoint template were malformed, every Refused cell measured against it
        // would "pass" for the wrong reason — the diagnostic would come from the broken scaffold rather than
        // from the attribute under test.
        GeneratorAssert.CompilesClean(EndpointSources.Build(endpoint));
    }

    public static IEnumerable<object[]> EndpointBaselines() =>
        EndpointSources.All.Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(ModifierCells))]
    public void MapProperty_modifiers_match_their_declared_contract(
        string modifier, string syntax, Endpoint endpoint, CellStatus status, string? diagnosticId)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(modifier);

        var attribute = $"[MapProperty(nameof(Src.Name), nameof(Dst.Name), {syntax})]";
        var helper = syntax.Contains("nameof(Conv)", StringComparison.Ordinal)
            ? "    private static string? Conv(string? v) => v;"
            : syntax.Contains("nameof(Never)", StringComparison.Ordinal)
                ? "    private static bool Never(Src s) => false;"
                : "";

        var src = EndpointSources.Build(endpoint, attribute, extraMembers: helper);

        if (status == CellStatus.Refused)
        {
            var reported = GeneratorAssert.Reports(src, diagnosticId!);
            Assert.NotEmpty(reported);
            // The error shape matters as much as the refusal: a message that does not name the modifier sends
            // the reader hunting elsewhere, which is what DWARF001 did for the non-public source member.
            Assert.Contains(reported, d =>
                d.GetMessage(CultureInfo.InvariantCulture)
                    .Contains(modifier, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            GeneratorAssert.CompilesClean(src);
        }
    }

    public static IEnumerable<object[]> ModifierCells() =>
        EndpointContractMatrix.ModifierCells.Select(c =>
            new object[] { c.Modifier, c.Syntax, c.Endpoint, c.Status, c.Id! });
}
