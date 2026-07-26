// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.Fuzzing.Fixtures;

/// <summary>How a fixture member is shaped. One vocabulary, so a new axis is a value rather than a new schema.</summary>
public enum MemberShape
{
    Scalar,
    NullableScalar,
    Reference,
    NullableReference,
    List,
    Array,
    Nested
}

/// <summary>One member of a generated fixture type.</summary>
public sealed record FixtureMember(string Name, MemberShape Shape)
{
    /// <summary>The C# type for this member on the source side.</summary>
    public string SourceType => Shape switch
    {
        MemberShape.Scalar => "int",
        MemberShape.NullableScalar => "int?",
        MemberShape.Reference => "string",
        MemberShape.NullableReference => "string?",
        MemberShape.List => "System.Collections.Generic.List<int>",
        MemberShape.Array => "int[]",
        MemberShape.Nested => "Leaf",
        _ => "int"
    };

    /// <summary>The target type. Kept identical to the source so a failure is never just a conversion.</summary>
    public string TargetType => SourceType;

    public string Declare() => $"public {SourceType} {Name} {{ get; set; }}"
                               + (Shape is MemberShape.List or MemberShape.Array or MemberShape.Nested
                                   ? " // reference-shaped"
                                   : "");
}

/// <summary>
///     A minimal, SHRINKABLE description of a mapping fixture.
///     <para>
///         The existing schemas (<c>CombinatorialSchema</c>, <c>SyntheticSchema</c>) generate source text
///         directly, which makes a failure hard to act on: the report is the whole generated schema, and
///         narrowing it means bisecting by hand. Raising the cost of acting on a fuzz failure is the same as
///         lowering the odds anyone does — so this model exists to be reduced, not just rendered.
///     </para>
/// </summary>
public sealed record Fixture(IReadOnlyList<FixtureMember> Members, bool CaseInsensitive = false)
{
    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine("namespace Demo;");
        sb.AppendLine("public sealed class Leaf { public int V { get; set; } }");
        sb.AppendLine("public sealed class Src {");
        foreach (var m in Members) sb.Append("    ").AppendLine(m.Declare());
        sb.AppendLine("}");
        sb.AppendLine("public sealed class Dst {");
        foreach (var m in Members) sb.Append("    ").AppendLine(m.Declare());
        sb.AppendLine("}");
        sb.AppendLine(CaseInsensitive ? "[DwarfMapper(CaseInsensitive = true)]" : "[DwarfMapper]");
        sb.AppendLine("public partial class M { public partial Dst Map(Src s); }");
        return sb.ToString();
    }

    /// <summary>A stable one-line description, so a shrunk repro can be pasted into an issue.</summary>
    public string Describe() =>
        $"Fixture({Members.Count} members: {string.Join(", ", Members.Select(m => m.Name + ':' + m.Shape))}"
        + (CaseInsensitive ? ", CaseInsensitive" : "") + ")";

    public static Fixture FromSeed(int seed, int memberCount = 8)
    {
        // Deterministic by construction — no Random, so a reported seed reproduces exactly. (DeterminismSource
        // ScanTests bans unseeded randomness in the generator; the same discipline belongs in its fixtures.)
        var shapes = Enum.GetValues<MemberShape>();
        var members = new List<FixtureMember>();
        for (var i = 0; i < memberCount; i++)
        {
            var shape = shapes[Math.Abs(seed * 31 + i * 17) % shapes.Length];
            members.Add(new FixtureMember("M" + i, shape));
        }

        return new Fixture(members, seed % 3 == 0);
    }
}
