// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using DwarfMapper.Generator.Tests.Fuzzing.Fixtures;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
///     Phase 4 of the test-hardening programme: a generic fixture vocabulary, a shrinker that makes fuzz
///     failures actionable, and the endpoint axis expressed as a property rather than a table.
/// </summary>
public class FixtureHarnessTests
{
    // ── The generic fixture core ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_generated_fixture_compiles_clean(int seed)
    {
        // The vocabulary is only useful if everything it can express is valid input. A fixture that fails to
        // compile would make every downstream property fail for a reason that has nothing to do with the
        // property.
        GeneratorAssert.CompilesClean(Fixture.FromSeed(seed).Render());
    }

    [Fact]
    public void Fixtures_are_reproducible_from_their_seed()
    {
        // A reported seed is worthless if it does not rebuild the same fixture. FromSeed is arithmetic rather
        // than Random for exactly this reason.
        Assert.Equal(Fixture.FromSeed(7).Render(), Fixture.FromSeed(7).Render());
        Assert.NotEqual(Fixture.FromSeed(7).Render(), Fixture.FromSeed(8).Render());
    }

    // ── The shrinker ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Shrinker_reduces_a_failure_to_its_essential_member()
    {
        // Oracle: "fails" iff the fixture contains a NullableScalar member. A synthetic property, deliberately
        // — it makes the CORRECT minimal repro knowable in advance (exactly one NullableScalar), so the test
        // asserts the shrinker's result rather than merely that it ran.
        var start = Fixture.FromSeed(3, memberCount: 12) with
        {
            Members = Enumerable.Range(0, 12)
                .Select(i => new FixtureMember("M" + i,
                    i == 7 ? MemberShape.NullableScalar : MemberShape.Scalar))
                .ToList()
        };

        var probes = 0;
        var shrunk = FixtureShrinker.Shrink(start, f =>
        {
            probes++;
            return f.Members.Any(m => m.Shape == MemberShape.NullableScalar);
        });

        Assert.Single(shrunk.Members);
        Assert.Equal(MemberShape.NullableScalar, shrunk.Members[0].Shape);
        Assert.True(probes > 0, "The oracle was never consulted — the shrinker did nothing.");

        // The repro must be small enough to paste into an issue; the spec's exit criterion is <= 5 members.
        Assert.True(shrunk.Members.Count <= 5, shrunk.Describe());
    }

    [Fact]
    public void Shrinker_relaxes_incidental_options()
    {
        // If a failure survives without CaseInsensitive, naming it in the repro would send the reader after
        // the wrong cause. The shrinker must strip it.
        var start = new Fixture(
            [new FixtureMember("A", MemberShape.Scalar), new FixtureMember("B", MemberShape.Scalar)],
            CaseInsensitive: true);

        var shrunk = FixtureShrinker.Shrink(start, f => f.Members.Count >= 1);

        Assert.False(shrunk.CaseInsensitive);
        Assert.Single(shrunk.Members);
    }

    [Fact]
    public void Shrinker_refuses_a_passing_input()
    {
        // Shrinking something that does not fail would "minimise" to an arbitrary fixture and present it as a
        // repro — an authoritative-looking answer to a question nobody asked.
        var fixture = Fixture.FromSeed(1);

        Assert.Throws<InvalidOperationException>(() => FixtureShrinker.Shrink(fixture, _ => false));
    }

    [Fact]
    public void Shrinker_preserves_the_failure_it_was_given()
    {
        // The one invariant that makes a shrunk repro trustworthy: the result must still fail. A shrinker that
        // over-reduces produces a passing "repro" and sends the reader hunting a bug that the fixture no
        // longer triggers.
        var start = Fixture.FromSeed(5, memberCount: 10);
        bool Oracle(Fixture f) => f.Members.Any(m => m.Shape == MemberShape.List);

        if (!Oracle(start)) return; // this seed has no List member; nothing to shrink

        var shrunk = FixtureShrinker.Shrink(start, Oracle);
        Assert.True(Oracle(shrunk), "Shrunk fixture no longer reproduces: " + shrunk.Describe());
    }

    // ── The endpoint axis, as a property ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_fixture_shaped_mapper_behaves_consistently_across_endpoints(int seed)
    {
        // P2 states the attribute x endpoint contract as a TABLE; this states the same guarantee as a
        // property, over generated shapes rather than curated ones. Scalars only, because the projection
        // endpoint legitimately refuses several shapes (nullable-to-non-nullable, collections) — mixing those
        // in would test the refusal rather than the consistency.
        var members = Enumerable.Range(0, 4)
            .Select(i => new FixtureMember("M" + i, (seed + i) % 2 == 0 ? MemberShape.Scalar : MemberShape.Reference))
            .ToList();
        var fixture = new Fixture(members);

        // Every endpoint that can express this shape must accept it. A divergence here is the same defect
        // class as the four projection bugs: one shape, two answers.
        foreach (var endpoint in new[] { Endpoint.CreateMap, Endpoint.UpdateInto, Endpoint.Projection })
        {
            var src = fixture.Render().Replace(
                "public partial class M { public partial Dst Map(Src s); }",
                endpoint switch
                {
                    Endpoint.CreateMap => "public partial class M { public partial Dst Map(Src s); }",
                    Endpoint.UpdateInto => "public partial class M { public partial void Update(Src s, Dst d); }",
                    _ => "public partial class M { public partial System.Linq.IQueryable<Dst> Project("
                         + "System.Linq.IQueryable<Src> q); }"
                },
                StringComparison.Ordinal);

            GeneratorAssert.CompilesClean(src);
        }
    }
}
