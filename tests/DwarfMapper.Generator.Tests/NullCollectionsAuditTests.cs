// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

public class NullCollectionsAuditTests
{
    private static void NoErrors(string src)
    {
        GeneratorAssert.CompilesClean(src);
    }

    // ─── A1: nullAsNull must propagate into nested element/key/value converters ──

    [Fact]
    public void A1_dict_with_nullable_list_value_uses_nn_tag_for_value_helper()
    {
        // When NullCollections=AsNull, the element helper for List<int>? values
        // must be synthesized with nullAsNull=true (has _nn in name/emits null for null).
        // NullableContextOptions.Enable is required so Roslyn sees List<int>? as
        // NullableAnnotation.Annotated; without it, nullable ref annotations are not tracked.
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public Dictionary<string, List<int>?> D { get; set; } = new(); }
                         public class Dst { public Dictionary<string, List<int>?> D { get; set; } = new(); }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s, NullableContextOptions.Enable);
        // The inner collection helper for the dict value must use nullAsNull semantics:
        // it should return null for null input (not empty).
        // Observable: a nullable-return helper is synthesized (return type has "List<int>?")
        // and the identity path emits "? null :" (ternary null return for null input).
        Assert.Contains("List<int>?", gen, StringComparison.Ordinal);
        Assert.Contains("? null", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void A1_list_of_nullable_list_uses_nn_tag_for_element_helper()
    {
        // NullableContextOptions.Enable is required so Roslyn sees List<int>? as
        // NullableAnnotation.Annotated; without it, nullable ref annotations are not tracked.
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public List<List<int>?> Outer { get; set; } = new(); }
                         public class Dst { public List<List<int>?> Outer { get; set; } = new(); }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s, NullableContextOptions.Enable);
        // The inner List<int>? element helper must return null for null input.
        // Observable: nullable return type ("List<int>?") and "? null" in the ternary path.
        Assert.Contains("List<int>?", gen, StringComparison.Ordinal);
        Assert.Contains("? null", gen, StringComparison.Ordinal);
    }

    // ─── A2: ImmutableArray<T>? return type under AsNull ─────────────────────

    [Fact]
    public void A2_ImmutableArray_AsNull_emits_nullable_return_type()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         using System.Collections.Immutable;
                         namespace Demo;
                         public class Src { public List<int>? Xs { get; set; } }
                         public class Dst { public ImmutableArray<int>? Xs { get; set; } }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        // Under AsNull, the ImmutableArray helper must return ImmutableArray<int>?
        Assert.Contains("ImmutableArray<int>?", gen, StringComparison.Ordinal);
        // And the null path must return null, not default(ImmutableArray<int>)
        Assert.DoesNotContain("default(global::System.Collections.Immutable.ImmutableArray<int>)", gen,
            StringComparison.Ordinal);
        Assert.Contains("return null;", gen, StringComparison.Ordinal);
    }

    // ─── A3: non-nullable target + AsNull must compile without CS8601 ─────────

    [Fact]
    public void A3_non_nullable_list_target_compiles_no_cs8601()
    {
        // This is the key test: AssertEmpty(compilation errors) means no CS8601.
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public List<int>? Xs { get; set; } }
                         public class Dst { public List<int> Xs { get; set; } = new(); }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        // Must compile with zero errors (especially no CS8601 nullable mismatch)
        GeneratorAssert.EmitsCompilableCode(s);
    }

    [Fact]
    public void A3_nullable_list_target_uses_nullable_return_type()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public List<int>? Xs { get; set; } }
                         public class Dst { public List<int>? Xs { get; set; } }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        // Nullable target → helper must return List<int>? (nullable)
        Assert.Contains("List<int>?", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_non_nullable_array_target_compiles_no_cs8601()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int[]? Xs { get; set; } }
                         public class Dst { public int[] Xs { get; set; } = System.Array.Empty<int>(); }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        GeneratorAssert.EmitsCompilableCode(s);
    }

    [Fact]
    public void A3_non_nullable_hashset_target_compiles_no_cs8601()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public HashSet<int>? Xs { get; set; } }
                         public class Dst { public HashSet<int> Xs { get; set; } = new(); }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        GeneratorAssert.EmitsCompilableCode(s);
    }

    [Fact]
    public void A3_non_nullable_dict_target_compiles_no_cs8601()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public Dictionary<string,int>? D { get; set; } }
                         public class Dst { public Dictionary<string,int> D { get; set; } = new(); }
                         [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                         public partial class M { public partial Dst Map(Src a); }
                         """;
        GeneratorAssert.EmitsCompilableCode(s);
    }

    // ─── A4: null guard must appear BEFORE allocation in EmitList/EmitHashSet ──

    [Fact]
    public void A4_list_null_guard_before_new_allocation()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public List<long> Xs { get; set; } = new(); }
                         public class Dst { public List<int> Xs { get; set; } = new(); }
                         [DwarfMapper] public partial class M { public partial Dst Map(Src a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        // In the collection helper, "if (src is null)" must appear BEFORE "var __r = new List<"
        var nullIdx = gen.IndexOf("if (src is null)", StringComparison.Ordinal);
        var allocIdx = gen.IndexOf("var __r = new global::System.Collections.Generic.List<", StringComparison.Ordinal);
        Assert.True(nullIdx >= 0, "null guard not found in generated code");
        Assert.True(allocIdx >= 0, "allocation not found in generated code");
        Assert.True(nullIdx < allocIdx, $"null guard (at {nullIdx}) must appear before allocation (at {allocIdx})");
    }

    [Fact]
    public void A4_hashset_null_guard_before_new_allocation()
    {
        const string s = """
                         using DwarfMapper;
                         using System.Collections.Generic;
                         namespace Demo;
                         public class Src { public HashSet<long> Xs { get; set; } = new(); }
                         public class Dst { public HashSet<int> Xs { get; set; } = new(); }
                         [DwarfMapper] public partial class M { public partial Dst Map(Src a); }
                         """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        var nullIdx = gen.IndexOf("if (src is null)", StringComparison.Ordinal);
        var allocIdx = gen.IndexOf("var __r = new global::System.Collections.Generic.HashSet<",
            StringComparison.Ordinal);
        Assert.True(nullIdx >= 0, "null guard not found in generated code");
        Assert.True(allocIdx >= 0, "allocation not found in generated code");
        Assert.True(nullIdx < allocIdx, $"null guard (at {nullIdx}) must appear before allocation (at {allocIdx})");
    }
}
