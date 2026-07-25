// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Characterization of an open correctness gap (investigate/nested-nullable-cs8604): a nullable nested
///     reference source mapped through a USER-DECLARED nested map method emits code that does not compile
///     (CS8604) and no DWARF diagnostic explains why — a "never silent" violation.
///     <para>
///     Root cause: <c>MapEmitter</c> decides whether to null-forgive a converter argument with
///     <c>needsBang = ConverterNeedsDepthCtx || (SourceIsNullableRef &amp;&amp; IsSynthesized(ConverterMethod))</c>.
///     <c>IsSynthesized</c> is true for auto-nested <c>__DwarfMap_Obj_</c> helpers but FALSE for a user's own
///     <c>MapFlat(FlatSrc s)</c>, so the user-declared nested path emits <c>MapFlat(s.Inner)</c> with a nullable
///     argument into a non-nullable parameter. Auto-nesting is unaffected (its helper is synthesized).
///     </para>
///     <para>
///     The fix is NOT a blanket <c>!</c> on every non-synthesized converter: a user conversion operator declared
///     to take a nullable parameter is legitimately null-tolerant, and forgiving its argument would drop a null
///     the user meant to pass. The correct fix threads the converter's parameter nullability from resolution
///     (where the <c>IMethodSymbol</c> is in scope) onto <c>MemberMap</c>, then null-forgives only when that
///     parameter is non-nullable — and emits DWARF070 for the compile-time signal, exactly as the scalar
///     nullable→non-nullable path already does. Manifest-verified, since it changes emission on a path the
///     corpus does not currently cover. See docs/superpowers/specs/2026-07-25-nested-nullable-parameter.md.
///     </para>
///     <para>
///     These tests assert the FIXED behavior and are <c>Skip</c>ped until the fix lands, so they neither break
///     the suite green nor lock in the bug. Un-skip them as the fix's red phase.
///     </para>
/// </summary>
public class NestedNullableParameterTests
{
    private const string NullableNestedViaUserMethod = """
                                                       using DwarfMapper;
                                                       #nullable enable
                                                       namespace Demo;
                                                       public class FlatSrc { public int Id { get; set; } }
                                                       public class FlatDst { public int Id { get; set; } }
                                                       public class NestedSrc { public FlatSrc? Inner { get; set; } }
                                                       public class NestedDst { public FlatDst Inner { get; set; } = new(); }
                                                       [DwarfMapper]
                                                       public partial class M
                                                       {
                                                           public partial FlatDst MapFlat(FlatSrc s);
                                                           public partial NestedDst MapNested(NestedSrc s);
                                                       }
                                                       """;

    [Fact]
    public void Nullable_nested_source_via_user_method_emits_no_CS8604()
    {
        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(NullableNestedViaUserMethod);
        Assert.DoesNotContain(warnings, d => d.Id == "CS8604");
    }

    [Fact]
    public void Nullable_nested_source_reports_DWARF070_not_a_bare_CS8604()
    {
        var (diags, _) = GeneratorTestHarness.Run(NullableNestedViaUserMethod);
        Assert.Contains(diags, d => d.Id == "DWARF070");
    }

    /// <summary>
    ///     The other side of the fix: a user conversion method DECLARED to take a nullable parameter is
    ///     null-tolerant, so its argument must NOT be null-forgiven (that would drop a null it was written to
    ///     accept) and no DWARF070 should fire. Guards against the fix over-reaching into a blanket `!`.
    /// </summary>
    [Fact]
    public void Null_tolerant_user_converter_is_not_forgiven_and_reports_no_DWARF070()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class Src { public string? Name { get; set; } }
                           public class Dst { public int Len { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               private static int NameLen(string? n) => n?.Length ?? 0;   // null-tolerant
                               [MapProperty(nameof(Src.Name), nameof(Dst.Len), Use = nameof(NameLen))]
                               public partial Dst Map(Src s);
                           }
                           """;

        var (diags, gen) = GeneratorTestHarness.Run(src);
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);

        Assert.Empty(errors);
        Assert.DoesNotContain(diags, d => d.Id == "DWARF070");
        // The null-tolerant converter must receive s.Name, NOT s.Name! — forgiving would strip its null.
        Assert.DoesNotContain("NameLen(s.Name!)", gen, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The destination-nullability gate: a nullable nested source into a NULLABLE destination member (a
    ///     self-referential linked-list node's terminal <c>Next</c>) must NOT report DWARF070 — the null
    ///     legitimately propagates into a nullable slot. An earlier version of the fix looked only at the
    ///     converter parameter and wrongly fired DWARF070 here, breaking three sample projects that map such
    ///     nodes. This pins that gate so the over-broad form cannot return.
    /// </summary>
    /// <summary>
    ///     Audit follow-up: the fix must cover the CONSTRUCTOR-argument path too, not just member assignment. A
    ///     nullable nested source passed into a non-nullable ctor parameter via a user-declared map emitted
    ///     CS8604 with no diagnostic on this path.
    /// </summary>
    [Fact]
    public void Nullable_nested_source_into_ctor_param_emits_no_CS8604()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class FlatSrc { public int Id { get; set; } }
                           public class FlatDst { public int Id { get; set; } }
                           public class NestedSrc { public FlatSrc? Inner { get; set; } }
                           public class NestedDst { public NestedDst(FlatDst inner) { Inner = inner; } public FlatDst Inner { get; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial FlatDst MapFlat(FlatSrc s);
                               public partial NestedDst MapNested(NestedSrc s);
                           }
                           """;

        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(src);
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(warnings, d => d.Id == "CS8604");
        Assert.Contains(diags, d => d.Id == "DWARF070");
    }

    /// <summary>
    ///     Audit follow-up: the explicit-[MapProperty] member path, likewise.
    /// </summary>
    [Fact]
    public void Nullable_nested_source_via_explicit_MapProperty_emits_no_CS8604()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class FlatSrc { public int Id { get; set; } }
                           public class FlatDst { public int Id { get; set; } }
                           public class NestedSrc { public FlatSrc? Node { get; set; } }
                           public class NestedDst { public FlatDst Inner { get; set; } = new(); }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial FlatDst MapFlat(FlatSrc s);
                               [MapProperty(nameof(NestedSrc.Node), nameof(NestedDst.Inner))]
                               public partial NestedDst MapNested(NestedSrc s);
                           }
                           """;

        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(src);
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(warnings, d => d.Id == "CS8604");
        Assert.Contains(diags, d => d.Id == "DWARF070");
    }

    [Fact]
    public void Nullable_nested_source_into_nullable_dest_reports_no_DWARF070_and_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           #nullable enable
                           namespace Demo;
                           public class Node { public int V { get; set; } public Node? Next { get; set; } }
                           public class NodeDto { public int V { get; set; } public NodeDto? Next { get; set; } }
                           [DwarfMapper(MaxDepth = 8)]
                           public partial class M { public partial NodeDto Map(Node n); }
                           """;

        var (diags, _) = GeneratorTestHarness.Run(src);
        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(src);

        Assert.DoesNotContain(diags, d => d.Id == "DWARF070");
        Assert.DoesNotContain(warnings, d => d.Id == "CS8604");
    }
}
