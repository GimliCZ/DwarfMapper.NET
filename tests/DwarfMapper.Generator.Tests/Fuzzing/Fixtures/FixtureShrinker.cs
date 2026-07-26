// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Fuzzing.Fixtures;

/// <summary>
///     Reduces a failing <see cref="Fixture" /> to a minimal one that still fails.
///     <para>
///         A fuzz suite without a shrinker reports the whole generated schema, so diagnosing a failure means
///         bisecting by hand. That cost is not neutral: it is paid at the exact moment someone is deciding
///         whether to investigate a red build or re-run it, which is how genuine fuzz findings get quietly
///         discarded. Shrinking is what turns "seed 37 failed" into "an <c>int?</c> member alone reproduces
///         it".
///     </para>
///     <para>
///         Deliberately greedy and deterministic rather than clever: drop members first (the biggest win),
///         then simplify the shapes that remain, then relax options. No randomness, so the same failure always
///         shrinks to the same repro and two engineers see the same thing.
///     </para>
/// </summary>
public static class FixtureShrinker
{
    /// <summary>
    ///     Shrinks <paramref name="failing" /> while <paramref name="stillFails" /> holds.
    /// </summary>
    /// <param name="failing">A fixture believed to fail.</param>
    /// <param name="stillFails">
    ///     The failure oracle. Must be a PURE re-test of the property: a flaky oracle produces a nonsense
    ///     repro, which is worse than none because it looks authoritative.
    /// </param>
    public static Fixture Shrink(Fixture failing, Func<Fixture, bool> stillFails)
    {
        ArgumentNullException.ThrowIfNull(failing);
        ArgumentNullException.ThrowIfNull(stillFails);

        if (!stillFails(failing))
            throw new InvalidOperationException(
                "Shrink was given a fixture that does not fail. Shrinking a passing input would 'minimise' to "
                + "an arbitrary fixture and present it as a repro.");

        var current = failing;

        // 1. Drop members — one pass, greedily, keeping any removal that preserves the failure.
        var progress = true;
        while (progress)
        {
            progress = false;
            for (var i = 0; i < current.Members.Count; i++)
            {
                if (current.Members.Count == 1) break;

                var candidate = current with
                {
                    Members = current.Members.Where((_, idx) => idx != i).ToList()
                };

                if (!stillFails(candidate)) continue;

                current = candidate;
                progress = true;
                break; // indices shifted; restart the sweep
            }
        }

        // 2. Simplify the remaining shapes toward the simplest that still fails. Scalar is the floor, so a
        //    surviving NullableScalar/List/Nested in the repro is SIGNAL — it means that shape is required.
        foreach (var simpler in new[] { MemberShape.Scalar, MemberShape.Reference, MemberShape.NullableScalar })
        {
            for (var i = 0; i < current.Members.Count; i++)
            {
                if (current.Members[i].Shape == simpler) continue;

                var members = current.Members.ToList();
                members[i] = members[i] with { Shape = simpler };
                var candidate = current with { Members = members };

                if (stillFails(candidate)) current = candidate;
            }
        }

        // 3. Relax options last: if the failure survives without CaseInsensitive, the option was incidental
        //    and mentioning it in the repro would misdirect the reader.
        if (current.CaseInsensitive)
        {
            var candidate = current with { CaseInsensitive = false };
            if (stillFails(candidate)) current = candidate;
        }

        return current;
    }
}
