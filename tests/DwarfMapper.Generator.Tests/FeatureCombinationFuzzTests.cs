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
    // helper methods. Member names are uniquely prefixed so features never collide when combined. The set
    // spans every member-level EMITTER: direct/rename, constant value, unflatten, deep path, conditional,
    // null-substitute, collection, dictionary, enum, nested object, narrowing/parse conversion, nullable,
    // and ignore.
    private static readonly (string Name, string Src, string Tgt, string Attr, string Helper)[] Features =
    {
        ("rename",    "public int F1o { get; set; }",                          "public int F1 { get; set; }",                   "[MapProperty(\"F1o\", \"F1\")]",                            ""),
        ("mapvalue",  "",                                                       "public string F2 { get; set; } = \"\";",        "[MapValue(\"F2\", \"v2\")]",                                ""),
        ("unflatten", "public string F3c { get; set; } = \"\";",               "public FzAddr F3 { get; set; } = new();",       "[MapProperty(\"F3c\", \"F3.City\")]",                       ""),
        ("deeppath",  "public FzInner F4i { get; set; } = new();",             "public string F4 { get; set; } = \"\";",        "[MapProperty(\"F4i.Name\", \"F4\")]",                       ""),
        ("when",      "public int F5 { get; set; } public int F5t { get; set; }", "public int F5 { get; set; }",                "[MapProperty(\"F5\", \"F5\", When = nameof(FzWhen))]",      "private static bool FzWhen(FzSrc s) => s.F5t > 0;"),
        ("nullsubst", "public string? F6 { get; set; }",                       "public string F6 { get; set; } = \"\";",        "[MapProperty(\"F6\", \"F6\", NullSubstitute = \"n6\")]",    ""),
        ("collection","public System.Collections.Generic.List<FzC> F7 { get; set; } = new();", "public System.Collections.Generic.List<FzCDto> F7 { get; set; } = new();", "", ""),
        ("dictionary","public System.Collections.Generic.Dictionary<string,int> F8 { get; set; } = new();", "public System.Collections.Generic.Dictionary<string,int> F8 { get; set; } = new();", "", ""),
        ("enum",      "public FzEnA F9 { get; set; }",                         "public FzEnB F9 { get; set; }",                 "", ""),
        ("nested",    "public FzN F10 { get; set; } = new();",                 "public FzNDto F10 { get; set; } = new();",      "", ""),
        ("narrow",    "public long F11 { get; set; }",                         "public int F11 { get; set; }",                  "", ""),
        ("parse",     "public string F12 { get; set; } = \"0\";",             "public int F12 { get; set; }",                  "", ""),
        ("nullable",  "public int? F13 { get; set; }",                         "public int? F13 { get; set; }",                 "", ""),
        ("ignore",    "",                                                       "public int F14 { get; set; }",                  "[MapIgnore(\"F14\")]",                                      ""),
        ("flatten",   "public FzFl F15 { get; set; } = new();",                 "public int F15x { get; set; }",                 "[Flatten(\"F15\")]",                                        ""),
        ("aftermap",  "",                                                       "public int F16 { get; set; }",                  "[MapIgnore(\"F16\")]",                                      "[AfterMap] private static void FzAfter(FzDst d) => d.F16 = 1;"),
    };

    // Mode axis: a class attribute. Reference-handling modes (FullSubsets=true) cross with feature
    // singles+pairs+all — that is the interaction surface the deferred-member bug hid in. Config modes
    // (FullSubsets=false) cross with feature SINGLES — a lighter check that each config compiles with each
    // feature. Modes whose invalid combinations SHOULD error (strict ImplicitConversions × lossy
    // conversions; AsNull × non-nullable collection) are deliberately excluded here and covered by the
    // dedicated ConversionPolicy / NullCollections tests.
    private static readonly (string Name, string Attr, bool FullSubsets)[] Modes =
    {
        ("none",            "[DwarfMapper]", true),
        ("preserve",        "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]", true),
        ("setnull",         "[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]", true),
        ("caseinsensitive", "[DwarfMapper(CaseInsensitive = true)]", false),
        ("flexible",        "[DwarfMapper(NameConvention = NameConvention.Flexible)]", false),
        ("byvalue",         "[DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]", false),
        ("setdefault",      "[DwarfMapper(NullStrategy = NullStrategy.SetDefault)]", false),
        ("requireboth",     "[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]", false),
    };

    // Method-kind axis: a construction mapper (T Map(S)) vs an update-into mapper (void Map(S, T dest)).
    // Update-into is a SEPARATE emitter that also assigns members — it had the same deferred-member bug.
    private static readonly string[] Kinds = { "construct", "update" };

    public static IEnumerable<object[]> Combos()
    {
        var n = Features.Length;
        var singles = new List<int[]>();
        var full = new List<int[]>();
        for (var i = 0; i < n; i++) { singles.Add(new[] { i }); full.Add(new[] { i }); }      // singles
        for (var i = 0; i < n; i++) for (var j = i + 1; j < n; j++) full.Add(new[] { i, j }); // pairs
        full.Add(Enumerable.Range(0, n).ToArray());                                            // all features at once

        foreach (var kind in Kinds)
            foreach (var (mn, _, fullSubsets) in Modes)
            {
                // Update-into is None-semantics — only the default mode applies.
                if (kind == "update" && mn != "none") continue;
                foreach (var sub in (fullSubsets ? full : singles))
                    yield return new object[]
                    {
                        kind + "/" + mn + ":" + string.Join("+", sub.Select(i => Features[i].Name)), kind, mn, sub,
                    };
            }
    }

    [Theory]
    [MemberData(nameof(Combos))]
    public void Feature_combination_compiles_cleanly(string label, string kind, string refMode, int[] featureIdx)
    {
        _ = label;
        var src = BuildSource(kind, refMode, featureIdx);
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    private static string BuildSource(string kind, string mode, int[] featureIdx)
    {
        var refAttr = Modes.First(r => r.Name == mode).Attr;
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
        // Self-referential child forces the Preserve/SetNull (and update-into recursion) path to engage.
        sb.Append("public class FzNode { public int V { get; set; } public FzNode? Next { get; set; } }\n");
        sb.Append("public class FzNodeDto { public int V { get; set; } public FzNodeDto? Next { get; set; } }\n");
        sb.Append("public class FzAddr { public string City { get; set; } = \"\"; }\n");
        sb.Append("public class FzInner { public string Name { get; set; } = \"\"; }\n");
        sb.Append("public class FzC { public int X { get; set; } }\n");
        sb.Append("public class FzCDto { public int X { get; set; } }\n");
        sb.Append("public class FzN { public int Y { get; set; } }\n");
        sb.Append("public class FzNDto { public int Y { get; set; } }\n");
        sb.Append("public enum FzEnA { A, B }\n");
        sb.Append("public enum FzEnB { A, B }\n");
        sb.Append("public class FzFl { public int F15x { get; set; } }\n");
        sb.Append("public class FzSrc { public FzNode Child { get; set; } = new();").Append(srcMembers).Append(" }\n");
        sb.Append("public class FzDst { public FzNodeDto Child { get; set; } = new();").Append(tgtMembers).Append(" }\n");
        sb.Append(refAttr).Append("\npublic partial class FzM\n{\n");
        sb.Append(attrs);
        sb.Append(kind == "update"
            ? "    public partial void Map(FzSrc s, FzDst dest);\n"
            : "    public partial FzDst Map(FzSrc s);\n");
        sb.Append(helpers);
        sb.Append("}\n");
        return sb.ToString();
    }
}
