// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

// CA1062: theory parameters are supplied by [MemberData] (never null); validation would be noise.
#pragma warning disable CA1062

/// <summary>
/// Exhaustive feature-COMBINATION fuzz. The per-feature tests and the FeatureInteractionCompileMatrix
/// cover each feature in isolation; this crosses the 3 reference-handling modes (None / Preserve / SetNull)
/// with subsets of the member-shaping features (rename, MapValue, unflatten, deep path, When, NullSubstitute)
/// — every combination includes a self-referential child so the Preserve/SetNull EMIT paths actually engage.
/// This is the surface that hid the unflatten×SetNull (non-compiling) and unflatten×Preserve (NRE) bugs:
/// the deferred-member emission was added to the main path but not the reference-handling paths. Every
/// generated combination must compile cleanly with no error diagnostic.
/// </summary>
public class FeatureCombinationFuzzTests
{
    // Each feature contributes self-consistent source members, target members, method attributes, and any
    // helper methods. Member names are uniquely prefixed so features never collide when combined.
    private static readonly (string Name, string Src, string Tgt, string Attr, string Helper)[] Features =
    {
        ("rename",    "public int F1o { get; set; }",                          "public int F1 { get; set; }",                   "[MapProperty(\"F1o\", \"F1\")]",                            ""),
        ("mapvalue",  "",                                                       "public string F2 { get; set; } = \"\";",        "[MapValue(\"F2\", \"v2\")]",                                ""),
        ("unflatten", "public string F3c { get; set; } = \"\";",               "public FzAddr F3 { get; set; } = new();",       "[MapProperty(\"F3c\", \"F3.City\")]",                       ""),
        ("deeppath",  "public FzInner F4i { get; set; } = new();",             "public string F4 { get; set; } = \"\";",        "[MapProperty(\"F4i.Name\", \"F4\")]",                       ""),
        ("when",      "public int F5 { get; set; } public int F5t { get; set; }", "public int F5 { get; set; }",                "[MapProperty(\"F5\", \"F5\", When = nameof(FzWhen))]",      "private static bool FzWhen(FzSrc s) => s.F5t > 0;"),
        ("nullsubst", "public string? F6 { get; set; }",                       "public string F6 { get; set; } = \"\";",        "[MapProperty(\"F6\", \"F6\", NullSubstitute = \"n6\")]",    ""),
    };

    private static readonly (string Name, string Attr)[] RefModes =
    {
        ("none", "[DwarfMapper]"),
        ("preserve", "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]"),
        ("setnull", "[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]"),
    };

    public static IEnumerable<object[]> Combos()
    {
        var n = Features.Length;
        var subsets = new List<int[]>();
        for (var i = 0; i < n; i++) subsets.Add(new[] { i });                              // singles
        for (var i = 0; i < n; i++) for (var j = i + 1; j < n; j++) subsets.Add(new[] { i, j }); // pairs
        subsets.Add(Enumerable.Range(0, n).ToArray());                                      // all features at once
        foreach (var (rn, _) in RefModes)
            foreach (var sub in subsets)
                yield return new object[] { rn + ":" + string.Join("+", sub.Select(i => Features[i].Name)), rn, sub };
    }

    [Theory]
    [MemberData(nameof(Combos))]
    public void Feature_combination_compiles_cleanly(string label, string refMode, int[] featureIdx)
    {
        _ = label;
        var src = BuildSource(refMode, featureIdx);
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    private static string BuildSource(string refMode, int[] featureIdx)
    {
        var refAttr = RefModes.First(r => r.Name == refMode).Attr;
        var srcMembers = new StringBuilder();
        var tgtMembers = new StringBuilder();
        var attrs = new StringBuilder();
        var helpers = new StringBuilder();
        foreach (var i in featureIdx)
        {
            var f = Features[i];
            srcMembers.Append(' ').Append(f.Src);
            tgtMembers.Append(' ').Append(f.Tgt);
            if (f.Attr.Length > 0) attrs.Append("    ").Append(f.Attr).Append('\n');
            if (f.Helper.Length > 0) helpers.Append("    ").Append(f.Helper).Append('\n');
        }

        var sb = new StringBuilder();
        sb.Append("using DwarfMapper;\n#nullable enable\nnamespace Fz;\n");
        // Self-referential child forces the Preserve/SetNull emit path to engage (isPublicWithCtx).
        sb.Append("public class FzNode { public int V { get; set; } public FzNode? Next { get; set; } }\n");
        sb.Append("public class FzNodeDto { public int V { get; set; } public FzNodeDto? Next { get; set; } }\n");
        sb.Append("public class FzAddr { public string City { get; set; } = \"\"; }\n");
        sb.Append("public class FzInner { public string Name { get; set; } = \"\"; }\n");
        sb.Append("public class FzSrc { public FzNode Child { get; set; } = new();").Append(srcMembers).Append(" }\n");
        sb.Append("public class FzDst { public FzNodeDto Child { get; set; } = new();").Append(tgtMembers).Append(" }\n");
        sb.Append(refAttr).Append("\npublic partial class FzM\n{\n");
        sb.Append(attrs);
        sb.Append("    public partial FzDst Map(FzSrc s);\n");
        sb.Append(helpers);
        sb.Append("}\n");
        return sb.ToString();
    }
}
