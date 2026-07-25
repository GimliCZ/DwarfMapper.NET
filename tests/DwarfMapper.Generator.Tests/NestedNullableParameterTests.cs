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

    [Fact(Skip = "Open: investigate/nested-nullable-cs8604 — generated MapNested emits MapFlat(s.Inner), a "
                 + "CS8604 nullable WARNING (a build error under TreatWarningsAsErrors). Verified present today "
                 + "via GeneratedCodeWarnings. Un-skip when the emitter null-forgives non-nullable converter "
                 + "parameters.")]
    public void Nullable_nested_source_via_user_method_emits_no_CS8604()
    {
        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(NullableNestedViaUserMethod);
        Assert.DoesNotContain(warnings, d => d.Id == "CS8604");
    }

    [Fact(Skip = "Open: investigate/nested-nullable-cs8604 — no diagnostic currently explains the nullable nested "
                 + "reference. Un-skip when DWARF070 is emitted for the nested nullable→non-nullable case.")]
    public void Nullable_nested_source_reports_DWARF070_not_a_bare_CS8604()
    {
        var (diags, _) = GeneratorTestHarness.Run(NullableNestedViaUserMethod);
        Assert.Contains(diags, d => d.Id == "DWARF070");
    }
}
