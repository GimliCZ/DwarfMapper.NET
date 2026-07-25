// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     INVARIANT: for one and the same mapper, the <c>IQueryable</c> projection path must resolve exactly the
///     members the runtime <c>.Map</c> path resolves. Two engines answer "which source member feeds this
///     target?" — <c>ResolveMembers</c> for <c>.Map</c>, <c>ResolveProjectionMembers</c> for <c>.Project</c> —
///     and every member-matching option has to reach both or the same mapper silently means two different
///     things depending on which method you call.
///     <para>
///         This is a REGRESSION CLASS, not a single bug. <c>NameConvention.Flexible</c> was threaded into the
///         runtime resolver but never into the projection resolver, so a Flexible mapper matched
///         <c>user_id → UserId</c> through <c>.Map</c> and refused the very same member through
///         <c>.Project</c> (DWARF001). Case-insensitivity had already been fixed once for the same reason (the
///         "C4" comparer propagation), which is the tell that one-off fixes do not hold here.
///     </para>
///     <para>
///         So this test does not assert the one shape that broke. It sweeps every member-matching option
///         against a source/target pair whose names only agree under that option, and asserts the pair compiles
///         clean — meaning BOTH methods bound every member. A future option added to one engine and not the
///         other fails here without anyone remembering to write a test for it.
///     </para>
/// </summary>
public class ProjectionRuntimeParityTests
{
    /// <summary>
    ///     A mapper carrying BOTH a runtime map and a projection over the same pair. Because the completeness
    ///     gate (DWARF001) runs per method, an unbound member on EITHER method fails the compile — which is
    ///     what makes "compiles clean" a true parity assertion rather than a smoke test.
    /// </summary>
    private static string BothPaths(string options, string sourceMembers, string targetMembers) => $$"""
        using System.Linq;
        using DwarfMapper;
        namespace Demo;

        public sealed class Src { {{sourceMembers}} }
        public sealed class Dto { {{targetMembers}} }

        [DwarfMapper({{options}})]
        public partial class M
        {
            public partial Dto Map(Src s);
            public partial IQueryable<Dto> Project(IQueryable<Src> q);
        }
        """;

    [Theory]
    // Each row: a member-matching option, plus names that ONLY match when that option reaches the resolver.
    // NameConvention.Flexible — the regression that prompted this test.
    [InlineData("NameConvention = NameConvention.Flexible",
        "public int user_id { get; set; } public string user_name { get; set; } = \"\";",
        "public int UserId { get; set; } public string UserName { get; set; } = \"\";")]
    // camelCase source is the other half of Flexible's contract.
    [InlineData("NameConvention = NameConvention.Flexible",
        "public int userId { get; set; }",
        "public int UserId { get; set; }")]
    // UPPER_CASE likewise.
    [InlineData("NameConvention = NameConvention.Flexible",
        "public int USER_ID { get; set; }",
        "public int UserId { get; set; }")]
    // CaseInsensitive — already fixed once (C4); pinned so it cannot regress the same way.
    [InlineData("CaseInsensitive = true",
        "public int userid { get; set; }",
        "public int UserId { get; set; }")]
    // Exact matching is the control: it must keep working with no option at all.
    [InlineData("",
        "public int UserId { get; set; }",
        "public int UserId { get; set; }")]
    public void Projection_resolves_the_same_members_as_the_runtime_map(
        string options, string sourceMembers, string targetMembers)
    {
        // Compiles clean == every member bound on BOTH the Map and the Project method. If the projection engine
        // ignores the option, DWARF001 fires on Project (verified: that is exactly how the Flexible bug showed).
        GeneratorAssert.CompilesClean(BothPaths(options, sourceMembers, targetMembers));
    }

    [Fact]
    public void Projection_reports_ambiguity_under_Flexible_just_like_the_runtime_path()
    {
        // The mirror obligation: sharing the matching rule means sharing its FAILURE mode. Two source members
        // that collide only after normalization (Id / _id) must be reported as ambiguous by the projection
        // path too — binding one arbitrarily would be a silently-wrong mapping.
        var src = BothPaths(
            "NameConvention = NameConvention.Flexible",
            "public int Id { get; set; } public int _id { get; set; }",
            "public int Id { get; set; }");

        Assert.NotEmpty(GeneratorAssert.Reports(src, "DWARF010"));
    }
}
