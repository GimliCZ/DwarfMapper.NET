// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DifferentialTests;

/// <summary>
///     Guards the accepted-divergence list itself, because it is the part of this project that is the
///     deliverable.
/// </summary>
/// <remarks>
///     An allowlist rots in two directions and both are silent. Entries can accumulate reasons nobody would
///     defend today, and entries can outlive the difference they described — at which point the harness is
///     carrying a claim about how DwarfMapper differs from Mapperly that is no longer true.
/// </remarks>
public class DivergenceLedgerTests
{
    [Fact]
    public void Every_accepted_divergence_states_a_reason()
    {
        // The one rule that keeps the list from becoming a way to make red things green. A reason is what
        // makes an entry reviewable by someone who was not there.
        var unexplained = AcceptedDivergences.All
            .Where(a => a.Why.Trim().Length < 40)
            .Select(a => $"{a.Shape}/{a.Oracle}{a.PathPrefix}")
            .ToList();

        Assert.True(unexplained.Count == 0,
            "Accepted divergence(s) with no real explanation:\n  " + string.Join("\n  ", unexplained)
            + "\n\nName the AXIS on which the two mappers differ and why DwarfMapper's side is right (or "
            + "deliberately different). A one-word reason is how an allowlist becomes a way of making red "
            + "things green.");
    }

    [Fact]
    public void Every_accepted_divergence_names_a_real_oracle()
    {
        var known = new[] { Oracles.Mapperly, Oracles.AutoMapper };

        var unknown = AcceptedDivergences.All
            .Where(a => !known.Contains(a.Oracle, StringComparer.Ordinal))
            .Select(a => a.Oracle)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            "Accepted divergence(s) naming an oracle this harness does not run: "
            + string.Join(", ", unknown));
    }

    [Fact]
    public void Every_accepted_divergence_is_actually_exercised()
    {
        // A stale entry is worse than no entry: it reads as a documented difference while silently covering
        // whatever else happens to fall under its path prefix.
        //
        // Computed from the catalogue rather than from a side-channel recorded by the other test class. The
        // first draft did the latter and quietly depended on xUnit running the two classes in a particular
        // order — it does not, and this check failed against an allowlist that was in fact being exercised.
        var hit = new HashSet<string>(StringComparer.Ordinal);

        foreach (var comparison in ShapeCatalog.All())
        foreach (var difference in MemberComparer.Differences(comparison.Dwarf, comparison.Oracle_))
        {
            var path = difference.Split(':')[0];
            foreach (var accepted in AcceptedDivergences.All)
                if (string.Equals(accepted.Shape, comparison.Shape, StringComparison.Ordinal)
                    && string.Equals(accepted.Oracle, comparison.Oracle, StringComparison.Ordinal)
                    && path.StartsWith(accepted.PathPrefix, StringComparison.Ordinal))
                    hit.Add(Key(accepted));
        }

        var unused = AcceptedDivergences.All
            .Where(a => !hit.Contains(Key(a)))
            .Select(Key)
            .ToList();

        Assert.True(unused.Count == 0,
            "Accepted divergence(s) that no comparison actually hit:\n  " + string.Join("\n  ", unused)
            + "\n\nEither the difference is gone — delete the entry, that is the ratchet tightening — or no "
            + "shape exercises it, in which case the entry is documenting something untested.");
    }

    private static string Key(AcceptedDivergence a) => $"{a.Shape}/{a.Oracle} at {a.PathPrefix}";
}
