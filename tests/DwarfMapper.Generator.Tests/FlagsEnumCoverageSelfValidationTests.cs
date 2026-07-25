// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using DwarfMapper.Generator.Tests.Fuzzing;
using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// SELF-VALIDATION: the corpus must be able to REACH a <c>[Flags]</c> enum with a COMBINED value.
/// <para>
/// A by-name enum converter emitted one switch arm per declared member and <c>_ =&gt; throw</c>, so
/// <c>Read | Write</c> (value 3) — the ordinary case for a flags enum, and <c>ByName</c> is the DEFAULT
/// strategy — threw <c>ArgumentOutOfRangeException</c> at run time on perfectly valid input. It survived the
/// entire fuzz + combinatorial suite for two compounding reasons, and BOTH had to be fixed:
/// </para>
/// <list type="number">
///   <item><description>no schema ever declared a <c>[Flags]</c> enum, so the shape was never generated; and</description></item>
///   <item><description><see cref="ObjectFactory" /> picked a single DECLARED member for any enum, so even
///   had the shape existed, it could never have produced a combined value — every enum the fuzzers ever fed
///   through the mapper happened to have a name, which is precisely the case the buggy switch handled.</description></item>
/// </list>
/// <para>
/// Fixing the converter without fixing those leaves the hole open for the next flags-shaped bug. These are the
/// assertions that keep the corpus able to find it — a coverage property, checked directly, rather than a
/// one-off manual proof that protects nothing tomorrow.
/// </para>
/// </summary>
public class FlagsEnumCoverageSelfValidationTests
{
    [Flags]
    private enum Probe
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
    }

    [Fact]
    public void ObjectFactory_produces_COMBINED_flag_values_not_just_declared_ones()
    {
        // The property that matters is not "it returns a Probe" but "it returns values that are NOT declared
        // members" — because a declared member is exactly what the broken converter could already handle.
        var produced = Enumerable.Range(0, 200)
            .Select(seed => (Probe)ObjectFactory.Create(typeof(Probe), new Random(seed), 0)!)
            .ToList();

        var declared = new HashSet<Probe>(Enum.GetValues<Probe>());
        var combined = produced.Where(v => !declared.Contains(v)).ToList();

        Assert.True(combined.Count > 0,
            "ObjectFactory never produced a COMBINED [Flags] value across 200 seeds — every value it emitted "
            + "was a single declared member. That is the state the suite was in when a by-name converter that "
            + "threw on every combined value passed every test: the fuzzers could not construct the input that "
            + "breaks it.\nProduced: " + string.Join(", ", produced.Distinct().OrderBy(v => (int)v)));
    }

    [Fact]
    public void The_combinatorial_schema_declares_a_Flags_enum()
    {
        var cells = CombinatorialSchema.DepthOneMatrix()
            .Where(c => c.BasicType.Contains("Flags", StringComparison.Ordinal))
            .ToList();

        Assert.True(cells.Count > 0,
            "The combinatorial matrix contains no [Flags] enum basic type, so no cell can exercise combined "
            + "flag values against the other axes (widening, cycle mode, update-into, null strategy).");

        // And it must actually carry the attribute — a plain enum named "Flags" would satisfy the check above
        // while testing nothing.
        Assert.Contains("[global::System.Flags]", cells[0].Source, StringComparison.Ordinal);
    }
}
